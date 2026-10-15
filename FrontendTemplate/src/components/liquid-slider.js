import './glass-surface.js';
import { PressMotion } from './motion.js';
import { clamp } from '../spring.js';

export class LiquidSlider extends HTMLElement {
  static observedAttributes = ['value', 'min', 'max', 'step', 'disabled', 'label', 'vertical', 'dir'];
  constructor() {
    super();
    this.attachShadow({ mode: 'open' }).innerHTML = `<style>
      :host { display:block; width:100%; height:44px; position:relative; }
      :host([vertical]) { width:68px; height:152px; }
      .rail { position:relative; width:100%; height:44px; }
      :host([vertical]) .rail { position:absolute; width:var(--slider-length,152px); left:50%; top:50%; transform:translate(-50%,-50%) rotate(-90deg); }
      .track { position:absolute; top:19px; left:0; width:100%; height:6px; border-radius:999px;
        background:linear-gradient(to right,#0088ff 0 var(--fill,50%),rgba(120,120,120,.2) var(--fill,50%) 100%); }
      glass-surface { position:absolute; width:40px; height:24px; top:10px; left:0; border-radius:999px;
        box-shadow:0 1px 4px rgba(0,0,0,.05); pointer-events:none; }
      input { position:absolute; inset:0; appearance:none; width:100%; height:44px; margin:0; opacity:0; cursor:ew-resize; touch-action:none; }
      input::-webkit-slider-thumb { appearance:none; width:40px; height:24px; }
      input::-moz-range-thumb { width:40px; height:24px; border:0; }
      .rail:has(input:focus-visible) { outline:2px solid #0088ff; outline-offset:4px; border-radius:999px; }
      :host([disabled]) { opacity:.4; }
      input:disabled { cursor:not-allowed; }
    </style><div class="rail"><div class="track" aria-hidden="true"></div><glass-surface aria-hidden="true"></glass-surface><input type="range" min="0" max="100" step="1" value="50" /></div>`;
    this.input = this.shadowRoot.querySelector('input');
    this.glass = this.shadowRoot.querySelector('glass-surface');
    this.track = this.shadowRoot.querySelector('.track');
    this.rail = this.shadowRoot.querySelector('.rail');
  }
  get min() { const n = Number(this.getAttribute('min') ?? 0); return Number.isFinite(n) ? n : 0; }
  get max() { const n = Number(this.getAttribute('max') ?? 100); return Number.isFinite(n) ? Math.max(this.min, n) : Math.max(this.min, 100); }
  get value() { const n = Number(this.getAttribute('value') ?? 50); return clamp(Number.isFinite(n) ? n : this.min, this.min, this.max); }
  set value(value) { const n = Number(value); this.setAttribute('value', String(clamp(Number.isFinite(n) ? n : this.min, this.min, this.max))); }
  connectedCallback() {
    this.abort = new AbortController(); const options = { signal: this.abort.signal };
    this.motion = new PressMotion(motion => this.render(motion));
    this.input.addEventListener('input', event => { event.stopPropagation(); this.value = this.input.value;
      this.dispatchEvent(new CustomEvent('input', { bubbles: true, composed: true, detail: { value: this.value } })); }, options);
    this.input.addEventListener('change', event => { event.stopPropagation();
      this.dispatchEvent(new CustomEvent('change', { bubbles: true, composed: true, detail: { value: this.value } })); }, options);
    this.input.addEventListener('pointerdown', event => { if (!this.input.disabled && event.isPrimary && event.button === 0) this.motion.press(); }, options);
    for (const type of ['pointerup', 'pointercancel']) window.addEventListener(type, () => this.motion.release(), options);
    this.input.addEventListener('keydown', event => {
      if (['ArrowLeft','ArrowRight','ArrowUp','ArrowDown','Home','End','PageUp','PageDown'].includes(event.key)) this.motion.press();
    }, options);
    this.input.addEventListener('keyup', () => this.motion.release(), options);
    this.input.addEventListener('blur', () => this.motion.release(), options);
    window.addEventListener('blur', () => this.motion.release(), options);
    document.addEventListener('visibilitychange', () => { if (document.hidden) { this.motion.release(); this.motion.dispose(); } }, options);
    this.observer = new ResizeObserver(() => this.motion.wake()); this.observer.observe(this);
    this.sync();
  }
  disconnectedCallback() { this.abort?.abort(); this.observer?.disconnect(); this.motion?.dispose(); }
  attributeChangedCallback() { if (this.input) this.sync(); }
  sync() {
    this.input.min = this.min; this.input.max = this.max;
    const step = Number(this.getAttribute('step') ?? 1);
    this.input.step = Number.isFinite(step) && step > 0 ? step : 1;
    this.input.value = this.value;
    this.input.disabled = this.hasAttribute('disabled');
    this.input.setAttribute('aria-label', this.getAttribute('label') || 'Giá trị');
    this.input.setAttribute('aria-orientation', this.hasAttribute('vertical') ? 'vertical' : 'horizontal');
    this.motion?.wake();
  }
  render(motion) {
    const fraction = this.max === this.min ? 0 : (this.value - this.min) / (this.max - this.min);
    const rtl = getComputedStyle(this).direction === 'rtl' && !this.hasAttribute('vertical');
    const visual = rtl ? 1 - fraction : fraction;
    const progress = clamp(motion.progress.value, 0, 1);
    if (this.hasAttribute('vertical')) this.style.setProperty('--slider-length', `${this.clientHeight}px`);
    const width = this.rail.offsetWidth;
    this.rail.style.setProperty('--fill', `${visual * 100}%`);
    this.glass.style.transform = `translateX(${clamp(width * visual - 20, -10, width - 30)}px) scale(${motion.scaleX.value},${motion.scaleY.value})`;
    const rect = this.track.getBoundingClientRect();
    this.glass.overlay = { rect: [rect.left, rect.top, rect.width, rect.height], color: [120/255,120/255,120/255,.2],
      fillColor: [0,136/255,1,1], fill: fraction, vertical: this.hasAttribute('vertical') };
    this.glass.configure({ radius: 999, blur: 8 * (1 - progress), refractionHeight: 10 * progress,
      refractionAmount: 14 * progress, chromatic: true, tint: [1,1,1,1-progress], highlight: progress });
  }
}
customElements.define('liquid-slider', LiquidSlider);
