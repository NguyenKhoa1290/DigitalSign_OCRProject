// Browser fixtures only. Every API call is intercepted; no live documents or certificates are changed.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { chromium } = require(process.env.HAU_PLAYWRIGHT_MODULE || 'playwright');
const web = process.env.HAU_TEST_WEB || 'http://localhost:5227';
const id = 'eeeeeeee-0000-0000-0000-000000000001';
const actor = 'eeeeeeee-0000-0000-0000-000000000002';
const department = 'eeeeeeee-0000-0000-0000-000000000003';
const specialist = 'eeeeeeee-0000-0000-0000-000000000004';
const version = 'eeeeeeee-0000-0000-0000-000000000005';
const nextVersion = 'eeeeeeee-0000-0000-0000-000000000006';
const artifacts = process.env.HAU_TEST_ARTIFACTS;
async function check(browser, width, role, status, type = 'CongVanDen', wrongUnit = false) {
  const context = await browser.newContext({ viewport: { width, height: width === 300 ? 740 : 900 } });
  const token = [Buffer.from('{"alg":"none"}').toString('base64url'), Buffer.from(JSON.stringify({
    sub: actor, name: 'Fixture ' + role, role, exp: Math.floor(Date.now() / 1000) + 3600
  })).toString('base64url'), 'fixture'].join('.');
  await context.addInitScript(value => { if (window === window.top) localStorage.setItem('auth_token', value); }, token);
  const page = await context.newPage(), errors = [], unexpected = [], writes = [];
  page.setDefaultTimeout(15000); page.on('pageerror', error => errors.push(error.message));
  const document = { id, title: 'Công văn fixture', status, docTypeName: type,
    docTypeId: 'aaaaaaaa-0000-0000-0000-000000000001', concurrencyVersion: version,
    minioPath: 'fixture.pdf', fileName: 'fixture.pdf', fileContentType: 'application/pdf',
    processingDepartmentId: department, processingDepartmentName: 'Khoa fixture',
    assignedToId: status === 'IncomingAssigned' ? specialist : null,
    assignedToName: status === 'IncomingAssigned' ? 'Chuyên viên fixture' : null, processes: [] };
  let failSigning = true;
  await context.route('**/api/**', async route => {
    const req = route.request(), url = new URL(req.url()), method = req.method();
    let data, responseStatus = 200;
    if (method === 'GET' && url.pathname === `/api/documents/${id}`) data = document;
    else if (method === 'GET' && url.pathname === `/api/documents/${id}/file`)
      return route.fulfill({ contentType: 'application/pdf', body: Buffer.from('%PDF-1.4\nfixture') });
    else if (method === 'GET' && url.pathname === `/api/users/${actor}`)
      data = { id: actor, fullName: 'Fixture Manager', departmentId: wrongUnit ? actor : department, roles: [role] };
    else if (method === 'GET' && url.pathname === '/api/departments')
      data = [{ id: department, deptName: 'Khoa fixture', deptCode: 'FIX', parentId: null }];
    else if (method === 'GET' && url.pathname === '/api/users/assignees') {
      assert.equal(url.searchParams.get('departmentId'), department);
      assert.equal(url.searchParams.get('role'), 'Specialist');
      data = [{ id: specialist, username: 'GV_FIX', fullName: 'Chuyên viên fixture', departmentName: 'Khoa fixture' }];
    } else if (method === 'POST') {
      const body = req.postDataJSON(); writes.push({ path: url.pathname, body });
      if (url.pathname === `/api/documents/${id}/incoming-submit`) {
        assert.equal(body.expectedVersion, version);
        document.status = 'PendingDirection'; document.concurrencyVersion = nextVersion; data = document;
      } else if (url.pathname === `/api/documents/${id}/submit` && type !== 'CongVanDen') {
        document.status = 'PendingDeptReview'; data = document;
      } else if (url.pathname === `/api/signatures/incoming/${id}/direction` || url.pathname === `/api/signatures/incoming/${id}/assignment`) {
        assert.equal(body.ExpectedVersion, version);
        assert(body.Comment.length > 0);
        if (failSigning) return route.fulfill({ status: 503, contentType: 'application/json',
          body: JSON.stringify({ success: false, message: 'Chưa ký được PDF fixture. Nội dung được giữ lại.' }) });
        if (url.pathname.endsWith('/direction')) {
          assert.equal(body.DepartmentId, department); document.status = 'PendingAssignment';
        } else {
          assert.equal(body.ToUserId, specialist); document.status = 'IncomingAssigned';
          document.assignedToId = specialist; document.assignedToName = 'Chuyên viên fixture';
        }
        document.concurrencyVersion = nextVersion; data = { docId: id, signatureId: actor };
      } else { unexpected.push(`${method} ${url.pathname}`); return route.abort(); }
    } else { unexpected.push(`${method} ${url.pathname}`); return route.abort(); }
    return route.fulfill({ status: responseStatus, contentType: 'application/json', body: JSON.stringify({ success: true, data }) });
  });
  try {
    await page.goto(web + '/documents/' + id);
    await page.getByRole('heading', { name: 'Công văn fixture', exact: true }).waitFor();
    const button = name => page.getByRole('button', { name, exact: true });
    if (type === 'CongVanDen') {
      assert.equal(await button('Nộp trình duyệt').count(), 0);
      assert.equal(await button('Ký nháy').count(), 0);
      assert.equal(await button('Ký số pháp nhân').count(), 0);
    }
    if (role === 'Clerk' && status === 'Draft') {
      assert.equal(await button('Phân công xử lý').count(), 0);
      const label = type === 'CongVanDen' ? 'Trình BGH chỉ đạo' : 'Nộp trình duyệt';
      await button(label).click();
      await page.locator('.modal-overlay .modal-footer .btn:not(.btn-outline)').click();
      await page.locator('.modal-overlay').waitFor({ state: 'hidden' });
      await page.getByText(type === 'CongVanDen' ? 'Chờ BGH ký chỉ đạo' : 'Chờ duyệt phòng', { exact: true }).waitFor();
      assert.equal(writes.length, 1);
    } else if (role === 'BoardOfDirectors' && status === 'PendingDirection') {
      await button('Ký chỉ đạo').click();
      const modal = page.getByRole('dialog', { name: 'Ký chỉ đạo công văn đến', exact: true });
      await modal.locator('#incoming-department').click();
      await modal.getByRole('option', { name: 'Khoa fixture (FIX)', exact: true }).click();
      await modal.locator('#incoming-comment').fill('Triển khai công việc fixture');
      await modal.getByRole('button', { name: 'Ký và chuyển đơn vị', exact: true }).click();
      await modal.getByRole('alert').filter({ hasText: 'Chưa ký được PDF fixture' }).waitFor();
      assert.equal(await modal.locator('#incoming-comment').inputValue(), 'Triển khai công việc fixture');
      if (artifacts) { fs.mkdirSync(artifacts, { recursive: true }); await page.screenshot({ path: path.join(artifacts, `direction-${width}.png`) }); }
      failSigning = false;
      await modal.getByRole('button', { name: 'Ký và chuyển đơn vị', exact: true }).click();
      await modal.waitFor({ state: 'hidden' });
      await page.getByText('Chờ ký phân công', { exact: true }).waitFor();
      assert.equal(await button('Phân công xử lý').count(), 0);
      assert.equal(writes.length, 2);
    } else if (role === 'Manager' && status === 'PendingAssignment' && !wrongUnit) {
      await button('Phân công xử lý').click();
      const modal = page.getByRole('dialog', { name: 'Phân công xử lý', exact: true });
      await modal.locator('#assignee-select').click();
      await modal.getByRole('option', { name: /Chuyên viên fixture/ }).click();
      await modal.locator('#assign-comment').fill('Xử lý và báo cáo kết quả fixture');
      await modal.getByRole('button', { name: 'Ký và phân công', exact: true }).click();
      await modal.getByRole('alert').filter({ hasText: 'Chưa ký được PDF fixture' }).waitFor();
      assert.equal(await modal.locator('#assign-comment').inputValue(), 'Xử lý và báo cáo kết quả fixture');
      if (artifacts) await page.screenshot({ path: path.join(artifacts, `assignment-${width}.png`) });
      failSigning = false;
      await modal.getByRole('button', { name: 'Ký và phân công', exact: true }).click();
      await modal.waitFor({ state: 'hidden' });
      await page.getByText('Đã phân công xử lý', { exact: true }).waitFor();
      await page.getByText('Chuyên viên fixture', { exact: true }).waitFor();
      assert.equal(writes.length, 2);
    } else {
      assert.equal(await button('Phân công xử lý').count(), 0);
      assert.equal(await button('Ký chỉ đạo').count(), 0);
      assert.equal(writes.length, 0);
      if (status === 'PendingDeptReview') await page.getByRole('status').filter({ hasText: 'quy trình cũ' }).waitFor();
    }
    assert(await page.locator('.page-container').evaluate(el => el.scrollWidth <= el.clientWidth + 1));
    assert.deepEqual(errors, []); assert.deepEqual(unexpected, []);
    console.log(`PASS ${width}px ${role} ${type} ${status}${wrongUnit ? ' other unit' : ''}`);
  } finally { await context.close(); }
}
(async () => {
  const browser = await chromium.launch({ headless: true, executablePath: process.env.HAU_TEST_CHROMIUM });
  try {
    for (const width of process.env.HAU_TEST_WIDTHS ? process.env.HAU_TEST_WIDTHS.split(',').map(Number) : [1366, 300]) {
      await check(browser, width, 'Clerk', 'Draft');
      await check(browser, width, 'BoardOfDirectors', 'PendingDirection');
      await check(browser, width, 'Manager', 'PendingAssignment');
      await check(browser, width, 'Manager', 'PendingAssignment', 'CongVanDen', true);
      await check(browser, width, 'Specialist', 'IncomingAssigned');
      await check(browser, width, 'Manager', 'PendingDeptReview');
      await check(browser, width, 'Clerk', 'Draft', 'CongVanDi');
    }
  } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
