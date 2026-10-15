import { test, expect } from '@playwright/test';
import { adaptiveFilter } from '../src/components/backdrop.js';

test.beforeEach(async ({ page }) => {
  page.__errors = [];
  page.on('pageerror', error => page.__errors.push(error.message));
  await page.goto('/');
  await expect(page.locator('flight-taskbar')).toHaveAttribute('data-renderer', 'webgl2');
});
test.afterEach(async ({ page }) => { expect(page.__errors).toEqual([]); });

test('navigation swaps connected components, supports history and disposes renderers', async ({ page }) => {
  await page.evaluate(() => { window.originalBar = document.querySelector('flight-taskbar'); });
  await page.getByRole('button', { name: 'Nút bấm 02', exact: true }).click();
  await expect(page).toHaveURL(/#buttons$/);
  await expect(page.locator('liquid-button')).toHaveCount(4);
  await expect(page.locator('flight-taskbar')).toHaveCount(0);
  expect(await page.evaluate(() => ({ connected: window.originalBar.isConnected, renderer: window.originalBar.renderer, frame: window.originalBar.frame })))
    .toEqual({ connected: false, renderer: null, frame: 0 });
  await page.evaluate(() => { window.oldButton = document.querySelector('liquid-button'); });
  await page.getByRole('button', { name: 'Cần gạt & slider 03', exact: true }).click();
  await expect(page.locator('liquid-toggle')).toHaveCount(3);
  expect(await page.evaluate(() => ({ connected: window.oldButton.isConnected, frame: window.oldButton.motion.frame,
    renderer: window.oldButton.glass.renderer }))).toEqual({ connected: false, frame: 0, renderer: null });
  await page.goBack();
  await expect(page.locator('liquid-button')).toHaveCount(4);
  await page.getByRole('button', { name: 'Taskbar 01', exact: true }).click();
  await expect(page.locator('flight-taskbar')).toHaveAttribute('data-renderer', 'webgl2');
  await page.getByRole('tab', { name: 'Tab 3', exact: true }).click();
  await expect(page.locator('flight-taskbar')).toHaveAttribute('selected-index', '2');
  await page.goto('/#unknown');
  await expect(page.locator('flight-taskbar')).toHaveAttribute('data-renderer', 'webgl2');
});

test('buttons have real press spring, keyboard activation and disabled behavior', async ({ page }, testInfo) => {
  await page.goto('/#buttons');
  const save = page.locator('#demo-save');
  await expect(save.locator('glass-surface')).toHaveAttribute('data-renderer', 'webgl2');
  const button = save.getByRole('button', { name: 'Tiếp tục' });
  const box = await button.boundingBox();
  await page.mouse.move(box.x + box.width / 2, box.y + box.height / 2);
  await page.mouse.down();
  await expect.poll(() => save.evaluate(el => el.motion.progress.value)).toBeGreaterThan(.9);
  expect(await save.evaluate(el => el.motion.scaleX.target)).toBeCloseTo(1 + 4 / 48);
  await page.mouse.up();
  await expect(page.locator('#button-count')).toHaveText('Đã kích hoạt 1 lần');
  await page.getByRole('button', { name: 'Nút kính', exact: true }).focus();
  await page.keyboard.press('Enter');
  await expect(page.locator('#button-count')).toHaveText('Đã kích hoạt 2 lần');
  await expect(page.getByRole('button', { name: 'Vô hiệu hoá', exact: true })).toBeDisabled();
  await page.getByRole('button', { name: 'Vô hiệu hoá', exact: true }).evaluate(button => button.click());
  await expect(page.locator('#button-count')).toHaveText('Đã kích hoạt 2 lần');
  await expect.poll(() => save.evaluate(el => el.motion.frame)).toBe(0);
  expect(await save.evaluate(el => el.glass.renderer.gl.getError())).toBe(0);
  // Verify the copy action without changing the user's system clipboard.
  await page.evaluate(() => Object.defineProperty(navigator, 'clipboard', {
    configurable: true, value: { writeText: async text => { window.copiedExample = text; } },
  }));
  await page.getByRole('button', { name: 'Sao chép ví dụ' }).click();
  await expect(page.locator('#copy-status')).toHaveText('Đã sao chép ví dụ.');
  expect(await page.evaluate(() => window.copiedExample)).toBe(await page.locator('#resource-code').textContent());
  await page.screenshot({ path: testInfo.outputPath('buttons-desktop.png'), fullPage: true });
});

test('toggle click, drag, cancellation, RTL, native slider keyboard and bounded API', async ({ page }, testInfo) => {
  await page.goto('/#switches');
  const toggle = page.locator('#notifications');
  const button = page.getByRole('switch', { name: 'Thông báo', exact: true });
  await expect(button).toHaveAttribute('aria-checked', 'true');
  expect(await toggle.evaluate(el => ({ host: el.getBoundingClientRect().width, track: el.track.getBoundingClientRect().width,
    thumb: el.glass.getBoundingClientRect().width }))).toEqual({ host: 56, track: 56, thumb: 36 });
  await page.evaluate(() => document.fonts.load('400 12px Inter', 'Độ sáng'));
  expect(await page.evaluate(() => document.fonts.check('400 12px Inter', 'Độ sáng'))).toBe(true);
  await button.click();
  await expect(button).toHaveAttribute('aria-checked', 'false');
  const box = await button.boundingBox();
  const x = box.x + box.width / 2, y = box.y + box.height / 2;
  await page.mouse.move(x,y); await page.mouse.down();
  await page.mouse.move(x+30,y, { steps: 5 }); await page.mouse.up();
  await expect(button).toHaveAttribute('aria-checked', 'true');
  await page.mouse.move(x,y); await page.mouse.down();
  await page.mouse.move(x-30,y, { steps: 5 });
  await page.evaluate(() => dispatchEvent(new Event('blur'))); await page.mouse.up();
  await expect(button).toHaveAttribute('aria-checked', 'true');
  await toggle.evaluate(el => { el.setAttribute('dir','rtl'); el.checked = false; });
  await expect(button).toHaveAttribute('aria-checked', 'false');
  await page.mouse.move(x,y); await page.mouse.down(); await page.mouse.move(x-30,y, { steps: 5 }); await page.mouse.up();
  await expect(button).toHaveAttribute('aria-checked', 'true');
  await button.focus(); await page.keyboard.press('Space');
  await expect(button).toHaveAttribute('aria-checked', 'false');
  const slider = page.getByRole('slider', { name: 'Mức độ demo', exact: true });
  await slider.focus(); await page.keyboard.press('Home');
  await expect(page.locator('#level-output')).toHaveText('0%');
  await page.keyboard.press('End');
  await expect(page.locator('#level-output')).toHaveText('100%');
  await page.locator('#demo-level').evaluate(el => { el.value = 500; });
  expect(await page.locator('#demo-level').evaluate(el => el.value)).toBe(100);
  await expect(page.getByRole('switch', { name: 'Cần gạt vô hiệu hoá' })).toBeDisabled();
  await expect.poll(() => toggle.evaluate(el => el.motion.frame)).toBe(0);
  expect(await toggle.evaluate(el => el.glass.renderer.gl.getError())).toBe(0);
  await page.screenshot({ path: testInfo.outputPath('switches-desktop.png'), fullPage: true });
});

test('adaptive filter samples actual backdrop luminance and matches the original Kotlin lerps', async ({ page }, testInfo) => {
  await page.goto('/#adaptive');
  const glass = page.locator('#adaptive-preview');
  await expect(glass).toHaveAttribute('data-renderer', 'webgl2');
  for (const tone of [0, 128, 255]) {
    await page.locator('#backdrop-luminance').evaluate((input, value) => { input.value = value; input.dispatchEvent(new Event('input', { bubbles: true })); }, tone);
    await expect.poll(() => glass.evaluate(el => el.filterState?.luminance)).toBeCloseTo(tone / 255);
    await expect.poll(() => glass.evaluate(el => el.filterState?.blur)).toBeCloseTo(adaptiveFilter(tone / 255).blur);
    const state = await glass.evaluate(el => el.filterState);
    const expected = adaptiveFilter(tone / 255);
    for (const key of ['brightness','contrast','saturation','blur']) expect(state[key]).toBeCloseTo(expected[key]);
    expect(state.contentColor).toBe(expected.contentColor);
    const uniforms = await glass.evaluate(el => ['uBrightness','uContrast','uBlur'].map(name => el.renderer.gl.getUniform(el.renderer.program,el.renderer.uniform(name))));
    expect(uniforms[0]).toBeCloseTo(expected.brightness); expect(uniforms[1]).toBeCloseTo(expected.contrast); expect(uniforms[2]).toBeCloseTo(expected.blur);
  }
  await page.getByLabel('Tự động xử lý filter').uncheck();
  await expect(glass).toHaveAttribute('data-adaptive','false');
  expect(await glass.evaluate(el => el.filterState)).toBe(null);
  await page.getByLabel('Tự động xử lý filter').check();
  await expect(glass).toHaveAttribute('data-adaptive','true');
  await page.screenshot({ path: testInfo.outputPath('adaptive-desktop.png'), fullPage: true });
});

test('Control Center UI events, vertical sliders, open/close spring and mobile overflow', async ({ page }, testInfo) => {
  await page.goto('/#control-center');
  const center = page.locator('liquid-control-center');
  await expect(center).toBeVisible();
  await page.getByRole('button', { name: 'Chế độ máy bay', exact: true }).click();
  await expect(page.getByRole('button', { name: 'Máy bay', exact: true })).toHaveAttribute('aria-pressed','true');
  await expect(page.locator('#center-status')).toHaveText('airplane: true (demo)');
  await page.getByRole('button', { name: 'Phát / tạm dừng' }).click();
  expect(await center.evaluate(el => el.state.playing)).toBe(true);
  const brightness = page.getByRole('slider', { name: 'Độ sáng demo', exact: true });
  await expect(brightness).toHaveAttribute('aria-orientation','vertical');
  await brightness.focus(); await page.keyboard.press('End');
  expect(await center.evaluate(el => el.state.brightness)).toBe(100);
  const handle = page.getByRole('button', { name: 'Kéo để mở hoặc đóng Control Center' });
  await handle.click();
  await expect(handle).toHaveAttribute('aria-expanded','false');
  await expect.poll(() => center.evaluate(el => el.frame)).toBe(0);
  expect(await center.evaluate(el => el.grid.inert)).toBe(true);
  await handle.click();
  await expect(handle).toHaveAttribute('aria-expanded','true');
  await expect.poll(() => center.evaluate(el => el.frame)).toBe(0);
  const box = await handle.boundingBox();
  await page.mouse.move(box.x+box.width/2,box.y+17); await page.mouse.down();
  await page.mouse.move(box.x+box.width/2,box.y-180,{steps:5}); await page.mouse.up();
  await expect(handle).toHaveAttribute('aria-expanded','false');
  await handle.click();
  await expect(handle).toHaveAttribute('aria-expanded','true');
  await page.setViewportSize({width:360,height:800});
  await expect.poll(() => page.evaluate(() => document.documentElement.scrollWidth)).toBe(360);
  const centerBox = await center.boundingBox();
  const demoBox = await page.locator('.resource-demo').boundingBox();
  expect(centerBox.x).toBeGreaterThan(demoBox.x);
  expect(centerBox.x + centerBox.width).toBeLessThan(demoBox.x + demoBox.width);
  await expect(page.locator('[data-page="control-center"]')).toBeInViewport();
  await page.screenshot({ path: testInfo.outputPath('control-center-mobile.png'), fullPage: true });
});

test('CSS fallback keeps new components usable; source zip and Kotlin reference links resolve', async ({ page, request }) => {
  await page.addInitScript(() => {
    const original = HTMLCanvasElement.prototype.getContext;
    HTMLCanvasElement.prototype.getContext = function(type,...args) { return type === 'webgl2' ? null : original.call(this,type,...args); };
  });
  await page.goto('/#switches');
  await page.reload(); // Hash-only navigation does not execute addInitScript.
  await expect(page.locator('#notifications').locator('glass-surface')).toHaveAttribute('data-renderer','css');
  await page.getByRole('switch', {name:'Thông báo',exact:true}).click();
  await expect(page.getByRole('switch', {name:'Thông báo',exact:true})).toHaveAttribute('aria-checked','false');
  await page.goto('/#adaptive');
  await expect(page.locator('#adaptive-preview')).toHaveAttribute('data-renderer','css');
  await expect.poll(() => page.locator('#adaptive-preview').evaluate(el => el.filterState?.luminance)).toBeGreaterThan(.5);
  const zip = await request.get('/resources.zip'); expect(zip.ok()).toBe(true);
  expect(zip.headers()['content-type']).toBe('application/zip');
  const bytes = await zip.body(); expect(bytes.readUInt32LE(0)).toBe(0x04034b50);
  expect(bytes.includes(Buffer.from('src/components/liquid-button.js'))).toBe(true);
  expect(bytes.includes(Buffer.from('original-kotlin/LiquidToggle.kt'))).toBe(true);
  for (const link of await page.locator('.source-links a').evaluateAll(links=>links.map(link=>link.getAttribute('href')))) {
    const response = await request.get(link); expect(response.ok()).toBe(true); expect(await response.text()).toContain('package com.kyant.backdrop');
  }
});

test('repeated gallery visits and reconnect/context restore preserve live WebGL resources', async ({ page }) => {
  test.setTimeout(60_000);
  for (const route of ['buttons','switches','adaptive','control-center','buttons','control-center','taskbar']) {
    await page.locator(`[data-page="${route}"]`).click();
    await expect(page.locator(`[data-page="${route}"]`)).toHaveAttribute('aria-current','page');
    if (route !== 'taskbar') await expect(page.locator('#resource-page')).toBeVisible();
  }
  await expect(page.locator('flight-taskbar')).toHaveAttribute('data-renderer','webgl2');
  await page.locator('[data-page="buttons"]').click();
  const button = page.locator('#demo-save');
  await expect(button.locator('glass-surface')).toHaveAttribute('data-renderer','webgl2');
  await button.evaluate(el => { window.reconnectButton = el; el.remove(); });
  expect(await page.evaluate(() => window.reconnectButton.glass.renderer)).toBe(null);
  await page.evaluate(() => document.querySelector('.button-showcase').append(window.reconnectButton));
  await expect(button.locator('glass-surface')).toHaveAttribute('data-renderer','webgl2');
  await expect.poll(() => button.evaluate(el => el.glass.frame)).toBe(0);
  expect(await button.evaluate(el => el.glass.renderer.gl.getError())).toBe(0);
  await button.evaluate(el => {
    window.lostContext = el.glass.renderer.gl.getExtension('WEBGL_lose_context'); window.lostContext.loseContext();
  });
  await expect(button.locator('glass-surface')).toHaveAttribute('data-renderer','css');
  await page.evaluate(() => window.lostContext.restoreContext());
  await expect(button.locator('glass-surface')).toHaveAttribute('data-renderer','webgl2');
  await button.getByRole('button').click();
  await expect(page.locator('#button-count')).toHaveText('Đã kích hoạt 1 lần');
});
