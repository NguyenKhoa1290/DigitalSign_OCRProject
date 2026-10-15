import './glass-surface.js';
import { PressMotion } from './motion.js';
import { Spring, clamp } from '../spring.js';

export class LiquidToggle extends HTMLElement {
  static observedAttributes = ['checked', 'disabled', 'label', 'dir'];
  constructor() {
    super();
    this.attachShadow({ mode: 'open' }).innerHTML = `<style>
      /* Kotlin source is 64×28. The web gallery's compact variant is 56×28,
         while preserving a 44px-high touch target. */
      :host { display:inline-block; width:56px; height:44px; vertical-align:middle; }
      button { position:relative; width:56px; height:44px; border:0; padding:0; border-radius:999px; background:none;
        cursor:pointer; touch-action:none; -webkit-tap-highlight-color:transparent; }
      button:focus-visible { outline:2px solid #0088ff; outline-offset:4px; }
      button:disabled { opacity:.4; cursor:not-allowed; }
      .track { position:absolute; top:8px; left:0; width:56px; height:28px; border-radius:999px; background:rgba(120,120,120,.2); }
      glass-surface { position:absolute; left:2px; top:10px; width:36px; height:24px; border-radius:999px;
        box-shadow:0 1px 4px rgba(0,0,0,.05); pointer-events:none; }
    </style><button type="button" role="switch" aria-checked="false"><span class="track" aria-hidden="true"></span><glass-surface aria-hidden="true"></glass-surface></button>`;
    this.button = this.shadowRoot.querySelector('button');
    this.track = this.shadowRoot.querySelector('.track');
    this.glass = this.shadowRoot.querySelector('glass-surface');
    this.valueSpring = new Spring(0, 1000, 1);
  }
  get checked() { return this.hasAttribute('checked'); }
  set checked(value) { this.toggleAttribute('checked', Boolean(value)); }
  connectedCallback() {
    this.abort = new AbortController(); const options = { signal: this.abort.signal };
    this.valueSpring.snap(this.checked ? 1 : 0);
    this.motion = new PressMotion(motion => this.render(motion));
    this.motion.springs.push(this.valueSpring);
    this.button.addEventListener('pointerdown', event => {
      if (!event.isPrimary || event.button !== 0 || this.hasAttribute('disabled') || this.pointer) return;
      this.pointer = { id: event.pointerId, x: event.clientX, start: this.checked, value: this.valueSpring.target, moved: false };
      this.button.setPointerCapture(event.pointerId); this.button.focus({ preventScroll: true }); this.motion.press();
    }, options);
    this.button.addEventListener('pointermove', event => {
      if (!this.pointer || this.pointer.id !== event.pointerId) return;
      const dx = event.clientX - this.pointer.x;
      this.pointer.moved ||= Math.abs(dx) > 3;
      this.valueSpring.target = clamp(this.pointer.value + dx / 16 * this.direction, 0, 1); this.motion.wake();
    }, options);
    this.button.addEventListener('pointerup', event => this.endPointer(event), options);
    this.button.addEventListener('pointercancel', event => this.endPointer(event, true), options);
    this.button.addEventListener('lostpointercapture', event => this.endPointer(event, true), options);
    this.button.addEventListener('click', event => {
      if (event.detail !== 0 && this.ignoreClick) { this.ignoreClick = false; return; }
      if (!this.hasAttribute('disabled')) this.commit(!this.checked);
    }, options);
    window.addEventListener('blur', () => this.cancel(), options);
    document.addEventListener('visibilitychange', () => { if (document.hidden) { this.cancel(); this.motion.dispose(); } }, options);
    this.sync(); this.render(this.motion);
  }
  get direction() { return getComputedStyle(this).direction === 'rtl' ? -1 : 1; }
  endPointer(event, cancelled = false) {
    if (!this.pointer || this.pointer.id !== event.pointerId) return;
    const pointer = this.pointer; this.pointer = null;
    if (this.button.hasPointerCapture(pointer.id)) this.button.releasePointerCapture(pointer.id);
    if (cancelled) this.valueSpring.target = pointer.start ? 1 : 0;
    else this.commit(pointer.moved ? this.valueSpring.target >= .5 : !pointer.start);
    this.ignoreClick = true; this.motion.release();
  }
  cancel() { if (this.pointer) this.endPointer({ pointerId: this.pointer.id }, true); }
  commit(value) {
    const changed = this.checked !== value;
    this.checked = value; this.valueSpring.target = value ? 1 : 0; this.motion?.wake();
    if (changed) this.dispatchEvent(new CustomEvent('change', { bubbles: true, composed: true, detail: { checked: value } }));
  }
  disconnectedCallback() { this.cancel(); this.abort?.abort(); this.motion?.dispose(); }
  attributeChangedCallback() { if (this.button) this.sync(); }
  sync() {
    this.button.disabled = this.hasAttribute('disabled');
    this.button.setAttribute('aria-label', this.getAttribute('label') || 'Bật / tắt');
    this.button.setAttribute('aria-checked', String(this.checked));
    if (this.pointer && this.button.disabled) this.cancel();
    if (!this.pointer) this.valueSpring.target = this.checked ? 1 : 0;
    this.motion?.wake();
  }
  render(motion) {
    const fraction = clamp(this.valueSpring.value, 0, 1);
    const progress = clamp(motion.progress.value, 0, 1);
    const visual = this.direction === -1 ? 1 - fraction : fraction;
    const rgb = [120 + (52 - 120) * fraction, 120 + (199 - 120) * fraction, 120 + (89 - 120) * fraction];
    const alpha = .2 + .8 * fraction;
    this.track.style.background = `rgba(${rgb.join(',')},${alpha})`;
    this.glass.style.transform = `translateX(${16 * visual}px) scale(${motion.scaleX.value},${motion.scaleY.value})`;
    const rect = this.track.getBoundingClientRect();
    this.glass.overlay = { rect: [rect.left, rect.top, rect.width, rect.height], color: [...rgb.map(x => x / 255), alpha] };
    this.glass.configure({ radius: 999, blur: 8 * (1 - progress), refractionHeight: 5 * progress,
      refractionAmount: 10 * progress, chromatic: true, tint: [1, 1, 1, 1 - progress], highlight: progress });
  }
}
customElements.define('liquid-toggle', LiquidToggle);
