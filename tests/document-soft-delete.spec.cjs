// Intercepted APIs only. Never deletes/restores application documents or calls OCR.
const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const path = require('node:path');
const { chromium } = require(process.env.HAU_PLAYWRIGHT_MODULE || 'playwright');
const web = process.env.HAU_TEST_WEB || 'http://localhost:5227';
const id = 'dddddddd-0000-0000-0000-000000000001';
const owner = 'cccccccc-0000-0000-0000-000000000001';
const pageResult = items => ({ items, totalCount: items.length, page: 1, pageSize: 15, totalPages: 1, hasNextPage: false, hasPreviousPage: false });

async function setup(browser, width, role, own = true, eligible = true) {
  const context = await browser.newContext({ viewport: { width, height: 844 } });
  const token = [Buffer.from('{"alg":"none"}').toString('base64url'), Buffer.from(JSON.stringify({
    sub: own ? owner : 'cccccccc-0000-0000-0000-000000000002', role, name: 'Deletion fixture',
    exp: Math.floor(Date.now() / 1000) + 3600 })).toString('base64url'), 'fixture'].join('.');
  await context.addInitScript(token => { if (location.protocol.startsWith('http')) localStorage.setItem('auth_token', token); }, token);
  let deleted = false, restoreCalls = 0, deleteCalls = 0, conflict = false;
  const doc = { id, title: 'Bản nháp kiểm thử', createdById: owner, createdAt: new Date().toISOString(),
    status: 'Draft', canDeleteDraft: eligible, docTypeName: 'CongVanDi', minioPath: '', processes: [] };
  await context.route('**/api/**', async route => {
    const req = route.request(), path = new URL(req.url()).pathname; let data;
    if (req.method() === 'DELETE' && path === `/api/documents/${id}`) {
      deleteCalls++;
      if (role === 'Admin') assert.ok(req.postDataJSON().reason.trim());
      if (conflict) return route.fulfill({ status: 409, json: { success: false, message: 'Công văn đã được phân công. Hãy tải lại.' } });
      deleted = true; data = null;
    } else if (req.method() === 'POST' && path === `/api/documents/${id}/restore`) {
      restoreCalls++; assert.ok(req.postDataJSON().reason.trim()); deleted = false; data = null;
    } else if (req.method() === 'GET' && path === `/api/documents/${id}`) data = doc;
    else if (req.method() === 'GET' && path === '/api/documents/deleted') data = pageResult(deleted ? [{ ...doc,
      isDeleted: true, deletedAt: new Date().toISOString(), deleteReason: 'Nhập nhầm',
      processes: [{ action: 'SoftDelete', comment: 'Nhập nhầm', timestamp: new Date().toISOString() }] }] : []);
    else if (req.method() === 'GET' && path === '/api/documents') data = pageResult(deleted ? [] : [doc]);
    else if (req.method() === 'GET' && path === '/api/documents/types') data = [];
    else throw new Error(`Unexpected fixture API: ${req.method()} ${path}`);
    await route.fulfill({ json: { success: true, data } });
  });
  const page = await context.newPage(), errors = [];
  page.on('pageerror', e => errors.push(e.message)); page.setDefaultTimeout(15000);
  return { context, page, errors, stats: () => ({ deleteCalls, restoreCalls }), conflict: value => { conflict = value; } };
}

async function check(browser, width, role, own, eligible, allowed) {
  const f = await setup(browser, width, role, own, eligible); const { page } = f;
  await page.goto(`${web}/documents/${id}`); await page.getByRole('heading', { name: 'Bản nháp kiểm thử' }).waitFor();
  const remove = page.getByRole('button', { name: 'Xóa bản nháp', exact: true });
  assert.equal(await remove.count(), allowed ? 1 : 0);
  if (allowed) {
    await remove.click(); const dialog = page.getByRole('dialog', { name: 'Xóa bản nháp' });
    await dialog.waitFor(); const confirm = dialog.getByRole('button', { name: 'Xóa bản nháp', exact: true });
    assert.equal(await dialog.getByRole('alert').count(), 0);
    if (process.env.HAU_DELETION_SCREENSHOTS && role === 'Admin') {
      await fs.mkdir(process.env.HAU_DELETION_SCREENSHOTS, { recursive: true });
      await page.screenshot({ path: path.join(process.env.HAU_DELETION_SCREENSHOTS, `delete-${width}.png`), fullPage: true, animations: 'disabled' });
    }
    if (role === 'Admin') { assert.equal(await confirm.isDisabled(), true); await dialog.getByLabel('Lý do (bắt buộc)').fill('Hỗ trợ nhập trùng'); }
    await dialog.getByRole('button', { name: 'Hủy', exact: true }).click(); assert.equal(f.stats().deleteCalls, 0);
    // Show a backend conflict without navigating away or reporting false success.
    f.conflict(true); await remove.click();
    if (role === 'Admin') await dialog.getByLabel('Lý do (bắt buộc)').fill('Hỗ trợ nhập trùng');
    await confirm.click(); await dialog.getByText('Công văn đã được phân công. Hãy tải lại.', { exact: true }).waitFor(); assert.equal(f.stats().deleteCalls, 1);
    assert.ok(page.url().endsWith(id));
    f.conflict(false); await confirm.click(); await page.waitForURL(`${web}/documents`); assert.equal(f.stats().deleteCalls, 2);
    if (role === 'Admin') {
      await page.getByRole('link', { name: 'Công văn đã xóa', exact: true }).click();
      await page.waitForFunction(() => document.querySelector('.page-container').scrollTop < 1);
      if (process.env.HAU_DELETION_SCREENSHOTS) {
        await page.getByRole('button', { name: 'Khôi phục', exact: true }).waitFor();
        await page.screenshot({ path: path.join(process.env.HAU_DELETION_SCREENSHOTS, `deleted-${width}.png`), fullPage: true, animations: 'disabled' });
      }
      await page.getByRole('button', { name: 'Khôi phục', exact: true }).click();
      const restoreDialog = page.getByRole('dialog', { name: 'Khôi phục bản nháp' });
      const restore = restoreDialog.getByRole('button', { name: 'Khôi phục', exact: true });
      assert.equal(await restore.isDisabled(), true);
      await restoreDialog.getByLabel('Lý do (bắt buộc)').fill('Đã đối chiếu hồ sơ'); await restore.click();
      await page.getByText('Không có công văn đã xóa', { exact: true }).waitFor();
      assert.equal(f.stats().restoreCalls, 1);
    }
  }
  assert.equal(await page.evaluate(() => document.documentElement.scrollWidth > window.innerWidth + 1), false);
  assert.deepEqual(f.errors, []);
  console.log(`PASS ${width}px ${role} owner=${own} eligible=${eligible} delete=${allowed}`);
  await f.context.close();
}
(async () => {
  const browser = await chromium.launch({ headless: true, executablePath: process.env.HAU_TEST_CHROMIUM });
  try {
    const cases = process.env.HAU_DELETION_VISUAL_ONLY ? [['Admin', false, true, true]] : [
      ['Specialist', true, true, true], ['Clerk', true, true, true], ['Admin', false, true, true],
      ['Specialist', false, true, false], ['Manager', true, true, false], ['BoardOfDirectors', true, true, false],
      ['Specialist', true, false, false]
    ];
    for (const width of [1366, 300]) for (const args of cases) await check(browser, width, ...args);
  } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
