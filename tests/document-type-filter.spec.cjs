// Browser regression with isolated fixtures; optional live API reads, no document writes.
const assert = require('node:assert/strict');
const { chromium } = require(process.env.HAU_PLAYWRIGHT_MODULE || 'playwright');
const web = process.env.HAU_TEST_WEB || 'http://localhost:5227';
const types = [
  ['CongVanDen', 'Công văn đến'], ['CongVanDi', 'Công văn đi'],
  ['ToTrinh', 'Tờ trình'], ['QuyetDinh', 'Quyết định']
].map(([typeName, label], i) => ({ id: `aaaaaaaa-0000-0000-0000-00000000000${i + 1}`, typeName, label }));
const documents = types.flatMap((type, i) => Array.from({ length: i === 0 ? 18 : 1 }, (_, j) => ({
  id: `bbbbbbbb-0000-0000-0000-${String(i * 100 + j + 1).padStart(12, '0')}`,
  title: `${type.label} mẫu ${j + 1}`, docTypeId: type.id, docTypeName: type.typeName,
  status: i === 0 ? 'Draft' : 'Published', createdAt: '2026-10-13T00:00:00Z'
})));
const fixtureToken = [Buffer.from('{"alg":"none"}').toString('base64url'),
  Buffer.from(JSON.stringify({ sub: 'cccccccc-0000-0000-0000-000000000001',
    unique_name: 'fixture-clerk', role: 'Clerk', exp: Math.floor(Date.now() / 1000) + 3600 })).toString('base64url'),
  'fixture'].join('.');

async function browserChecks(browser, viewport) {
  const context = await browser.newContext({ viewport });
  await context.addInitScript(token => localStorage.setItem('auth_token', token), fixtureToken);
  const page = await context.newPage();
  const errors = [];
  page.on('pageerror', error => errors.push(error.message));
  await context.route('**/api/**', async route => {
    const request = route.request(), url = new URL(request.url());
    assert.equal(request.method(), 'GET', 'Fixture browser must not write to the live API');
    let data;
    if (url.pathname === '/api/documents/types') data = types;
    else if (url.pathname === '/api/documents') {
      let items = documents;
      const type = url.searchParams.get('docTypeId'), status = url.searchParams.get('status');
      if (type) items = items.filter(doc => doc.docTypeId === type);
      if (status) items = items.filter(doc => doc.status === status);
      const page = Number(url.searchParams.get('page') || 1), pageSize = Number(url.searchParams.get('pageSize') || 15);
      data = { items: items.slice((page - 1) * pageSize, page * pageSize), totalCount: items.length, page, pageSize };
    } else throw new Error(`Unexpected fixture API: ${url.pathname}`);
    await route.fulfill({ status: 200, contentType: 'application/json',
      headers: { 'Access-Control-Allow-Origin': '*' }, body: JSON.stringify({ success: true, data }) });
  });
  async function choose(label, option) {
    await page.getByRole('button', { name: label, exact: true }).click();
    const response = page.waitForResponse(r => new URL(r.url()).pathname === '/api/documents');
    await page.getByRole('option', { name: option, exact: true }).click();
    const url = new URL((await response).url());
    await page.waitForSelector('.spinner', { state: 'hidden' });
    return url;
  }
  await page.goto(web + '/documents');
  await page.locator('.pagination-info').waitFor();
  await page.getByRole('button', { name: '2', exact: true }).click();
  await page.waitForFunction(() => document.querySelector('.pagination-info')?.textContent.includes('16–21'));
  for (const type of types) {
    // Accept the old raw label here so the pre-fix build fails on the query contract itself.
    const url = await choose('Lọc theo loại văn bản', new RegExp(`^(${type.label}|${type.typeName})$`));
    assert.equal(url.searchParams.get('docTypeId'), type.id, 'Frontend must send backend DocTypeId');
    assert.equal(url.searchParams.has('typeId'), false);
    assert.equal(url.searchParams.get('page'), '1', 'Type change must reset pagination');
    const cards = page.locator(viewport.width < 768 ? '.document-mobile-card' : '.document-desktop-table tbody tr');
    assert.equal(await cards.count(), type.typeName === 'CongVanDen' ? 15 : 1);
    assert((await cards.allTextContents()).every(text => text.includes(type.label) && !text.includes(type.typeName)));
  }
  await choose('Lọc theo loại văn bản', 'Tất cả loại');
  await page.getByRole('button', { name: '2', exact: true }).click();
  await page.waitForFunction(() => document.querySelector('.pagination-info')?.textContent.includes('16–21'));
  let url = await choose('Lọc theo trạng thái', 'Nháp');
  assert.equal(url.searchParams.get('page'), '1', 'Status change must reset pagination');
  assert.equal(url.searchParams.get('status'), 'Draft');
  await choose('Lọc theo loại văn bản', 'Quyết định');
  await page.getByText('Không có công văn nào', { exact: true }).waitFor();
  await choose('Lọc theo trạng thái', 'Tất cả trạng thái');
  await page.getByText('Quyết định mẫu 1', { exact: true }).filter({ visible: true }).waitFor();
  await page.goto(web + '/documents/create');
  await page.locator('#document-type-select').click();
  for (const type of types) assert.equal(await page.getByRole('option', { name: type.label, exact: true }).count(), 1);
  assert.deepEqual(errors, []);
  await context.close();
  console.log(`PASS ${viewport.width}px: four types, query contract, pagination, combined/empty/reset filters and create options`);
}

async function liveApiChecks() {
  const api = process.env.HAU_TEST_API || 'http://localhost:5000';
  const login = await fetch(api + '/api/auth/login', { method: 'POST', headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ username: process.env.HAU_TEST_USERNAME || 'admin', password: process.env.HAU_TEST_PASSWORD || 'Admin@123' }) });
  assert.equal(login.status, 200);
  const { accessToken } = await login.json();
  const headers = { Authorization: `Bearer ${accessToken}` };
  try {
    for (const type of types) {
      const response = await fetch(`${api}/api/documents?docTypeId=${type.id}&pageSize=100`, { headers });
      assert.equal(response.status, 200);
      const { data } = await response.json();
      assert(data.items.every(doc => doc.docTypeId === type.id));
      console.log(`PASS live API ${type.label}: ${data.totalCount} records`);
    }
  } finally {
    const logout = await fetch(api + '/api/auth/logout', { method: 'POST', headers });
    assert.equal(logout.status, 200);
  }
}

(async () => {
  const browser = await chromium.launch({ headless: true, executablePath: process.env.HAU_TEST_CHROMIUM });
  try {
    for (const viewport of [{ width: 1366, height: 768 }, { width: 390, height: 844 }]) await browserChecks(browser, viewport);
  } finally { await browser.close(); }
  if (process.env.HAU_CHECK_LIVE_API === '1') await liveApiChecks();
})().catch(error => { console.error(error.message); process.exitCode = 1; });
