import { test, expect } from '@playwright/test';

test.beforeEach(async ({ page }) => {
  const errors = [];
  page.on('pageerror', error => errors.push(error.message));
  page.__taskbarErrors = errors;
  await page.goto('/');
  await expect(page.locator('flight-taskbar')).toHaveAttribute('data-renderer', 'webgl2');
});

test.afterEach(async ({ page }) => {
  expect(page.__taskbarErrors).toEqual([]);
});

test('WebGL rendering, five original tabs, click transition, persistence and idle scheduling', async ({ page }, testInfo) => {
  const tabs = page.getByRole('tab');
  await expect(tabs).toHaveCount(5);
  await expect(page.getByRole('tab', { name: 'Tab 1', exact: true })).toHaveAttribute('aria-selected', 'true');
  await page.evaluate(() => {
    window.changes = [];
    document.querySelector('flight-taskbar').addEventListener('tabchange', e => window.changes.push(e.detail.index));
  });
  await page.getByRole('tab', { name: 'Tab 5', exact: true }).click();
  await expect(page.locator('flight-taskbar')).toHaveAttribute('selected-index', '4');
  await expect.poll(() => page.evaluate(() => document.querySelector('flight-taskbar').frame)).toBe(0);
  expect(await page.evaluate(() => window.changes)).toEqual([4]);
  expect(await page.evaluate(() => {
    const element = document.querySelector('flight-taskbar');
    return { index: element.indexSpring.value, error: element.renderer.gl.getError() };
  })).toEqual({ index: 4, error: 0 });
  await page.screenshot({ path: testInfo.outputPath('desktop-tab5.png') });
  await page.reload();
  await expect(page.getByRole('tab', { name: 'Tab 5', exact: true })).toHaveAttribute('aria-selected', 'true');
});

test('active glass can be held, stretched, dragged and snapped; pointer capture survives leaving the bar', async ({ page }, testInfo) => {
  const first = await page.getByRole('tab', { name: 'Tab 1', exact: true }).boundingBox();
  const last = await page.getByRole('tab', { name: 'Tab 5', exact: true }).boundingBox();
  const x = first.x + first.width / 2;
  const y = first.y + first.height / 2;
  await page.mouse.move(x, y);
  await page.mouse.down();
  await expect.poll(() => page.evaluate(() => document.querySelector('flight-taskbar').pressSpring.value)).toBeGreaterThan(0.95);
  expect(await page.evaluate(() => document.querySelector('flight-taskbar').scaleXSpring.value)).toBeGreaterThan(1.3);
  await page.mouse.move(last.x + last.width / 2, y, { steps: 16 });
  await page.screenshot({ path: testInfo.outputPath('drag-held.png') });
  await page.mouse.move(last.x + last.width / 2 + 60, y - 130, { steps: 5 });
  await page.mouse.up();
  await expect(page.getByRole('tab', { name: 'Tab 5', exact: true })).toHaveAttribute('aria-selected', 'true');
  // A new click immediately after a drag must not be swallowed as its trailing click.
  await page.getByRole('tab', { name: 'Tab 2', exact: true }).click();
  await expect(page.locator('flight-taskbar')).toHaveAttribute('selected-index', '1');
  await expect.poll(() => page.evaluate(() => document.querySelector('flight-taskbar').frame)).toBe(0);
  expect(await page.evaluate(() => document.querySelector('flight-taskbar').renderer.gl.getError())).toBe(0);
});

test('keyboard, one roving tab stop and RTL direction', async ({ page }) => {
  const first = page.getByRole('tab', { name: 'Tab 1', exact: true });
  await first.focus();
  await page.keyboard.press('ArrowRight');
  await expect(page.getByRole('tab', { name: 'Tab 2', exact: true })).toBeFocused();
  await page.keyboard.press('End');
  await expect(page.getByRole('tab', { name: 'Tab 5', exact: true })).toHaveAttribute('aria-selected', 'true');
  await page.keyboard.press('Home');
  await expect(first).toBeFocused();
  expect(await page.getByRole('tab').evaluateAll(tabs => tabs.filter(tab => tab.tabIndex === 0).length)).toBe(1);
  await page.locator('flight-taskbar').evaluate(el => el.setAttribute('dir', 'rtl'));
  await page.keyboard.press('ArrowLeft');
  await expect(page.getByRole('tab', { name: 'Tab 2', exact: true })).toHaveAttribute('aria-selected', 'true');
  await expect.poll(() => page.evaluate(() => document.querySelector('flight-taskbar').frame)).toBe(0);
});

test('mobile touch drag, responsive sizing and original 64px height', async ({ page }, testInfo) => {
  await page.setViewportSize({ width: 360, height: 800 });
  const bar = await page.locator('flight-taskbar').boundingBox();
  expect(bar.width).toBe(320);
  expect(bar.height).toBe(64);
  const preview = await page.getByRole('region', { name: 'Xem trước taskbar' }).boundingBox();
  expect(bar.y).toBe(preview.y + (preview.height - 64) / 2);
  await expect.poll(() => page.evaluate(() => document.documentElement.scrollWidth)).toBe(360);
  const first = await page.getByRole('tab', { name: 'Tab 1', exact: true }).boundingBox();
  const fourth = await page.getByRole('tab', { name: 'Tab 4', exact: true }).boundingBox();
  const cdp = await page.context().newCDPSession(page);
  const y = first.y + first.height / 2;
  const start = first.x + first.width / 2;
  const end = fourth.x + fourth.width / 2;
  await cdp.send('Input.dispatchTouchEvent', { type: 'touchStart', touchPoints: [{ x: start, y }] });
  for (let i = 1; i <= 12; i++) {
    await cdp.send('Input.dispatchTouchEvent', { type: 'touchMove', touchPoints: [{ x: start + (end - start) * i / 12, y }] });
  }
  await cdp.send('Input.dispatchTouchEvent', { type: 'touchEnd', touchPoints: [] });
  await expect(page.getByRole('tab', { name: 'Tab 4', exact: true })).toHaveAttribute('aria-selected', 'true');
  await expect.poll(() => page.evaluate(() => document.querySelector('flight-taskbar').frame)).toBe(0);
  await page.screenshot({ path: testInfo.outputPath('mobile-tab4.png') });
  await page.setViewportSize({ width: 240, height: 640 });
  await expect.poll(() => page.evaluate(() => document.documentElement.scrollWidth)).toBe(240);
});

test('cancelled gesture restores the selection and context loss recovers', async ({ page }) => {
  const box = await page.getByRole('tab', { name: 'Tab 1', exact: true }).boundingBox();
  await page.mouse.move(box.x + box.width / 2, box.y + box.height / 2);
  await page.mouse.down();
  await page.mouse.move(box.x + box.width * 3, box.y + box.height / 2, { steps: 5 });
  await page.evaluate(() => window.dispatchEvent(new Event('blur')));
  await page.mouse.up();
  await expect(page.getByRole('tab', { name: 'Tab 1', exact: true })).toHaveAttribute('aria-selected', 'true');
  await page.evaluate(() => {
    const gl = document.querySelector('flight-taskbar').renderer.gl;
    window.contextExtension = gl.getExtension('WEBGL_lose_context');
    window.contextExtension.loseContext();
  });
  await expect(page.locator('flight-taskbar')).toHaveAttribute('data-renderer', 'css');
  await page.evaluate(() => window.contextExtension.restoreContext());
  await expect(page.locator('flight-taskbar')).toHaveAttribute('data-renderer', 'webgl2');
  await page.getByRole('tab', { name: 'Tab 3', exact: true }).click();
  await expect(page.locator('flight-taskbar')).toHaveAttribute('selected-index', '2');
});

test('system dark theme, reduced motion and bounded public API', async ({ page }, testInfo) => {
  await page.emulateMedia({ colorScheme: 'dark', reducedMotion: 'reduce' });
  await expect(page.locator('flight-taskbar')).toHaveAttribute('data-dark', 'true');
  await page.getByRole('tab', { name: 'Tab 3', exact: true }).click();
  await expect.poll(() => page.evaluate(() => document.querySelector('flight-taskbar').frame)).toBe(0);
  expect(await page.locator('flight-taskbar').evaluate(el => ({ press: el.pressSpring.value, index: el.indexSpring.value }))).toEqual({ press: 0, index: 2 });
  await page.screenshot({ path: testInfo.outputPath('dark-tab3.png') });
  await page.locator('flight-taskbar').evaluate(el => { el.selectedIndex = 999; });
  await expect(page.locator('flight-taskbar')).toHaveAttribute('selected-index', '4');
  await page.locator('flight-taskbar').evaluate(el => { el.selectedIndex = NaN; });
  await expect(page.locator('flight-taskbar')).toHaveAttribute('selected-index', '0');
});

test('CSS fallback keeps click and drag usable without WebGL', async ({ page }, testInfo) => {
  await page.addInitScript(() => {
    const original = HTMLCanvasElement.prototype.getContext;
    HTMLCanvasElement.prototype.getContext = function (type, ...args) {
      return type === 'webgl2' ? null : original.call(this, type, ...args);
    };
  });
  await page.reload();
  await expect(page.locator('flight-taskbar')).toHaveAttribute('data-renderer', 'css');
  await page.getByRole('tab', { name: 'Tab 5', exact: true }).click();
  await expect(page.locator('flight-taskbar')).toHaveAttribute('selected-index', '4');
  const last = await page.getByRole('tab', { name: 'Tab 5', exact: true }).boundingBox();
  const first = await page.getByRole('tab', { name: 'Tab 1', exact: true }).boundingBox();
  await page.mouse.move(last.x + last.width / 2, last.y + 28);
  await page.mouse.down();
  await page.mouse.move(first.x + first.width / 2, first.y + 28, { steps: 12 });
  await page.mouse.up();
  await expect(page.locator('flight-taskbar')).toHaveAttribute('selected-index', '0');
  await expect.poll(() => page.evaluate(() => document.querySelector('flight-taskbar').frame)).toBe(0);
  await page.screenshot({ path: testInfo.outputPath('css-fallback.png') });
});

test('custom textured backdrop and two independent instances survive reconnect', async ({ page }, testInfo) => {
  await page.evaluate(() => {
    const canvas = document.createElement('canvas');
    canvas.width = innerWidth;
    canvas.height = innerHeight;
    const ctx = canvas.getContext('2d');
    ctx.fillStyle = '#a8d9ed';
    ctx.fillRect(0, 0, canvas.width, canvas.height);
    for (let x = 0; x < canvas.width; x += 36) {
      ctx.fillStyle = x % 72 === 0 ? '#394c74' : '#e3f3eb';
      ctx.fillRect(x, 0, 18, canvas.height);
    }
    document.body.style.background = `url(${canvas.toDataURL()}) center / 100% 100%`;
    const bar = document.querySelector('flight-taskbar');
    bar.backdrop = canvas;
    bar.refreshBackdrop();
    window.secondBar = document.createElement('flight-taskbar');
    window.secondBar.style.cssText = 'position:fixed;bottom:32px;left:20px;width:320px';
    window.secondBar.selectedIndex = 3;
    document.body.append(window.secondBar);
  });
  const first = page.locator('flight-taskbar').first();
  const second = page.locator('flight-taskbar').last();
  await expect(second).toHaveAttribute('data-renderer', 'webgl2');
  await expect(second.getByRole('tab', { name: 'Tab 4', exact: true })).toHaveAttribute('aria-selected', 'true');
  await first.getByRole('tab', { name: 'Tab 2', exact: true }).click();
  await expect(second.getByRole('tab', { name: 'Tab 4', exact: true })).toHaveAttribute('aria-selected', 'true');
  await expect.poll(() => first.evaluate(el => el.frame)).toBe(0);
  const box = await first.boundingBox();
  await page.screenshot({ path: testInfo.outputPath('textured-refraction.png'), clip: {
    x: box.x - 40, y: box.y - 50, width: box.width + 80, height: box.height + 100,
  } });
  await page.evaluate(() => { window.secondBar.remove(); });
  expect(await page.evaluate(() => window.secondBar.frame)).toBe(0);
  await page.evaluate(() => { document.body.append(window.secondBar); });
  await expect(second).toHaveAttribute('data-renderer', 'webgl2');
  await second.getByRole('tab', { name: 'Tab 5', exact: true }).click();
  await expect(second).toHaveAttribute('selected-index', '4');
  await expect(first).toHaveAttribute('selected-index', '1');
  expect(await second.evaluate(el => el.renderer.gl.getError())).toBe(0);
});
