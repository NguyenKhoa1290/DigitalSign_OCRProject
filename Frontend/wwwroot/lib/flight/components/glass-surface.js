import { SurfaceRenderer } from './surface-renderer.js';
import { adaptiveFilter, getGlassBackdrop, sampleLuminance } from './backdrop.js';
import { TransformGesture } from './transform-gesture.js';

export const SURFACE_DEFAULTS = Object.freeze({ radius: 24, blur: 8, refractionHeight: 24,
  refractionAmount: 24, depth: false, chromatic: false, brightness: 0, contrast: 1,
  saturation: 1.5, highlight: 1, highlightAngle: 45, highlightFalloff: 1, innerShadow: 0, alphaMask: false,
  sampleScale: 1, glow: 0, tint: [250 / 255, 250 / 255, 250 / 255, .15] });

export class GlassSurface extends HTMLElement {
  static observedAttributes = ['adaptive', 'disabled', 'radius'];
  constructor() {
    super();
    this.attachShadow({ mode: 'open' }).innerHTML = `<style>
      :host { display:block; position:relative; border-radius:var(--glass-radius,24px); isolation:isolate; }
      .fallback,canvas { position:absolute; inset:0; width:100%; height:100%; pointer-events:none; border-radius:inherit; }
      .fallback { background:var(--glass-tint,rgba(250,250,250,.15)); backdrop-filter:var(--glass-filter,blur(8px) saturate(1.5));
        -webkit-backdrop-filter:var(--glass-filter,blur(8px) saturate(1.5)); box-shadow:inset 0 0 0 1px rgba(255,255,255,.5); }
      :host([data-renderer="webgl2"]) .fallback { background:transparent; backdrop-filter:none; -webkit-backdrop-filter:none; box-shadow:none; }
      :host([data-renderer="css"]) canvas { display:none; }
      .content { position:relative; z-index:1; height:100%; color:inherit; border-radius:inherit; }
      :host([data-adaptive="true"]) .content { color:var(--glass-content,#000); transition:color 1s; }
      @media(prefers-reduced-motion:reduce) { .content { transition:none!important; } }
    </style><div class="fallback" aria-hidden="true"></div><canvas aria-hidden="true"></canvas><div class="content"><slot></slot></div>`;
    this.canvas = this.shadowRoot.querySelector('canvas');
    this._settings = { ...SURFACE_DEFAULTS, tint: [...SURFACE_DEFAULTS.tint] };
    this.frame = 0;
  }
  get adaptive() { return this.hasAttribute('adaptive'); }
  get settings() { return { ...this._settings, tint: [...this._settings.tint] }; }
  get filterState() { return this._filter ? { ...this._filter } : null; }
  set backdrop(source) { this._backdrop = source; this.refresh(true); }
  get backdrop() { return this._backdrop || getGlassBackdrop(); }
  configure(patch) {
    for (const key of Object.keys(SURFACE_DEFAULTS)) {
      if (!(key in patch)) continue;
      if (key === 'tint') {
        if (Array.isArray(patch.tint) && patch.tint.length === 4 && patch.tint.every(Number.isFinite)) this._settings.tint = patch.tint.map(x => Math.max(0, Math.min(1, x)));
      } else if (typeof SURFACE_DEFAULTS[key] === 'boolean') this._settings[key] = Boolean(patch[key]);
      else if (Number.isFinite(Number(patch[key]))) {
        const bounds = { brightness: [-1, 1], contrast: [0, 3], saturation: [0, 3], glow: [0, 1], highlight: [0, 1], highlightAngle: [-360,360], sampleScale: [.1,10] }[key] || [0, 256];
        this._settings[key] = Math.max(bounds[0], Math.min(bounds[1], Number(patch[key])));
      }
    }
    this.refresh();
  }
  connectedCallback() {
    // A removed page must release its WebGL context, not just textures/programs.
    // Reconnecting the same public component receives a fresh canvas/context.
    if (this.contextReleased) {
      const canvas = document.createElement('canvas'); canvas.setAttribute('aria-hidden', 'true');
      this.canvas.replaceWith(canvas); this.canvas = canvas; this.contextReleased = false;
    }
    this.abort = new AbortController();
    this._luminance ??= matchMedia('(prefers-color-scheme:dark)').matches ? 0 : 1;
    this.sampleTime = 0;
    const options = { signal: this.abort.signal };
    this.canvas.addEventListener('webglcontextlost', event => { event.preventDefault(); this.dataset.renderer = 'css'; this.refresh(); }, options);
    this.canvas.addEventListener('webglcontextrestored', () => { this.createRenderer(); this.refresh(true); }, options);
    window.addEventListener('glass-backdropchange', () => this.refresh(true), options);
    window.addEventListener('resize', () => this.refresh(true), options);
    window.addEventListener('scroll', () => this.refresh(), { ...options, passive: true });
    document.addEventListener('visibilitychange', () => {
      if (document.hidden) { cancelAnimationFrame(this.frame); this.frame = 0; }
      else this.refresh(true);
    }, options);
    this.createRenderer();
    this.resizeObserver = new ResizeObserver(() => this.refresh());
    this.resizeObserver.observe(this);
    this.refresh(true);
  }
  disconnectedCallback() {
    this.abort?.abort(); this.resizeObserver?.disconnect();
    cancelAnimationFrame(this.frame); this.frame = 0;
    const gl = this.renderer?.gl;
    this.renderer?.dispose(); this.renderer = null;
    if (gl && !gl.isContextLost()) gl.getExtension('WEBGL_lose_context')?.loseContext();
    this.contextReleased = true;
  }
  attributeChangedCallback() { this.refresh(); }
  createRenderer() {
    this.renderer?.dispose();
    this.sizeKey = null; this.uploadedBackdrop = null;
    try { this.renderer = new SurfaceRenderer(this.canvas); this.dataset.renderer = 'webgl2'; }
    catch (error) { this.renderer = null; this.dataset.renderer = 'css'; this.dispatchEvent(new CustomEvent('renderfallback', { detail: { reason: error.message } })); }
  }
  refresh(upload = false) {
    this.needsUpload ||= upload;
    if (!this.isConnected || this.frame || document.hidden) return;
    this.frame = requestAnimationFrame(() => { this.frame = 0; this.draw(); });
  }
  draw() {
    const bounds = this.getBoundingClientRect();
    // Render in untransformed local coordinates; map samples back to viewport.
    // Preserves inverse backdrop sampling when the host is scaled or rotated.
    let matrix = new DOMMatrix();
    for(let node=this; node instanceof Element; node=node.parentElement || node.getRootNode().host) {
      const transform=getComputedStyle(node).transform;
      if(transform && transform!=='none') matrix=new DOMMatrix(transform).multiply(matrix);
    }
    const width=this.offsetWidth, height=this.offsetHeight;
    const rect = { width, height,
      left: bounds.left+bounds.width/2-(matrix.a*width+matrix.c*height)/2,
      top: bounds.top+bounds.height/2-(matrix.b*width+matrix.d*height)/2 };
    if (rect.width <= 0 || rect.height <= 0) return;
    const settings = this.settings;
    settings.basis=[matrix.a,matrix.b,matrix.c,matrix.d];
    settings.sdf=this.sdf; settings.sampleOffset=this.sampleOffset;
    settings.overlay = this.overlay;
    if (this.hasAttribute('radius') && Number.isFinite(Number(this.getAttribute('radius')))) settings.radius = Math.max(0, Number(this.getAttribute('radius')));
    this.dataset.adaptive = String(this.adaptive);
    if (this.adaptive) {
      try {
        const now=performance.now();
        if(!this.sampleTime || now-this.sampleTime>=1000) {
          if(this.sampleTime)this._luminance=this.luminanceTarget;
          this.luminanceFrom=this._luminance;
          this.luminanceTarget=sampleLuminance(this.backdrop,bounds);
          this.sampleTime=now;
          this.needsUpload=true;
          this.style.setProperty('--glass-content',this.luminanceTarget>.5 ? '#000' : '#fff');
        }
        const t=matchMedia('(prefers-reduced-motion:reduce)').matches ? 1 : Math.min(1,(now-this.sampleTime)/1000);
        // Compose tween(1000) defaults to FastOutSlowIn (cubic .4,0,.2,1).
        let lo=0,hi=1;
        for(let i=0;i<12;i++) { const m=(lo+hi)/2, x=1.2*(1-m)*(1-m)*m+.6*(1-m)*m*m+m*m*m; if(x<t) lo=m; else hi=m; }
        const m=(lo+hi)/2, eased=t===1 ? 1 : 3*(1-m)*m*m+m*m*m;
        this._luminance=this.luminanceFrom+(this.luminanceTarget-this.luminanceFrom)*eased;
        this._filter = adaptiveFilter(this._luminance);
        Object.assign(settings, this._filter);
        settings.depth = true; settings.refractionAmount = Math.min(rect.width, rect.height) / 2;
        settings.refractionHeight = 24; settings.tint = [0, 0, 0, 0];
        this._filter.contentColor=this.luminanceTarget>.5 ? '#000000' : '#ffffff';
        this.dispatchEvent(new CustomEvent('filterchange', { bubbles: true, composed: true, detail: this.filterState }));
        delete this.dataset.samplingError;
      } catch { this.dataset.samplingError = 'Backdrop must be same-origin or CORS-enabled'; }
    } else this._filter = null;
    if(this.adaptive && !document.hidden) this.refresh();
    this.style.setProperty('--glass-radius', `${settings.radius}px`);
    this.style.setProperty('--glass-tint', `rgba(${settings.tint.slice(0, 3).map(x => x * 255).join(',')},${settings.tint[3]})`);
    // CSS approximation; exact brightness/contrast matrix is in the WebGL shader.
    this.style.setProperty('--glass-filter', `blur(${settings.blur}px) saturate(${settings.saturation}) contrast(${settings.contrast}) brightness(${1 + settings.brightness})`);
    if (!this.renderer || this.dataset.renderer !== 'webgl2') return;
    try {
      const size = `${rect.width}:${rect.height}:${devicePixelRatio}`;
      if (size !== this.sizeKey) { this.sizeKey = size; this.renderer.resize(rect); }
      const backdrop = this.backdrop;
      if (this.needsUpload || this.uploadedBackdrop !== backdrop) {
        this.renderer.upload(backdrop); this.uploadedBackdrop = backdrop; this.needsUpload = false;
      }
      this.renderer.render(rect, settings);
    } catch (error) {
      this.renderer.dispose(); this.renderer = null; this.dataset.renderer = 'css';
      this.dispatchEvent(new CustomEvent('renderfallback', { detail: { reason: error.message } }));
    }
  }
}
export class AdaptiveGlass extends GlassSurface {
  get adaptive() { return !this.hasAttribute('manual'); }
  static observedAttributes = [...GlassSurface.observedAttributes, 'manual'];
  connectedCallback(){
    super.connectedCallback();this.tabIndex=0;
    this.setAttribute('aria-label','Kính tự điều chỉnh, kéo hoặc dùng hai ngón để thu phóng và xoay');
    this.gesture=new TransformGesture(this,state=>{this.style.transform=`translate(${state.x}px,${state.y}px) rotate(${state.rotation}deg) scale(${state.scale})`;this.refresh();});
  }
  disconnectedCallback(){this.gesture?.dispose();super.disconnectedCallback();}
}
customElements.define('glass-surface', GlassSurface);
customElements.define('adaptive-glass', AdaptiveGlass);
