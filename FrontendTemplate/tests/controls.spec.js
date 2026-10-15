import { test, expect } from '@playwright/test';
import { CONTROL_DEFINITIONS, DEFAULT_SETTINGS, PLAYGROUND_SETTINGS } from '../src/settings.js';

async function setSlider(page, key, value) {
  await page.locator(`#${key}`).evaluate((input, value) => {
    input.value = String(value);
    input.dispatchEvent(new Event('input', { bubbles: true }));
  }, value);
  await expect.poll(() => page.locator('flight-taskbar').evaluate(el => el.frame)).toBe(0);
}

test.beforeEach(async ({ page }) => {
  const errors = [];
  page.on('pageerror', error => errors.push(error.message));
  page.__errors = errors;
  await page.goto('/');
  await expect(page.locator('flight-taskbar')).toHaveAttribute('data-renderer', 'webgl2');
});
test.afterEach(async ({ page }) => { expect(page.__errors).toEqual([]); });

test('original five playground ranges, live shader uniforms and visible blur on a textured background', async ({ page }, testInfo) => {
  const bar = page.locator('flight-taskbar');
  const sourceRanges = [
    ['cornerRadiusFraction', '0', '1'], ['blurRadius', '0', '32'],
    ['refractionHeightFraction', '0', '1'], ['refractionAmountFraction', '0', '1'],
    ['chromaticAberration', '0', '1'],
  ];
  for (const [key, min, max] of sourceRanges) {
    await expect(page.locator(`#${key}`)).toHaveAttribute('min', min);
    await expect(page.locator(`#${key}`)).toHaveAttribute('max', max);
  }
  await page.getByRole('button', { name: 'Sọc', exact: true }).click();
  await setSlider(page, 'blurRadius', 0);
  const sharp = await bar.screenshot();
  await setSlider(page, 'blurRadius', 32);
  const blurred = await bar.screenshot();
  expect(sharp.equals(blurred)).toBe(false);
  await expect(page.locator('#blurRadius-value')).toHaveText('32px');
  await setSlider(page, 'refractionHeightFraction', 0.5);
  await setSlider(page, 'refractionAmountFraction', 0.25);
  await setSlider(page, 'cornerRadiusFraction', 0);
  const uniforms = await bar.evaluate(el => {
    const renderer = el.renderer;
    const gl = renderer.gl;
    return ['uBlurRadius', 'uRefractionHeight', 'uRefractionAmount', 'uCornerFraction'].map(name => gl.getUniform(renderer.program, renderer.uniform(name)));
  });
  expect(uniforms).toEqual([32, 16, 16, 0]);
  expect(await bar.evaluate(el => el.renderer.gl.getError())).toBe(0);
  await page.screenshot({ path: testInfo.outputPath('controls-desktop.png'), fullPage: true });
});

test('Kotlin chromatic boolean semantics, depth switch, exact presets and saved settings', async ({ page }) => {
  const bar = page.locator('flight-taskbar');
  await setSlider(page, 'chromaticAberration', 0);
  expect(await bar.evaluate(el => el.renderer.gl.getUniform(el.renderer.program, el.renderer.uniform('uChromaticAberration')))).toBe(0);
  await setSlider(page, 'chromaticAberration', 0.01);
  expect(await bar.evaluate(el => el.renderer.gl.getUniform(el.renderer.program, el.renderer.uniform('uChromaticAberration')))).toBe(1);
  await page.getByLabel('Khúc xạ chiều sâu').check();
  await expect.poll(() => bar.evaluate(el => el.frame)).toBe(0);
  expect(await bar.evaluate(el => el.renderer.gl.getUniform(el.renderer.program, el.renderer.uniform('uDepthEffect')))).toBe(1);
  await page.getByRole('button', { name: 'Thông số playground gốc' }).click();
  expect(await bar.evaluate(el => el.settings)).toEqual(PLAYGROUND_SETTINGS);
  await page.reload();
  expect(await bar.evaluate(el => el.settings)).toEqual(PLAYGROUND_SETTINGS);
  await page.getByRole('button', { name: 'Mặc định', exact: true }).click();
  expect(await bar.evaluate(el => el.settings)).toEqual(DEFAULT_SETTINGS);
  await expect(page.getByLabel('Khúc xạ chiều sâu')).not.toBeChecked();
  await expect(page.getByRole('button', { name: 'Gradient', exact: true })).toHaveAttribute('aria-pressed', 'true');
});

test('downloads the current taskbar configuration as AI-ready JSON', async ({ page }) => {
  await setSlider(page, 'blurRadius', 16);
  await page.locator('[data-background="stripes"]').click();
  await page.getByRole('tab', { name: 'Tab 3', exact: true }).click();
  const downloadPromise = page.waitForEvent('download');
  await page.locator('#download-config').click();
  const download = await downloadPromise;
  expect(download.suggestedFilename()).toBe('flight-taskbar-config.json');
  const stream = await download.createReadStream();
  let text = '';
  for await (const chunk of stream) text += chunk;
  expect(JSON.parse(text)).toMatchObject({
    format: 'flight-taskbar-web/config', version: 1, component: 'flight-taskbar',
    taskbar: { selectedIndex: 2, settings: { blurRadius: 16 } }, preview: { background: 'stripes' },
  });
  await expect(page.locator('#config-export-status')).toHaveText('Đã tải flight-taskbar-config.json.');
});

test('advanced sliders change actual surface uniforms and spring behavior; extremes stay finite', async ({ page }) => {
  await page.locator('.advanced-controls summary').click();
  await setSlider(page, 'surfaceAlpha', 0.8);
  await setSlider(page, 'saturation', 0);
  await setSlider(page, 'highlightAlpha', 0);
  await setSlider(page, 'shadowRadius', 0);
  await setSlider(page, 'pressedScale', 1.6);
  await setSlider(page, 'valueStiffness', 500);
  await setSlider(page, 'valueDamping', 0.5);
  await setSlider(page, 'scaleDampingX', 0.3);
  await setSlider(page, 'scaleDampingY', 0.4);
  const config = await page.locator('flight-taskbar').evaluate(el => ({
    stiffness: el.indexSpring.stiffness, damping: el.indexSpring.dampingRatio,
    scaleX: el.scaleXSpring.dampingRatio, scaleY: el.scaleYSpring.dampingRatio,
    uniforms: ['uSurfaceAlpha', 'uSaturation', 'uHighlightAlpha', 'uShadowRadius'].map(name => el.renderer.gl.getUniform(el.renderer.program, el.renderer.uniform(name))),
  }));
  expect(config.stiffness).toBe(500);
  expect(config.damping).toBe(0.5);
  expect(config.scaleX).toBe(0.3);
  expect(config.scaleY).toBe(0.4);
  expect(config.uniforms[0]).toBeCloseTo(0.8);
  expect(config.uniforms.slice(1)).toEqual([0, 0, 0]);
  const box = await page.getByRole('tab', { name: 'Tab 1', exact: true }).boundingBox();
  await page.mouse.move(box.x + box.width / 2, box.y + box.height / 2);
  await page.mouse.down();
  expect(await page.locator('flight-taskbar').evaluate(el => el.scaleXSpring.target)).toBe(1.6);
  await page.mouse.up();
  for (const valueType of ['min', 'max']) {
    await page.locator('flight-taskbar').evaluate((el, { definitions, valueType }) => {
      el.settings = Object.fromEntries(definitions.map(control => [control.key, control[valueType]]));
    }, { definitions: CONTROL_DEFINITIONS, valueType });
    await expect.poll(() => page.locator('flight-taskbar').evaluate(el => el.frame), { timeout: 15_000 }).toBe(0);
    expect(await page.locator('flight-taskbar').evaluate(el => el.springs.every(spring => Number.isFinite(spring.value)) && el.renderer.gl.getError() === 0)).toBe(true);
  }
});

test('controls work with keyboard, remain below a sticky mobile preview and resize without overflow', async ({ page }, testInfo) => {
  await page.setViewportSize({ width: 360, height: 800 });
  await page.locator('#blurRadius').focus();
  await page.keyboard.press('Home');
  await expect(page.locator('#blurRadius-value')).toHaveText('0px');
  await page.keyboard.press('End');
  await expect(page.locator('#blurRadius-value')).toHaveText('32px');
  const bar = await page.locator('flight-taskbar').boundingBox();
  const panel = await page.locator('.controls-panel').boundingBox();
  expect(panel.y).toBeGreaterThan(bar.y + bar.height);
  await page.locator('.advanced-controls summary').click();
  await page.locator('#scaleDampingY').scrollIntoViewIfNeeded();
  const preview = await page.locator('.stage').boundingBox();
  expect(preview.y).toBe(0);
  expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBe(360);
  await page.evaluate(() => window.scrollTo(0, 0));
  await page.screenshot({ path: testInfo.outputPath('controls-mobile.png') });
});

test('fallback keeps supported controls live and disables unavailable shader controls', async ({ page }) => {
  await page.addInitScript(() => {
    const original = HTMLCanvasElement.prototype.getContext;
    HTMLCanvasElement.prototype.getContext = function (type, ...args) {
      return type === 'webgl2' ? null : original.call(this, type, ...args);
    };
  });
  await page.reload();
  await expect(page.locator('#refractionHeightFraction')).toBeDisabled();
  await expect(page.locator('#refractionAmountFraction')).toBeDisabled();
  await expect(page.locator('#chromaticAberration')).toBeDisabled();
  await expect(page.getByLabel('Khúc xạ chiều sâu')).toBeDisabled();
  await expect(page.locator('#fallback-note')).toBeVisible();
  await setSlider(page, 'blurRadius', 16);
  const filter = await page.locator('flight-taskbar').evaluate(el => getComputedStyle(el.shadowRoot.querySelector('.surface')).backdropFilter);
  expect(filter).toContain('blur(16px)');
});
