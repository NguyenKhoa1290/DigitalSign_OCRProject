// Local Docker smoke test. Requires Node 20+ and Playwright with Chromium.
// HAU_PLAYWRIGHT_MODULE may point to an external Playwright installation.
const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const path = require('node:path');
const { execFileSync } = require('node:child_process');
const { randomUUID } = require('node:crypto');
const { chromium } = require(process.env.HAU_PLAYWRIGHT_MODULE || 'playwright');
const api = 'http://localhost:5000';
const web = 'http://localhost:5227';
const documents = [], users = [], objects = [], sessions = [];
let token, browser, specialistToken, specialistUser, checks = 0;
function pass(name) { checks++; console.log(`PASS ${name}`); }
async function request(method, route, body, expected = 200, credential = token) {
  const options = {
    method, headers: { ...(credential ? { Authorization: `Bearer ${credential}` } : {}),
      ...(body && !(body instanceof FormData) ? { 'Content-Type': 'application/json' } : {}) },
    body: body ? (body instanceof FormData ? body : JSON.stringify(body)) : undefined
  };
  let response;
  for (let attempt = 0; attempt < 3; attempt++) {
    response = await fetch(api + route, options);
    if (response.status !== 429) break;
    const retry = Number(response.headers.get('retry-after')) || 60;
    console.log(`Gateway rate limit: retry in ${retry}s.`);
    await new Promise(resolve => setTimeout(resolve, retry * 1000));
  }
  assert.equal(response.status, expected, `${method} ${route}: expected ${expected}, got ${response.status}`);
  if (expected >= 400 || expected === 204) return response;
  const json = await response.json();
  return json.data ?? json;
}
function blankPdf() {
  const parts = ['%PDF-1.4\n'];
  const offsets = [0];
  const bodies = ['<< /Type /Catalog /Pages 2 0 R >>', '<< /Type /Pages /Kids [3 0 R] /Count 1 >>',
    '<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Contents 4 0 R >>', '<< /Length 0 >>\nstream\n\nendstream'];
  bodies.forEach((body, i) => { offsets.push(Buffer.byteLength(parts.join(''))); parts.push(`${i+1} 0 obj\n${body}\nendobj\n`); });
  const xref = Buffer.byteLength(parts.join(''));
  parts.push('xref\n0 5\n0000000000 65535 f \n' + offsets.slice(1).map(n => `${String(n).padStart(10, '0')} 00000 n \n`).join(''));
  parts.push(`trailer\n<< /Size 5 /Root 1 0 R >>\nstartxref\n${xref}\n%%EOF\n`);
  return Buffer.from(parts.join(''));
}
async function main() {
  const login = await request('POST', '/api/auth/login', { username: process.env.HAU_TEST_USERNAME || 'admin', password: process.env.HAU_TEST_PASSWORD || 'Admin@123' }, 200, null);
  token = login.accessToken; sessions.push(token);
  // Recover only artifacts from a known failed local run, if normal cleanup was interrupted.
  if (process.env.HAU_CLEANUP_DOCUMENT) {
    const id = process.env.HAU_CLEANUP_DOCUMENT;
    assert.match(id, /^[a-f0-9-]{36}$/i);
    const doc = await request('GET', `/api/documents/${id}`);
    assert.equal(doc.title, 'Smoke document integration');
    documents.push(id); if (doc.minioPath) objects.push(doc.minioPath);
    const found = await request('GET', '/api/users?pageSize=100&search=smoke_');
    for (const user of found.items) {
      const match = /^smoke_(Manager|Specialist)_(\d+)$/.exec(user.username);
      if (match && Date.now() - Number(match[2]) < 15 * 60000) users.push(user.id);
    }
    return;
  }
  const me = await request('GET', '/api/users/me');
  let types;
  for (let attempt = 0; attempt < 30; attempt++) {
    const ready = await fetch(api + '/api/documents/types', { headers: { Authorization: `Bearer ${token}` } });
    if (ready.status === 200) { types = (await ready.json()).data; break; }
    assert([502, 503].includes(ready.status), `Unexpected readiness status ${ready.status}`);
    await new Promise(resolve => setTimeout(resolve, 1000));
  }
  assert(types?.length, 'DocumentService is not ready or has no document types');
  const metadata = { title: 'Smoke document integration', docNumber: 'SMOKE-045', docTypeId: types[0].id, issuedDate: '2026-10-05' };
  let doc = await request('POST', '/api/documents', metadata, 201); documents.push(doc.id);
  const route = `/api/documents/${doc.id}`;
  await request('GET', `${route}/file`, null, 404);
  await request('GET', `${route}/file`, null, 401, null);
  await request('GET', `/api/documents/${randomUUID()}/file`, null, 404);
  pass('PDF authentication and missing-file responses');
  const pdf = blankPdf(), form = new FormData();
  form.append('file', new Blob([pdf], { type: 'application/pdf' }), 'smoke.pdf');
  doc = await request('POST', `${route}/upload`, form); objects.push(doc.minioPath);
  const file = await fetch(api + `${route}/file`, { headers: { Authorization: `Bearer ${token}` } });
  assert.equal(file.status, 200); assert.equal(file.headers.get('content-type'), 'application/pdf');
  assert.equal(file.headers.get('cache-control'), 'no-store');
  assert.deepEqual(Buffer.from(await file.arrayBuffer()), pdf);
  const download = await fetch(api + `${route}/file?download=true`, { headers: { Authorization: `Bearer ${token}` } });
  assert.match(download.headers.get('content-disposition'), /attachment;.*\.pdf/);
  const range = await fetch(api + `${route}/file`, { headers: { Authorization: `Bearer ${token}`, Range: 'bytes=0-9' } });
  assert.equal(range.status, 206); assert.deepEqual(Buffer.from(await range.arrayBuffer()), pdf.subarray(0, 10));
  pass('Stored PDF bytes, attachment and HTTP range');
  // Wait for the asynchronous OCR callback before editing metadata.
  for (let i = 0; i < 30; i++) {
    doc = await request('GET', route);
    if (doc.ocrDataRaw) break;
    await new Promise(resolve => setTimeout(resolve, 1000));
  }
  const storedPath = doc.minioPath, storedOcr = doc.ocrDataRaw;
  doc = await request('PUT', route, { ...metadata, title: '  Edited metadata  ', docNumber: ' NEW-045 ' });
  assert.equal(doc.title, 'Edited metadata'); assert.equal(doc.docNumber, 'NEW-045');
  assert.equal(doc.status, 'Draft'); assert.equal(doc.minioPath, storedPath); assert.equal(doc.ocrDataRaw, storedOcr);
  assert(doc.processes.some(p => p.action === 'Update' && p.fromUserId === me.id));
  await request('PUT', route, { ...metadata, title: '  ' }, 400);
  await request('PUT', route, { ...metadata, docTypeId: randomUUID() }, 400);
  doc = await request('PUT', route, { ...metadata, docNumber: null, issuedDate: null });
  assert.equal(doc.docNumber, null); assert.equal(doc.issuedDate, null);
  pass('Draft metadata edit, history, validation and nullable fields');
  const directory = await request('GET', '/api/users/assignees?search=admin');
  assert(directory.some(u => u.id === me.id)); assert(directory.every(u => !('email' in u) && !('roles' in u)));
  await request('POST', `${route}/assign`, { assignedToId: me.id }, 400);
  await request('POST', `${route}/assign`, { toUserId: '00000000-0000-0000-0000-000000000000' }, 400);
  await request('POST', `${route}/assign`, { toUserId: randomUUID() }, 400);
  doc = await request('POST', `${route}/assign`, { toUserId: me.id, comment: 'API assignment smoke' });
  assert.equal(doc.status, 'Draft');
  assert(doc.processes.some(p => p.action === 'Assign' && p.fromUserId === me.id && p.toUserId === me.id && p.comment === 'API assignment smoke'));
  pass('Assignment recipient, actor, history and invalid payloads');
  const roles = await request('GET', '/api/roles');
  for (const roleName of ['Manager', 'Specialist']) {
    const username = `smoke_${roleName}_${Date.now()}`;
    const user = await request('POST', '/api/users', { username, password: 'Smoke@12345', fullName: 'Smoke Integration User', roleIds: [roles.find(r => r.roleName === roleName).id] }, 201);
    users.push(user.id);
    const session = await request('POST', '/api/auth/login', { username, password: 'Smoke@12345' }, 200, null); sessions.push(session.accessToken);
    if (roleName === 'Manager') {
      await request('PUT', route, metadata, 403, session.accessToken);
      await request('GET', '/api/users/assignees?search=admin', null, 200, session.accessToken);
      await request('POST', `${route}/assign`, { toUserId: me.id }, 200, session.accessToken);
    } else {
      specialistToken = session.accessToken; specialistUser = user;
      await request('POST', `${route}/assign`, { toUserId: me.id }, 403, session.accessToken);
    }
    await request('PUT', `/api/users/${user.id}`, { fullName: user.fullName, isActive: false });
    await request('POST', `${route}/assign`, { toUserId: user.id }, 400);
  }
  await request('PUT', `/api/users/${specialistUser.id}`, { fullName: specialistUser.fullName, isActive: true });
  pass('Role permissions and inactive recipients');
  browser = await chromium.launch({ headless: true });
  const context = await browser.newContext({ viewport: { width: 1366, height: 768 }, acceptDownloads: true });
  const page = await context.newPage(), errors = [];
  page.on('pageerror', e => errors.push(e.message));
  page.setDefaultTimeout(20000);
  await page.goto(web + '/login');
  await page.locator('#username').fill(process.env.HAU_TEST_USERNAME || 'admin');
  await page.locator('#password').fill(process.env.HAU_TEST_PASSWORD || 'Admin@123');
  await page.locator('button[type=submit]').click();
  await page.waitForURL(web + '/');
  const browserToken = await page.evaluate(() => localStorage.getItem('auth_token'));
  assert(browserToken); sessions.push(browserToken);
  pass('Browser login through Gateway');
  await page.goto(web + `/documents/${doc.id}`);
  await page.getByRole('button', { name: 'Sửa thông tin', exact: true }).click();
  await page.locator('#edit-title').fill('UI edited document');
  await page.locator('#edit-number').fill('UI-045');
  await page.locator('#edit-date').fill('2026-10-05');
  await page.locator('#edit-date').press('Tab');
  const editResponse = page.waitForResponse(r => r.request().method() === 'PUT' && r.url().endsWith(route));
  await page.getByRole('button', { name: 'Lưu thay đổi', exact: true }).click();
  const edited = await editResponse; assert.equal(edited.status(), 200);
  assert.equal(edited.request().postDataJSON().DocNumber, 'UI-045');
  await page.getByRole('heading', { name: 'UI edited document', exact: true }).waitFor();
  assert.match(await page.locator('iframe.pdf-frame').getAttribute('src'), /^blob:/);
  const downloadEvent = page.waitForEvent('download');
  await page.getByRole('button', { name: 'Tải PDF', exact: true }).click();
  const browserDownload = await downloadEvent;
  assert.match(browserDownload.suggestedFilename(), /\.pdf$/);
  assert.deepEqual(await fs.readFile(await browserDownload.path()), pdf);
  pass('Desktop edit payload, PDF viewer and browser download');
  for (const viewport of [{ width: 1366, height: 768 }, { width: 450, height: 500 }]) {
    await page.setViewportSize(viewport);
    await page.getByRole('button', { name: 'Phân công xử lý', exact: true }).click();
    await page.locator('#assignee-search').fill('admin');
    await page.getByRole('button', { name: 'Tìm', exact: true }).click();
    await page.locator('#assignee-select:not([disabled])').click();
    await page.getByRole('option').filter({ hasText: new RegExp(`\\(${me.username}\\)`, 'i') }).click();
    await page.locator('#assign-comment').fill(`UI assignment ${viewport.width}`);
    await page.locator('#assign-comment').press('Tab');
    const dialog = await page.getByRole('dialog').boundingBox();
    assert(dialog.x >= 0 && dialog.x + dialog.width <= viewport.width + 1);
    await fs.mkdir(path.join(__dirname, 'artifacts'), { recursive: true });
    await page.screenshot({ path: path.join(__dirname, 'artifacts', `document-assignment-${viewport.width}.png`) });
    if (viewport.width === 1366) {
      await page.route('**' + route + '/assign', handler => handler.fulfill({ status: 503, contentType: 'application/json', body: JSON.stringify({ success: false, message: 'Smoke unavailable' }) }));
      await page.getByRole('button', { name: 'Xác nhận phân công', exact: true }).click();
      await page.getByRole('alert').filter({ hasText: 'Phân công thất bại' }).waitFor();
      assert.equal(await page.getByRole('dialog').count(), 1);
      await page.unroute('**' + route + '/assign');
      pass('Assignment failure keeps modal open and displays error');
    }
    const assignmentResponse = page.waitForResponse(r => r.request().method() === 'POST' && r.url().endsWith(route + '/assign'));
    await page.getByRole('button', { name: 'Xác nhận phân công', exact: true }).click();
    const assigned = await assignmentResponse;
    assert.equal(assigned.status(), 200); assert.equal(assigned.request().postDataJSON().toUserId, me.id);
    await page.getByRole('dialog').waitFor({ state: 'hidden' });
    pass(`UI assignment and modal ${viewport.width}x${viewport.height}`);
  }
  await page.getByRole('button', { name: 'Sửa thông tin', exact: true }).click();
  await page.locator('#edit-type').click();
  await page.getByRole('option').first().click();
  await page.screenshot({ path: path.join(__dirname, 'artifacts', 'document-edit-450.png') });
  await page.getByRole('button', { name: 'Hủy', exact: true }).click();
  assert.deepEqual(errors, []);
  pass('Mobile edit modal and no browser runtime errors');
  await request('POST', `${route}/submit`, { comment: 'Smoke state validation' });
  await request('PUT', route, metadata, 422);
  await page.reload();
  await page.getByRole('button', { name: 'Phân công xử lý', exact: true }).waitFor();
  assert.equal(await page.getByRole('button', { name: 'Sửa thông tin', exact: true }).count(), 0);
  await request('POST', `${route}/reject`, { reason: 'Smoke cleanup' });
  doc = await request('PUT', route, { ...metadata, title: 'Edit rejected document' });
  assert.equal(doc.status, 'Rejected');
  pass('Pending edit blocked in API/UI and Rejected edit allowed');
  const creatorContext = await browser.newContext({ viewport: { width: 1366, height: 768 } });
  await creatorContext.addInitScript(auth => localStorage.setItem('auth_token', auth), specialistToken);
  const creator = await creatorContext.newPage();
  await creator.goto(web + '/documents/create');
  await creator.getByPlaceholder('Nhập tiêu đề công văn...').fill('UI create integration');
  await creator.getByPlaceholder('VD: 01/2024/QĐ-HAU').fill('UI-CREATE-045');
  await creator.locator('input[type=date]').fill('2026-10-05');
  await creator.locator('#document-type-select').click();
  await creator.getByRole('option', { name: types[0].typeName, exact: true }).click();
  await creator.locator('#fileInput').setInputFiles({ name: 'ui-create.pdf', mimeType: 'application/pdf', buffer: pdf });
  const creationResponse = creator.waitForResponse(r => r.request().method() === 'POST' && r.url().endsWith('/api/documents'));
  const uploadResponse = creator.waitForResponse(r => r.request().method() === 'POST' && /\/api\/documents\/[^/]+\/upload$/.test(r.url()));
  await creator.locator('button[type=submit]').click();
  const creation = await creationResponse;
  assert.equal(creation.status(), 201);
  const created = (await creation.json()).data; documents.push(created.id);
  assert.equal(created.docNumber, 'UI-CREATE-045'); assert.equal(created.docTypeId, types[0].id); assert.equal(created.issuedDate, '2026-10-05');
  const uploadedResponse = await uploadResponse; assert.equal(uploadedResponse.status(), 200);
  const uploaded = (await uploadedResponse.json()).data; objects.push(uploaded.minioPath);
  await creator.locator('iframe.pdf-frame').waitFor();
  assert.match(await creator.locator('iframe.pdf-frame').getAttribute('src'), /^blob:/);
  pass('Specialist UI creation DTO, multipart upload response and PDF viewer');
  console.log(`Completed ${checks} smoke groups.`);
}
async function cleanup() {
  if (browser) await browser.close();
  for (const id of documents) {
    const doc = await request('GET', `/api/documents/${id}`);
    if (['PendingDeptReview', 'DeptSigned', 'PendingDirectorSign'].includes(doc.status))
      await request('POST', `/api/documents/${id}/reject`, { reason: 'Smoke cleanup' });
    await request('DELETE', `/api/documents/${id}`);
  }
  // Logout validates the user, so revoke user sessions before deleting accounts.
  for (const session of sessions.slice(1)) await request('POST', '/api/auth/logout', null, 200, session);
  for (const id of users) await request('DELETE', `/api/users/${id}`);
  if (objects.length) {
    const inspected = JSON.parse(execFileSync('docker', ['inspect', 'hau_minio'], { encoding: 'utf8' }))[0];
    const env = Object.fromEntries(inspected.Config.Env.map(v => { const n = v.indexOf('='); return [v.slice(0, n), v.slice(n + 1)]; }));
    for (const object of objects) {
      assert.match(object, /^documents\/[a-f0-9-]+\.pdf$/i);
      execFileSync('docker', ['run', '--rm', '--network', Object.keys(inspected.NetworkSettings.Networks)[0],
        '-e', 'MINIO_ROOT_USER', '-e', 'MINIO_ROOT_PASSWORD', '--entrypoint', '/bin/sh', 'quay.io/minio/mc:latest', '-c',
        'mc alias set local http://minio:9000 "$MINIO_ROOT_USER" "$MINIO_ROOT_PASSWORD" >/dev/null && mc rm "local/$1"', 'cleanup', object],
        { env: { ...process.env, MINIO_ROOT_USER: env.MINIO_ROOT_USER, MINIO_ROOT_PASSWORD: env.MINIO_ROOT_PASSWORD }, stdio: 'pipe' });
    }
  }
  // Revoke temporary sessions last, after administrative cleanup.
  if (token) await request('POST', '/api/auth/logout', null, 200, token);
  console.log('Cleanup: temporary documents, users, PDF objects and sessions removed.');
}
main().catch(async e => {
  console.error(e.stack); process.exitCode = 1;
  if (browser) {
    await fs.mkdir(path.join(__dirname, 'artifacts'), { recursive: true });
    let index = 0;
    for (const context of browser.contexts()) for (const page of context.pages())
      await page.screenshot({ path: path.join(__dirname, 'artifacts', `failure-${index++}.png`) });
  }
}).finally(async () => {
  try { await cleanup(); } catch (e) { console.error(`Cleanup failed: ${e.message}`); process.exitCode = 1; }
});
