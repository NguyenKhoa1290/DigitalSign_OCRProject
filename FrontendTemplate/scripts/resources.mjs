import { existsSync } from 'node:fs';
import { readFile, readdir, mkdir, writeFile } from 'node:fs/promises';
import path from 'node:path';

const sourceNames = ['LiquidButton.kt', 'LiquidToggle.kt', 'LiquidSlider.kt', 'LiquidBottomTabs.kt', 'LiquidBottomTab.kt',
  'DampedDragAnimation.kt', 'InteractiveHighlight.kt', 'ControlCenterContent.kt', 'AdaptiveLuminanceGlassContent.kt',
  'LayerBackdrop.kt', 'InverseLayerScope.kt', 'Shaders.kt', 'ColorFilter.kt',
  'DialogContent.kt','LockScreenContent.kt','MagnifierContent.kt','ProgressiveBlurContent.kt',
  'ScrollContainerContent.kt','LazyScrollContainerContent.kt','GlassPlaygroundContent.kt',
  'SdfShader.kt','LoremIpsum.kt','HomeContent.kt','CatalogDestination.kt','UISensor.android.kt'];
function createSourcePath(root) {
  const directRoot = '../AndroidLiquidGlass-kmp';
  const nestedRoot = `${directRoot}/AndroidLiquidGlass-kmp`;
  const kotlinRoot = existsSync(path.resolve(root, nestedRoot)) ? nestedRoot : directRoot;
  const catalogRoot = `${kotlinRoot}/app/src/commonMain/kotlin/com/kyant/backdrop/catalog`;
  const effectRoot = `${kotlinRoot}/backdrop/src/commonMain/kotlin/com/kyant/backdrop`;
  return name => {
    if(name==='CatalogDestination.kt')return `${catalogRoot}/${name}`;
    if(name==='UISensor.android.kt')return `${kotlinRoot}/app/src/androidMain/kotlin/com/kyant/backdrop/catalog/utils/UISensor.kt`;
    if (name.startsWith('Liquid')) return `${catalogRoot}/components/${name}`;
    if (name.endsWith('Content.kt')) return `${catalogRoot}/destinations/${name}`;
    if (['DampedDragAnimation.kt','InteractiveHighlight.kt','SdfShader.kt','LoremIpsum.kt'].includes(name)) return `${catalogRoot}/utils/${name}`;
    if (name === 'LayerBackdrop.kt') return `${effectRoot}/backdrops/${name}`;
    if (name === 'ColorFilter.kt') return `${effectRoot}/effects/${name}`;
    return `${effectRoot}/internal/${name}`;
  };
}

// Stored ZIP entries, UTF-8 names, standard CRC32. No runtime/package dependency.
function crc32(buffer) {
  let crc = 0xffffffff;
  for (const byte of buffer) {
    crc ^= byte;
    for (let bit = 0; bit < 8; bit++) crc = (crc >>> 1) ^ (crc & 1 ? 0xedb88320 : 0);
  }
  return (crc ^ 0xffffffff) >>> 0;
}
function zip(entries) {
  const chunks = [], directory = []; let offset = 0;
  for (const [filename, data] of entries) {
    const name = Buffer.from(filename); const body = Buffer.isBuffer(data) ? data : Buffer.from(data);
    const local = Buffer.alloc(30); local.writeUInt32LE(0x04034b50); local.writeUInt16LE(20,4);
    local.writeUInt16LE(0x800,6); local.writeUInt16LE(0x21,12); local.writeUInt32LE(crc32(body),14);
    local.writeUInt32LE(body.length,18); local.writeUInt32LE(body.length,22); local.writeUInt16LE(name.length,26);
    const central = Buffer.alloc(46); central.writeUInt32LE(0x02014b50); central.writeUInt16LE(20,4); central.writeUInt16LE(20,6);
    central.writeUInt16LE(0x800,8); central.writeUInt16LE(0x21,14); central.writeUInt32LE(crc32(body),16);
    central.writeUInt32LE(body.length,20); central.writeUInt32LE(body.length,24); central.writeUInt16LE(name.length,28);
    central.writeUInt32LE(offset,42); directory.push(central,name); chunks.push(local,name,body);
    offset += local.length + name.length + body.length;
  }
  const centralSize = directory.reduce((n, buffer) => n + buffer.length,0);
  const end = Buffer.alloc(22); end.writeUInt32LE(0x06054b50); end.writeUInt16LE(entries.length,8);
  end.writeUInt16LE(entries.length,10); end.writeUInt32LE(centralSize,12); end.writeUInt32LE(offset,16);
  return Buffer.concat([...chunks,...directory,end]);
}

const example = `<!doctype html>
<html lang="vi"><meta charset="UTF-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>Liquid Glass resource kit</title>
<link rel="stylesheet" href="./src/fonts.css">
<style>
*{box-sizing:border-box} body{font:14px Inter,Arial,sans-serif;margin:0;padding:32px;background:linear-gradient(180deg,#9ad7ff,#daf0ff 50%,#b9d4bc);background-attachment:fixed;color:#16384e}
main{max-width:640px;margin:auto} section{margin:32px 0} h2{font-size:15px} flight-taskbar{max-width:480px}
liquid-slider{max-width:320px} adaptive-glass{width:280px;padding:32px} liquid-control-center{margin:auto}
</style>
<script type="module">
import './src/flight-taskbar.js';
import {setGlassBackdrop} from './src/components/index.js';
// When changing the backdrop, supply a viewport canvas/image AND use the same
// image for the CSS page background. Same-origin / CORS-enabled only.
document.addEventListener('activate',()=>console.log('button activated'));
document.addEventListener('controlchange',event=>console.log(event.detail));
const samples={
  controls:'<section><h2>Taskbar</h2><flight-taskbar></flight-taskbar></section><section><h2>Button</h2><liquid-button tint="#0088ff">Tiếp tục</liquid-button> <liquid-button>Nút kính</liquid-button></section><section><h2>Toggle / slider</h2><liquid-toggle checked label="Thông báo"></liquid-toggle><liquid-slider value="65" label="Mức độ"></liquid-slider></section><section><h2>Adaptive filter</h2><adaptive-glass><h3>Filter tự động</h3><p>Đo luminance nền, điều chỉnh kính.</p></adaptive-glass></section>',
  'control-center':'<liquid-control-center></liquid-control-center>',
  dialog:'<button id="open-dialog">Mở hộp thoại</button><liquid-dialog>Thông báo mẫu</liquid-dialog>',
  'lock-screen':'<liquid-lock-screen></liquid-lock-screen>',
  magnifier:'<liquid-magnifier></liquid-magnifier>',
  'progressive-blur':'<progressive-blur></progressive-blur>',
  'scroll-container':'<glass-scroll></glass-scroll>',
  'lazy-scroll-container':'<glass-scroll lazy></glass-scroll>',
  'glass-playground':'<glass-playground></glass-playground>'
};
const select=document.querySelector('#resource-choice');
function render(){document.querySelector('#samples').innerHTML=samples[select.value];document.querySelector('#open-dialog')?.addEventListener('click',()=>document.querySelector('liquid-dialog').show());}
select.addEventListener('change',render);render();
</script>
<main><h1>Liquid Glass — Resource kit</h1>
<label>Component <select id="resource-choice"><option value="controls">Taskbar / buttons / switches / adaptive</option><option value="control-center">Control Center</option><option value="dialog">Dialog</option><option value="lock-screen">Lock screen</option><option value="magnifier">Magnifier</option><option value="progressive-blur">Progressive blur</option><option value="scroll-container">Scroll container</option><option value="lazy-scroll-container">Lazy scroll container</option><option value="glass-playground">Glass playground</option></select></label>
<div id="samples"></div>
</main></html>`;

export async function buildResourceKit(root, dist) {
  const sourcePath = createSourcePath(root);
  const entries = [];
  const webFiles = ['flight-taskbar.js','flight-icon.js','glass-renderer.js','shaders.js','spring.js','settings.js'];
  for (const item of webFiles) entries.push([`src/${item}`,await readFile(path.join(root,'src',item))]);
  entries.push(['src/fonts.css',await readFile(path.join(root,'src/fonts.css'))]);
  for (const item of await readdir(path.join(root,'src/components'))) if (item.endsWith('.js')) entries.push([`src/components/${item}`,await readFile(path.join(root,'src/components',item))]);
  entries.push(['src/assets/clock_sdf.webp',await readFile(path.join(root,'src/assets/clock_sdf.webp'))]);
  for (const item of ['inter-vietnamese.woff2','inter-latin.woff2']) entries.push([`src/assets/${item}`,await readFile(path.join(root,'src/assets',item))]);
  for (const item of ['LICENSE','NOTICE','INTER-OFL.txt','Huongdancomponent.md']) entries.push([item,await readFile(path.join(root,item))]);
  entries.push(['index.html',example]);
  const provenance = [];
  await mkdir(path.join(root,'resources/kotlin'),{recursive:true});
  await mkdir(path.join(dist,'resources/kotlin'),{recursive:true});
  for (const name of sourceNames) {
    try {
      const original = await readFile(path.resolve(root,sourcePath(name)));
      entries.push([`original-kotlin/${name}`,original]);
      provenance.push({file:name,source:sourcePath(name)});
      await writeFile(path.join(root,'resources/kotlin',name),original);
      await writeFile(path.join(dist,'resources/kotlin',name),original);
    } catch (error) { if (error.code !== 'ENOENT') throw error; }
  }
  entries.push(['sources.json',JSON.stringify(provenance,null,2)]);
  entries.push(['README.md',`# Liquid Glass Web Resource Kit

Dependency-free ES modules / Web Components. Serve this folder over HTTP, not file://.
Example: npx --yes http-server . -p 8080 (or any existing static HTTP server).

Import ./src/components/index.js for the new components. Import ./src/flight-taskbar.js
separately for the taskbar. Keep only the modules your app imports; imports resolve their dependencies.
Native Kotlin reference snapshots are under original-kotlin/; sources.json records their actual local origins.
They are references, not a standalone Android build. Copyright 2025 Kyant, Apache-2.0; keep LICENSE/NOTICE.

APIs:
- liquid-button: tint="#0088ff", label, disabled; activate event.
- liquid-toggle: compact 56x28 web visual (44px touch target), checked property/attribute, label, disabled, dir="rtl"; change.detail.checked.
- liquid-slider: value/min/max/step, label, vertical, disabled; input/change.detail.value.
- glass-surface: configure({blur,refractionHeight,refractionAmount,depth,chromatic,tint,...}).
- adaptive-glass: automatic 5x5 luminance sampling; manual attribute disables auto; filterchange.detail.
- liquid-control-center: open property or open="false"; state getter; controlchange.detail.
- liquid-dialog: show(), close('cancel'|'confirm'), theme; dialogaction.detail.action.
- liquid-lock-screen: original clock SDF asset, pointer/keyboard drag, reset().
- liquid-magnifier: text attribute, theme, cursor pointer/keyboard drag, reset(). Canvas text capture.
- progressive-blur: fixed blur4, smoothstep alpha fade, theme.
- glass-scroll: count=20; lazy gives virtualized count=100. One shared WebGL context.
- glass-playground: 256px square, five original slider ranges, pan/pinch/rotate, reset(), sheet.
- setGlassBackdrop(canvasOrImage): explicit viewport source. Use the SAME image in CSS, then call
  setGlassBackdrop again after redrawing. Per-surface backdrop overrides are also supported.

Limits: WebGL2 refraction; CSS fallback preserves blur/tint and interaction but not lens/dispersion.
Browser DOM is not automatically captured. CORS/same-origin images are required. Button/toggle/slider
glass uses native DOM content, not refraction of the slotted text itself. Nine-tap blur approximates
Compose blur. Adaptive samples continuously and interpolates luminance/filter with 1s tween.
Control Center extends the original
layout with UI-only actions; it does not change OS Wi-Fi, volume, brightness or play actual audio.
Motion is opt-in via Control Center's sensor button; permission denial/unavailability keeps manual angle.
The vertical slider and labeled controls in Control Center are web extensions.
`]);
  const archive = zip(entries);
  await writeFile(path.join(root,'resources.zip'),archive);
  await writeFile(path.join(dist,'resources.zip'),archive);
  console.log(`Resource kit: ${entries.length} files, ${provenance.length} original Kotlin references`);
}
