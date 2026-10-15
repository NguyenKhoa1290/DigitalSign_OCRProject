// Navigation regression with isolated API fixtures; no writes or cloud OCR calls.
const assert = require('node:assert/strict');
const { chromium } = require(process.env.HAU_PLAYWRIGHT_MODULE || 'playwright');
const web = process.env.HAU_TEST_WEB || 'http://localhost:5227';

async function check(browser, role, width) {
  const context = await browser.newContext({ viewport: { width, height: 844 } });
  const token = [Buffer.from('{"alg":"none"}').toString('base64url'),
    Buffer.from(JSON.stringify({ sub: 'aaaaaaaa-0000-0000-0000-000000000001', name: 'Navigation fixture',
      role, exp: Math.floor(Date.now()/1000) + 3600 })).toString('base64url'), 'fixture'].join('.');
  await context.addInitScript(token => { localStorage.setItem('auth_token', token); window.fixtureDocument = Math.random(); }, token);
  let modelRequests = 0;
  await context.route('**/api/**', async route => {
    const url = new URL(route.request().url());
    assert.equal(route.request().method(), 'GET');
    let data;
    if (url.pathname === '/api/documents') data = { items: [], page: 1, pageSize: 15, totalCount: 0, totalPages: 0 };
    else if (url.pathname === '/api/documents/aaaaaaaa-0000-0000-0000-000000000001') data = {
      id: 'aaaaaaaa-0000-0000-0000-000000000001', title: 'OCR permission fixture',
      status: 'Draft', minioPath: '', processes: []
    };
    else if (url.pathname === '/api/documents/types') data = [];
    else if (url.pathname === '/api/ocr/gemini/models') {
      modelRequests++;
      assert.equal(role, 'Clerk', 'Specialist must not load OCR models');
      data = [{ id: 'fixture-model', displayName: 'Fixture model' }];
    } else throw new Error(`Unexpected fixture API ${url.pathname}`);
    await route.fulfill({ contentType: 'application/json', body: JSON.stringify({ success: true, data }) });
  });
  const page = await context.newPage(), errors = [];
  page.on('pageerror', error => errors.push(error.message)); page.setDefaultTimeout(15000);
  await page.goto(web + '/documents');
  await page.getByRole('heading', { name: 'Danh sách Công văn', exact: true }).waitFor();
  const identity = await page.evaluate(() => window.fixtureDocument);
  const sidebar = page.locator('.sidebar');
  const bar = page.locator('flight-taskbar');
  async function selected(route) {
    const active = await sidebar.locator('a.nav-item.active').evaluateAll(elements => elements.map(element => new URL(element.href).pathname));
    assert.deepEqual(active, [route], 'Exactly one sidebar item should match the current page');
    assert.equal(await page.evaluate(() => window.fixtureDocument), identity, 'Navigation must not reload');
    if (width < 960) assert.equal(await bar.evaluate(element => element.selectedIndex),
      await bar.evaluate((element, route) => element.getAttribute('routes').split('|').indexOf(route), route));
  }
  async function open(route, label) {
    if (width > 960) await sidebar.locator(`a[href="${route}"]`).click();
    else await bar.getByRole('tab', { name: label, exact: true }).click();
    await page.waitForURL(web + route);
    await selected(route);
  }
  await selected('/documents');
  await open('/documents/create', 'Tạo mới');
  await page.getByRole('heading', { name: 'Tạo Công văn mới', exact: true }).waitFor();
  await page.goBack(); await page.waitForURL(web + '/documents'); await selected('/documents');
  if (role === 'Clerk') {
    await open('/documents/ocr-scan', 'Quét OCR');
    await page.getByRole('heading', { name: 'Quét OCR và xuất Word', exact: true }).waitFor();
    assert(modelRequests > 0);
    await open('/documents', 'Công văn');
    await page.goBack(); await page.waitForURL(web + '/documents/ocr-scan'); await selected('/documents/ocr-scan');
  } else {
    assert.equal(await sidebar.locator('a[href="/documents/ocr-scan"]').count(), 0);
    assert(!(await bar.getAttribute('routes')).split('|').includes('/documents/ocr-scan'));
    assert.equal(await bar.locator('[role="tab"]').count(), 4);
    await page.goto(web + '/documents/ocr-scan');
    await page.getByRole('heading', { name: 'Không có quyền truy cập', exact: true }).waitFor();
    assert.equal(modelRequests, 0);
  }
  await page.goto(web + '/documents/aaaaaaaa-0000-0000-0000-000000000001');
  await page.getByRole('heading', { name: 'OCR permission fixture', exact: true }).waitFor();
  assert.equal(await page.getByRole('button', { name: 'Chạy OCR', exact: true }).count(), role === 'Clerk' ? 1 : 0);
  assert.equal(await page.getByRole('button', { name: 'Sửa thông tin', exact: true }).count(), 1);
  assert.equal(await page.getByRole('button', { name: 'Tải lên file', exact: true }).count(), 1);
  assert.equal(await page.getByRole('button', { name: 'Nộp trình duyệt', exact: true }).count(), 1);
  await page.getByRole('link', { name: 'Kết quả OCR', exact: true }).click();
  await page.getByRole('heading', { name: 'Kết quả OCR', exact: true }).waitFor();
  assert.equal(modelRequests, role === 'Clerk' ? modelRequests : 0);
  assert.deepEqual(errors, []);
  await context.close();
  console.log(`PASS ${role} ${width}px: unique selection, navigation, Back and OCR visibility/permission`);
}
(async () => {
  const browser = await chromium.launch({ headless: true, executablePath: process.env.HAU_TEST_CHROMIUM });
  try {
    for (const role of ['Clerk', 'Specialist']) for (const width of [1366, 300]) await check(browser, role, width);
  } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
