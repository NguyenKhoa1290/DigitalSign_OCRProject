// Explicit viewport capture: browsers cannot sample arbitrary DOM behind glass.
let source = null;
let gradient;
let viewportKey;
export function setGlassBackdrop(canvasOrImage = null) {
  source = canvasOrImage;
  window.dispatchEvent(new Event('glass-backdropchange'));
}
export function getGlassBackdrop() {
  if (source) return source;
  const key = `${innerWidth}:${innerHeight}`;
  if (key !== viewportKey) {
    viewportKey = key;
    gradient = document.createElement('canvas');
    gradient.width = innerWidth; gradient.height = innerHeight;
    const ctx = gradient.getContext('2d');
    const fill = ctx.createLinearGradient(0, 0, 0, innerHeight);
    fill.addColorStop(0, '#9ad7ff'); fill.addColorStop(.5, '#daf0ff'); fill.addColorStop(1, '#b9d4bc');
    ctx.fillStyle = fill; ctx.fillRect(0, 0, innerWidth, innerHeight);
  }
  return gradient;
}
export function sampleLuminance(backdrop, rect) {
  const canvas = document.createElement('canvas');
  canvas.width = canvas.height = 5;
  const context = canvas.getContext('2d', { willReadFrequently: true });
  const width = backdrop.naturalWidth || backdrop.width;
  const height = backdrop.naturalHeight || backdrop.height;
  const x = Math.max(0, Math.min(innerWidth - 1, rect.left));
  const y = Math.max(0, Math.min(innerHeight - 1, rect.top));
  const w = Math.max(1, Math.min(rect.width, innerWidth - x));
  const h = Math.max(1, Math.min(rect.height, innerHeight - y));
  context.drawImage(backdrop, x / innerWidth * width, y / innerHeight * height,
    w / innerWidth * width, h / innerHeight * height, 0, 0, 5, 5);
  const pixels = context.getImageData(0, 0, 5, 5).data;
  let sum = 0;
  for (let i = 0; i < pixels.length; i += 4) sum += (.2126 * pixels[i] + .7152 * pixels[i + 1] + .0722 * pixels[i + 2]) / 255;
  return sum / 25;
}
// AdaptiveLuminanceGlassContent.kt: signed-square remapping and exact lerps.
export function adaptiveFilter(luminance) {
  const value = Math.max(0, Math.min(1, luminance));
  const x = value * 2 - 1;
  const l = Math.sign(x) * x * x;
  return { luminance: value, brightness: l > 0 ? .1 + .4 * l : .1 + .3 * l,
    contrast: l > 0 ? 1 - l : 1, saturation: 1.5,
    blur: l > 0 ? 8 + 8 * l : 8 + 6 * l,
    contentColor: value > .5 ? '#000000' : '#ffffff' };
}
