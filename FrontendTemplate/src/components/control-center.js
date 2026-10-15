import './glass-surface.js';
import './liquid-slider.js';
import { flightSvg } from '../flight-icon.js';
import { Spring, clamp } from '../spring.js';

// Geometry/effects from ControlCenterContent.kt. Added controls are web demos,
// not access to device Wi-Fi, Bluetooth, display brightness or system volume.
export class LiquidControlCenter extends HTMLElement {
  static observedAttributes = ['open'];
  constructor() {
    super();
    this._state = { airplane: false, wifi: true, bluetooth: true, focus: false, playing: false, brightness: 65, volume: 40, flashlight:false,camera:false,recording:false };
    this.highlightAngle=45;
    this.progress = new Spring(1, 300, .5);
    this.safeProgress = new Spring(1,300,1);
    this.attachShadow({ mode: 'open' }).innerHTML = `<style>
      :host { display:block; width:320px; max-width:100%; color:white; font:13px Inter,Arial,Helvetica,sans-serif; }
      * { box-sizing:border-box; } button { font:inherit; cursor:pointer; }
      .handle { display:block; width:100%; height:34px; border:0; background:none; color:inherit; touch-action:none; }
      .handle:before { content:''; display:block; width:40px; height:5px; border-radius:99px; margin:auto; background:rgba(255,255,255,.75); }
      .grid { display:grid; grid-template-columns:repeat(4,minmax(0,1fr)); gap:16px; transform-origin:top center; }
      .tile { min-width:0; height:68px; border-radius:34px; }
      .double { grid-column:span 2; height:152px; }
      .wide { grid-column:span 2; }
      .vertical { height:152px; grid-row:span 2; }
      .connections { display:grid; grid-template-columns:repeat(2,1fr); gap:8px; padding:16px; }
      .connection { width:100%; max-width:56px; height:auto; aspect-ratio:1; border:0; border-radius:999px; background:rgba(255,255,255,.2); color:white; }
      .connection[aria-pressed=true] { background:#0088ff; }
      svg { width:28px; height:28px; display:block; margin:auto; }
      .media { display:flex; flex-direction:column; align-items:center; justify-content:center; height:100%; gap:10px; padding:16px; }
      .media span { font-size:11px; opacity:.75; }
      .play { border:0; background:none; color:white; font-size:24px; }
      .tile-button { border:0; width:100%; height:100%; padding:12px; color:white; background:none; border-radius:34px; }
      .tile-button[aria-pressed=true] { background:rgba(0,136,255,.7); }
      .symbol { display:block; font-size:22px; line-height:1; margin-bottom:4px; }
      .vertical-inner { display:flex; flex-direction:column; align-items:center; justify-content:center; height:100%; gap:2px; padding:12px 0; }
      liquid-slider[vertical] { width:44px; height:104px; }
      .slider-symbol { font-size:20px; }
      .readout { font-size:10px; opacity:.85; }
      .bottom-large{grid-column:1 / span 2;grid-row:4 / span 2;height:152px}
      .bottom-a{grid-column:3;grid-row:4}.bottom-b{grid-column:4;grid-row:4}.bottom-c{grid-column:3;grid-row:5}
      .sensor{margin-top:16px;display:grid;gap:10px;font:11px Inter,Arial,sans-serif}.sensor button{background:rgba(255,255,255,.15);border:1px solid rgba(255,255,255,.3);color:white;border-radius:12px;padding:10px}
      .sensor label{display:flex;align-items:center;gap:12px}.sensor input{min-width:0;flex:1;accent-color:#0088ff}.sensor output{width:40px;font-variant-numeric:tabular-nums}
      .hint { margin:15px 0 0; text-align:center; font-size:11px; opacity:.75; }
      button:focus-visible { outline:2px solid #fff; outline-offset:3px; }
      @media(max-width:350px) { .grid { gap:12px; } .connections { padding:12px; gap:4px; } .connection { max-width:48px; } }
    </style>
      <button class="handle" type="button" aria-label="Kéo để mở hoặc đóng Control Center" aria-expanded="true"></button>
      <div class="grid">
        <glass-surface class="tile double"><div class="connections">
          <button class="connection" data-action="airplane" aria-label="Chế độ máy bay" aria-pressed="false">${flightSvg}</button>
          <button class="connection" data-action="wifi" aria-label="Wi-Fi" aria-pressed="true">Wi-Fi</button>
          <button class="connection" data-action="bluetooth" aria-label="Bluetooth" aria-pressed="true">BT</button>
        </div></glass-surface>
        <glass-surface class="tile double"><div class="media"><span>Phát thử tương tác</span><strong>Liquid Glass</strong><button class="play" data-action="playing" aria-label="Phát / tạm dừng" aria-pressed="false">▷</button><span class="media-state">Đang tạm dừng</span></div></glass-surface>
        <glass-surface class="tile"><button class="tile-button" data-action="focus" aria-label="Tập trung" aria-pressed="false"><span class="symbol">☾</span></button></glass-surface>
        <glass-surface class="tile"><button class="tile-button" data-action="airplane" aria-label="Máy bay" aria-pressed="false">${flightSvg}</button></glass-surface>
        <glass-surface class="tile vertical"><div class="vertical-inner"><liquid-slider vertical label="Độ sáng demo" value="65"></liquid-slider><span class="slider-symbol" aria-hidden="true">☀</span><output class="readout brightness">65%</output></div></glass-surface>
        <glass-surface class="tile vertical"><div class="vertical-inner"><liquid-slider vertical label="Âm lượng demo" value="40"></liquid-slider><span class="slider-symbol" aria-hidden="true">♪</span><output class="readout volume">40%</output></div></glass-surface>
        <glass-surface class="tile wide"><button class="tile-button" data-action="focus" aria-label="Chế độ tập trung" aria-pressed="false">☾ &nbsp; Tập trung</button></glass-surface>
        <glass-surface class="tile bottom-large" aria-label="Ô kính lớn hàng dưới"></glass-surface>
        <glass-surface class="tile bottom-a"><button class="tile-button" data-action="flashlight" aria-label="Đèn pin demo" aria-pressed="false">${flightSvg}</button></glass-surface>
        <glass-surface class="tile bottom-b"><button class="tile-button" data-action="camera" aria-label="Camera demo" aria-pressed="false">${flightSvg}</button></glass-surface>
        <glass-surface class="tile bottom-c"><button class="tile-button" data-action="recording" aria-label="Ghi màn hình demo" aria-pressed="false">${flightSvg}</button></glass-surface>
      </div><div class="sensor"><button type="button" class="sensor-enable">Bật phản chiếu theo cảm biến</button><label>Góc sáng<input class="light-angle" type="range" min="-180" max="180" value="45" aria-label="Góc sáng Control Center"><output class="angle-output">45°</output></label><span class="sensor-status" role="status">Dùng góc thủ công; chưa bật cảm biến.</span></div><p class="hint">Demo UI — không đổi cài đặt hệ thống.</p>`;
    this.grid = this.shadowRoot.querySelector('.grid');
    this.handle = this.shadowRoot.querySelector('.handle');
  }
  get open() { return this.getAttribute('open') !== 'false'; }
  set open(value) { this.setAttribute('open', String(Boolean(value))); }
  get state() { return { ...this._state }; }
  connectedCallback() {
    this.abort = new AbortController(); const options = { signal: this.abort.signal };
    this.shadowRoot.querySelectorAll('glass-surface').forEach(glass => glass.configure({ radius: 34, blur: 0,
      refractionHeight: 24, refractionAmount: 48, depth: true, highlightFalloff:2, tint: [0,0,0,.05] }));
    this.shadowRoot.querySelector('.light-angle').addEventListener('input',e=>this.setHighlightAngle(Number(e.target.value)),options);
    this.shadowRoot.querySelector('.sensor-enable').addEventListener('click',()=>this.enableMotion(),options);
    this.shadowRoot.addEventListener('click', event => {
      const button = event.target.closest('[data-action]');
      if (!button) return;
      const key = button.dataset.action; this._state[key] = !this._state[key]; this.sync(); this.notify(key);
    }, options);
    this.shadowRoot.querySelectorAll('liquid-slider').forEach((slider, i) => slider.addEventListener('input', event => {
      const key = i === 0 ? 'brightness' : 'volume'; this._state[key] = event.detail.value; this.sync(); this.notify(key);
    }, options));
    this.handle.addEventListener('click', event => { if (event.detail !== 0 && this.ignoreClick) { this.ignoreClick = false; return; } this.open = !this.open; }, options);
    this.handle.addEventListener('pointerdown', event => {
      if (!event.isPrimary || event.button !== 0) return;
      this.ignoreClick = false;
      this.pointer = { id: event.pointerId, y: event.clientY, progress: this.progress.value, start: this.open };
      this.handle.setPointerCapture(event.pointerId);
    }, options);
    this.handle.addEventListener('pointermove', event => {
      if (!this.pointer || event.pointerId !== this.pointer.id) return;
      const delta = (event.clientY - this.pointer.y) / 220;
      this.pointer.moved ||= Math.abs(delta) > .02;
      this.progress.snap(clamp(this.pointer.progress + delta, 0, 1.2));this.safeProgress.snap(clamp(this.progress.value,0,1)); this.render();
    }, options);
    this.handle.addEventListener('pointerup', event => this.endPointer(event), options);
    this.handle.addEventListener('pointercancel', event => this.endPointer(event, true), options);
    this.handle.addEventListener('lostpointercapture', event => this.endPointer(event, true), options);
    window.addEventListener('blur', () => { if (this.pointer) this.endPointer({ pointerId: this.pointer.id }, true); }, options);
    document.addEventListener('visibilitychange', () => {
      if (document.hidden) { cancelAnimationFrame(this.frame); this.frame = 0; }
      else this.animate();
    }, options);
    this.progress.snap(this.open ? 1 : 0);this.safeProgress.snap(this.open ? 1 : 0); this.sync(); this.render();
  }
  setHighlightAngle(angle){
    if(!Number.isFinite(angle))return;this.highlightAngle=angle;
    this.shadowRoot.querySelector('.light-angle').value=angle;this.shadowRoot.querySelector('.angle-output').value=`${Math.round(angle)}°`;
    this.shadowRoot.querySelectorAll('glass-surface.tile').forEach(glass=>glass.configure({highlightAngle:angle}));
  }
  async enableMotion(){
    const status=this.shadowRoot.querySelector('.sensor-status');
    if(this.motionEnabled){this.motionEnabled=false;this.motionAbort?.abort();status.textContent='Đã tắt cảm biến; dùng góc thủ công.';this.shadowRoot.querySelector('.sensor-enable').textContent='Bật phản chiếu theo cảm biến';return;}
    if(!isSecureContext || typeof DeviceMotionEvent==='undefined'){status.textContent='Trình duyệt không hỗ trợ cảm biến; dùng thanh góc sáng.';return;}
    try{
      if(typeof DeviceMotionEvent.requestPermission==='function' && await DeviceMotionEvent.requestPermission()!=='granted'){status.textContent='Quyền cảm biến bị từ chối; dùng thanh góc sáng.';return;}
      if(!this.isConnected)return;this.motionAbort=new AbortController();this.motionEnabled=true;
      window.addEventListener('devicemotion',e=>{
        if(document.hidden)return;const g=e.accelerationIncludingGravity;if(!Number.isFinite(g?.x) || !Number.isFinite(g?.y) || Math.hypot(g.x,g.y)<.01)return;
        const screenAngle=screen.orientation?.angle || 0;
        this.setHighlightAngle(this.highlightAngle*.5+(Math.atan2(g.y,g.x)*180/Math.PI-screenAngle)*.5);
        status.textContent='Phản chiếu đang theo góc trọng lực thiết bị.';
      },{signal:this.motionAbort.signal});
      this.shadowRoot.querySelector('.sensor-enable').textContent='Tắt phản chiếu theo cảm biến';status.textContent='Đang chờ dữ liệu cảm biến; vẫn có thể chỉnh góc thủ công.';
    }catch{status.textContent='Không truy cập được cảm biến; dùng thanh góc sáng.';}
  }
  endPointer(event, cancelled = false) {
    if (!this.pointer || event.pointerId !== this.pointer.id) return;
    const pointer = this.pointer; this.pointer = null;
    if (this.handle.hasPointerCapture(pointer.id)) this.handle.releasePointerCapture(pointer.id);
    if (pointer.moved) { this.ignoreClick = true; this.open = cancelled ? pointer.start : this.progress.value >= .5; }
    else this.animate();
  }
  notify(key) { this.dispatchEvent(new CustomEvent('controlchange', { bubbles: true, composed: true, detail: { key, value: this._state[key], state: this.state } })); }
  sync() {
    this.shadowRoot.querySelectorAll('[data-action]').forEach(button => button.setAttribute('aria-pressed', String(this._state[button.dataset.action])));
    this.shadowRoot.querySelector('.play').textContent = this._state.playing ? 'Ⅱ' : '▷';
    this.shadowRoot.querySelector('.media-state').textContent = this._state.playing ? 'Đang phát (demo)' : 'Đang tạm dừng';
    this.shadowRoot.querySelector('.brightness').value = `${this._state.brightness}%`;
    this.shadowRoot.querySelector('.volume').value = `${this._state.volume}%`;
  }
  attributeChangedCallback() { if (this.isConnected) this.animate(); }
  animate() {
    this.progress.target = this.open ? 1 : 0;
    this.safeProgress.target=this.progress.target;
    this.progress.dampingRatio = this.open ? .5 : 1;
    if (this.frame || document.hidden) return;
    this.last = performance.now();
    const tick = time => {
      this.frame = 0;
      if (matchMedia('(prefers-reduced-motion: reduce)').matches) {this.progress.snap(this.progress.target);this.safeProgress.snap(this.safeProgress.target);}
      else {const dt=Math.min(.5, Math.max(.001, (time - this.last) / 1000));this.progress.step(dt);this.safeProgress.step(dt);}
      this.last = time; this.render();
      if (!this.progress.settled || !this.safeProgress.settled) this.frame = requestAnimationFrame(tick);
    };
    this.frame = requestAnimationFrame(tick);
  }
  render() {
    const progress = clamp(this.safeProgress.value, 0, 1);
    this.handle.setAttribute('aria-expanded', String(this.open));
    this.grid.inert = progress < .05;
    this.grid.style.opacity = progress ** 2;
    this.grid.style.transform = `translateY(${-48 * (1-progress)}px) scale(${1 / (1 + .1 * Math.max(0,this.progress.value-1))},${1 + .1 * Math.max(0,this.progress.value-1)})`;
    this.shadowRoot.querySelectorAll('glass-surface.tile').forEach(glass => glass.configure({ refractionHeight: 24 * progress, refractionAmount: 48 * progress }));
    if(this.lastBackdropProgress===undefined || Math.abs(this.lastBackdropProgress-progress)>.04 || (progress===this.safeProgress.target && progress!==this.lastBackdropProgress)){
      this.lastBackdropProgress=progress;
      this.dispatchEvent(new CustomEvent('centerprogress',{bubbles:true,composed:true,detail:{progress}}));
    }
  }
  disconnectedCallback() { this.pointer = null; this.abort?.abort();this.motionAbort?.abort();this.motionEnabled=false; cancelAnimationFrame(this.frame); this.frame = 0; }
}
customElements.define('liquid-control-center', LiquidControlCenter);
