import { PressMotion } from '../lib/flight/components/motion.js';

// Keep native button/link semantics and Blazor handlers; share the sample's springs.
let active = null;
const motions = new WeakMap();
function buttonAt(event) {
    const button = event.target.closest?.('button.btn, a.btn');
    return button && !button.matches(':disabled, [aria-disabled="true"]') ? button : null;
}
function press(button, pointer = null) {
    release();
    let motion = motions.get(button);
    if (!motion) {
        motion = new PressMotion(state => {
            if (!button.isConnected) { state.dispose(); return; }
            button.style.setProperty('--button-scale-x', state.scaleX.value);
            button.style.setProperty('--button-scale-y', state.scaleY.value);
            button.style.setProperty('--button-glow', Math.max(0, Math.min(1, state.progress.value)));
        }, 1 + 4 / 48, 300, .5);
        motions.set(button, motion);
    }
    active = { button, motion, pointer };
    motion.press();
}
function release() {
    if (!active) return;
    active.button.style.removeProperty('--button-offset-x');
    active.button.style.removeProperty('--button-offset-y');
    active.motion.release();
    active = null;
}
document.addEventListener('pointerdown', event => {
    const button = buttonAt(event);
    if (button && event.isPrimary && event.button === 0)
        press(button, { id: event.pointerId, x: event.clientX, y: event.clientY });
});
document.addEventListener('pointermove', event => {
    if (!active?.pointer || event.pointerId !== active.pointer.id) return;
    active.button.style.setProperty('--button-offset-x', `${48 * Math.tanh(.05 * (event.clientX - active.pointer.x) / 48)}px`);
    active.button.style.setProperty('--button-offset-y', `${48 * Math.tanh(.05 * (event.clientY - active.pointer.y) / 48)}px`);
}, { passive: true });
for (const type of ['pointerup', 'pointercancel']) document.addEventListener(type, release);
document.addEventListener('keydown', event => {
    const button = buttonAt(event);
    if (button && !event.repeat && [' ', 'Enter'].includes(event.key)) press(button);
});
document.addEventListener('keyup', release);
document.addEventListener('focusout', release);
window.addEventListener('blur', release);
document.addEventListener('visibilitychange', () => { if (document.hidden) release(); });
