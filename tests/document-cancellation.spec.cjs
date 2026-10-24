// All API calls use fixtures. Never cancel live documents in this test.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { chromium } = require(process.env.HAU_PLAYWRIGHT_MODULE || 'playwright');
const web = process.env.HAU_TEST_WEB || 'http://localhost:5227';
const id = 'bbbbbbbb-0000-0000-0000-000000000001';
const version = 'bbbbbbbb-0000-0000-0000-000000000002';
const actor = 'bbbbbbbb-0000-0000-0000-000000000003';
async function run(browser, width, role) {
  const context = await browser.newContext({ viewport: { width, height: 850 }, hasTouch: width === 300, isMobile: width === 300 });
  const token = [Buffer.from('{"alg":"none"}').toString('base64url'), Buffer.from(JSON.stringify({ sub: actor, name: role,
    role, exp: Math.floor(Date.now()/1000) + 3600 })).toString('base64url'), 'fixture'].join('.');
  await context.addInitScript(value => { if (window === window.top) localStorage.setItem('auth_token', value); }, token);
  const page = await context.newPage(), unexpected = [], errors = [], writes = [];
  page.setDefaultTimeout(15000); page.on('pageerror', error => errors.push(error.message));
  const doc = { id, title: 'Công văn đã phát hành fixture', status: 'Published', docTypeName: 'CongVanDi',
    docTypeId: actor, concurrencyVersion: version, processes: [], createdByName: 'Fixture', minioPath: 'original.pdf' };
  let cancelled = false, fail = true;
  await context.route('**/api/**', async route => {
    const request = route.request(), url = new URL(request.url()); let data;
    if (request.method() === 'GET' && url.pathname === '/api/documents/types') data = [{ id: actor, typeName: 'CongVanDi' }];
    else if (request.method() === 'GET' && url.pathname === '/api/documents') data = { items: cancelled ? [] : [doc], totalCount: cancelled ? 0 : 1, page: 1, pageSize: 15 };
    else if (request.method() === 'GET' && url.pathname === '/api/documents/deleted')
      data = { items: [{ ...doc, isDeleted: true, isCancelled: true, deleteReason: 'Sai hồ sơ', processes: [{ action: 'Cancel', comment: 'Sai hồ sơ', timestamp: '2026-10-24T09:00:00Z' }] }], totalCount: 1, page: 1, pageSize: 15 };
    else if (request.method() === 'POST' && url.pathname === `/api/documents/${id}/cancel`) {
      assert.equal(role, 'Admin'); const body = request.postDataJSON(); writes.push(body);
      assert.equal(body.reason, 'Sai hồ sơ'); assert.equal(body.expectedVersion, version);
      if (fail) return route.fulfill({ status: 503, contentType: 'application/json', body: JSON.stringify({ message: 'Dịch vụ tạm lỗi fixture' }) });
      cancelled = true; data = null;
    } else { unexpected.push(request.method() + ' ' + url.pathname); return route.abort(); }
    return route.fulfill({ contentType: 'application/json', body: JSON.stringify({ success: true, data }) });
  });
  try {
    await page.goto(web + '/documents');
    await page.getByRole('heading', { name: 'Danh sách Công văn', exact: true }).waitFor();
    await page.getByText(doc.title, { exact: true }).filter({ visible: true }).first().waitFor();
    if (role !== 'Admin') {
      assert.equal(await page.locator('.document-action-trigger').count(), 0);
      if (width === 300) {
        const card = page.locator('.document-mobile-card');
        await card.dispatchEvent('touchstart'); await page.waitForTimeout(650); await card.dispatchEvent('touchend');
        assert.equal(await page.getByRole('menu').count(), 0);
      }
    } else {
      if (width === 300) {
        assert.equal(await page.locator('.document-action-trigger:visible').count(), 0);
        const card = page.locator('.document-mobile-card');
        await card.dispatchEvent('touchstart'); await card.dispatchEvent('touchmove'); await page.waitForTimeout(650);
        assert.equal(await page.getByRole('menu').count(), 0);
        const title = page.locator('.document-mobile-title'); await title.scrollIntoViewIfNeeded();
        const box = await title.boundingBox(), cdp = await context.newCDPSession(page);
        await cdp.send('Input.dispatchTouchEvent', { type: 'touchStart', touchPoints: [{ x: box.x + 30, y: box.y + 10 }] });
        await page.waitForTimeout(700);
        await page.getByRole('menuitem', { name: /Hủy công văn/ }).waitFor();
        await cdp.send('Input.dispatchTouchEvent', { type: 'touchEnd', touchPoints: [] });
        await cdp.detach();
      } else await page.getByRole('button', { name: 'Thao tác công văn ' + doc.title, exact: true }).click();
      await page.getByRole('menuitem', { name: /Hủy công văn/ }).waitFor();
      assert.equal(new URL(page.url()).pathname, '/documents');
      if (process.env.HAU_TEST_ARTIFACTS) {
        fs.mkdirSync(process.env.HAU_TEST_ARTIFACTS, { recursive: true });
        await page.screenshot({ path: path.join(process.env.HAU_TEST_ARTIFACTS, `menu-${width}.png`) });
      }
      if (width === 300) await page.getByRole('menuitem', { name: /Hủy công văn/ }).tap();
      else await page.getByRole('menuitem', { name: /Hủy công văn/ }).click();
      const dialog = page.getByRole('dialog', { name: 'Hủy công văn', exact: true });
      const confirm = dialog.getByRole('button', { name: 'Hủy công văn', exact: true });
      assert(await confirm.isDisabled());
      await dialog.locator('textarea').fill('Sai hồ sơ');
      await confirm.click();
      await dialog.getByRole('alert').filter({ hasText: 'Dịch vụ tạm lỗi fixture' }).waitFor();
      assert.equal(await dialog.locator('textarea').inputValue(), 'Sai hồ sơ');
      if (process.env.HAU_TEST_ARTIFACTS) await page.screenshot({ path: path.join(process.env.HAU_TEST_ARTIFACTS, `confirm-${width}.png`) });
      fail = false; await confirm.click(); await dialog.waitFor({ state: 'hidden' });
      await page.getByText('Không có công văn nào', { exact: true }).waitFor();
      assert.equal(writes.length, 2);
      await page.goto(web + '/documents/deleted');
      await page.getByText('Đã hủy', { exact: true }).waitFor();
      assert.equal(await page.getByRole('button', { name: 'Khôi phục', exact: true }).count(), 0);
    }
    assert.deepEqual(unexpected, []); assert.deepEqual(errors, []);
    console.log(`PASS ${width}px ${role}: cancellation permissions, menu, confirmation, failure/retry and deleted record`);
  } catch (error) {
    console.error(JSON.stringify({ width, role, writes, unexpected, errors, url: page.url(), dialog: await page.locator('.modal').innerText().catch(() => 'no dialog') }));
    throw error;
  } finally { await context.close(); }
}
(async () => {
  const browser = await chromium.launch({ headless: true, executablePath: process.env.HAU_TEST_CHROMIUM });
  try { for (const width of process.env.HAU_TEST_WIDTHS ? process.env.HAU_TEST_WIDTHS.split(',').map(Number) : [1366, 300]) for (const role of ['Admin', 'Specialist']) await run(browser, width, role); }
  finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
