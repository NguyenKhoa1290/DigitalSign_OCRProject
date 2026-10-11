import './glass-surface.js';
import { PressMotion } from './motion.js';

export class LiquidButton extends HTMLElement {
  static observedAttributes = ['disabled', 'label', 'tint'];
  constructor() {
    super();
    this.attachShadow({ mode: 'open' }).innerHTML = `<style>
      :host { display:inline-block; color:inherit; vertical-align:middle; }
      glass-surface { border-radius:999px; height:48px; }
      button { display:flex; gap:8px; align-items:center; justify-content:center; min-width:48px; width:100%; height:48px;
        padding:0 16px; border:0; border-radius:999px; background:transparent; color:inherit; font:inherit;
        cursor:pointer; touch-action:none; -webkit-tap-highlight-color:transparent; }
      button:focus-visible { outline:2px solid #0088ff; outline-offset:4px; }
      button:disabled { opacity:.4; cursor:not-allowed; }
    </style><glass-surface><button type="button" part="button"><slot></slot></button></glass-surface>`;
    this.button = this.shadowRoot.querySelector('button');
    this.glass = this.shadowRoot.querySelector('glass-surface');
  }
  connectedCallback() {
    this.abort = new AbortController();
    const options = { signal: this.abort.signal };
    this.motion = new PressMotion(motion => {
      const progress = Math.max(0, Math.min(1, motion.progress.value));
      this.glass.style.transform = `translate(${this.offsetX || 0}px,${this.offsetY || 0}px) scale(${motion.scaleX.value},${motion.scaleY.value})`;
      this.glass.configure({ glow: progress });
    }, 1 + 4 / 48, 300, .5);
    this.button.addEventListener('pointerdown', event => {
      if (this.button.disabled || !event.isPrimary || event.button !== 0) return;
      this.pointer = { id: event.pointerId, x: event.clientX, y: event.clientY };
      this.button.setPointerCapture(event.pointerId); this.motion.press();
    }, options);
    this.button.addEventListener('pointermove', event => {
      if (!this.pointer || event.pointerId !== this.pointer.id) return;
      this.offsetX = 48 * Math.tanh(.05 * (event.clientX - this.pointer.x) / 48);
      this.offsetY = 48 * Math.tanh(.05 * (event.clientY - this.pointer.y) / 48);
      this.motion.wake();
    }, options);
    for (const type of ['pointerup', 'pointercancel', 'lostpointercapture']) this.button.addEventListener(type, () => this.release(), options);
    this.button.addEventListener('keydown', event => { if ([' ', 'Enter'].includes(event.key) && !this.button.disabled) this.motion.press(); }, options);
    this.button.addEventListener('keyup', () => this.release(), options);
    window.addEventListener('blur', () => this.release(), options);
    document.addEventListener('visibilitychange', () => { if (document.hidden) { this.release(); this.motion.dispose(); } }, options);
    this.button.addEventListener('click', () => {
      if (!this.button.disabled) this.dispatchEvent(new CustomEvent('activate', { bubbles: true, composed: true }));
    }, options);
    this.sync();
  }
  release() { this.pointer = null; this.offsetX = this.offsetY = 0; this.motion?.release(); }
  disconnectedCallback() { this.abort?.abort(); this.motion?.dispose(); }
  attributeChangedCallback() { if (this.button) this.sync(); }
  sync() {
    this.button.disabled = this.hasAttribute('disabled');
    if (this.hasAttribute('label')) this.button.setAttribute('aria-label', this.getAttribute('label'));
    else this.button.removeAttribute('aria-label');
    const tint = this.getAttribute('tint');
    const match = /^#([0-9a-f]{6})$/i.exec(tint || '');
    const color = match ? [0, 2, 4].map(i => parseInt(match[1].slice(i, i + 2), 16) / 255) : [1, 1, 1];
    this.glass.configure({ radius: 999, blur: 2, refractionHeight: 12, refractionAmount: 24,
      tint: [...color, match ? .75 : .15] });
    this.style.color = match ? '#ffffff' : '';
  }
  get disabled() { return this.hasAttribute('disabled'); }
  set disabled(value) { this.toggleAttribute('disabled', Boolean(value)); }
}
customElements.define('liquid-button', LiquidButton);
