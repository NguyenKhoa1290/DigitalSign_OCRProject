// Full browser submit contract with intercepted APIs; documents/PDFs stay in fixtures.
const assert = require('node:assert/strict');
const { chromium } = require(process.env.HAU_PLAYWRIGHT_MODULE || 'playwright');
const web = process.env.HAU_TEST_WEB || 'http://localhost:5227';
const typeId = 'aaaaaaaa-0000-0000-0000-000000000001', docId = 'bbbbbbbb-0000-0000-0000-000000000001';
const pdf = Buffer.from('%PDF-1.4\n%%EOF');

async function check(browser, width, issuedDate, failUpload) {
  const context = await browser.newContext({ viewport: { width, height: 844 }, timezoneId: 'Asia/Ho_Chi_Minh' });
  const token = [Buffer.from('{"alg":"none"}').toString('base64url'), Buffer.from(JSON.stringify({
    sub: 'cccccccc-0000-0000-0000-000000000001', name: 'Fixture specialist', role: 'Specialist',
    exp: Math.floor(Date.now()/1000) + 3600 })).toString('base64url'), 'fixture'].join('.');
  await context.addInitScript(token => localStorage.setItem('auth_token', token), token);
  let document, creates = 0, uploads = 0;
  await context.route('**/api/**', async route => {
    const req = route.request(), url = new URL(req.url()); let data, status = 200;
    if (req.method() === 'GET' && url.pathname === '/api/documents/types')
      data = [{ id: typeId, typeName: 'CongVanDen' }];
    else if (req.method() === 'POST' && url.pathname === '/api/documents') {
      creates++;
      const body = req.postDataJSON();
      assert.equal(body.IssuedDate, issuedDate);
      assert(!body.DocNumber, 'Document number is optional during creation');
      document = { id: docId, title: body.Title, docNumber: null, issuedDate, docTypeId: typeId,
        docTypeName: 'CongVanDen', status: 'Draft', statusDisplay: 'Nháp', minioPath: '',
        createdAt: '2026-10-14T09:10:41Z', updatedAt: '2026-10-14T09:10:41Z', processes: [] };
      data = document; status = 201;
    } else if (req.method() === 'POST' && url.pathname === `/api/documents/${docId}/upload`) {
      uploads++;
      const body = req.postDataBuffer().toString('latin1');
      assert.match(req.headers()['content-type'], /^multipart\/form-data/);
      assert.match(body, /Content-Type: application\/pdf/i);
      assert(body.includes('fixture.pdf'));
      assert(body.includes('%PDF-1.4'));
      if (failUpload) { status = 413; data = null; }
      else { document = { ...document, minioPath: 'documents/fixture.pdf' }; data = document; }
    } else if (req.method() === 'GET' && url.pathname === `/api/documents/${docId}`) data = document;
    else if (req.method() === 'GET' && url.pathname === `/api/documents/${docId}/file`)
      return route.fulfill({ contentType: 'application/pdf', body: pdf });
    else throw new Error(`Unexpected fixture API ${req.method()} ${url.pathname}`);
    await route.fulfill({ status, contentType: 'application/json', body: JSON.stringify({ success: status < 400, data }) });
  });
  const page = await context.newPage(), errors = [];
  page.on('pageerror', error => errors.push(error.message)); page.setDefaultTimeout(15000);
  await page.goto(web + '/documents/create');
  await page.locator('input[placeholder="Nhập tiêu đề công văn..."]').fill('Fixture optional metadata');
  if (issuedDate) { await page.locator('input[type="date"]').fill(issuedDate); await page.locator('input[type="date"]').press('Tab'); }
  await page.locator('#document-type-select').click();
  await page.getByRole('option', { name: 'Công văn đến', exact: true }).click();
  const largePdf = Buffer.alloc(9 * 1024 * 1024, 32); pdf.copy(largePdf);
  await page.locator('input[type="file"]').setInputFiles({ name: 'fixture.pdf', mimeType: 'application/pdf', buffer: largePdf });
  await page.getByText('File sẵn sàng tải lên', { exact: true }).waitFor();
  await page.getByRole('button', { name: 'Tạo Công văn', exact: true }).click();
  await page.waitForURL(web + `/documents/${docId}`);
  await page.getByRole('heading', { name: 'Fixture optional metadata', exact: true }).waitFor();
  assert.equal(creates, 1); assert.equal(uploads, 1);
  await page.getByText('14/10/2026 16:10', { exact: true }).waitFor();
  assert(!(await page.locator('.page-container').innerText()).includes('01/01/0001'));
  if (failUpload) {
    await page.getByText(/Công văn đã tạo nhưng file chưa tải lên/).waitFor();
    assert.equal(await page.getByText('Tạo công văn thành công!', { exact: true }).count(), 0,
      'Partial success must not also show a success toast');
  } else {
    await page.getByText('Tạo công văn thành công!', { exact: true }).waitFor();
    assert.match(await page.locator('iframe.pdf-frame').getAttribute('src'), /^blob:/);
  }
  await page.getByRole('button', { name: 'Sửa thông tin', exact: true }).click();
  assert.equal(await page.locator('#edit-number').inputValue(), '');
  assert.equal(await page.locator('#edit-date').inputValue(), issuedDate || '');
  assert.deepEqual(errors, []);
  console.log(`PASS ${width}px date=${issuedDate || 'empty'} upload=${failUpload ? '413' : 'success'}: optional fields, 9MiB multipart, dates and one result`);
  await context.close();
}
(async () => {
  const browser = await chromium.launch({ headless: true, executablePath: process.env.HAU_TEST_CHROMIUM });
  try {
    for (const width of [1366, 300]) {
      await check(browser, width, null, false);
      await check(browser, width, '2026-10-14', false);
      await check(browser, width, null, true);
    }
  } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
