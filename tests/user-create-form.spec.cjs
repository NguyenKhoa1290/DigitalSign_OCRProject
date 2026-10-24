// Isolated browser fixtures: all API requests are intercepted; no live user records are changed.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { chromium } = require(process.env.HAU_PLAYWRIGHT_MODULE || 'playwright');
const web = process.env.HAU_TEST_WEB || 'http://localhost:5227';
const userId = 'cccccccc-0000-0000-0000-000000000001';
const roleId = 'bbbbbbbb-0000-0000-0000-000000000001';
const token = [Buffer.from('{"alg":"none"}').toString('base64url'),
  Buffer.from(JSON.stringify({ sub: userId, name: 'Fixture Admin', role: 'Admin',
    exp: Math.floor(Date.now() / 1000) + 3600 })).toString('base64url'), 'fixture'].join('.');

async function check(browser, viewport) {
  const context = await browser.newContext({ viewport });
  await context.addInitScript(value => localStorage.setItem('auth_token', value), token);
  const page = await context.newPage(), errors = [], unexpected = [], posts = [];
  page.on('pageerror', error => errors.push(error.message));
  page.setDefaultTimeout(15000);
  let responseMode = 'username', users = [], rolesAvailable = true, releaseRoles, releaseSave;
  await context.route('**/api/**', async route => {
    const req = route.request(), path = new URL(req.url()).pathname, method = req.method();
    let data, status = 200, message;
    if (method === 'GET' && path === '/api/users')
      data = { items: users, page: 1, pageSize: 20, totalCount: users.length, totalPages: 1 };
    else if (method === 'GET' && path === '/api/departments') data = [];
    else if (method === 'GET' && path === '/api/roles') {
      if (releaseRoles) await new Promise(resolve => { releaseRoles.resolve = resolve; });
      data = rolesAvailable ? [{ id: roleId, roleName: 'Specialist' }] : [];
    } else if (method === 'POST' && path === '/api/users') {
      const body = req.postDataJSON(); posts.push(body);
      if (releaseSave) await new Promise(resolve => { releaseSave.resolve = resolve; });
      if (responseMode === 'unavailable')
        return route.fulfill({ status: 503, contentType: 'text/html', body: '<h1>Unavailable</h1>' });
      if (responseMode === 'validation') {
        status = 400;
        data = null;
        return route.fulfill({ status, contentType: 'application/problem+json', body: JSON.stringify({
          title: 'One or more validation errors occurred.',
          errors: { Username: ['Mã cán bộ/giảng viên phải từ 3 đến 50 ký tự.'] }
        }) });
      }
      if (responseMode === 'success') {
        assert.deepEqual(body.RoleIds, [roleId]);
        users = [{ id: userId, username: body.Username, fullName: body.FullName, email: body.Email,
          roles: ['Specialist'], isActive: true, createdAt: '2026-10-19T00:00:00Z' }];
        data = users[0]; status = 201;
      } else {
        status = 409;
        message = responseMode === 'username'
          ? "Người dùng với username 'GV_FIXTURE' đã tồn tại trong hệ thống."
          : "Người dùng với email 'fixture@example.test' đã tồn tại trong hệ thống.";
      }
    } else {
      unexpected.push(`${method} ${path}`);
      return route.abort();
    }
    await route.fulfill({ status, contentType: 'application/json',
      body: JSON.stringify({ success: status < 400, data, message }) });
  });
  try {
    await page.goto(web + '/admin/users');
    await page.getByRole('button', { name: 'Thêm người dùng', exact: true }).click();
    const modal = page.locator('.user-modal');
    const input = label => modal.locator('.form-group')
      .filter({ has: page.getByText(label, { exact: true }) }).locator('input');
    const name = input('Họ tên'), username = input('Mã cán bộ/giảng viên');
    const email = modal.locator('input[type=email]'), password = input('Mật khẩu');
    await name.fill('Người dùng thử nghiệm');
    await username.fill('GV_FIXTURE');
    await email.fill('fixture@example.test');
    await password.fill('Temporary123!');
    await modal.locator('#user-role-select').click();
    await modal.getByRole('option', { name: 'Chuyên viên', exact: true }).click();

    // Leave the form open while typing slowly, then click outside it without discarding the draft.
    if (viewport.width === 1366) await page.waitForTimeout(35000);
    assert(await modal.isVisible(), 'An idle form must remain open');
    await page.mouse.click(2, 2);
    assert(await modal.isVisible(), 'Backdrop clicks must not discard a user being entered');
    assert.equal(await name.inputValue(), 'Người dùng thử nghiệm');

    const save = modal.locator('.modal-footer .btn-primary');
    const error = modal.getByRole('alert');
    async function submitError(mode, text) {
      responseMode = mode;
      await save.click();
      await error.filter({ hasText: text }).waitFor();
      assert(await modal.isVisible());
      assert.equal(await name.inputValue(), 'Người dùng thử nghiệm');
      assert.equal(await username.inputValue(), 'GV_FIXTURE');
      assert.equal(await email.inputValue(), 'fixture@example.test');
      assert.equal(await password.inputValue(), 'Temporary123!');
      assert.equal(new URL(page.url()).pathname, '/admin/users');
      await save.waitFor({ state: 'visible' });
      assert(await save.isEnabled(), 'Errors must allow correction and retry');
    }
    await submitError('username', 'Mã cán bộ/giảng viên này đã tồn tại');
    if (process.env.HAU_TEST_ARTIFACTS) {
      fs.mkdirSync(process.env.HAU_TEST_ARTIFACTS, { recursive: true });
      await error.scrollIntoViewIfNeeded();
      await page.screenshot({ path: path.join(process.env.HAU_TEST_ARTIFACTS, `user-conflict-${viewport.width}.png`) });
    }
    await submitError('email', 'Email này đã được sử dụng');
    await submitError('validation', 'Mã cán bộ/giảng viên phải từ 3 đến 50 ký tự.');
    await submitError('unavailable', 'Không thể tạo người dùng');
    rolesAvailable = false;
    const beforeMissingRole = posts.length;
    await submitError('success', 'Không tải được vai trò đã chọn');
    assert.equal(posts.length, beforeMissingRole, 'Do not create an account without its intended role');
    rolesAvailable = true;

    responseMode = 'success';
    releaseRoles = {};
    const beforeSave = posts.length;
    await save.dispatchEvent('click');
    await page.waitForFunction(() => document.querySelector('.user-modal .btn-primary')?.disabled);
    await save.dispatchEvent('click');
    await modal.getByRole('button', { name: 'Hủy', exact: true }).dispatchEvent('click');
    await modal.locator('.modal-close').dispatchEvent('click');
    assert(await modal.isVisible(), 'The form cannot be closed during a pending save');
    async function waitForPending(pending) {
      const deadline = Date.now() + 15000;
      while (!pending.resolve) {
        assert(Date.now() < deadline, 'Expected fixture request did not arrive');
        await page.waitForTimeout(20);
      }
    }
    await waitForPending(releaseRoles);
    releaseSave = {};
    releaseRoles.resolve(); releaseRoles = null;
    await waitForPending(releaseSave);
    assert.equal(posts.length, beforeSave + 1, 'Repeated clicks must send exactly one create request');
    releaseSave.resolve(); releaseSave = null;
    await modal.waitFor({ state: 'hidden' });
    await page.locator('.pagination-info').filter({ hasText: '1 người dùng' }).waitFor();
    assert.equal(posts.length, beforeSave + 1);

    await page.getByRole('button', { name: 'Thêm người dùng', exact: true }).click();
    assert.equal(await name.inputValue(), '', 'A new form must start clean');
    assert.equal(await error.count(), 0);
    await modal.getByRole('button', { name: 'Hủy', exact: true }).click();
    await modal.waitFor({ state: 'hidden' });
    await page.getByRole('button', { name: 'Thêm người dùng', exact: true }).click();
    await modal.locator('.modal-close').click();
    await modal.waitFor({ state: 'hidden' });
    assert.deepEqual(errors, []);
    assert.deepEqual(unexpected, []);
    console.log(`PASS ${viewport.width}px: preserve form, specific conflicts, validation, unavailable API/role, single pending save, explicit close`);
  } finally {
    releaseRoles?.resolve?.(); releaseSave?.resolve?.();
    await context.close();
  }
}
(async () => {
  const browser = await chromium.launch({ headless: true, executablePath: process.env.HAU_TEST_CHROMIUM });
  try {
    for (const viewport of [{ width: 1366, height: 900 }, { width: 300, height: 740 }])
      await check(browser, viewport);
  } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
