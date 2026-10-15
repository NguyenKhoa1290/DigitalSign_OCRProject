// Isolated browser fixtures: every API request is intercepted; no live database writes.
const assert = require('node:assert/strict');
const { chromium } = require(process.env.HAU_PLAYWRIGHT_MODULE || 'playwright');
const web = process.env.HAU_TEST_WEB || 'http://localhost:5227';
const departments = [
  { id: 'aaaaaaaa-0000-0000-0000-000000000001', deptName: 'Trường HAU', deptCode: 'HAU', parentId: null },
  { id: 'aaaaaaaa-0000-0000-0000-000000000002', deptName: 'Khoa Công nghệ thông tin', deptCode: 'CNTT', parentId: 'aaaaaaaa-0000-0000-0000-000000000001' },
  { id: 'aaaaaaaa-0000-0000-0000-000000000003', deptName: 'Phòng Tổng hợp', deptCode: 'TH', parentId: 'aaaaaaaa-0000-0000-0000-000000000001' },
  { id: 'aaaaaaaa-0000-0000-0000-000000000004', deptName: 'Bộ môn Phần mềm', deptCode: 'PM', parentId: 'aaaaaaaa-0000-0000-0000-000000000002' },
  { id: 'aaaaaaaa-0000-0000-0000-000000000005', deptName: 'Nhóm Nghiên cứu', deptCode: 'NC', parentId: 'aaaaaaaa-0000-0000-0000-000000000004' },
  { id: 'aaaaaaaa-0000-0000-0000-000000000006', deptName: 'Trường khác', deptCode: 'OTHER', parentId: null },
  { id: 'aaaaaaaa-0000-0000-0000-000000000007', deptName: 'Khoa khác', deptCode: 'KOTHER', parentId: 'aaaaaaaa-0000-0000-0000-000000000006' }
];
const roleId = 'bbbbbbbb-0000-0000-0000-000000000001';
const userId = 'cccccccc-0000-0000-0000-000000000001';
const token = [Buffer.from('{"alg":"none"}').toString('base64url'),
  Buffer.from(JSON.stringify({ sub: 'dddddddd-0000-0000-0000-000000000001',
    unique_name: 'fixture-admin', role: 'Admin', exp: Math.floor(Date.now() / 1000) + 3600 })).toString('base64url'),
  'fixture'].join('.');

async function check(browser, viewport) {
  const context = await browser.newContext({ viewport });
  await context.addInitScript(value => localStorage.setItem('auth_token', value), token);
  const page = await context.newPage(), errors = [], writes = [];
  page.on('pageerror', error => errors.push(error.message));
  page.setDefaultTimeout(15000);
  let users = [], departmentFailure = false, emptyDepartments = false;
  await context.route('**/api/**', async route => {
    const req = route.request(), url = new URL(req.url()), method = req.method();
    const headers = { 'Access-Control-Allow-Origin': '*', 'Access-Control-Allow-Headers': '*',
      'Access-Control-Allow-Methods': 'GET,POST,PUT,OPTIONS' };
    if (method === 'OPTIONS') return route.fulfill({ status: 204, headers });
    let data, status = 200;
    if (url.pathname === '/api/departments') {
      status = departmentFailure ? 503 : 200;
      data = emptyDepartments ? [] : departments;
    } else if (url.pathname === '/api/roles') data = [{ id: roleId, roleName: 'Specialist' }];
    else if (method === 'GET' && url.pathname === '/api/users')
      data = { items: users, page: 1, pageSize: 20, totalCount: users.length, totalPages: 1 };
    else if (method === 'POST' && url.pathname === '/api/users') {
      const body = req.postDataJSON(); writes.push(body);
      assert.equal(body.DepartmentId, departments[0].id);
      assert.equal(body.Email, null);
      assert.equal(body.PhoneNumber, '0912345678');
      assert.deepEqual(body.RoleIds, [roleId]);
      users = [{ id: userId, username: body.Username, fullName: body.FullName, email: null,
        phoneNumber: body.PhoneNumber, roles: ['Specialist'], departmentId: body.DepartmentId,
        departmentName: departments[0].deptName, isActive: true, createdAt: '2026-10-14T00:00:00Z' }];
      data = users[0]; status = 201;
    } else if (method === 'PUT' && url.pathname === `/api/users/${userId}`) {
      const body = req.postDataJSON(); writes.push(body);
      assert.equal(body.Email, null, 'New accounts without email must not submit an invalid empty email');
      assert.equal(body.PhoneNumber, null, 'Blank optional phone must be null');
      users[0] = { ...users[0], fullName: body.FullName, phoneNumber: body.PhoneNumber,
        departmentId: body.DepartmentId,
        departmentName: departments.find(d => d.id === body.DepartmentId)?.deptName ?? null };
      data = users[0];
    } else throw new Error(`Unexpected fixture request: ${method} ${url.pathname}`);
    await route.fulfill({ status, contentType: 'application/json', headers,
      body: JSON.stringify({ success: status < 400, data }) });
  });
  const modal = page.locator('.user-modal');
  const input = label => modal.locator('.form-group').filter({ has: page.getByText(label, { exact: true }) }).locator('input');
  async function chooseDepartment(level, label) {
    await modal.locator(`#user-department-level-${level}`).click();
    await modal.getByRole('option', { name: label, exact: true }).click();
  }
  async function save(label) {
    const response = page.waitForResponse(r => r.request().method() !== 'GET' && /\/api\/users(?:\/[^/]+)?$/.test(new URL(r.url()).pathname));
    await modal.getByRole('button', { name: label, exact: true }).click();
    assert((await response).ok());
    await modal.waitFor({ state: 'hidden' });
    await page.locator('.pagination-info').waitFor();
  }
  async function edit() {
    if (viewport.width >= 768) await page.getByRole('button', { name: /Mở thao tác cho/ }).click();
    else {
      const card = page.locator('.user-mobile-card');
      await card.dispatchEvent('touchstart');
      await page.getByRole('menu').waitFor();
      await card.dispatchEvent('touchend');
    }
    await page.getByRole('menuitem', { name: 'Sửa', exact: true }).click();
    await modal.waitFor();
  }
  await page.goto(web + '/admin/users');
  await page.getByText('Không tìm thấy người dùng', { exact: true }).waitFor();
  await page.getByRole('button', { name: 'Thêm người dùng', exact: true }).click();
  await input('Họ tên').fill('Người dùng thử nghiệm');
  await input('Mã cán bộ/giảng viên').fill('GV_TEST');
  await input('Mật khẩu').fill('Temporary123!');
  await input('Số điện thoại').fill('0912345678');
  await modal.locator('#user-role-select').click();
  await modal.getByRole('option', { name: 'Chuyên viên', exact: true }).click();
  assert(await modal.locator('#user-department-level-1').isDisabled());
  await chooseDepartment(0, 'Trường HAU (HAU)');
  await save('Thêm');
  await edit();
  assert.equal(await modal.locator('#user-department-level-0').innerText(), 'Trường HAU (HAU)');
  await input('Số điện thoại').fill('');
  await input('Họ tên').fill('Người dùng đã sửa');
  await save('Lưu thay đổi');
  assert.equal(writes.at(-1).DepartmentId, departments[0].id, 'Editing other fields preserves the existing assignment');
  await edit();
  await chooseDepartment(1, 'Khoa Công nghệ thông tin (CNTT)');
  await save('Lưu thay đổi');
  assert.equal(writes.at(-1).DepartmentId, departments[1].id);
  await edit();
  assert.equal(await modal.locator('#user-department-level-0').innerText(), 'Trường HAU (HAU)');
  assert.equal(await modal.locator('#user-department-level-1').innerText(), 'Khoa Công nghệ thông tin (CNTT)');
  await chooseDepartment(2, 'Bộ môn Phần mềm (PM)');
  await save('Lưu thay đổi');
  assert.equal(writes.at(-1).DepartmentId, departments[3].id);
  await edit();
  await chooseDepartment(3, 'Nhóm Nghiên cứu (NC)');
  await save('Lưu thay đổi');
  assert.equal(writes.at(-1).DepartmentId, departments[4].id, 'Deeper hierarchies must not lose existing assignments');
  await edit();
  assert.equal(await modal.locator('#user-department-level-3').innerText(), 'Nhóm Nghiên cứu (NC)');
  await chooseDepartment(2, 'Thuộc đơn vị cha đã chọn');
  assert.equal(await modal.locator('#user-department-level-3').count(), 0);
  await save('Lưu thay đổi');
  assert.equal(writes.at(-1).DepartmentId, departments[1].id, 'Omitting the subunit assigns to the selected faculty');
  await edit();
  await chooseDepartment(1, 'Thuộc đơn vị cha đã chọn');
  await save('Lưu thay đổi');
  assert.equal(writes.at(-1).DepartmentId, departments[0].id, 'Omitting the faculty assigns to the selected school');
  await edit();
  await chooseDepartment(1, 'Khoa Công nghệ thông tin (CNTT)');
  await chooseDepartment(2, 'Bộ môn Phần mềm (PM)');
  await chooseDepartment(0, 'Trường khác (OTHER)');
  assert.equal(await modal.locator('#user-department-level-1').innerText(), 'Thuộc đơn vị cha đã chọn');
  assert(await modal.locator('#user-department-level-2').isDisabled());
  await modal.locator('#user-department-level-1').click();
  assert.equal(await modal.getByRole('option', { name: 'Khoa Công nghệ thông tin (CNTT)', exact: true }).count(), 0);
  await modal.getByRole('option', { name: 'Thuộc đơn vị cha đã chọn', exact: true }).click();
  await save('Lưu thay đổi');
  assert.equal(writes.at(-1).DepartmentId, departments[5].id, 'Changing school must clear all old descendant choices');
  await edit();
  await chooseDepartment(0, 'Chưa phân phòng ban');
  await save('Lưu thay đổi');
  assert.equal(writes.at(-1).DepartmentId, null);
  departmentFailure = true;
  await page.getByRole('button', { name: 'Thêm người dùng', exact: true }).click();
  await page.getByText('Không tải được danh sách phòng ban. Vui lòng thử lại.', { exact: true }).waitFor();
  assert.equal(await modal.count(), 0, 'A failed department request must not silently show an empty selector');
  departmentFailure = false; emptyDepartments = true;
  await page.getByRole('button', { name: 'Thêm người dùng', exact: true }).click();
  await page.getByText('Chưa có phòng ban. Tạo phòng ban tại mục Phòng ban trước khi phân người dùng.', { exact: true }).waitFor();
  assert(await modal.evaluate(element => element.scrollWidth <= element.clientWidth), 'Form must fit the viewport');
  assert.deepEqual(errors, []);
  await context.close();
  console.log(`PASS ${viewport.width}px: create, preserve, cascading choices, parent fallback, four levels, branch reset, remove and failed/empty list`);
}
(async () => {
  const browser = await chromium.launch({ headless: true, executablePath: process.env.HAU_TEST_CHROMIUM });
  try {
    for (const viewport of [{ width: 1366, height: 768 }, { width: 390, height: 844 }, { width: 300, height: 740 }])
      await check(browser, viewport);
  } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
