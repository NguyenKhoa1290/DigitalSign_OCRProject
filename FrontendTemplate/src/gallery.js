import { setGlassBackdrop, getGlassBackdrop } from './components/index.js';
import { flightSvg } from './flight-icon.js';
import {EXTRA_PAGES} from './gallery-pages.js';

const PAGES = {
  ...EXTRA_PAGES,
  buttons: { title: 'Nút bấm', subtitle: 'LiquidButton', description: 'Kính capsule, glow theo điểm nhấn và spring phản hồi. Thử giữ rồi kéo nhẹ nút.',
    sources: ['LiquidButton.kt', 'InteractiveHighlight.kt'], files: ['liquid-button.js', 'glass-surface.js', 'motion.js'],
    properties: '48px · blur 2 · lens 12 / 24 · scale khi nhấn 1 + 4 / 48',
    example: `<script type="module" src="./src/components/index.js"></script>
<liquid-button id="save" tint="#0088ff">Lưu thay đổi</liquid-button>
<script>
  document.querySelector('#save').addEventListener('activate', () => {
    // Thực hiện hành động của ứng dụng.
  });
</script>`,
    markup: `<div class="button-showcase"><liquid-button id="demo-save" tint="#0088ff">${flightSvg} Tiếp tục</liquid-button><liquid-button id="demo-neutral">Nút kính</liquid-button><liquid-button id="demo-orange" tint="#ff8d28">Tải tài nguyên</liquid-button><liquid-button disabled>Vô hiệu hoá</liquid-button></div><output class="demo-output" id="button-count" aria-live="polite">Chưa có thao tác</output>` },
  switches: { title: 'Cần gạt & slider', subtitle: 'LiquidToggle / LiquidSlider', description: 'Nhấn hoặc kéo cần gạt. Slider hỗ trợ kéo chuột, touch và phím mũi tên, Home / End.',
    sources: ['LiquidToggle.kt', 'LiquidSlider.kt', 'DampedDragAnimation.kt'], files: ['liquid-toggle.js', 'liquid-slider.js', 'motion.js'],
    properties: 'Toggle web compact: track 56 × 28, thumb 36 × 24, drag 16 · Kotlin gốc: 64 × 28 / 40 × 24 · Slider: track 6, lens 10 / 14 · pressed scale 1.5',
    example: `<script type="module" src="./src/components/index.js"></script>
<liquid-toggle id="switch" checked label="Thông báo"></liquid-toggle>
<liquid-slider id="level" min="0" max="100" value="65" label="Mức độ"></liquid-slider>
<script>
  document.querySelector('#switch').addEventListener('change', e => {
    console.log(e.detail.checked);
  });
  document.querySelector('#level').addEventListener('input', e => {
    console.log(e.detail.value);
  });
</script>`,
    markup: `<div class="switch-showcase"><div class="switch-row"><div><strong>Thông báo</strong><span>Nhấn hoặc kéo sang hai bên</span></div><liquid-toggle id="notifications" label="Thông báo" checked></liquid-toggle></div><div class="switch-row"><div><strong>Chế độ máy bay</strong><span>Trạng thái minh hoạ</span></div><liquid-toggle id="airplane-demo" label="Máy bay demo"></liquid-toggle></div><div class="switch-row"><div><strong>Không tương tác</strong><span>Disabled</span></div><liquid-toggle label="Cần gạt vô hiệu hoá" disabled></liquid-toggle></div><div class="demo-slider"><div><label>Mức độ <span>LiquidSlider</span></label><output id="level-output">65%</output></div><liquid-slider id="demo-level" label="Mức độ demo" value="65"></liquid-slider></div></div><output id="switch-status" class="demo-output" aria-live="polite">Thông báo: bật · Máy bay: tắt</output>` },
  adaptive: { title: 'Filter tự động', subtitle: 'AdaptiveLuminanceGlass', description: 'Lấy mẫu nền 5 × 5 pixel, rồi tự đổi brightness, contrast, saturation, blur và màu chữ theo mã gốc.',
    sources: ['AdaptiveLuminanceGlassContent.kt', 'ColorFilter.kt'], files: ['backdrop.js', 'glass-surface.js', 'surface-renderer.js'],
    properties: 'Luminance .2126R + .7152G + .0722B · saturation 1.5 · lens 24 / minDimension ÷ 2 · depth bật',
    example: `<script type="module">
  import { setGlassBackdrop } from './src/components/index.js';
  // Canvas/image phủ viewport, cùng hình ảnh với nền CSS của trang.
  // setGlassBackdrop(backgroundCanvas);
  document.querySelector('adaptive-glass').addEventListener('filterchange', e => {
    console.log(e.detail.luminance, e.detail.brightness, e.detail.blur);
  });
</script>
<adaptive-glass style="width:280px; padding:32px">
  <h3>Tự thích nghi với nền</h3>
</adaptive-glass>`,
    markup: `<div class="adaptive-showcase"><adaptive-glass id="adaptive-preview"><div class="adaptive-copy">luminance:<br><span id="luminance-value">1.00</span></div></adaptive-glass></div><p class="interaction-hint">Kéo / hai ngón để zoom, xoay · Filter tween 1 giây theo Kotlin</p><div class="tone-controls"><label for="backdrop-luminance">Độ sáng nền</label><input type="range" id="backdrop-luminance" min="0" max="255" value="180" /><output id="tone-output">180 / 255</output></div><label class="auto-option"><input type="checkbox" id="adaptive-enabled" checked /> Tự động xử lý filter</label><dl id="adaptive-readout" class="filter-readout" aria-live="polite"></dl>` },
  'control-center': { title: 'Control Center', subtitle: 'LiquidControlCenter', description: 'Bố cục kính của template gốc, thêm trạng thái tương tác để lấy làm tài nguyên. Kéo thanh trên cùng để thu/mở.',
    sources: ['ControlCenterContent.kt','UISensor.android.kt'], files: ['control-center.js', 'glass-surface.js', 'liquid-slider.js'],
    properties: 'Grid: item 68, span đôi 152, gap 16, radius 34 · lens 24 / 48 · depth bật · spring mở 300 / .5',
    example: `<script type="module" src="./src/components/index.js"></script>
<liquid-control-center id="center" open="true"></liquid-control-center>
<script>
  document.querySelector('#center').addEventListener('controlchange', e => {
    console.log(e.detail.key, e.detail.value, e.detail.state);
  });
</script>`,
    markup: `<div class="center-showcase"><liquid-control-center></liquid-control-center></div><output id="center-status" class="demo-output" aria-live="polite">Chọn một nút hoặc thay đổi slider.</output>` },
};

const originalPage = document.querySelector('#taskbar-page');
const resourcePage = document.querySelector('#resource-page');
let parkedTaskbar;
let currentPage;
let pageAbort;
let resourceBackground = 'gradient';

function paintBackground(kind = resourceBackground, tone = 180) {
  resourceBackground = kind;
  const canvas = document.createElement('canvas'); canvas.width = innerWidth; canvas.height = innerHeight;
  const context = canvas.getContext('2d');
  if (kind === 'tone') {
    context.fillStyle = `rgb(${tone},${tone},${tone})`; context.fillRect(0,0,canvas.width,canvas.height);
  } else {
    const fill = context.createLinearGradient(0,0,0,canvas.height);
    fill.addColorStop(0,'#9ad7ff'); fill.addColorStop(.5,'#daf0ff'); fill.addColorStop(1,'#b9d4bc');
    context.fillStyle = fill; context.fillRect(0,0,canvas.width,canvas.height);
    if (kind === 'stripes') for (let x=0;x<canvas.width;x+=36) {
      context.fillStyle = x % 72 === 0 ? 'rgba(48,72,109,.55)' : 'rgba(255,255,255,.6)'; context.fillRect(x,0,18,canvas.height);
    }
  }
  const center=resourcePage.querySelector('liquid-control-center');
  if (center) {
    const progress=Math.max(0,Math.min(1,center.safeProgress.value));
    context.fillStyle = `rgba(0,0,0,${.4*progress})`; context.fillRect(0,0,canvas.width,canvas.height);
    const blurred=document.createElement('canvas');blurred.width=canvas.width;blurred.height=canvas.height;
    const ctx=blurred.getContext('2d');ctx.filter=`blur(${4*progress}px)`;ctx.drawImage(canvas,0,0);context.clearRect(0,0,canvas.width,canvas.height);context.drawImage(blurred,0,0);
  }
  document.documentElement.style.setProperty('--resource-background', `url(${canvas.toDataURL()})`);
  const edge=context.getImageData(canvas.width-1,canvas.height-1,1,1).data;
  document.documentElement.style.setProperty('--resource-edge-color',`rgb(${edge[0]},${edge[1]},${edge[2]})`);
  setGlassBackdrop(canvas);
}

function renderPage(id) {
  if (currentPage === id) return;
  pageAbort?.abort(); pageAbort = new AbortController(); const options = { signal: pageAbort.signal };
  if (id === 'taskbar') {
    resourcePage.replaceChildren(); resourcePage.hidden = true;
    if (parkedTaskbar) { originalPage.append(parkedTaskbar); parkedTaskbar = null; }
    originalPage.hidden = false;
    document.body.classList.remove('resource-mode');
    document.querySelector('flight-taskbar')?.refresh();
  } else {
    const page = PAGES[id];
    if (!parkedTaskbar) {
      parkedTaskbar = document.createDocumentFragment();
      while (originalPage.firstChild) parkedTaskbar.append(originalPage.firstChild);
    }
    originalPage.hidden = true;
    resourcePage.hidden = false;
    document.body.classList.add('resource-mode');
    resourcePage.innerHTML = `<header class="resource-heading"><span class="eyebrow">COMPONENT / ${page.subtitle}</span><h1 id="resource-title">${page.title}</h1><p>${page.description}</p></header>
      <div class="resource-layout"><section class="resource-demo" aria-label="Xem thử ${page.title}"><div class="demo-toolbar"><span>LIVE PREVIEW</span>${id !== 'adaptive' ? '<div role="group" aria-label="Nền component"><button type="button" data-resource-background="gradient" aria-pressed="true">Gradient</button><button type="button" data-resource-background="stripes" aria-pressed="false">Sọc</button></div>' : '<span>5 × 5 luminance samples</span>'}</div>${page.markup}</section>
      <aside class="resource-info"><h2>Dùng lại component</h2><p class="source-values">${page.properties}</p><div class="source-links">${page.sources.map(file=>`<a href="./resources/kotlin/${file}" target="_blank" rel="noopener">${file} ↗</a>`).join('')}</div><details open><summary>Ví dụ HTML / JavaScript</summary><pre><code id="resource-code"></code></pre></details><div class="resource-actions"><button id="copy-example" type="button">Sao chép ví dụ</button><a href="./resources.zip" download="liquid-glass-resources.zip">Tải source .zip</a></div><p id="copy-status" class="copy-status" role="status"></p><h3>File component</h3><ul>${page.files.map(file=>`<li><a href="./src/components/${file}" target="_blank" rel="noopener">src/components/${file}</a></li>`).join('')}</ul><p class="resource-disclaimer">WebGL2: khúc xạ thật. CSS fallback: blur, màu và tương tác; không mô phỏng đầy đủ lens. Nội dung DOM không tự được capture—cần truyền canvas/image nền khi tích hợp.</p></aside></div>`;
    document.querySelector('#resource-code').textContent = page.example;
    document.querySelector('#copy-example').addEventListener('click', async () => {
      try { await navigator.clipboard.writeText(page.example); document.querySelector('#copy-status').textContent = 'Đã sao chép ví dụ.'; }
      catch { const selection = getSelection(); const range = document.createRange(); range.selectNodeContents(document.querySelector('#resource-code')); selection.removeAllRanges(); selection.addRange(range);
        document.querySelector('#copy-status').textContent = 'Đã chọn đoạn mã. Nhấn Ctrl+C để sao chép.'; }
    }, options);
    resourcePage.querySelectorAll('[data-resource-background]').forEach(button => button.addEventListener('click', () => {
      resourcePage.querySelectorAll('[data-resource-background]').forEach(item => item.setAttribute('aria-pressed', String(item === button)));
      paintBackground(button.dataset.resourceBackground);
    }, options));
    paintBackground(id === 'adaptive' ? 'tone' : 'gradient');
    resourcePage.querySelectorAll('[data-destination]').forEach(button=>button.addEventListener('click',()=>{location.hash=button.dataset.destination;},options));
    if(id==='dialog'){
      const dialog=resourcePage.querySelector('liquid-dialog');
      resourcePage.querySelector('#open-dialog').addEventListener('activate',()=>{dialog.show();document.querySelector('#dialog-status').value='Hộp thoại đang mở';},options);
      dialog.addEventListener('dialogaction',e=>{document.querySelector('#dialog-status').value=`Đã chọn: ${e.detail.action}`;},options);
    }
    if(id==='control-center')resourcePage.addEventListener('centerprogress',()=>paintBackground(),options);
    if (id === 'buttons') {
      let count = 0;
      resourcePage.addEventListener('activate', () => { document.querySelector('#button-count').value = `Đã kích hoạt ${++count} lần`; }, options);
    }
    if (id === 'switches') {
      resourcePage.addEventListener('change', () => { document.querySelector('#switch-status').value = `Thông báo: ${document.querySelector('#notifications').checked ? 'bật' : 'tắt'} · Máy bay: ${document.querySelector('#airplane-demo').checked ? 'bật' : 'tắt'}`; }, options);
      document.querySelector('#demo-level').addEventListener('input', event => { document.querySelector('#level-output').value = `${event.detail.value}%`; }, options);
    }
    if (id === 'adaptive') {
      const preview = document.querySelector('#adaptive-preview');
      preview.addEventListener('filterchange', ({detail}) => {
        document.querySelector('#luminance-value').textContent=detail.luminance.toFixed(2);
        document.querySelector('#adaptive-readout').innerHTML = ['luminance','brightness','contrast','saturation','blur'].map(key=>`<div><dt>${key}</dt><dd>${detail[key].toFixed(3)}${key === 'blur' ? 'px' : ''}</dd></div>`).join('');
      }, options);
      document.querySelector('#backdrop-luminance').addEventListener('input', event => {
        document.querySelector('#tone-output').value = `${event.target.value} / 255`; paintBackground('tone',Number(event.target.value));
      }, options);
      document.querySelector('#adaptive-enabled').addEventListener('change', event => {
        preview.toggleAttribute('manual', !event.target.checked);
        document.querySelector('#adaptive-readout').hidden = !event.target.checked;
      }, options);
    }
    if (id === 'control-center') resourcePage.addEventListener('controlchange', ({detail}) => {
      document.querySelector('#center-status').value = `${detail.key}: ${detail.value} (demo)`;
    }, options);
  }
  currentPage = id;
  document.querySelectorAll('[data-page]').forEach(button => {
    if (button.dataset.page === id) {
      button.setAttribute('aria-current','page');
      button.scrollIntoView({ block:'nearest', inline:'nearest' });
    } else button.removeAttribute('aria-current');
  });
  window.scrollTo(0,0);
}
function route() {
  const id = location.hash.slice(1);
  renderPage(Object.hasOwn(PAGES,id) ? id : 'taskbar');
}
document.querySelectorAll('[data-page]').forEach(button => button.addEventListener('click',()=> {
  if (location.hash === `#${button.dataset.page}`) renderPage(button.dataset.page);
  else location.hash = button.dataset.page;
}));
window.addEventListener('hashchange',route);
window.addEventListener('resize',()=> {
  if (currentPage && currentPage !== 'taskbar') paintBackground(resourceBackground,Number(document.querySelector('#backdrop-luminance')?.value || 180));
  document.querySelector('[data-page][aria-current="page"]')?.scrollIntoView({ block:'nearest', inline:'nearest' });
});
route();
