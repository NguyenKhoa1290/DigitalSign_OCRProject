// Intercepted APIs only; no Gemini quota, database or MinIO writes.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { chromium } = require(process.env.HAU_PLAYWRIGHT_MODULE || 'playwright');
const image = Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aOhkAAAAASUVORK5CYII=', 'base64');
async function run(browser, width) {
  const context = await browser.newContext({ viewport: { width, height: 850 } });
  const token = [Buffer.from('{"alg":"none"}').toString('base64url'), Buffer.from(JSON.stringify({ sub: 'bbbbbbbb-0000-0000-0000-000000000001', name: 'Clerk fixture', role: 'Clerk', exp: Math.floor(Date.now()/1000)+3600 })).toString('base64url'), 'fixture'].join('.');
  await context.addInitScript(value => { if (window === window.top && location.protocol.startsWith('http')) localStorage.setItem('auth_token', value); }, token);
  const page = await context.newPage(), unexpected = [], errors = [], exports = []; let fail = true;
  page.setDefaultTimeout(15000); page.on('pageerror', e => errors.push(e.message));
  await context.route('**/api/**', async route => {
    const req = route.request(), url = new URL(req.url());
    if (url.pathname === '/api/ocr/gemini/models') return route.fulfill({ json: { success: true, data: [{ id: 'gemini-first', displayName: 'First Gemini' },{ id: 'gemini-second', displayName: 'Second Gemini' }] } });
    if (url.pathname === '/api/ocr/input-info') return route.fulfill({ json: { totalPages: 89 } });
    if (url.pathname === '/api/ocr/gemini/export-word-upload') {
      exports.push({ pages: url.searchParams.get('pages'), model: url.searchParams.get('model_id'), body: req.postDataBuffer().toString('latin1') });
      if (fail) return route.fulfill({ status: 429, json: { detail: 'Hết hạn mức fixture; chọn model khác.' } });
      return route.fulfill({ contentType: 'application/vnd.openxmlformats-officedocument.wordprocessingml.document', body: Buffer.from('fixture-docx') });
    }
    unexpected.push(req.method()+' '+url.pathname); return route.abort();
  });
  try {
    await page.goto((process.env.HAU_TEST_WEB || 'http://localhost:5227')+'/documents/ocr-scan');
    const input = page.locator('#ocr-file'); await input.waitFor(); assert(await input.getAttribute('multiple') !== null);
    await input.setInputFiles(Array.from({length:6},(_,i)=>({ name: `image-${i+1}.png`, mimeType: 'image/png', buffer: image })));
    await page.getByText('Tổng 6 ảnh.', { exact: false }).waitFor();
    assert.equal(await page.locator('.ocr-image-previews img').count(), 6);
    await page.getByRole('button', { name: 'Chạy OCR', exact: true }).click();
    const dialog = page.getByRole('dialog'), confirm = dialog.getByRole('button', { name: 'Tạo và tải Word', exact: true });
    await dialog.locator('#ocr-pages').fill('1-6'); assert(await confirm.isDisabled());
    await dialog.locator('#ocr-pages').fill('6,2,2'); await confirm.click();
    await dialog.getByRole('alert').filter({ hasText: 'Hết hạn mức fixture' }).waitFor();
    assert.equal(await dialog.locator('#ocr-pages').inputValue(), '6,2,2');
    await dialog.locator('#ocr-dialog-model').selectOption('gemini-second'); fail = false;
    const downloading = page.waitForEvent('download'); await confirm.click();
    assert.equal((await downloading).suggestedFilename(), 'ban-nhap-ocr-gemini.docx');
    await dialog.waitFor({ state: 'hidden' });
    assert.equal(exports[0].pages, '2,6'); assert.equal(exports[1].model, 'gemini-second');
    assert.equal((exports[1].body.match(/name=files|name="files"/g)||[]).length, 6);
    await input.setInputFiles({ name: 'long.pdf', mimeType: 'application/pdf', buffer: Buffer.from('%PDF-1.4\n%%EOF') });
    await page.locator('iframe.ocr-pdf-preview').waitFor();
    await page.getByRole('button', { name: 'Chạy OCR', exact: true }).click();
    await dialog.locator('#ocr-pages').fill('90'); assert(await confirm.isDisabled());
    await dialog.locator('#ocr-pages').fill('1, 3, 87-89'); assert(await confirm.isEnabled());
    if (process.env.HAU_TEST_ARTIFACTS) {
      fs.mkdirSync(process.env.HAU_TEST_ARTIFACTS,{recursive:true});
      await page.screenshot({path:path.join(process.env.HAU_TEST_ARTIFACTS,`select-pages-${width}.png`), animations:'disabled'});
    }
    const pdfDownload = page.waitForEvent('download'); await confirm.click(); await pdfDownload;
    await dialog.waitFor({state:'hidden'}); assert.equal(exports[2].pages,'1,3,87,88,89');
    assert.deepEqual(unexpected,[]); assert.deepEqual(errors,[]);
    console.log(`PASS ${width}px multi-image, PDF preview, 5-page selection, original page numbers, 429 retry and model switching`);
  } finally { await context.close(); }
}
(async()=>{const browser=await chromium.launch({headless:true,executablePath:process.env.HAU_TEST_CHROMIUM});try{for(const width of [1366,300])await run(browser,width);}finally{await browser.close();}})().catch(e=>{console.error(e);process.exitCode=1;});
