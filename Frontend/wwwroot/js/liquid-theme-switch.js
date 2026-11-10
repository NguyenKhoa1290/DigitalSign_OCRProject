// Adapt FlightTaskbarWeb's LiquidToggle optics/springs to the existing native
// Blazor switch. Native click/keyboard activation remains the state authority.
import '../lib/flight/components/glass-surface.js';
import { PressMotion } from '../lib/flight/components/motion.js';
import { Spring, clamp } from '../lib/flight/spring.js';

const bindings = new Map();
function bind(button) {
    const track = button.querySelector('.theme-switch-track');
    const glass = button.querySelector('.theme-switch-thumb');
    if (!track || !glass || bindings.has(button)) return;
    const checked = () => button.getAttribute('aria-checked') === 'true';
    const value = new Spring(checked() ? 1 : 0, 1000, 1);
    const abort = new AbortController(), options = { signal: abort.signal };
    let pointer = null, ignorePointerClick = false;
    const direction = () => getComputedStyle(button).direction === 'rtl' ? -1 : 1;
    const motion = new PressMotion(state => {
        const fraction = clamp(value.value, 0, 1), progress = clamp(state.progress.value, 0, 1);
        const visual = direction() === -1 ? 1-fraction : fraction;
        const rgb = [120+(0-120)*fraction, 120+(136-120)*fraction, 120+(255-120)*fraction];
        const alpha = .2+.8*fraction;
        track.style.background = `rgba(${rgb.join(',')},${alpha})`;
        glass.style.transform = `translateX(${16*visual}px) scale(${state.scaleX.value},${state.scaleY.value})`;
        const rect = track.getBoundingClientRect();
        glass.overlay = { rect:[rect.left,rect.top,rect.width,rect.height], color:[...rgb.map(v=>v/255),alpha] };
        glass.configure({ radius:999, blur:8*(1-progress), refractionHeight:5*progress,
            refractionAmount:10*progress, chromatic:true, tint:[1,1,1,1-progress], highlight:progress });
    });
    motion.springs.push(value);
    const finish = (event, cancelled=false) => {
        if (!pointer || pointer.id !== event.pointerId) return;
        const previous = pointer; pointer = null;
        if (button.hasPointerCapture(previous.id)) button.releasePointerCapture(previous.id);
        const target = cancelled ? previous.start : value.target >= .5;
        if (previous.moved || cancelled) {
            ignorePointerClick = true;
            if (!cancelled && !button.disabled && target !== checked()) button.click();
            value.target = target ? 1 : 0;
        } else value.target = checked() ? 1 : 0; // Native click commits a tap.
        motion.release();
    };
    button.addEventListener('pointerdown', event => {
        if (button.disabled || pointer || !event.isPrimary || event.button !== 0) return;
        button.dataset.input = 'pointer';
        ignorePointerClick = false;
        pointer = { id:event.pointerId, x:event.clientX, start:checked(), value:value.target, moved:false };
        button.setPointerCapture(event.pointerId); button.focus({ preventScroll:true }); motion.press();
    }, options);
    button.addEventListener('pointermove', event => {
        if (!pointer || event.pointerId !== pointer.id) return;
        const dx = event.clientX-pointer.x;
        pointer.moved ||= Math.abs(dx)>3;
        value.target = clamp(pointer.value+dx/16*direction(),0,1); motion.wake();
    }, options);
    button.addEventListener('pointerup', event=>finish(event), options);
    for (const name of ['pointercancel','lostpointercapture']) button.addEventListener(name,event=>finish(event,true),options);
    button.addEventListener('click', event => {
        if (event.detail !== 0 && ignorePointerClick) {
            ignorePointerClick = false; event.preventDefault(); event.stopPropagation();
        }
    }, { ...options, capture:true });
    button.addEventListener('keydown', event => {
        button.dataset.input = 'keyboard';
        if (!event.repeat && !button.disabled && [' ','Enter'].includes(event.key)) motion.press();
    }, options);
    window.addEventListener('keydown', event => {
        if (event.key === 'Tab') button.dataset.input = 'keyboard';
    }, { ...options, capture:true });
    button.addEventListener('keyup', ()=>motion.release(), options);
    button.addEventListener('focusout', ()=>motion.release(), options);
    const cancel = () => { if (pointer) finish({ pointerId:pointer.id },true); else motion.release(); };
    window.addEventListener('blur',cancel,options);
    document.addEventListener('visibilitychange',()=>{ if(document.hidden){cancel();motion.dispose();}else motion.wake(); },options);
    const observer = new MutationObserver(()=>{
        if (button.disabled && pointer) cancel();
        if (!pointer) value.target = checked() ? 1 : 0;
        motion.wake();
    });
    observer.observe(button,{ attributes:true, attributeFilter:['aria-checked','disabled'] });
    bindings.set(button,()=>{ pointer=null;abort.abort();observer.disconnect();motion.dispose(); });
    motion.update(motion);
}
function reconcile() {
    for (const [button,dispose] of bindings) if (!button.isConnected) { dispose();bindings.delete(button); }
    document.querySelectorAll('.theme-switch').forEach(bind);
}
reconcile();
new MutationObserver(reconcile).observe(document.body,{ childList:true,subtree:true });
