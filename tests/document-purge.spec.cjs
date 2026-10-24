// Every API request is intercepted; no live document is permanently deleted.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { chromium } = require(process.env.HAU_PLAYWRIGHT_MODULE || 'playwright');
const actor = 'bbbbbbbb-0000-0000-0000-000000000001';
async function run(browser, width) {
  const context = await browser.newContext({ viewport: { width, height: 850 }, hasTouch: width === 300, isMobile: width === 300 });
  const token = [Buffer.from('{"alg":"none"}').toString('base64url'), Buffer.from(JSON.stringify({ sub: actor, name: 'Fixture', role: 'Admin', exp: Math.floor(Date.now()/1000)+3600 })).toString('base64url'), 'fixture'].join('.');
  await context.addInitScript(value => localStorage.setItem('auth_token', value), token);
  const page = await context.newPage(), unexpected = [], errors = []; let removed = false, fail = true, writes = 0;
  page.on('pageerror', e => errors.push(e.message)); page.setDefaultTimeout(15000);
  const doc = { id: actor, title: 'Công văn fixture', status: 'Draft', isDeleted: true, concurrencyVersion: '00000000-0000-0000-0000-000000000000', processes: [] };
  await context.route('**/api/**', async route => {
    const req = route.request(), url = new URL(req.url()); let data;
    if (req.method() === 'GET' && url.pathname === '/api/documents/deleted') data = { items: removed ? [] : [doc, {...doc, id: 'bbbbbbbb-0000-0000-0000-000000000002', title: 'Đã hủy fixture', isCancelled: true}], totalCount: removed ? 0 : 2, page: 1, pageSize: 15 };
    else if (req.method() === 'POST' && url.pathname === `/api/documents/${actor}/purge`) {
      writes++; assert.deepEqual(req.postDataJSON(), { reason: 'Sai hồ sơ', confirmationTitle: doc.title, expectedVersion: doc.concurrencyVersion });
      if (fail) return route.fulfill({ status: 503, contentType: 'application/json', body: JSON.stringify({ message: 'Lỗi fixture' }) });
      removed = true; data = { docId: actor, filesPendingCleanup: false };
    } else { unexpected.push(req.method()+' '+url.pathname); return route.abort(); }
    return route.fulfill({ contentType: 'application/json', body: JSON.stringify({ success: true, data }) });
  });
  try {
    await page.goto((process.env.HAU_TEST_WEB || 'http://localhost:5227')+'/documents/deleted');
    const card = page.locator('article').filter({ hasText: doc.title });
    await card.getByRole('button', { name: 'Khôi phục', exact: true }).waitFor();
    assert.equal(await page.getByRole('button', { name: 'Khôi phục', exact: true }).count(), 1);
    assert.equal(await page.getByRole('button', { name: 'Xóa vĩnh viễn' }).count(), 2);
    await card.getByRole('button', { name: 'Xóa vĩnh viễn' }).click();
    const dialog = page.getByRole('dialog'), confirm = dialog.getByRole('button', { name: 'Xóa vĩnh viễn', exact: true });
    assert(await confirm.isDisabled());
    await dialog.locator('textarea').fill('Sai hồ sơ'); await dialog.locator('input').fill('Sai tên'); assert(await confirm.isDisabled());
    await dialog.locator('input').fill(doc.title); await confirm.click();
    await dialog.getByRole('alert').filter({ hasText: 'Lỗi fixture' }).waitFor();
    assert.equal(await dialog.locator('textarea').inputValue(), 'Sai hồ sơ');
    assert.equal(await dialog.locator('input').inputValue(), doc.title);
    if (process.env.HAU_TEST_ARTIFACTS) {
      fs.mkdirSync(process.env.HAU_TEST_ARTIFACTS, { recursive: true });
      await page.screenshot({ path: path.join(process.env.HAU_TEST_ARTIFACTS, `purge-${width}.png`) });
    }
    fail = false; await confirm.click(); await dialog.waitFor({ state: 'hidden' });
    await page.getByText('Không có công văn đã xóa', { exact: true }).waitFor();
    assert.equal(writes, 2); assert.deepEqual(unexpected, []); assert.deepEqual(errors, []);
    console.log(`PASS ${width}px: restore/purge, cancelled document, exact title, failure/retry and list refresh`);
  } finally { await context.close(); }
}
(async () => { const browser = await chromium.launch({ headless: true, executablePath: process.env.HAU_TEST_CHROMIUM }); try { for (const width of [1366, 300]) await run(browser, width); } finally { await browser.close(); } })().catch(e => { console.error(e); process.exitCode = 1; });
