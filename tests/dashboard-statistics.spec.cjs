// Local Docker API/UI test: Node 20+, Playwright/Chromium and Docker CLI.
const assert = require('node:assert/strict');
const { execFileSync } = require('node:child_process');
const fs = require('node:fs/promises');
const path = require('node:path');
const { chromium } = require(process.env.HAU_PLAYWRIGHT_MODULE || 'playwright');
const api = 'http://localhost:5000', web = 'http://localhost:5227';
const documents = [], users = [], sessions = [], certificates = [];
let token, browser, checks = 0;
function pass(name) { checks++; console.log('PASS ' + name); }
async function request(method, route, body, expected = 200, credential = token) {
  let response;
  for (let attempt = 0; attempt < 3; attempt++) {
    response = await fetch(api + route, { method,
      headers: { ...(credential ? { Authorization: `Bearer ${credential}` } : {}), ...(body ? { 'Content-Type': 'application/json' } : {}) },
      body: body ? JSON.stringify(body) : undefined });
    if (response.status !== 429) break;
    await new Promise(resolve => setTimeout(resolve, (Number(response.headers.get('retry-after')) || 60) * 1000));
  }
  assert.equal(response.status, expected, `${method} ${route}`);
  if (expected >= 400) return;
  if (route.endsWith('/stats')) assert.equal(response.headers.get('cache-control'), 'no-store');
  const json = await response.json(); return json.data ?? json;
}
const inspected = JSON.parse(execFileSync('docker', ['inspect', 'hau_postgres'], { encoding: 'utf8' }))[0];
const environment = Object.fromEntries(inspected.Config.Env.map(v => { const n = v.indexOf('='); return [v.slice(0,n), v.slice(n+1)]; }));
function sql(text) {
  return execFileSync('docker', ['exec', '-i', 'hau_postgres', 'psql', '-X', '-v', 'ON_ERROR_STOP=1',
    '-U', environment.POSTGRES_USER, '-d', environment.POSTGRES_DB, '-t', '-A'], { input: text, encoding: 'utf8', stdio: ['pipe','pipe','pipe'] }).trim();
}
function uuid(id) { assert.match(id, /^[a-f0-9-]{36}$/i); return `'${id}'`; }
function expectedDocumentStatistics(actor) {
  return JSON.parse(sql(`WITH docs AS (
    SELECT d.*, p."Timestamp" AS created, p."FromUserId" AS creator,
      EXISTS(SELECT 1 FROM "DocumentProcesses" a WHERE a."DocId"=d."Id" AND a."Action"='Assign' AND a."FromUserId"=${uuid(actor)}) AS assigned
    FROM "Documents" d LEFT JOIN LATERAL (
      SELECT "Timestamp", "FromUserId" FROM "DocumentProcesses" p WHERE p."DocId"=d."Id" AND p."Action"='Submit'
      ORDER BY p."Timestamp", p."Id" LIMIT 1
    ) p ON true
  ), bounds AS (SELECT date_trunc('day', now() AT TIME ZONE 'Asia/Ho_Chi_Minh') AT TIME ZONE 'Asia/Ho_Chi_Minh' AS start)
  SELECT json_build_object(
    'totalDocuments',count(*),
    'todayDocuments',count(*) FILTER(WHERE created >= bounds.start AND created < bounds.start+interval '1 day'),
    'pendingDocuments',count(*) FILTER(WHERE "Status" IN ('PendingDeptReview','DeptSigned','PendingDirectorSign','DirectorSigned')),
    'publishedDocuments',count(*) FILTER(WHERE "Status"='Published'),
    'pendingOcrDocuments',count(*) FILTER(WHERE btrim("MinioPath")<>'' AND "OcrDataRaw" IS NULL),
    'myDraftDocuments',count(*) FILTER(WHERE "Status"='Draft' AND creator=${uuid(actor)}),
    'myPendingDocuments',count(*) FILTER(WHERE "Status" IN ('PendingDeptReview','DeptSigned','PendingDirectorSign') AND creator=${uuid(actor)}),
    'pendingDeptDocuments',count(*) FILTER(WHERE "Status"='PendingDeptReview'),
    'pendingDirectorDocuments',count(*) FILTER(WHERE "Status"='PendingDirectorSign'),
    'directorSignedDocuments',count(*) FILTER(WHERE "Status" IN ('DirectorSigned','Published')),
    'assignedDocuments',count(*) FILTER(WHERE assigned)
  ) FROM docs CROSS JOIN bounds;`));
}
async function login(username, password) {
  const session = await request('POST','/api/auth/login',{username,password},200,null);
  sessions.push(session.accessToken); return session.accessToken;
}
function number(value) { return new Intl.NumberFormat('vi-VN').format(value); }
async function main() {
  token = await login(process.env.HAU_TEST_USERNAME || 'admin',process.env.HAU_TEST_PASSWORD || 'Admin@123');
  const me = await request('GET','/api/users/me');
  const baseline = await request('GET','/api/documents/stats');
  assert.deepEqual(baseline, expectedDocumentStatistics(me.id));
  for (const route of ['/api/documents/stats','/api/users/stats','/api/signatures/certificates/stats'])
    await request('GET',route,null,401,null);
  pass('JWT, no-store and baseline PostgreSQL counts');
  const roles = await request('GET','/api/roles'), accounts = {};
  for (const role of ['Clerk','Specialist','Manager','BoardOfDirectors']) {
    const username = `dashboard_${role}_${Date.now()}`;
    const user = await request('POST','/api/users',{username,password:'Dashboard@12345',fullName:'Dashboard statistics fixture',roleIds:[roles.find(r=>r.roleName===role).id]},201);
    users.push(user.id); accounts[role] = { ...user, token: await login(username,'Dashboard@12345') };
    await request('GET','/api/users/stats',null,403,accounts[role].token);
    await request('GET','/api/signatures/certificates/stats',null,403,accounts[role].token);
  }
  const userStats = await request('GET','/api/users/stats');
  assert.equal(userStats.totalUsers,Number(sql('SELECT count(*) FROM "AppUsers";')));
  pass('Total users includes entire database; administration counts reject other roles');
  const types = await request('GET','/api/documents/types');
  const statuses = ['Draft','PendingDeptReview','DeptSigned','PendingDirectorSign','DirectorSigned','Published','Rejected', ...Array(15).fill('Draft')];
  for (const status of statuses) {
    const doc = await request('POST','/api/documents',{title:'Dashboard fixture '+status,docTypeId:types[0].id},201,accounts.Specialist.token);
    documents.push(doc.id);
    // Only owned fixture IDs are modified. This test does not exercise signing/workflow transitions.
    sql(`UPDATE "Documents" SET "Status"='${status}' WHERE "Id"=${uuid(doc.id)};`);
  }
  sql(`UPDATE "Documents" SET "MinioPath"='documents/dashboard-fixture.pdf' WHERE "Id"=${uuid(documents[0])};
    UPDATE "DocumentProcesses" SET "Timestamp"=(now()-interval '2 days') WHERE "DocId"=${uuid(documents[7])};
    INSERT INTO "DocumentProcesses" ("Id","DocId","FromUserId","Action","Timestamp") VALUES
      (gen_random_uuid(),${uuid(documents[1])},${uuid(accounts.Manager.id)},'Assign',now()),
      (gen_random_uuid(),${uuid(documents[1])},${uuid(accounts.Manager.id)},'Assign',now());`);
  const updated = await request('GET','/api/documents/stats');
  assert.equal(updated.totalDocuments,baseline.totalDocuments+22); assert(updated.totalDocuments>20);
  assert.equal(updated.todayDocuments,baseline.todayDocuments+21);
  assert.deepEqual(updated,expectedDocumentStatistics(me.id));
  for (const account of Object.values(accounts))
    assert.deepEqual(await request('GET','/api/documents/stats',null,200,account.token),expectedDocumentStatistics(account.id));
  assert.equal((await request('GET','/api/documents/stats',null,200,accounts.Specialist.token)).myDraftDocuments,16);
  assert.equal((await request('GET','/api/documents/stats',null,200,accounts.Manager.token)).assignedDocuments,1);
  pass('All workflow/OCR counters, personal creator, distinct assignment and counts beyond pagination');
  const certificatesBefore = await request('GET','/api/signatures/certificates/stats');
  certificates.push(accounts.Manager.id);
  await request('POST','/api/signatures/certificates/issue',{userId:accounts.Manager.id,username:accounts.Manager.username,fullName:accounts.Manager.fullName,certificateType:'Personal',validityDays:1});
  assert.equal((await request('GET','/api/signatures/certificates/stats')).activeCertificates,certificatesBefore.activeCertificates+1);
  const list = await request('GET','/api/signatures/certificates');
  const now = Date.now();
  assert.equal((await request('GET','/api/signatures/certificates/stats')).activeCertificates,
    list.filter(c=>Date.parse(c.notBefore)<=now && Date.parse(c.notAfter)>now).length);
  await request('DELETE',`/api/signatures/certificates/${accounts.Manager.id}`); certificates.length=0;
  assert.equal((await request('GET','/api/signatures/certificates/stats')).activeCertificates,certificatesBefore.activeCertificates);
  pass('Active certificate issuance, validity and revocation');
  browser = await chromium.launch({headless:true});
  const context = await browser.newContext({viewport:{width:1366,height:768}});
  const page = await context.newPage(), errors=[];
  page.on('pageerror',e=>errors.push(e.message)); page.setDefaultTimeout(20000);
  await page.goto(web+'/login');
  await page.locator('#username').fill(process.env.HAU_TEST_USERNAME || 'admin');
  await page.locator('#password').fill(process.env.HAU_TEST_PASSWORD || 'Admin@123');
  await page.locator('button[type=submit]').click(); await page.waitForURL(web+'/');
  sessions.push(await page.evaluate(()=>localStorage.getItem('auth_token')));
  await page.locator('.stat-value').first().waitFor();
  async function expectCard(label,count) {
    const card=page.locator('.stat-card').filter({has:page.getByText(label,{exact:true})});
    assert.equal(await card.locator('.stat-value').innerText(),count===null?'—':number(count));
  }
  await expectCard('Tổng người dùng',userStats.totalUsers);
  await expectCard('Tổng văn bản',updated.totalDocuments);
  await expectCard('Chờ xử lý',updated.pendingDocuments);
  await expectCard('Chứng thư số hoạt động',certificatesBefore.activeCertificates);
  await fs.mkdir(path.join(__dirname,'artifacts'),{recursive:true});
  await page.screenshot({path:path.join(__dirname,'artifacts','dashboard-stats-1366.png')});
  pass('Admin browser login and exact live values');
  await page.route('**/api/signatures/certificates/stats',r=>r.fulfill({status:503,contentType:'application/json',body:'{}'}));
  await page.getByRole('button',{name:'Làm mới số liệu',exact:true}).click();
  await page.getByRole('alert').filter({hasText:'Một số số liệu chưa tải được'}).waitFor();
  await expectCard('Chứng thư số hoạt động',null); await expectCard('Tổng văn bản',updated.totalDocuments);
  await page.unroute('**/api/signatures/certificates/stats');
  await page.route('**/api/signatures/certificates/stats',r=>r.fulfill({status:200,contentType:'application/json',body:'{"success":true,"data":{}}'}));
  await page.getByRole('button',{name:'Làm mới số liệu',exact:true}).click();
  await page.locator('.stat-value').first().waitFor();
  await expectCard('Chứng thư số hoạt động',null);
  assert.equal(await page.getByRole('alert').count(),1);
  await page.route('**/api/documents/stats',r=>r.fulfill({status:503,contentType:'application/json',body:'{}'}));
  await page.getByRole('button',{name:'Làm mới số liệu',exact:true}).click();
  await page.locator('.stat-value').first().waitFor();
  await expectCard('Tổng văn bản',null); await expectCard('Tổng người dùng',userStats.totalUsers);
  await page.unroute('**/api/documents/stats'); await page.unroute('**/api/signatures/certificates/stats');
  await page.getByRole('button',{name:'Làm mới số liệu',exact:true}).click();
  await page.getByRole('alert').waitFor({state:'hidden'}); await expectCard('Tổng văn bản',updated.totalDocuments);
  pass('Partial source failures show dashes; refresh recovers real counts');
  await page.setViewportSize({width:450,height:500});
  assert(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth));
  await page.screenshot({path:path.join(__dirname,'artifacts','dashboard-stats-450.png')});
  pass('Admin mobile layout without horizontal overflow');
  await context.close();
  const personalCards = {
    Clerk:[['Công văn hôm nay','todayDocuments'],['Chờ xử lý OCR','pendingOcrDocuments'],['Đã ban hành','publishedDocuments']],
    Specialist:[['Dự thảo của tôi','myDraftDocuments'],['Chờ phê duyệt','myPendingDocuments']],
    Manager:[['Chờ ký nháy','pendingDeptDocuments'],['Đã phân công','assignedDocuments']],
    BoardOfDirectors:[['Chờ ký số pháp nhân','pendingDirectorDocuments'],['Đã ký số pháp nhân','directorSignedDocuments']]
  };
  for (const [role,account] of Object.entries(accounts)) {
    const stats = expectedDocumentStatistics(account.id);
    const roleContext = await browser.newContext({viewport:{width:450,height:500}});
    await roleContext.addInitScript(auth=>localStorage.setItem('auth_token',auth),account.token);
    const rolePage=await roleContext.newPage(), forbiddenCalls=[];
    rolePage.on('pageerror',e=>errors.push(e.message));
    rolePage.on('request',r=>{if(/\/api\/(users\/stats|signatures\/certificates\/stats)$/.test(r.url())) forbiddenCalls.push(r.url());});
    await rolePage.goto(web+'/'); await rolePage.locator('.stat-value').first().waitFor();
    for (const [label,field] of personalCards[role]) {
      const card=rolePage.locator('.stat-card').filter({has:rolePage.getByText(label,{exact:true})});
      assert.equal(await card.locator('.stat-value').innerText(),number(stats[field]));
    }
    assert.deepEqual(forbiddenCalls,[]);
    assert(await rolePage.evaluate(()=>document.documentElement.scrollWidth<=innerWidth));
    await roleContext.close(); pass(`${role} real metrics and no administrative requests`);
  }
  assert.deepEqual(errors,[]); console.log(`Completed ${checks} dashboard smoke groups.`);
}
async function cleanup() {
  if(browser) await browser.close();
  // Published fixtures cannot be deleted via workflow API; delete exactly the fixture IDs in PostgreSQL.
  if(documents.length) {
    const ids=documents.map(uuid).join(',');
    sql(`BEGIN; DELETE FROM "DocumentProcesses" WHERE "DocId" IN (${ids}); DELETE FROM "Documents" WHERE "Id" IN (${ids}); COMMIT;`);
  }
  for(const id of certificates) await request('DELETE',`/api/signatures/certificates/${id}`);
  for(const session of sessions.slice(1)) if(session) await request('POST','/api/auth/logout',null,200,session);
  for(const id of users) await request('DELETE',`/api/users/${id}`);
  if(token) await request('POST','/api/auth/logout',null,200,token);
  console.log('Cleanup: owned fixture documents/users/certificate removed; test sessions logged out.');
}
main().catch(async e=>{
  console.error(e.stack); process.exitCode=1;
  if(browser) {
    let index=0; await fs.mkdir(path.join(__dirname,'artifacts'),{recursive:true});
    for(const context of browser.contexts()) for(const page of context.pages())
      await page.screenshot({path:path.join(__dirname,'artifacts',`dashboard-failure-${index++}.png`)});
  }
}).finally(async()=>{try{await cleanup();}catch(e){console.error('Cleanup failed: '+e.message);process.exitCode=1;}});
