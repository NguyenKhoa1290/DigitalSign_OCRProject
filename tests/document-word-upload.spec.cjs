// Browser transfer fixtures only; no writes to application DB/MinIO or calls to OCR.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { chromium } = require(process.env.HAU_PLAYWRIGHT_MODULE || 'playwright');
const web = process.env.HAU_TEST_WEB || 'http://localhost:5227';
const id = 'bbbbbbbb-0000-0000-0000-000000000001';
const typeId = 'aaaaaaaa-0000-0000-0000-000000000001';
const wordMime = 'application/vnd.openxmlformats-officedocument.wordprocessingml.document';
const wordName = 'Công văn đã chỉnh sửa.DOCX';
const wordBytes = Buffer.from('PK\x03\x04word-transfer-fixture');
async function check(browser, width) {
  const context = await browser.newContext({ viewport: { width, height: 800 } });
  const token = [Buffer.from('{"alg":"none"}').toString('base64url'), Buffer.from(JSON.stringify({
    sub: 'cccccccc-0000-0000-0000-000000000001', name: 'Word fixture', role: 'Clerk',
    exp: Math.floor(Date.now()/1000) + 3600 })).toString('base64url'), 'fixture'].join('.');
  await context.addInitScript(value => { if (window === window.top) localStorage.setItem('auth_token', value); }, token);
  let doc, uploads = 0;
  const unexpected = [];
  await context.route('**/api/**', async route => {
    const request = route.request(), url = new URL(request.url()); let data;
    if (request.method() === 'GET' && url.pathname === '/api/documents/types')
      data = [{ id: typeId, typeName: 'CongVanDen' }];
    else if (request.method() === 'POST' && url.pathname === '/api/documents') {
      doc = { id, title: request.postDataJSON().Title, docTypeId: typeId, docTypeName: 'CongVanDen', status: 'Draft', minioPath: '', processes: [] };
      data = doc;
    } else if (request.method() === 'POST' && url.pathname === `/api/documents/${id}/upload`) {
      const isWord = uploads++ === 0;
      assert(request.postDataBuffer().includes(Buffer.from('Content-Type: ' + (isWord ? wordMime : 'application/pdf'))));
      doc = { ...doc, minioPath: 'documents/' + (isWord ? 'edited.docx' : 'final.pdf'),
        fileName: isWord ? wordName : 'Bản chốt.pdf', fileContentType: isWord ? wordMime : 'application/pdf' };
      data = doc;
    } else if (request.method() === 'GET' && url.pathname === `/api/documents/${id}`) data = doc;
    else if (request.method() === 'GET' && url.pathname === `/api/documents/${id}/file`)
      return route.fulfill({ contentType: doc.fileContentType, body: doc.fileContentType === wordMime ? wordBytes : Buffer.from('%PDF-1.4\n%%EOF') });
    else { unexpected.push(request.method() + ' ' + url.pathname); return route.abort(); }
    return route.fulfill({ contentType: 'application/json', body: JSON.stringify({ success: true, data }) });
  });
  const page = await context.newPage(), errors = [];
  page.setDefaultTimeout(15000); page.on('pageerror', error => errors.push(error.message));
  try {
    await page.goto(web + '/documents/create');
    await page.locator('input[placeholder="Nhập tiêu đề công văn..."]').fill('Word fixture');
    await page.locator('#document-type-select').click();
    await page.getByRole('option', { name: 'Công văn đến', exact: true }).click();
    const input = page.locator('input[type=file]');
    assert.match(await input.getAttribute('accept'), /\.docx/);
    await input.setInputFiles({ name: 'old.doc', mimeType: 'application/msword', buffer: wordBytes });
    await page.getByText('Chỉ hỗ trợ PDF, Word (.docx), PNG, JPG/JPEG hoặc WEBP.', { exact: true }).waitFor();
    await input.setInputFiles({ name: wordName, mimeType: wordMime, buffer: wordBytes });
    await page.getByRole('button', { name: 'Tạo Công văn', exact: true }).click();
    await page.waitForURL(web + '/documents/' + id);
    await page.getByText('Văn bản Word', { exact: true }).waitFor();
    assert.equal(await page.locator('iframe.pdf-frame').count(), 0);
    assert.equal(await page.getByRole('button', { name: 'Chạy OCR', exact: true }).count(), 0);
    assert.equal(await page.getByRole('link', { name: 'Kết quả OCR', exact: true }).count(), 0);
    await page.getByText('Đây là tệp văn bản hoàn chỉnh, không cần OCR.', { exact: false }).waitFor();
    assert(await page.getByRole('button', { name: 'Trình BGH chỉ đạo', exact: true }).isDisabled());
    let download = page.waitForEvent('download');
    await page.getByRole('button', { name: 'Tải Word', exact: true }).click();
    assert.equal((await download).suggestedFilename(), wordName);
    await page.goto(web + '/documents/' + id + '/ocr');
    await page.getByText('Văn bản Word không cần OCR', { exact: true }).waitFor();
    assert.equal(await page.getByText('Chưa có dữ liệu OCR', { exact: true }).count(), 0);
    await page.goto(web + '/documents/' + id);
    await page.reload();
    await page.getByRole('button', { name: 'Tải Word', exact: true }).waitFor();
    assert(await page.locator('.page-container').evaluate(el => el.scrollWidth <= el.clientWidth + 1));
    if (process.env.HAU_TEST_ARTIFACTS) {
      fs.mkdirSync(process.env.HAU_TEST_ARTIFACTS, { recursive: true });
      await page.getByText('Văn bản Word', { exact: true }).scrollIntoViewIfNeeded();
      await page.screenshot({ path: path.join(process.env.HAU_TEST_ARTIFACTS, `word-${width}.png`), fullPage: true });
    }
    await page.locator('input[type=file]').setInputFiles({ name: 'Bản chốt.pdf', mimeType: 'application/pdf', buffer: Buffer.from('%PDF-1.4\n%%EOF') });
    await page.locator('iframe.pdf-frame').waitFor();
    await page.waitForFunction(() => [...document.querySelectorAll('button')]
      .some(button => button.textContent.trim() === 'Trình BGH chỉ đạo' && !button.disabled));
    assert(await page.getByRole('button', { name: 'Trình BGH chỉ đạo', exact: true }).isEnabled());
    download = page.waitForEvent('download');
    await page.getByRole('button', { name: 'Tải PDF', exact: true }).click();
    assert.equal((await download).suggestedFilename(), 'Bản chốt.pdf');
    assert.equal(uploads, 2); assert.deepEqual(errors, []); assert.deepEqual(unexpected, []);
    console.log(`PASS ${width}px: Word create/download/reload, no OCR/PDF viewer, PDF replacement enables incoming submit`);
  } finally { await context.close(); }
}
(async () => {
  const browser = await chromium.launch({ headless: true, executablePath: process.env.HAU_TEST_CHROMIUM });
  try { for (const width of process.env.HAU_TEST_WIDTHS ? process.env.HAU_TEST_WIDTHS.split(',').map(Number) : [1366, 300]) await check(browser, width); }
  finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
