import { test, expect } from '@playwright/test';

test('selector scale does not magnify the captured icon; dispersion changes the lens rim', async ({ page }, testInfo) => {
  await page.goto('/');
  const bar = page.locator('flight-taskbar');
  await expect(bar).toHaveAttribute('data-renderer', 'webgl2');
  await expect.poll(() => bar.evaluate(el => el.frame)).toBe(0);
  const result = await bar.evaluate(el => {
    const renderer = el.renderer;
    const gl = renderer.gl;
    // Neutral backdrop separates blue icon pixels from the blue demo gradient.
    const backdrop = document.createElement('canvas');
    backdrop.width = innerWidth;
    backdrop.height = innerHeight;
    const context = backdrop.getContext('2d');
    context.fillStyle = '#888888';
    context.fillRect(0, 0, backdrop.width, backdrop.height);
    renderer.refreshBackdrop(backdrop);
    const width = renderer.canvas.width;
    const height = renderer.canvas.height;
    const dpr = renderer.dpr;
    const tabWidth = (renderer.width - 8) / 5;
    const centerX = 4 + tabWidth / 2;
    const state = { index: 0, panelOffset: 0, press: 1, highlight: 0, scaleX: 1, scaleY: 1 };
    const render = (patch, settings = {}) => {
      renderer.render({ ...state, ...patch }, { ...el.settings, ...settings });
      const pixels = new Uint8Array(width * height * 4);
      gl.readPixels(0, 0, width, height, gl.RGBA, gl.UNSIGNED_BYTE, pixels);
      return pixels;
    };
    const pixel = (data, x, y) => {
      const column = Math.round((64 + x) * dpr);
      const row = height - 1 - Math.round((64 + y) * dpr);
      const start = (row * width + column) * 4;
      return data.slice(start, start + 3);
    };
    const iconBounds = data => {
      const xs = [], ys = [];
      for (let y = 0; y < 42; y += 1 / dpr) {
        for (let x = centerX - 24; x < centerX + 24; x += 1 / dpr) {
          const [r, g, b] = pixel(data, x, y);
          if (b > r + 80 && g > r + 35) { xs.push(x); ys.push(y); }
        }
      }
      return { width: Math.max(...xs) - Math.min(...xs), height: Math.max(...ys) - Math.min(...ys), count: xs.length };
    };
    // No lens displacement: isolate icon size from edge refraction.
    const noLens = { refractionHeightFraction: 0, refractionAmountFraction: 0 };
    const normal = iconBounds(render({ press: 0 }, noLens));
    const held = iconBounds(render({}, noLens));
    const enlargedGlass = iconBounds(render({ scaleX: 1.8, scaleY: 1.8 }, noLens));
    const scale = 78 / 56;
    const withoutDispersion = render({ scaleX: scale, scaleY: scale }, { chromaticAberration: 0 });
    const withDispersion = render({ scaleX: scale, scaleY: scale }, { chromaticAberration: 1 });
    let changedRimPixels = 0;
    let changedInteriorPixels = 0;
    for (let y = -10; y < 74; y++) {
      for (let x = centerX - tabWidth; x < centerX + tabWidth; x++) {
        const cx = (x - centerX) / scale;
        const cy = (y - 32) / scale;
        const radius = Math.min(tabWidth / 2, 28);
        const qx = Math.abs(cx) - (tabWidth / 2 - radius);
        const qy = Math.abs(cy) - (28 - radius);
        const sdf = Math.hypot(Math.max(qx, 0), Math.max(qy, 0)) - radius + Math.min(Math.max(qx, qy), 0);
        const a = pixel(withoutDispersion, x, y);
        const b = pixel(withDispersion, x, y);
        const difference = a.reduce((sum, value, channel) => sum + Math.abs(value - b[channel]), 0);
        if (difference > 8 && sdf <= 0 && sdf > -10) changedRimPixels++;
        if (difference > 8 && sdf < -11) changedInteriorPixels++;
      }
    }
    return { normal, held, enlargedGlass, changedRimPixels, changedInteriorPixels,
      error: gl.getError(), image: renderer.canvas.toDataURL() };
  });
  await testInfo.attach('pressed-glass-rainbow-rim', {
    body: Buffer.from(result.image.split(',')[1], 'base64'), contentType: 'image/png',
  });
  expect(result.normal.count).toBeGreaterThan(100);
  expect(result.held.height / result.normal.height).toBeGreaterThan(1.1);
  expect(result.held.height / result.normal.height).toBeLessThan(1.35);
  expect(result.enlargedGlass.width).toBe(result.held.width);
  expect(result.enlargedGlass.height).toBe(result.held.height);
  // Inner shadow can move a threshold pixel, but must not enlarge the glyph.
  expect(Math.abs(result.enlargedGlass.count - result.held.count)).toBeLessThan(5);
  expect(result.changedRimPixels).toBeGreaterThan(30);
  expect(result.changedInteriorPixels).toBe(0);
  expect(result.error).toBe(0);
});
