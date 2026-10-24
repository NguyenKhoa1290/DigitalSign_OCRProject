// Intercepted APIs only: no writes to the application DB/MinIO or Gemini requests.
const assert = require('node:assert/strict');
const { chromium } = require(process.env.HAU_PLAYWRIGHT_MODULE || 'playwright');
const web = process.env.HAU_TEST_WEB || 'http://localhost:5227';
const id = 'bbbbbbbb-0000-0000-0000-000000000001';
const typeId = 'aaaaaaaa-0000-0000-0000-000000000001';
const imageName = 'Ảnh công văn thử nghiệm.png';
const image = Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aOhkAAAAASUVORK5CYII=', 'base64');
const pdf = Buffer.from('%PDF-1.4\n%%EOF');
const limitMessage = 'Mỗi lượt OCR hỗ trợ tối đa 5 trang. Tệp này có 6 trang; hãy tách tài liệu trước khi quét.';

async function check(browser, width) {
  const context = await browser.newContext({ viewport: { width, height: 844 } });
  const token = [Buffer.from('{"alg":"none"}').toString('base64url'), Buffer.from(JSON.stringify({
    sub: 'cccccccc-0000-0000-0000-000000000001', name: 'Image fixture', role: 'Clerk',
    exp: Math.floor(Date.now()/1000) + 3600 })).toString('base64url'), 'fixture'].join('.');
  await context.addInitScript(token => {
    if (location.protocol === 'http:' || location.protocol === 'https:') localStorage.setItem('auth_token', token);
  }, token);
  let document, uploads = 0, ocrCalls = 0, exports = 0;
  await context.route('**/api/**', async route => {
    const req = route.request(), url = new URL(req.url()); let data;
    if (req.method() === 'GET' && url.pathname === '/api/documents/types') data = [{ id: typeId, typeName: 'CongVanDen' }];
    else if (req.method() === 'POST' && url.pathname === '/api/documents') {
      const body = req.postDataJSON();
      document = { id, title: body.Title, docTypeId: typeId, status: 'Draft', minioPath: '', processes: [] };
      data = document;
    } else if (req.method() === 'POST' && url.pathname === `/api/documents/${id}/upload`) {
      const isImage = uploads++ === 0;
      assert.match(req.postDataBuffer().toString('latin1'), isImage ? /Content-Type: image\/png/i : /Content-Type: application\/pdf/i);
      document = { ...document, minioPath: `documents/object.${isImage ? 'png' : 'pdf'}`,
        fileName: isImage ? imageName : 'replacement.pdf', fileContentType: isImage ? 'image/png' : 'application/pdf' };
      data = document;
    } else if (req.method() === 'GET' && url.pathname === `/api/documents/${id}`) data = document;
    else if (req.method() === 'GET' && url.pathname === `/api/documents/${id}/file`)
      return route.fulfill({ contentType: document.fileContentType, body: document.fileContentType === 'image/png' ? image : pdf });
    else if (req.method() === 'POST' && url.pathname === '/api/ocr/process') {
      ocrCalls++; return route.fulfill({ status: 400, contentType: 'application/json', body: JSON.stringify({ detail: limitMessage }) });
    } else if (req.method() === 'GET' && url.pathname === '/api/ocr/gemini/models')
      data = [{ id: 'gemini-fixture', displayName: 'Fixture Gemini' }];
    else if (req.method() === 'POST' && url.pathname === '/api/ocr/input-info') {
      return route.fulfill({ contentType: 'application/json', body: JSON.stringify({ totalPages: 6 }) });
    } else if (req.method() === 'POST' && url.pathname === '/api/ocr/gemini/export-word-upload') {
      exports++; return route.fulfill({ status: 400, contentType: 'application/json', body: JSON.stringify({ detail: limitMessage }) });
    } else throw new Error(`Unexpected fixture API ${req.method()} ${url.pathname}`);
    await route.fulfill({ contentType: 'application/json', body: JSON.stringify({ success: true, data }) });
  });
  const page = await context.newPage(), errors = [];
  page.on('pageerror', error => errors.push(error.message)); page.setDefaultTimeout(15000);
  await page.goto(web + '/documents/create');
  await page.locator('input[placeholder="Nhập tiêu đề công văn..."]').fill('Image fixture');
  await page.locator('#document-type-select').click();
  await page.getByRole('option', { name: 'Công văn đến', exact: true }).click();
  const input = page.locator('input[type="file"]');
  assert.match(await input.getAttribute('accept'), /\.png/);
  await input.setInputFiles({ name: 'unsupported.svg', mimeType: 'image/svg+xml', buffer: Buffer.from('<svg/>') });
  await page.getByText('Chỉ hỗ trợ PDF, Word (.docx), PNG, JPG/JPEG hoặc WEBP.', { exact: true }).waitFor();
  await input.setInputFiles({ name: imageName, mimeType: 'image/png', buffer: image });
  await page.getByText('File sẵn sàng tải lên', { exact: true }).waitFor();
  await page.getByRole('button', { name: 'Tạo Công văn', exact: true }).click();
  await page.waitForURL(web + `/documents/${id}`);
  const preview = page.locator('img.document-image'); await preview.waitFor();
  await page.waitForFunction(() => document.querySelector('img.document-image')?.naturalWidth > 0);
  assert.equal(await page.locator('iframe.pdf-frame').count(), 0);
  assert(await preview.evaluate(img => img.getBoundingClientRect().width <= innerWidth));
  let downloading = page.waitForEvent('download');
  await page.getByRole('button', { name: 'Tải ảnh', exact: true }).click();
  assert.equal((await downloading).suggestedFilename(), imageName);

  const chooser = page.waitForEvent('filechooser');
  await page.getByRole('button', { name: 'Tải lên file', exact: true }).click();
  await (await chooser).setFiles({ name: 'replacement.pdf', mimeType: 'application/pdf', buffer: pdf });
  await page.locator('iframe.pdf-frame').waitFor();
  assert.equal(await page.locator('img.document-image').count(), 0); assert.equal(uploads, 2);
  downloading = page.waitForEvent('download'); await page.getByRole('button', { name: 'Tải PDF', exact: true }).click();
  assert.equal((await downloading).suggestedFilename(), 'replacement.pdf');
  const ocr = page.getByRole('button', { name: 'Chạy OCR', exact: true }); await ocr.click();
  await page.getByText(limitMessage, { exact: true }).waitFor();
  assert(await ocr.isEnabled()); assert.equal(ocrCalls, 1);

  await page.goto(web + '/documents/ocr-scan');
  await page.getByText(/Chọn tối đa 5 trang\/ảnh mỗi lượt OCR/).waitFor();
  await page.locator('#ocr-file').setInputFiles({ name: 'six.pdf', mimeType: 'application/pdf', buffer: pdf });
  await page.locator('iframe.ocr-pdf-preview').waitFor();
  await page.getByRole('button', { name: 'Chạy OCR', exact: true }).click();
  const exportButton = page.getByRole('button', { name: 'Tạo và tải Word', exact: true }); await exportButton.click();
  await page.getByRole('status').filter({ hasText: limitMessage }).waitFor();
  assert(await exportButton.isEnabled()); assert.equal(exports, 1);
  assert.deepEqual(errors, []);
  console.log(`PASS ${width}px: create/preview/download PNG, replace with PDF, OCR limit errors and recovered buttons`);
  await context.close();
}
(async () => {
  const browser = await chromium.launch({ headless: true, executablePath: process.env.HAU_TEST_CHROMIUM });
  try { for (const width of [1366, 300]) await check(browser, width); }
  finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
