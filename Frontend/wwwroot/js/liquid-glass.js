import '../lib/flight/components/glass-surface.js';
import '../lib/flight/components/liquid-button.js';
import '../lib/flight/flight-taskbar.js';
import { setGlassBackdrop } from '../lib/flight/components/backdrop.js';

const backdrop = document.createElement('canvas');
const context = backdrop.getContext('2d');
let navigationReference = null;

function normalizePath(path) {
    return path.replace(/\/$/, '') || '/';
}

export function bindTaskbarNavigation(reference) {
    navigationReference = reference;
    syncTaskbarSelection();
}

export function unbindTaskbarNavigation() {
    navigationReference = null;
}

export function syncTaskbarSelection() {
    const currentPath = normalizePath(window.location.pathname);
    document.querySelectorAll('flight-taskbar').forEach(taskbar => {
        const routes = (taskbar.getAttribute('routes') || '').split('|');
        let activeIndex = routes.findIndex(route => normalizePath(route) === currentPath);
        if (activeIndex < 0) {
            activeIndex = routes.findIndex(route => route !== '/' && currentPath.startsWith(`${normalizePath(route)}/`));
        }
        taskbar.selectedIndex = activeIndex >= 0 ? activeIndex : 0;
    });
}

function drawBackdrop() {
    const ratio = Math.min(window.devicePixelRatio || 1, 2);
    backdrop.width = Math.max(1, Math.round(window.innerWidth * ratio));
    backdrop.height = Math.max(1, Math.round(window.innerHeight * ratio));

    context.setTransform(ratio, 0, 0, ratio, 0, 0);
    const width = window.innerWidth;
    const height = window.innerHeight;
    const base = context.createLinearGradient(0, 0, width, height);
    base.addColorStop(0, '#e8f7ff');
    base.addColorStop(.45, '#eef5fb');
    base.addColorStop(1, '#d9e8f5');
    context.fillStyle = base;
    context.fillRect(0, 0, width, height);

    const blue = context.createRadialGradient(width * .78, height * .1, 0, width * .78, height * .1, Math.max(width, height) * .32);
    blue.addColorStop(0, 'rgba(83, 209, 255, .55)');
    blue.addColorStop(1, 'rgba(83, 209, 255, 0)');
    context.fillStyle = blue;
    context.fillRect(0, 0, width, height);

    const green = context.createRadialGradient(width * .38, height * .94, 0, width * .38, height * .94, Math.max(width, height) * .34);
    green.addColorStop(0, 'rgba(0, 150, 63, .20)');
    green.addColorStop(1, 'rgba(0, 150, 63, 0)');
    context.fillStyle = green;
    context.fillRect(0, 0, width, height);
    setGlassBackdrop(backdrop);
    document.querySelectorAll('flight-taskbar').forEach(taskbar => {
        taskbar.backdrop = backdrop;
        taskbar.refreshBackdrop();
    });
}

function configureTaskbar(taskbar) {
    if (taskbar.dataset.configured === 'true') return;

    const routes = (taskbar.getAttribute('routes') || '').split('|');
    taskbar.backdrop = backdrop;
    taskbar.addEventListener('tabchange', event => {
        const route = routes[event.detail.index];
        if (!route || normalizePath(route) === normalizePath(window.location.pathname)) return;
        if (!navigationReference) { syncTaskbarSelection(); return; }
        navigationReference.invokeMethodAsync('NavigateFromTaskbar', route)
            .catch(error => { console.error('Taskbar navigation failed:', error); syncTaskbarSelection(); });
    });
    taskbar.dataset.configured = 'true';
    syncTaskbarSelection();
}

function configureTaskbars() {
    document.querySelectorAll('flight-taskbar').forEach(configureTaskbar);
}

drawBackdrop();
configureTaskbars();
new MutationObserver(configureTaskbars).observe(document.body, { childList: true, subtree: true });
window.addEventListener('resize', drawBackdrop, { passive: true });
