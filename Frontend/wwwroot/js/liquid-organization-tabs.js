import '../lib/flight/components/glass-surface.js';
import { PressMotion } from '../lib/flight/components/motion.js';
import { Spring, clamp } from '../lib/flight/spring.js';

// Adapt the template's LiquidToggle thumb to two native Blazor tab buttons.
export function createOrganizationSwitch(element, selected) {
    const glass = element.querySelector('.organization-selector');
    const buttons = [...element.querySelectorAll('[role="tab"]')];
    const abort = new AbortController();
    const options = { signal: abort.signal };
    const value = new Spring(selected === 'departments' ? 1 : 0, 1000, 1);
    let disposed = false;
    const motion = new PressMotion(state => {
        if (disposed || !element.isConnected) { state.dispose(); return; }
        const progress = clamp(state.progress.value, 0, 1);
        const distance = buttons[1].offsetLeft - buttons[0].offsetLeft;
        glass.style.transform = `translateX(${distance * clamp(value.value, 0, 1)}px) scale(${state.scaleX.value},${state.scaleY.value})`;
        glass.configure({ radius: 10, blur: 8 * (1 - progress), refractionHeight: 5 * progress,
            refractionAmount: 10 * progress, chromatic: true,
            tint: [1, 1, 1, 1 - progress], highlight: progress });
    }, 1.04);
    motion.springs.push(value);
    element.addEventListener('pointerdown', event => {
        if (event.isPrimary && event.button === 0 && event.target.closest('[role="tab"]')) motion.press();
    }, options);
    const release = () => motion.release();
    for (const type of ['pointerup', 'pointercancel']) window.addEventListener(type, release, options);
    window.addEventListener('blur', release, options);
    element.addEventListener('keydown', event => {
        if (!event.repeat && [' ', 'Enter'].includes(event.key)) motion.press();
    }, options);
    element.addEventListener('keyup', release, options);
    element.addEventListener('focusout', release, options);
    document.addEventListener('visibilitychange', () => {
        if (document.hidden) { motion.release(); motion.dispose(); }
    }, options);
    const resize = new ResizeObserver(() => motion.wake());
    resize.observe(element);
    motion.update(motion);
    return {
        setSelected(section) {
            if (disposed) return;
            value.target = section === 'departments' ? 1 : 0;
            motion.wake();
        },
        dispose() {
            disposed = true;
            abort.abort();
            resize.disconnect();
            motion.dispose();
        }
    };
}
