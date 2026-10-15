// Isolated UI regression: fixture reads and in-browser PDF selection, no creation/upload.
const assert = require('node:assert/strict');
const { chromium } = require(process.env.HAU_PLAYWRIGHT_MODULE || 'playwright');
const web = process.env.HAU_TEST_WEB || 'http://localhost:5227';
const types = ['CongVanDen', 'CongVanDi', 'ToTrinh', 'QuyetDinh'].map((typeName, index) => ({
  id: `aaaaaaaa-0000-0000-0000-00000000000${index + 1}`, typeName
}));
const labels = ['Công văn đến', 'Công văn đi', 'Tờ trình', 'Quyết định'];
async function check(browser, role, width) {
  const context = await browser.newContext({ viewport: { width, height: 844 } });
  const token = [Buffer.from('{"alg":"none"}').toString('base64url'),
    Buffer.from(JSON.stringify({ sub: 'bbbbbbbb-0000-0000-0000-000000000001', name: 'Create fixture',
      role, exp: Math.floor(Date.now()/1000) + 3600 })).toString('base64url'), 'fixture'].join('.');
  await context.addInitScript(token => localStorage.setItem('auth_token', token), token);
  await context.route('**/api/**', async route => {
    assert.equal(route.request().method(), 'GET', 'No live creation or uploads');
    assert.equal(new URL(route.request().url()).pathname, '/api/documents/types');
    await route.fulfill({ contentType: 'application/json', body: JSON.stringify({ success: true, data: types }) });
  });
  const page = await context.newPage(), errors = [];
  page.setDefaultTimeout(15000); page.on('pageerror', error => errors.push(error.message));
  await page.goto(web + '/documents/create');
  const trigger = page.locator('#document-type-select');
  await trigger.waitFor();
  for (const label of labels) {
    await trigger.click();
    await page.locator('.page-container').evaluate(element => {
      const list = element.querySelector('.custom-select-options').getBoundingClientRect();
      if (list.bottom > innerHeight - 90) element.scrollTop += list.bottom - (innerHeight - 90);
    });
    const option = page.getByRole('option', { name: label, exact: true });
    assert(await option.evaluate(element => {
      const rect = element.getBoundingClientRect();
      return element.contains(document.elementFromPoint(rect.left + rect.width/2, rect.top + rect.height/2));
    }), `${label} must be clickable above the card/upload panel`);
    await option.click(); assert.equal(await trigger.innerText(), label);
  }
  const fileInput = page.locator('input[type="file"]');
  assert.equal(await fileInput.isVisible(), false, 'The browser default file control must be hidden');
  let chooserEvents = 0;
  page.on('filechooser', () => chooserEvents++);
  let chooserPromise = page.waitForEvent('filechooser');
  const button = page.getByRole('button', { name: 'Chọn file', exact: true });
  await button.click();
  let chooser = await chooserPromise;
  await chooser.setFiles({ name: 'cong-van-thu-nghiem.pdf', mimeType: 'application/pdf', buffer: Buffer.from('%PDF-1.4\n%%EOF') });
  await page.getByText('cong-van-thu-nghiem.pdf', { exact: true }).waitFor();
  await page.getByText('File sẵn sàng tải lên', { exact: true }).waitFor();
  assert.equal(chooserEvents, 1, 'Clicking the button must not open two pickers through bubbling');
  chooserPromise = page.waitForEvent('filechooser');
  await page.getByRole('button', { name: 'Đổi file', exact: true }).focus();
  await page.keyboard.press('Enter'); chooser = await chooserPromise;
  await chooser.setFiles({ name: 'cong-van-thay-the.pdf', mimeType: 'application/pdf', buffer: Buffer.from('%PDF-1.4\n%%EOF') });
  await page.getByText('cong-van-thay-the.pdf', { exact: true }).waitFor();
  assert.equal(chooserEvents, 2);
  assert.equal(await page.getByText('cong-van-thu-nghiem.pdf', { exact: true }).count(), 0);
  assert(await page.locator('.create-layout').evaluate(element => element.scrollWidth <= element.clientWidth));
  assert.deepEqual(errors, []);
  console.log(`PASS ${role} ${width}px: four visible options, styled PDF picker, click/Enter and replace`);
  await context.close();
}
(async () => {
  const browser = await chromium.launch({ headless: true, executablePath: process.env.HAU_TEST_CHROMIUM });
  try {
    for (const role of ['Specialist', 'Clerk']) for (const width of [1366, 390, 300]) await check(browser, role, width);
  } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
