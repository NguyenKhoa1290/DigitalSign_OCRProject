// Browser fixtures only: no document or MinIO object is changed.
const assert = require('node:assert/strict');
const { chromium } = require(process.env.HAU_PLAYWRIGHT_MODULE || 'playwright');
const web = process.env.HAU_TEST_WEB || 'http://localhost:5227';
const id = 'aaaaaaaa-0000-0000-0000-000000000001';
const pdf = Buffer.from('%PDF-1.4\n%%EOF');
const original = 'Kế hoạch tốt nghiệp 2026.pdf';

async function check(browser, width) {
  const context = await browser.newContext({ viewport: { width, height: 844 } });
  const token = [Buffer.from('{"alg":"none"}').toString('base64url'), Buffer.from(JSON.stringify({
    sub: 'bbbbbbbb-0000-0000-0000-000000000001', name: 'Filename fixture', role: 'Clerk',
    exp: Math.floor(Date.now() / 1000) + 3600 })).toString('base64url'), 'fixture'].join('.');
  await context.addInitScript(token => {
    if (location.protocol === 'http:' || location.protocol === 'https:') localStorage.setItem('auth_token', token);
  }, token);
  let uploads = 0;
  let document = { id, title: 'Filename fixture', status: 'Draft', statusDisplay: 'Nháp',
    minioPath: 'documents/legacy.pdf', fileName: null, processes: [] };
  await context.route('**/api/**', async route => {
    const request = route.request(), url = new URL(request.url()); let data;
    if (request.method() === 'GET' && url.pathname === `/api/documents/${id}`) data = document;
    else if (request.method() === 'POST' && url.pathname === `/api/documents/${id}/upload`) {
      uploads++;
      document = { ...document, minioPath: 'documents/new.pdf', fileName: original };
      data = document;
    } else if (request.method() === 'GET' && url.pathname === `/api/documents/${id}/file`)
      return route.fulfill({ contentType: 'application/pdf', body: pdf });
    else throw new Error(`Unexpected fixture API: ${request.method()} ${url.pathname}`);
    await route.fulfill({ contentType: 'application/json', body: JSON.stringify({ success: true, data }) });
  });
  const page = await context.newPage(), errors = [];
  page.on('pageerror', error => errors.push(error.message)); page.setDefaultTimeout(15000);
  await page.goto(`${web}/documents/${id}`);
  const downloadButton = page.getByRole('button', { name: 'Tải PDF', exact: true });
  await downloadButton.waitFor();
  let downloading = page.waitForEvent('download');
  await downloadButton.click();
  assert.equal((await downloading).suggestedFilename(), `cong-van-${id}.pdf`);

  const chooser = page.waitForEvent('filechooser');
  await page.getByRole('button', { name: 'Tải lên file', exact: true }).click();
  await (await chooser).setFiles({ name: original, mimeType: 'application/pdf', buffer: pdf });
  await page.getByText('Tải file thành công!', { exact: true }).waitFor();
  await page.locator('iframe.pdf-frame').waitFor();
  await page.waitForFunction(() => !document.querySelector('button.btn-outline:disabled'));
  downloading = page.waitForEvent('download');
  await downloadButton.click();
  assert.equal((await downloading).suggestedFilename(), original);
  assert.equal(uploads, 1);

  // Persistence comes from the API, so a full reload must preserve the name too.
  await page.reload(); await downloadButton.waitFor();
  downloading = page.waitForEvent('download'); await downloadButton.click();
  assert.equal((await downloading).suggestedFilename(), original);
  assert.deepEqual(errors, []);
  console.log(`PASS ${width}px: legacy fallback, replacement filename with Vietnamese/spaces, preserved after reload`);
  await context.close();
}
(async () => {
  const browser = await chromium.launch({ headless: true, executablePath: process.env.HAU_TEST_CHROMIUM });
  try { for (const width of [1366, 300]) await check(browser, width); }
  finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
