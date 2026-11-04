// Read-only image fixture on the deployed site; no upload, OCR or database writes.
const assert = require('node:assert/strict');
const fs = require('node:fs'), path = require('node:path');
const { chromium } = require(process.env.HAU_PLAYWRIGHT_MODULE || 'playwright');
const base = process.env.HAU_TEST_WEB || 'https://hauquanlycongvan.com';
const id = 'bbbbbbbb-0000-0000-0000-000000000001';
async function run(browser, width) {
  const context = await browser.newContext({ viewport: { width, height: 780 }, isMobile: true, hasTouch: true, deviceScaleFactor: 3 });
  const token = [Buffer.from('{"alg":"none"}').toString('base64url'), Buffer.from(JSON.stringify({ sub: id, name: 'Scroll fixture', role: 'Specialist', exp: Math.floor(Date.now()/1000)+3600 })).toString('base64url'), 'fixture'].join('.');
  await context.addInitScript(t => { if (window === window.top) localStorage.setItem('auth_token', t); }, token);
  let fileRequests = 0;
  const unexpected = [], errors = [];
  await context.route('**/api/**', route => {
    const p = new URL(route.request().url()).pathname;
    if (p === '/api/notifications/stream') return route.fulfill({ contentType: 'text/event-stream', body: 'data: {"unreadCount":0}\n\n' });
    if (new URL(route.request().url()).pathname.endsWith('/ocr-jobs/latest')) return route.fulfill({json:{success:true,data:null}});
    if (p === `/api/documents/${id}`) return route.fulfill({ json: { success: true, data: {
      id, title: 'Scroll fixture', status: 'Draft', docTypeName: 'CongVanDi', createdById: id,
      createdByName: 'Scroll fixture', minioPath: 'fixture.jpg', fileName: 'fixture.jpg', fileContentType: 'image/jpeg', processes: []
    } } });
    if (p === `/api/documents/${id}/file`) { fileRequests++; return route.fulfill({ contentType: 'image/jpeg', body: fs.readFileSync('photo_2026-10-24_22-56-13.jpg') }); }
    unexpected.push(p); return route.abort();
  });
  const page = await context.newPage(); page.setDefaultTimeout(60000);
  page.on('pageerror', e => errors.push(e.message));
  try {
    await page.goto(`${base}/documents/${id}`);
    await page.waitForFunction(() => document.querySelector('.document-image')?.naturalWidth > 0);
    await page.locator('flight-taskbar').waitFor({ state: 'visible' });
    await page.locator('.document-image').scrollIntoViewIfNeeded();
    await page.waitForTimeout(1000);
    const initial = await page.evaluate(() => {
      const taskbar = document.querySelector('flight-taskbar'), canvas = taskbar.shadowRoot.querySelector('canvas');
      window.previewProbe = { resets: 0, loads: 0, textures: 0, maxScroll: 0, scrollEvents: 0 };
      document.querySelector('.page-container').addEventListener('scroll', () => window.previewProbe.scrollEvents++);
      const renderer = taskbar.renderer, upload = renderer.upload.bind(renderer);
      renderer.upload = (...args) => { window.previewProbe.textures++; return upload(...args); };
      new MutationObserver(records => window.previewProbe.resets += records.filter(r => r.attributeName === 'width' || r.attributeName === 'height').length).observe(canvas, { attributes: true });
      document.querySelector('.document-image').addEventListener('load', () => window.previewProbe.loads++);
      return { src: document.querySelector('.document-image').src, renderer: taskbar.dataset.renderer };
    });
    assert.equal(initial.renderer, 'webgl2', 'Exercise the actual WebGL taskbar, not its CSS fallback');
    const cdp = await context.newCDPSession(page);
    for (let n = 0; n < 4; n++) {
      const x = Math.floor(width/2), startY = n % 2 ? 170 : 600, sign = n % 2 ? 1 : -1;
      await cdp.send('Input.dispatchTouchEvent', { type: 'touchStart', touchPoints: [{ x, y: startY }] });
      for (let step = 1; step <= 10; step++) {
        await cdp.send('Input.dispatchTouchEvent', { type: 'touchMove', touchPoints: [{ x, y: startY + sign * step * 40 }] });
        await page.waitForTimeout(25);
      }
      await cdp.send('Input.dispatchTouchEvent', { type: 'touchEnd', touchPoints: [] });
      await page.waitForTimeout(150);
      // Browser chrome can emit repeated viewport resize/scroll events without changing taskbar size.
      await page.evaluate(() => { window.dispatchEvent(new Event('resize')); window.dispatchEvent(new Event('scroll')); });
      await page.evaluate(() => window.previewProbe.maxScroll = Math.max(window.previewProbe.maxScroll, document.querySelector('.page-container').scrollTop));
    }
    assert(await page.evaluate(() => window.previewProbe.scrollEvents > 0), 'Touch gestures must move the preview scroll container');
    for (const height of [810, 750, 780]) await page.setViewportSize({ width, height });
    await page.waitForTimeout(300);
    const result = await page.evaluate(() => {
      const image = document.querySelector('.document-image'), rect = image.getBoundingClientRect();
      const filters = [...document.querySelectorAll('.document-detail-layout .card')].map(card => getComputedStyle(card).backdropFilter);
      return { ...window.previewProbe, src: image.src, decoded: image.naturalWidth > 0, imageWidth: rect.width, filters, scroll: document.querySelector('.page-container').scrollTop };
    });
    assert.equal(result.src, initial.src); assert.equal(result.loads, 0); assert.equal(fileRequests, 1);
    assert(result.decoded); assert(result.imageWidth <= width); assert.deepEqual(unexpected, []); assert.deepEqual(errors, []);
    if (process.env.HAU_EXPECT_FIXED === '1') {
      assert.equal(result.resets, 0, 'Scrolling/resizing must not clear/reallocate the taskbar canvas');
      assert.equal(result.textures, 0, 'Browser chrome resize must reuse label/background textures');
      assert(result.filters.every(filter => filter === 'none'), 'Mobile document cards should not blur the scrolling backdrop');
    }
    assert(result.maxScroll > 0, 'Native touch gestures must actually scroll the page');
    // A real width change must still rebuild the canvas and fit the rotated viewport.
    await page.setViewportSize({ width: 780, height: width });
    await page.waitForTimeout(400);
    const rotated = await page.evaluate(() => ({ resets: window.previewProbe.resets, canvas: document.querySelector('flight-taskbar').canvas.width }));
    assert(rotated.resets > result.resets); assert(rotated.canvas > 0);
    await page.setViewportSize({ width, height: 780 }); await page.waitForTimeout(400);
    assert.deepEqual(errors, []); assert.deepEqual(unexpected, []); assert.equal(fileRequests, 1);
    if (process.env.HAU_TEST_ARTIFACTS) {
      fs.mkdirSync(process.env.HAU_TEST_ARTIFACTS, { recursive: true });
      await page.locator('.document-image').scrollIntoViewIfNeeded();
      await page.screenshot({ path: path.join(process.env.HAU_TEST_ARTIFACTS, `scroll-${width}.png`), animations: 'disabled' });
      fs.writeFileSync(path.join(process.env.HAU_TEST_ARTIFACTS, `scroll-${width}.json`), JSON.stringify(result, null, 2));
    }
    console.log(JSON.stringify({ width, ...result }));
  } finally { await context.close(); }
}
(async () => {
  const browser = await chromium.launch({ headless: true, executablePath: process.env.HAU_TEST_CHROMIUM });
  try { for (const width of [390, 300]) await run(browser, width); } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
