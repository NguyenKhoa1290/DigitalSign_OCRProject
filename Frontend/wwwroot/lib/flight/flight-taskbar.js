/* Copyright 2025 Kyant. Apache-2.0. Modified: Kotlin/Compose component ported
 * to a dependency-free custom element with WebGL2 and Pointer Events.
 */
import { GlassRenderer } from './glass-renderer.js';
import { Spring, clamp } from './spring.js';
import { DEFAULT_SETTINGS, normalizeSettings } from './settings.js';
import { taskbarIconSvg } from './taskbar-icons.js';

const DEFAULT_COUNT = 5;
const MAX_COUNT = 6;
const tabButton = i => `<button type="button" role="tab" aria-selected="${i === 0}" tabindex="${i === 0 ? 0 : -1}" data-index="${i}"><span class="content"><span class="icon">${taskbarIconSvg('home')}</span><span>Tab ${i + 1}</span></span></button>`;
const accentItem = i => `<div class="accent-item"><span class="icon">${taskbarIconSvg('home')}</span><span>Tab ${i + 1}</span></div>`;
const styles = `
  :host { display: block; width: 100%; height: 64px; font: 12px Inter, Arial, Helvetica, sans-serif; }
  * { box-sizing: border-box; }
  .taskbar { position: relative; isolation: isolate; width: 100%; height: 64px; touch-action: none;
    user-select: none; -webkit-user-select: none; -webkit-tap-highlight-color: transparent; }
  canvas { position: absolute; left: -64px; top: -64px; pointer-events: none; z-index: 1; }
  .surface { position: absolute; inset: 0; border-radius: var(--panel-radius,32px); background: rgba(250,250,250,var(--surface-alpha,.4));
    backdrop-filter: blur(var(--blur,8px)) saturate(var(--saturation,1.5)); -webkit-backdrop-filter: blur(var(--blur,8px)) saturate(var(--saturation,1.5));
    box-shadow: inset 0 0 0 1px rgba(255,255,255,var(--edge-alpha,.5)), 0 var(--shadow-offset,4px) var(--shadow-radius,24px) rgba(0,0,0,var(--shadow-alpha,.1)); }
  .selection { position: absolute; top: 4px; left: 4px; height: 56px; width: var(--tab-width);
    border-radius: var(--selector-radius,28px); background: rgba(0,0,0,.1); transform-origin: center; }
  .tabs, .accent { display: grid; grid-template-columns: repeat(var(--tab-count,5),minmax(0,1fr));
    position: absolute; inset: 4px; margin: 0; }
  .tabs { z-index: 2; }
  button, .accent-item { display: flex; flex-direction: column; justify-content: center; align-items: center;
    gap: 2px; min-width: 0; height: 56px; padding: 0; font: inherit; line-height: 14px; }
  button { color: #000; background: none; border: 0; border-radius: 999px; cursor: pointer;
    touch-action: none; outline: none; }
  button .content { display: flex; flex-direction: column; align-items: center; gap: 2px; min-width: 0; }
  .content > span:last-child, .accent-item > span:last-child { max-width: 100%; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
  .taskbar[data-count="6"] button, .taskbar[data-count="6"] .accent-item { font-size: 10px; }
  .taskbar[data-count="6"] svg { width: 24px; height: 24px; }
  @media (max-width: 360px) { .taskbar[data-count="6"] button, .taskbar[data-count="6"] .accent-item { font-size: 9px; } }
  svg { display: block; flex: none; width: 28px; height: 28px; }
  button:focus-visible { outline: 2px solid #0088ff; outline-offset: 3px; }
  .taskbar[data-input="pointer"] button:focus-visible { outline: none; }
  .accent { color: #0088ff; pointer-events: none; clip-path: inset(0 var(--clip-right) 0 var(--clip-left) round var(--selector-radius,28px)); }
  :host([data-renderer="css"]) button[aria-selected="true"] .content { color: #0088ff; }
  :host([data-renderer="webgl2"]) .fallback, :host([data-renderer="webgl2"]) button .content { opacity: 0; }
  :host([live-backdrop][data-renderer="webgl2"]) .surface {
    opacity: 1; background: transparent; box-shadow: none;
  }
  :host([data-renderer="css"]) canvas { display: none; }
  :host([data-dark="true"]) .surface { background: rgba(18,18,18,var(--surface-alpha,.4)); }
  :host([data-dark="true"]) button { color: #fff; }
  :host([data-dark="true"]) .accent { color: #0091ff; }
  :host([data-dark="true"]) .selection { background: rgba(255,255,255,.1); }
  :host([blue]) .surface { background: rgba(7,58,103,.72); box-shadow: inset 0 0 0 1px rgba(184,233,255,.42), 0 var(--shadow-offset,4px) var(--shadow-radius,24px) rgba(5,38,64,.28); }
  :host([blue]) button { color: #eaf7ff; }
  :host([blue]) .accent { color: #aee8ff; }
  :host([blue]) .selection { background: rgba(24,144,207,.45); }
`;

export class FlightTaskbar extends HTMLElement {
  static observedAttributes = ['selected-index', 'theme', 'dir', 'labels', 'icons', 'blue', 'live-backdrop'];

  constructor() {
    super();
    this.attachShadow({ mode: 'open' });
    this.shadowRoot.innerHTML = `<style>${styles}</style>
      <div class="taskbar" role="tablist" aria-label="Chọn tab" aria-orientation="horizontal">
        <div class="surface fallback" aria-hidden="true"></div>
        <div class="selection fallback" aria-hidden="true"></div>
        <div class="tabs">${Array.from({ length: DEFAULT_COUNT }, (_, i) => tabButton(i)).join('')}</div>
        <div class="accent fallback" aria-hidden="true">${Array.from({ length: DEFAULT_COUNT }, (_, i) => accentItem(i)).join('')}</div>
        <canvas aria-hidden="true"></canvas>
      </div>`;
    this.nav = this.shadowRoot.querySelector('.taskbar');
    this.canvas = this.shadowRoot.querySelector('canvas');
    this.buttons = [...this.shadowRoot.querySelectorAll('button')];
    this.selection = this.shadowRoot.querySelector('.selection');
    this.count = DEFAULT_COUNT;
    this._settings = { ...DEFAULT_SETTINGS };
    this.indexSpring = new Spring(0, 1000, 1);
    this.pressSpring = new Spring(0, 1000, 1);
    this.scaleXSpring = new Spring(1, 250, 0.6);
    this.scaleYSpring = new Spring(1, 250, 0.7);
    this.velocitySpring = new Spring(0, 300, 0.5, 0.01);
    this.offsetSpring = new Spring(0, 300, 1, 0.5);
    this.highlightSpring = new Spring(0, 300, 0.5);
    this.springs = [this.indexSpring, this.pressSpring, this.scaleXSpring, this.scaleYSpring,
      this.velocitySpring, this.offsetSpring, this.highlightSpring];
    this._selectedIndex = 0;
    this._labels = [];
    this._icons = [];
    this.frame = 0;
    this.renderFrame = this.renderFrame.bind(this);
    this.updateLabels();
    this.updateIcons();
  }

  connectedCallback() {
    this.syncTabCount();
    this.updateLabels();
    this.updateIcons();
    if(this.contextReleased){const canvas=document.createElement('canvas');canvas.setAttribute('aria-hidden','true');this.canvas.replaceWith(canvas);this.canvas=canvas;this.contextReleased=false;}
    this.abort = new AbortController();
    const options = { signal: this.abort.signal };
    this.darkQuery = matchMedia('(prefers-color-scheme: dark)');
    this.motionQuery = matchMedia('(prefers-reduced-motion: reduce)');
    this.darkQuery.addEventListener('change', () => this.refresh(), options);
    this.motionQuery.addEventListener('change', () => this.wake(), options);
    this.nav.addEventListener('click', event => this.onClick(event), options);
    this.nav.addEventListener('keydown', event => this.onKeyDown(event), options);
    this.nav.addEventListener('pointerdown', event => this.onPointerDown(event), options);
    this.nav.addEventListener('pointermove', event => this.onPointerMove(event), options);
    this.nav.addEventListener('pointerup', event => this.endPointer(event), options);
    this.nav.addEventListener('pointercancel', event => this.endPointer(event, true), options);
    this.nav.addEventListener('lostpointercapture', event => this.endPointer(event, true), options);
    window.addEventListener('blur', () => this.cancelGesture(), options);
    window.addEventListener('resize', () => this.refresh(), options);
    window.addEventListener('scroll', () => this.refresh(), { ...options, passive: true });
    document.addEventListener('keydown', () => { this.nav.dataset.input = 'keyboard'; }, options);
    document.addEventListener('visibilitychange', () => {
      if (document.hidden) {
        this.cancelGesture();
        cancelAnimationFrame(this.frame);
        this.frame = 0;
      } else this.refresh();
    }, options);
    this.canvas.addEventListener('webglcontextlost', event => {
      event.preventDefault();
      this.dataset.renderer = 'css';
      this.wake();
    }, options);
    this.canvas.addEventListener('webglcontextrestored', () => {
      this.createRenderer();
      this.refresh();
    }, options);
    this.createRenderer();
    this.indexSpring.snap(this.selectedIndex);
    this.updateSelection();
    this.nav.setAttribute('aria-label', this.getAttribute('aria-label') || 'Chọn tab');
    this.observer = new ResizeObserver(() => this.refresh());
    this.observer.observe(this);
    this.refresh();
  }

  disconnectedCallback() {
    this.cancelGesture();
    this.abort?.abort();
    this.observer?.disconnect();
    cancelAnimationFrame(this.frame);
    this.frame = 0;
    const gl=this.renderer?.gl;
    this.renderer?.dispose();
    this.renderer = null;
    if(gl && !gl.isContextLost())gl.getExtension('WEBGL_lose_context')?.loseContext();
    this.contextReleased=true;
  }

  attributeChangedCallback(name, oldValue, newValue) {
    if (oldValue === newValue) return;
    if (name === 'selected-index') {
      const parsed = Number(newValue);
      this.select(Number.isFinite(parsed) ? Math.trunc(parsed) : 0, { reflect: false });
    } else if (name === 'labels') {
      if (this.isConnected) this.syncTabCount();
      this.updateLabels();
    } else if (name === 'icons') {
      this.updateIcons();
    } else if (this.isConnected) this.refresh();
  }

  get selectedIndex() { return this._selectedIndex; }
  set selectedIndex(value) {
    const parsed = Number(value);
    this.select(Number.isFinite(parsed) ? Math.trunc(parsed) : 0);
  }

  get labels() { return [...this._labels]; }

  syncTabCount() {
    const requested = (this.getAttribute('labels') || '').split('|').length;
    const count = clamp(requested, 2, MAX_COUNT);
    if (count === this.count) return;
    this.count = count;
    this.nav.dataset.count = String(count);
    this.nav.style.setProperty('--tab-count', count);
    this.shadowRoot.querySelector('.tabs').innerHTML = Array.from({ length: count }, (_, i) => tabButton(i)).join('');
    this.shadowRoot.querySelector('.accent').innerHTML = Array.from({ length: count }, (_, i) => accentItem(i)).join('');
    this.buttons = [...this.shadowRoot.querySelectorAll('button')];
    this.renderer?.setTabCount(count);
    this.select(Math.min(this.selectedIndex, count - 1));
  }

  updateLabels() {
    const labels = (this.getAttribute('labels') || '').split('|').map(label => label.trim()).filter(Boolean);
    this._labels = Array.from({ length: this.count }, (_, index) => labels[index] || `Tab ${index + 1}`);
    this.buttons?.forEach((button, index) => {
      button.querySelector('.content span:last-child').textContent = this._labels[index];
    });
    this.shadowRoot?.querySelectorAll('.accent-item span:last-child').forEach((label, index) => {
      label.textContent = this._labels[index];
    });
    this.renderer?.setLabels(this._labels);
  }

  updateIcons() {
    const icons = (this.getAttribute('icons') || '').split('|').map(icon => icon.trim()).filter(Boolean);
    this._icons = Array.from({ length: this.count }, (_, index) => icons[index] || 'home');
    this.shadowRoot?.querySelectorAll('.icon').forEach((icon, index) => {
      icon.innerHTML = taskbarIconSvg(this._icons[index % this.count]);
    });
    this.renderer?.setIcons(this._icons);
  }

  get backdrop() { return this._backdrop || null; }
  set backdrop(source) {
    this._backdrop = source;
    this.refreshBackdrop();
  }

  get settings() { return { ...this._settings }; }
  set settings(patch) { this.setSettings(patch); }

  setSettings(patch) {
    this._settings = normalizeSettings(patch, this._settings);
    const settings = this._settings;
    this.indexSpring.stiffness = settings.valueStiffness;
    this.indexSpring.dampingRatio = settings.valueDamping;
    this.scaleXSpring.dampingRatio = settings.scaleDampingX;
    this.scaleYSpring.dampingRatio = settings.scaleDampingY;
    if (this.pointer || this.pendingRelease) {
      this.scaleXSpring.target = this.scaleYSpring.target = settings.pressedScale;
    }
    this.applySurfaceSettings();
    this.wake();
  }

  resetSettings() { this.setSettings(DEFAULT_SETTINGS); }

  applySurfaceSettings() {
    const settings = this._settings;
    const radius = Math.min(this.tabWidth / 2 || 28, 28) * settings.cornerRadiusFraction;
    for (const [name, value] of Object.entries({
      '--panel-radius': `${32 * settings.cornerRadiusFraction}px`,
      '--selector-radius': `${radius}px`, '--blur': `${settings.blurRadius}px`,
      '--surface-alpha': settings.surfaceAlpha, '--saturation': settings.saturation,
      '--edge-alpha': settings.highlightAlpha * 0.5,
      '--shadow-radius': `${settings.shadowRadius}px`, '--shadow-offset': `${settings.shadowRadius / 6}px`,
      '--shadow-alpha': settings.shadowRadius > 0 ? 0.1 : 0,
    })) this.nav.style.setProperty(name, value);
  }

  createRenderer() {
    this.renderer?.dispose();
    try {
      this.renderer = new GlassRenderer(this.canvas);
      this.renderer.setTabCount(this.count);
      this.renderer.setLabels(this._labels);
      this.renderer.setIcons(this._icons);
      this.dataset.renderer = 'webgl2';
    } catch (error) {
      this.renderer = null;
      this.dataset.renderer = 'css';
      this.dispatchEvent(new CustomEvent('renderfallback', { detail: { reason: error.message } }));
    }
  }

  refresh() {
    if (!this.isConnected) return;
    this.rect = this.getBoundingClientRect();
    if (this.rect.width <= 8) return;
    this.tabWidth = (this.rect.width - 8) / this.count;
    this.direction = getComputedStyle(this).direction;
    const theme = this.getAttribute('theme');
    this.dark = theme === 'dark' || (theme !== 'light' && this.darkQuery.matches);
    this.blue = this.hasAttribute('blue');
    this.dataset.dark = String(this.dark);
    this.nav.style.setProperty('--tab-width', `${this.tabWidth}px`);
    this.applySurfaceSettings();
    if (this.renderer && this.dataset.renderer === 'webgl2') {
      try {
        this.renderer.resize(this.rect, this.dark, this.direction, this.blue);
        if (this.backdrop) this.renderer.refreshBackdrop(this.backdrop);
      } catch (error) { this.useFallback(error); }
    }
    this.wake();
  }

  refreshBackdrop() {
    if (!this.isConnected || !this.renderer) return;
    try { this.renderer.refreshBackdrop(this.backdrop); }
    catch (error) { this.useFallback(error); }
    this.wake();
  }

  useFallback(error) {
    this.dataset.renderer = 'css';
    this.renderer?.dispose();
    this.renderer = null;
    this.dispatchEvent(new CustomEvent('renderfallback', { detail: { reason: error.message } }));
  }

  select(index, { notify = false, reflect = true } = {}) {
    index = clamp(index, 0, this.count - 1);
    const changed = index !== this._selectedIndex;
    this._selectedIndex = index;
    if (reflect && this.getAttribute('selected-index') !== String(index)) {
      this.setAttribute('selected-index', String(index));
    }
    this.updateSelection();
    this.indexSpring.target = index;
    if (this.isConnected && changed) {
      this.press();
      this.pendingRelease = true;
      this.wake();
    }
    if (notify && changed) {
      this.dispatchEvent(new CustomEvent('tabchange', {
        bubbles: true, composed: true, detail: { index, label: this._labels[index] },
      }));
    }
  }

  updateSelection() {
    this.buttons.forEach((button, i) => {
      button.setAttribute('aria-selected', String(i === this._selectedIndex));
      button.tabIndex = i === this._selectedIndex ? 0 : -1;
    });
  }

  press() {
    this.pressSpring.target = 1;
    this.scaleXSpring.target = this.scaleYSpring.target = this._settings.pressedScale;
    this.wake();
  }

  release() {
    this.pressSpring.target = 0;
    this.scaleXSpring.target = this.scaleYSpring.target = 1;
    this.highlightSpring.target = 0;
    this.velocitySpring.target = 0;
    this.offsetSpring.target = 0;
    this.pendingRelease = false;
  }

  onClick(event) {
    if (event.detail !== 0 && this.ignorePointerClick) {
      this.ignorePointerClick = false;
      return;
    }
    const button = event.target.closest('button');
    if (button) this.select(Number(button.dataset.index), { notify: true });
  }

  onKeyDown(event) {
    this.nav.dataset.input = 'keyboard';
    const button = event.target.closest('button');
    if (!button) return;
    const index = Number(button.dataset.index);
    const forward = this.direction === 'rtl' ? -1 : 1;
    const target = { ArrowRight: (index + forward + this.count) % this.count,
      ArrowLeft: (index - forward + this.count) % this.count, Home: 0, End: this.count - 1 }[event.key];
    if (target === undefined) return;
    event.preventDefault();
    this.select(target, { notify: true });
    this.buttons[target].focus({ preventScroll: true });
  }

  onPointerDown(event) {
    this.nav.dataset.input = 'pointer';
    if (!event.isPrimary || event.button !== 0 || this.pointer) return;
    this.ignorePointerClick = false;
    const button = event.target.closest('button');
    // Original drag handler is attached to the active glass selector.
    if (!button || Number(button.dataset.index) !== this.selectedIndex) return;
    this.rect = this.getBoundingClientRect();
    this.pointer = { id: event.pointerId, startX: event.clientX, lastX: event.clientX,
      index: this.selectedIndex, moved: false };
    this.nav.setPointerCapture(event.pointerId);
    this.pendingRelease = false;
    this.press();
    this.highlightSpring.target = 1;
    button.focus({ preventScroll: true });
  }

  onPointerMove(event) {
    if (!this.pointer || event.pointerId !== this.pointer.id) return;
    const pointer = this.pointer;
    const dx = event.clientX - pointer.lastX;
    pointer.lastX = event.clientX;
    pointer.moved ||= Math.abs(event.clientX - pointer.startX) > 4;
    const sign = this.direction === 'rtl' ? -1 : 1;
    this.indexSpring.target = clamp(this.indexSpring.target + dx / this.tabWidth * sign, 0, this.count - 1);
    this.offsetSpring.snap(event.clientX - pointer.startX);
    this.wake();
  }

  endPointer(event, cancelled = false) {
    if (!this.pointer || event.pointerId !== this.pointer.id) return;
    const pointer = this.pointer;
    this.pointer = null;
    // Clear before releasing capture, since lostpointercapture also fires.
    if (this.nav.hasPointerCapture(pointer.id)) this.nav.releasePointerCapture(pointer.id);
    const index = cancelled ? pointer.index : Math.round(this.indexSpring.target);
    this.select(index, { notify: !cancelled });
    this.indexSpring.target = index;
    this.ignorePointerClick = true;
    this.pendingRelease = true;
    this.highlightSpring.target = 0;
    this.offsetSpring.target = 0;
    this.wake();
  }

  cancelGesture() {
    if (this.pointer) this.endPointer({ pointerId: this.pointer.id }, true);
  }

  wake() {
    if (this.isConnected && !document.hidden && !this.frame) {
      this.lastTime = performance.now();
      this.frame = requestAnimationFrame(this.renderFrame);
    }
  }

  renderFrame(time) {
    this.frame = 0;
    if (!this.isConnected || !this.rect || this.rect.width <= 8) return;
    const dt = Math.min(Math.max((time - this.lastTime) / 1000, 0.001), 0.05);
    this.lastTime = time;
    const reduced = this.motionQuery.matches;
    if (reduced) {
      this.indexSpring.snap(this.indexSpring.target);
      this.release();
      for (const spring of this.springs) spring.snap(spring.target);
    } else {
      if (this.pointer) this.velocitySpring.target = this.indexSpring.velocity / (this.count - 1);
      for (const spring of this.springs) spring.step(dt);
      if (this.pendingRelease && Math.abs(this.indexSpring.value - this.indexSpring.target) < 0.1) this.release();
    }
    const logicalIndex = this.indexSpring.value;
    const index = this.direction === 'rtl' ? this.count - 1 - logicalIndex : logicalIndex;
    const velocity = this.velocitySpring.value / 10;
    const fraction = clamp(this.offsetSpring.value / this.rect.width, -1, 1);
    const panelOffset = 4 * Math.sign(fraction) * (1 - (1 - Math.abs(fraction)) ** 3);
    const state = {
      index: clamp(index, 0, this.count - 1), panelOffset, press: clamp(this.pressSpring.value, 0, 1),
      highlight: clamp(this.highlightSpring.value, 0, 1),
      scaleX: this.scaleXSpring.value / (1 - clamp(velocity * 0.75, -0.2, 0.2)),
      scaleY: this.scaleYSpring.value * (1 - clamp(velocity * 0.25, -0.2, 0.2)),
    };
    this.selection.style.transform = `translateX(${index * this.tabWidth + panelOffset}px) scale(${state.scaleX}, ${state.scaleY})`;
    this.nav.style.setProperty('--clip-left', `${index * this.tabWidth}px`);
    this.nav.style.setProperty('--clip-right', `${(this.count - 1 - index) * this.tabWidth}px`);
    if (this.renderer && this.dataset.renderer === 'webgl2') this.renderer.render(state, this._settings);
    if (this.pointer || this.pendingRelease || this.springs.some(spring => !spring.settled)) {
      this.frame = requestAnimationFrame(this.renderFrame);
    }
  }
}

if (!customElements.get('flight-taskbar')) customElements.define('flight-taskbar', FlightTaskbar);
