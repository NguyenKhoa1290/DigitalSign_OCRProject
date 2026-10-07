// Local Docker integration test. Requires Node 20+, Playwright/Chromium and Docker CLI.
const assert = require('node:assert/strict');
const { execFileSync } = require('node:child_process');
const fs = require('node:fs/promises');
const path = require('node:path');
const { chromium } = require(process.env.HAU_PLAYWRIGHT_MODULE || 'playwright');
const api = 'http://localhost:5000', web = 'http://localhost:5227';
const documents = [], users = [], sessions = [], certificates = [], objects = [], traces = new Set(), responseJobs = [];
let token, browser, checks = 0;
const wait = ms => new Promise(resolve => setTimeout(resolve, ms));
function pass(name) { checks++; console.log('PASS ' + name); }
function environment(container) {
  return Object.fromEntries(JSON.parse(execFileSync('docker', ['inspect', container], {encoding:'utf8'}))[0].Config.Env
    .map(v => {const i=v.indexOf('=');return [v.slice(0,i),v.slice(i+1)];}));
}
const pg = environment('hau_postgres');
function sql(statement) {
  return execFileSync('docker', ['exec','-i','hau_postgres','psql','-X','-v','ON_ERROR_STOP=1',
    '-U',pg.POSTGRES_USER,'-d',pg.POSTGRES_DB,'-t','-A'],
    {input:statement,encoding:'utf8',stdio:['pipe','pipe','pipe']}).trim();
}
function uuid(id) {assert.match(id,/^[a-f0-9-]{36}$/i);return `'${id}'`;}
function trace(value) {if (/^[a-f0-9]{32}$/i.test(value||'')) traces.add(value);}
async function ready() {
  for(let i=0;i<40;i++) {
    try {if ((await fetch(api+'/health')).ok)return;} catch {}
    await wait(500);
  }
  throw new Error('Gateway did not become ready');
}
async function request(method,route,body,expected=200,credential=token) {
  let response;
  for(let attempt=0;attempt<3;attempt++) {
    response=await fetch(api+route,{method,headers:{...(credential?{Authorization:`Bearer ${credential}`}:{ }),
      ...(body && !(body instanceof FormData)?{'Content-Type':'application/json'}:{})},
      body:body?(body instanceof FormData?body:JSON.stringify(body)):undefined});
    trace(response.headers.get('x-trace-id'));
    if(response.status!==429)break;
    console.log('Rate limit: waiting for retry window');
    await wait((Number(response.headers.get('retry-after'))||60)*1000);
  }
  assert.equal(response.status,expected,`${method} ${route}`);
  const headerTrace=response.headers.get('x-trace-id');
  if(expected>=400) {await response.text();return {headerTrace};}
  if(route.startsWith('/api/admin/') || route.startsWith('/api/notifications'))
    assert.equal(response.headers.get('cache-control'),'no-store');
  const json=await response.json();return {data:json.data??json,headerTrace};
}
async function data(...args) {return (await request(...args)).data;}
async function login(username,password) {
  const session=await data('POST','/api/auth/login',{username,password},200,null);
  sessions.push(session.accessToken);return session.accessToken;
}
async function eventually(read,assertion) {
  let last;
  for(let i=0;i<25;i++) {try {const value=await read();assertion(value);return value;}catch(e){last=e;await wait(100);}}
  throw last;
}
function watch(page,errors) {
  page.setDefaultTimeout(20000);page.on('pageerror',e=>errors.push(e.message));
  page.on('response',r=>responseJobs.push(r.allHeaders().then(h=>trace(h['x-trace-id'])).catch(()=>{})));
}
async function select(page,id,label) {
  await page.locator('#'+id).click();await page.getByRole('option',{name:label,exact:true}).click();
}
function blankPdf() {
  const parts=['%PDF-1.4\n'],offsets=[0];
  const bodies=['<< /Type /Catalog /Pages 2 0 R >>','<< /Type /Pages /Kids [3 0 R] /Count 1 >>',
    '<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Contents 4 0 R >>','<< /Length 0 >>\nstream\n\nendstream'];
  bodies.forEach((body,i)=>{offsets.push(Buffer.byteLength(parts.join('')));parts.push(`${i+1} 0 obj\n${body}\nendobj\n`);});
  const xref=Buffer.byteLength(parts.join(''));
  parts.push('xref\n0 5\n0000000000 65535 f \n'+offsets.slice(1).map(n=>`${String(n).padStart(10,'0')} 00000 n \n`).join(''));
  parts.push(`trailer\n<< /Size 5 /Root 1 0 R >>\nstartxref\n${xref}\n%%EOF\n`);return Buffer.from(parts.join(''));
}
async function main() {
  await ready();
  token=await login(process.env.HAU_TEST_USERNAME||'admin',process.env.HAU_TEST_PASSWORD||'Admin@123');
  for(const route of ['/api/admin/system-logs','/api/admin/activity','/api/notifications'])
    await request('GET',route,null,401,null);
  const roles=await data('GET','/api/roles'), accounts={};
  for(const role of ['Clerk','Specialist','Manager','BoardOfDirectors']) {
    const username=`monitor_${role}_${Date.now()}`;
    const user=await data('POST','/api/users',{username,password:'Monitoring@12345',fullName:'Monitoring fixture '+role,
      roleIds:[roles.find(r=>r.roleName===role).id]},201);
    users.push(user.id);accounts[role]={...user,token:await login(username,'Monitoring@12345')};
    for(const route of ['/api/admin/system-logs','/api/admin/activity'])
      await request('GET',route,null,403,accounts[role].token);
  }
  pass('JWT and Admin-only journal APIs; no-store responses');
  const types=await data('GET','/api/documents/types');
  let creationTrace;
  for(let i=0;i<2;i++) {
    const created=await request('POST','/api/documents',{title:'Monitoring fixture',docTypeId:types[0].id},201,accounts.Specialist.token);
    documents.push(created.data.id);creationTrace??=created.headerTrace;
  }
  const doc=documents[0], rejected=documents[1];
  const createdAudit=await data('GET',`/api/admin/activity?traceId=${creationTrace}`);
  assert.equal(createdAudit.items.length,1);assert.equal(createdAudit.items[0].action,'Create');
  assert.equal(createdAudit.items[0].actorId,accounts.Specialist.id);assert.equal(createdAudit.items[0].resourceId,doc);
  pass('Document audit shares HTTP trace, records correct actor and avoids duplicate mutation events');
  // Simulate the authenticated OCR callback, without pretending this is a real OCR/PDF signing test.
  const response=await fetch(`http://localhost:5049/api/documents/${doc}/ocr`,{method:'PATCH',
    headers:{'Content-Type':'application/json','X-Service-Token':environment('hau_document_service').ServiceAuth__OcrServiceToken},
    body:JSON.stringify({ocrDataRaw:'{}'})});
  assert.equal(response.status,200);await response.text();
  let notices=await data('GET','/api/notifications',null,200,accounts.Specialist.token);
  assert(notices.items.some(n=>n.kind==='OcrCompleted' && n.targetUrl===`/documents/${doc}`));
  const docAudit=()=>data('GET',`/api/admin/activity?service=DocumentService&pageSize=100`);
  assert((await docAudit()).items.some(e=>e.action==='UpdateOCR' && e.resourceId===doc));
  pass('Direct service OCR callback persists audit and creator notification through the database trigger');
  const assign=()=>data('POST',`/api/documents/${doc}/assign`,{toUserId:accounts.Clerk.id,comment:'private-body-sentinel'},200,accounts.Manager.token);
  await assign();
  let clerk=await data('GET','/api/notifications',null,200,accounts.Clerk.token);
  assert.equal(clerk.totalCount,1);assert.equal(clerk.items[0].kind,'Assignment');
  await data('POST',`/api/documents/${doc}/submit`,{comment:'private-body-sentinel'},200,accounts.Specialist.token);
  assert((await data('GET','/api/notifications',null,200,accounts.Manager.token)).items.some(n=>n.kind==='ReviewRequested'));
  await data('POST',`/api/documents/${doc}/dept-sign`,{},200,accounts.Manager.token);
  await data('POST',`/api/documents/${doc}/submit-director`,{},200,accounts.Manager.token);
  assert((await data('GET','/api/notifications',null,200,accounts.BoardOfDirectors.token)).items.some(n=>n.kind==='ReviewRequested'));
  await data('POST',`/api/documents/${doc}/director-sign`,{},200,accounts.BoardOfDirectors.token);
  await data('POST',`/api/documents/${doc}/publish`,null,200,accounts.Clerk.token);
  await data('POST',`/api/documents/${rejected}/submit`,{},200,accounts.Specialist.token);
  await data('POST',`/api/documents/${rejected}/reject`,{reason:'private-body-sentinel'},200,accounts.Manager.token);
  notices=await data('GET','/api/notifications',null,200,accounts.Specialist.token);
  for(const kind of ['OcrCompleted','Signed','Published','Rejected'])assert(notices.items.some(n=>n.kind===kind));
  assert.equal(notices.items.filter(n=>n.kind==='Signed').length,2);
  clerk=await data('GET','/api/notifications',null,200,accounts.Clerk.token);
  assert(clerk.items.some(n=>n.kind==='Published'));
  pass('Workflow metadata transitions notify requester, reviewer roles and assignees');
  for(let i=0;i<21;i++)await assign();
  clerk=await data('GET','/api/notifications',null,200,accounts.Clerk.token);
  const second=await data('GET','/api/notifications?page=2',null,200,accounts.Clerk.token);
  assert.equal(clerk.items.length,20);assert.equal(second.items.length,3);assert.equal(clerk.totalCount,23);
  assert.equal(new Set([...clerk.items,...second.items].map(n=>n.id)).size,23);
  const first=clerk.items[0];
  await request('PATCH',`/api/notifications/${first.id}/read`,null,404,accounts.Specialist.token);
  const spoofed=await data('GET',`/api/notifications?userId=${accounts.Clerk.id}`,null,200,accounts.Specialist.token);
  assert.deepEqual(spoofed.items,notices.items);
  await data('PATCH',`/api/notifications/${first.id}/read`,null,200,accounts.Clerk.token);
  const readAt=(await data('GET','/api/notifications',null,200,accounts.Clerk.token)).items.find(n=>n.id===first.id).readAt;
  await data('PATCH',`/api/notifications/${first.id}/read`,null,200,accounts.Clerk.token);
  assert.equal((await data('GET','/api/notifications',null,200,accounts.Clerk.token)).items.find(n=>n.id===first.id).readAt,readAt);
  assert.equal((await data('GET','/api/notifications?unreadOnly=true',null,200,accounts.Clerk.token)).totalCount,22);
  const specialistUnread=notices.unreadCount;
  await data('POST','/api/notifications/read-all',null,200,accounts.Clerk.token);
  assert.equal((await data('GET','/api/notifications',null,200,accounts.Clerk.token)).unreadCount,0);
  assert.equal((await data('GET','/api/notifications',null,200,accounts.Specialist.token)).unreadCount,specialistUnread);
  pass('Server pagination, own-user isolation, idempotent read and read-all ownership');
  certificates.push(accounts.Manager.id);
  const issued=await request('POST','/api/signatures/certificates/issue',{userId:accounts.Manager.id,username:accounts.Manager.username,
    fullName:accounts.Manager.fullName,certificateType:'Personal',validityDays:1});
  const manager=await data('GET','/api/notifications?kind=CertificateExpiring',null,200,accounts.Manager.token);
  assert.equal(manager.totalCount,1);assert.equal(manager.items[0].targetUrl,'/certificates/me');
  await data('PATCH',`/api/notifications/${manager.items[0].id}/read`,null,200,accounts.Manager.token);
  const repeated=await data('GET','/api/notifications?kind=CertificateExpiring',null,200,accounts.Manager.token);
  assert.equal(repeated.totalCount,1);assert(repeated.items[0].readAt);
  const adminUser=await data('POST','/api/users',{username:`monitor_Admin_${Date.now()}`,password:'Monitoring@12345',
    fullName:'Monitoring fixture Admin',roleIds:[roles.find(r=>r.roleName==='Admin').id]},201);
  users.push(adminUser.id);const adminCredential=await login(adminUser.username,'Monitoring@12345');
  certificates.push(adminUser.id);
  await data('POST','/api/signatures/certificates/issue',{userId:adminUser.id,username:adminUser.username,
    fullName:adminUser.fullName,certificateType:'Personal',validityDays:1});
  const adminAlert=await data('GET','/api/notifications?kind=CertificateExpiring',null,200,adminCredential);
  assert.equal(adminAlert.totalCount,1);assert.equal(adminAlert.items[0].targetUrl,'/admin/certificates');
  pass('Certificate expiry alert links to the real page and is deduplicated across polls');
  await data('GET','/api/users/me');await data('GET','/api/signatures/certificates');await data('GET','/api/ocr/health');
  const invalidOcrUpload=await request('POST','/api/ocr/process-upload',{},422);
  await eventually(()=>data('GET',`/api/admin/activity?traceId=${invalidOcrUpload.headerTrace}`),p=>{
    assert.equal(p.items[0].action,'ProcessOCR');assert.equal(p.items[0].path,'/api/ocr/process-upload');assert.equal(p.items[0].level,'Warning');
  });
  await request('GET','/api/users/private-query-sentinel?token=private-query-sentinel',null,404);
  const failedSubmit=await request('POST',`/api/documents/${doc}/submit`,{},422,accounts.Specialist.token);
  await eventually(()=>data('GET',`/api/admin/activity?traceId=${failedSubmit.headerTrace}`),p=>{
    assert.equal(p.items[0].action,'Submit');assert.equal(p.items[0].level,'Warning');assert.equal(p.items[0].statusCode,422);
  });
  for(const service of ['IdentityService','DocumentService','SignService','OCRService','ApiGateway']) {
    const logs=await eventually(()=>data('GET',`/api/admin/system-logs?service=${service}`),p=>assert(p.totalCount>0));
    assert(logs.items.every(e=>e.service===service));
  }
  const redacted=await eventually(()=>data('GET','/api/admin/system-logs?service=IdentityService&level=Warning'),
    p=>assert(p.items.some(e=>e.path==='/api/[unmapped]')));
  assert(redacted.items.every(e=>e.level==='Warning'));
  const byActor=await data('GET',`/api/admin/activity?actorId=${accounts.Manager.id}&pageSize=100`);
  assert(byActor.items.every(e=>e.actorId===accounts.Manager.id));assert(byActor.items.some(e=>e.action==='Assign'));
  const issuedAudit=await data('GET',`/api/admin/activity?traceId=${issued.headerTrace}`);
  assert.equal(issuedAudit.items[0].action,'IssueCertificate');
  assert.equal(issuedAudit.items[0].actorId,(await data('GET','/api/users/me')).id);
  const paged=await data('GET',`/api/admin/activity?actorId=${accounts.Manager.id}&pageSize=5&page=2`);
  assert.equal(paged.items.length,5);assert.equal(paged.page,2);assert(paged.totalCount>20);
  const interval=await data('GET',`/api/admin/activity?traceId=${creationTrace}&from=${encodeURIComponent(new Date(Date.now()-3600000).toISOString())}&to=${encodeURIComponent(new Date(Date.now()+3600000).toISOString())}`);
  assert.equal(interval.totalCount,1);
  for(const suffix of ['service=Invalid','level=Invalid','actorId=bad','from=2026-10-06T00:00:00Z&to=2026-10-05T00:00:00Z'])
    await request('GET','/api/admin/activity?'+suffix,null,400);
  await request('GET','/api/notifications?kind=Invalid',null,400);
  const persisted=sql(`SELECT coalesce(json_agg(e)::text,'[]') FROM "MonitoringEvents" e WHERE "ResourceId" IN (${documents.map(uuid).join(',')}) OR "ActorId" IN (${users.map(uuid).join(',')});`);
  for(const secret of ['private-body-sentinel','private-query-sentinel','Monitoring@12345',accounts.Specialist.token])
    assert(!persisted.includes(secret));
  pass('All five sources, filters, pagination, invalid queries and diagnostic data redaction');
  const savedAudit=createdAudit.items[0].id;
  execFileSync('docker',['compose','up','-d','--no-deps','--force-recreate','api-gateway'],{stdio:'pipe'});
  await ready();
  assert.equal((await data('GET',`/api/admin/activity?traceId=${creationTrace}`)).items[0].id,savedAudit);
  const retained=await data('GET','/api/notifications',null,200,accounts.Clerk.token);
  assert.equal(retained.totalCount,23);assert.equal(retained.unreadCount,0);
  assert.equal(retained.items.find(n=>n.id===first.id).readAt,readAt);
  assert.equal((await data('GET','/api/notifications?kind=CertificateExpiring',null,200,accounts.Manager.token)).totalCount,1);
  pass('Audit and notification IDs/read states persist after Gateway recreation; initializer does not duplicate history');
  await assign();
  browser=await chromium.launch({headless:true});const errors=[];
  const context=await browser.newContext({viewport:{width:1366,height:768}});
  const page=await context.newPage();watch(page,errors);
  await page.goto(web+'/login');await page.locator('#username').fill(process.env.HAU_TEST_USERNAME||'admin');
  await page.locator('#password').fill(process.env.HAU_TEST_PASSWORD||'Admin@123');
  await page.locator('button[type=submit]').click();await page.waitForURL(web+'/');
  sessions.push(await page.evaluate(()=>localStorage.getItem('auth_token')));
  await fs.mkdir(path.join(__dirname,'artifacts'),{recursive:true});
  for(const route of ['/admin/system-logs','/admin/activity']) {
    await page.goto(web+route);await page.locator('.journal-entry').first().waitFor();
    await select(page,'journal-service','DocumentService');
    await page.locator('#journal-trace').fill(creationTrace);
    await page.getByRole('button',{name:'Lọc / Làm mới',exact:true}).click();
    await eventually(()=>page.locator('.journal-entry').count(),n=>assert.equal(n,1));
    assert((await page.locator('.journal-entry').innerText()).includes(creationTrace));
    await page.screenshot({path:path.join(__dirname,'artifacts',path.basename(route)+'-1366.png')});
    await page.setViewportSize({width:450,height:500});
    assert(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth));
    await page.screenshot({path:path.join(__dirname,'artifacts',path.basename(route)+'-450.png')});
    await page.locator('.journal-entry').scrollIntoViewIfNeeded();
    await page.screenshot({path:path.join(__dirname,'artifacts',path.basename(route)+'-450-event.png')});
    await page.setViewportSize({width:1366,height:768});
  }
  await page.locator('#journal-trace').fill('');await page.locator('#journal-actor').fill(accounts.Manager.id);
  await page.getByRole('button',{name:'Lọc / Làm mới',exact:true}).click();
  await page.getByRole('button',{name:'Trang sau',exact:true}).click();
  await eventually(()=>page.locator('.journal-count').innerText(),v=>assert(v.includes('Trang 2')));
  await page.route('**/api/admin/activity?*',r=>r.fulfill({status:503,contentType:'application/json',body:'{}'}));
  await page.getByRole('button',{name:'Lọc / Làm mới',exact:true}).click();await page.getByRole('alert').waitFor();
  await page.unroute('**/api/admin/activity?*');
  pass('Admin journal UI loads live events, trace/service filters, pagination, error state and mobile layout');
  const personal=await browser.newContext({viewport:{width:450,height:500}});
  await personal.addInitScript(auth=>localStorage.setItem('auth_token',auth),accounts.Clerk.token);
  const ownPage=await personal.newPage();watch(ownPage,errors);
  await ownPage.goto(web+'/notifications');await ownPage.locator('.notification-entry').first().waitFor();
  assert(await ownPage.evaluate(()=>document.documentElement.scrollWidth<=innerWidth));
  await ownPage.screenshot({path:path.join(__dirname,'artifacts','notifications-450.png')});
  await ownPage.locator('.notification-entry').first().scrollIntoViewIfNeeded();
  await ownPage.screenshot({path:path.join(__dirname,'artifacts','notifications-450-event.png')});
  await ownPage.setViewportSize({width:1366,height:768});
  await ownPage.screenshot({path:path.join(__dirname,'artifacts','notifications-1366.png')});
  await select(ownPage,'notification-kind','Công văn được giao');await ownPage.locator('#notification-unread').check();
  await eventually(()=>ownPage.locator('.notification-entry').count(),n=>assert.equal(n,1));
  const card=ownPage.locator('.notification-entry').first(), newId=await card.getAttribute('data-notification-id');
  assert.equal(await card.getByRole('link',{name:'Xem chi tiết'}).getAttribute('href'),`/documents/${doc}`);
  await ownPage.route('**/api/notifications/*/read',r=>r.fulfill({status:503,contentType:'application/json',body:'{}'}));
  await card.getByRole('button',{name:'Đánh dấu đã đọc',exact:true}).click();await ownPage.getByRole('alert').waitFor();
  assert(await ownPage.locator(`[data-notification-id="${newId}"]`).getAttribute('class').then(c=>c.includes('notification-new')));
  await ownPage.unroute('**/api/notifications/*/read');
  await card.getByRole('button',{name:'Đánh dấu đã đọc',exact:true}).click();
  await ownPage.getByText('Không có thông báo phù hợp',{exact:true}).waitFor();
  await ownPage.route('**/api/notifications?*',r=>r.fulfill({status:503,contentType:'application/json',body:'{}'}));
  await ownPage.getByRole('button',{name:'Làm mới thông báo',exact:true}).click();await ownPage.getByRole('alert').waitFor();
  await ownPage.unroute('**/api/notifications?*');
  await ownPage.getByRole('button',{name:'Làm mới thông báo',exact:true}).click();
  await ownPage.getByRole('alert').waitFor({state:'hidden'});
  pass('Own notification UI filters, detail links, read/error handling and desktop/mobile layouts');
  await assign();
  await ownPage.locator('.notification-new').waitFor({timeout:40000});
  pass('Open notification page automatically renders new notifications within the 30-second polling cycle');
  let delay=true;
  await ownPage.route('**/api/notifications?*',async r=>{if(delay){delay=false;await wait(800);}await r.continue();});
  const inFlight=ownPage.waitForRequest(r=>r.url().includes('/api/notifications?'));
  await ownPage.getByRole('button',{name:'Làm mới thông báo',exact:true}).click();await inFlight;
  await select(ownPage,'notification-kind','Công văn phát hành');
  await ownPage.getByText('Không có thông báo phù hợp',{exact:true}).waitFor();
  assert((await ownPage.locator('.notification-summary').innerText()).includes('0 thông báo phù hợp'));
  await ownPage.unroute('**/api/notifications?*');await select(ownPage,'notification-kind','Công văn được giao');
  await ownPage.locator('.notification-new').waitFor();
  pass('Filter changes during an in-flight refresh display the latest filter instead of stale results');
  await ownPage.getByRole('button',{name:'Đánh dấu tất cả đã đọc',exact:true}).click();
  await ownPage.getByText('Không có thông báo phù hợp',{exact:true}).waitFor();
  await ownPage.getByRole('link',{name:'Quay lại Dashboard',exact:true}).click();await ownPage.waitForURL(web+'/');
  await personal.close();await context.close();assert.deepEqual(errors,[]);
  pass('Read-all refreshes UI and navigation disposes the polling page without JavaScript errors');
  const uploadDoc=await data('POST','/api/documents',{title:'Monitoring upload fixture',docTypeId:types[0].id},201);
  documents.push(uploadDoc.id);const form=new FormData();
  form.append('file',new Blob([blankPdf()],{type:'application/pdf'}),'monitoring.pdf');
  const upload=await request('POST',`/api/documents/${uploadDoc.id}/upload`,form);
  objects.push(upload.data.minioPath);
  await eventually(()=>data('GET',`/api/admin/activity?traceId=${upload.headerTrace}`),p=>{
    assert.equal(p.items.length,1);assert.equal(p.items[0].action,'UploadDocument');assert.equal(p.items[0].resourceId,uploadDoc.id);
  });
  // Let the blank PDF's asynchronous callback finish before removing this fixture.
  await eventually(()=>data('GET',`/api/documents/${uploadDoc.id}`),d=>assert(d.ocrDataRaw));
  pass('Successful PDF upload records a business audit even though upload creates no DocumentProcess');
  console.log(`Completed ${checks} monitoring/notification smoke groups.`);
}
async function cleanup() {
  if(browser)await browser.close();await Promise.allSettled(responseJobs);
  if(documents.length)sql(`BEGIN;DELETE FROM "DocumentProcesses" WHERE "DocId" IN (${documents.map(uuid).join(',')});
    DELETE FROM "Documents" WHERE "Id" IN (${documents.map(uuid).join(',')});COMMIT;`);
  for(const id of certificates)await data('DELETE',`/api/signatures/certificates/${id}`);
  for(const session of sessions.slice(1))if(session)await data('POST','/api/auth/logout',null,200,session);
  for(const id of users)await data('DELETE',`/api/users/${id}`);
  if(objects.length) {
    const inspected=JSON.parse(execFileSync('docker',['inspect','hau_minio'],{encoding:'utf8'}))[0],env=environment('hau_minio');
    for(const object of objects) {
      assert.match(object,/^documents\/[a-f0-9-]+\.pdf$/i);
      execFileSync('docker',['run','--rm','--network',Object.keys(inspected.NetworkSettings.Networks)[0],
        '-e','MINIO_ROOT_USER','-e','MINIO_ROOT_PASSWORD','--entrypoint','/bin/sh','quay.io/minio/mc:latest','-c',
        'mc alias set local http://minio:9000 "$MINIO_ROOT_USER" "$MINIO_ROOT_PASSWORD" >/dev/null && mc rm "local/$1"','cleanup',object],
        {env:{...process.env,MINIO_ROOT_USER:env.MINIO_ROOT_USER,MINIO_ROOT_PASSWORD:env.MINIO_ROOT_PASSWORD},stdio:'pipe'});
    }
  }
  if(token)await data('POST','/api/auth/logout',null,200,token);
  // Only this run's document IDs, user IDs and validated observed trace IDs are removed.
  const clauses=[];
  if(documents.length)clauses.push(`"ResourceId" IN (${documents.map(uuid).join(',')})`);
  if(users.length)clauses.push(`"ActorId" IN (${users.map(uuid).join(',')})`);
  if(traces.size)clauses.push(`"TraceId" IN (${[...traces].map(t=>`'${t}'`).join(',')})`);
  if(clauses.length)sql(`DELETE FROM "MonitoringEvents" WHERE ${clauses.join(' OR ')};`);
  console.log('Cleanup: this run\'s fixtures, PDF object, certificate, sessions and journal events removed.');
}
main().catch(async e=>{
  console.error(e.stack);process.exitCode=1;
  if(browser) {let i=0;await fs.mkdir(path.join(__dirname,'artifacts'),{recursive:true});
    for(const context of browser.contexts())for(const page of context.pages())
      await page.screenshot({path:path.join(__dirname,'artifacts',`monitoring-failure-${i++}.png`)});}
}).finally(async()=>{try {await cleanup();}catch(e){console.error('Cleanup failed: '+e.message);process.exitCode=1;}});
