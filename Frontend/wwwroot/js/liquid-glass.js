import '../lib/flight/components/glass-surface.js';
import '../lib/flight/components/liquid-button.js';
import './liquid-action-buttons.js';
import './liquid-theme-switch.js';
import '../lib/flight/flight-taskbar.js';
import { setGlassBackdrop } from '../lib/flight/components/backdrop.js';

const backdrop = document.createElement('canvas');
const context = backdrop.getContext('2d');
let backdropViewport = null;
let backdropResizeFrame = 0;
let navigationReference = null;
const taskbarSettings = fetch(new URL('./flight-taskbar-config.json', import.meta.url), { cache: 'no-store' })
    .then(response => {
        if (!response.ok) throw new Error(`Taskbar config HTTP ${response.status}`);
        return response.json();
    })
    .then(config => {
        if (config.format !== 'flight-taskbar-web/config' || config.version !== 1 || config.component !== 'flight-taskbar')
            throw new Error('Unsupported taskbar config');
        return config.taskbar.settings;
    })
    .catch(error => { console.error('Taskbar config failed:', error); return {}; });

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
    const path = normalizePath(window.location.pathname);
    const currentPath = ['/admin/users', '/admin/departments'].includes(path) ? '/admin/organization' : path;
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
    const dark = document.documentElement.dataset.theme === 'dark';
    const ratio = Math.min(window.devicePixelRatio || 1, 2);
    const width = window.innerWidth;
    const height = window.innerHeight;
    if (backdropViewport && backdropViewport.width === width && backdropViewport.ratio === ratio
        && (backdropViewport.height === height || (width <= 960 && Math.abs(backdropViewport.height - height) <= 160)))
        return; // Mobile browser chrome changes height while scrolling; keep the existing texture.
    backdropViewport = { width, height, ratio };
    backdrop.width = Math.max(1, Math.round(window.innerWidth * ratio));
    backdrop.height = Math.max(1, Math.round(window.innerHeight * ratio));

    context.setTransform(ratio, 0, 0, ratio, 0, 0);
    const base = context.createLinearGradient(0, 0, width, height);
    base.addColorStop(0, dark ? '#0d1825' : '#e8f7ff');
    base.addColorStop(.45, dark ? '#152538' : '#eef5fb');
    base.addColorStop(1, dark ? '#112131' : '#d9e8f5');
    context.fillStyle = base;
    context.fillRect(0, 0, width, height);

    const blue = context.createRadialGradient(width * .78, height * .1, 0, width * .78, height * .1, Math.max(width, height) * .32);
    blue.addColorStop(0, dark ? 'rgba(47, 122, 182, .22)' : 'rgba(83, 209, 255, .55)');
    blue.addColorStop(1, 'rgba(83, 209, 255, 0)');
    context.fillStyle = blue;
    context.fillRect(0, 0, width, height);

    const green = context.createRadialGradient(width * .38, height * .94, 0, width * .38, height * .94, Math.max(width, height) * .34);
    green.addColorStop(0, dark ? 'rgba(0, 130, 90, .10)' : 'rgba(0, 150, 63, .20)');
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
    const theme = document.documentElement.dataset.theme === 'dark' ? 'dark' : 'light';
    if (taskbar.getAttribute('theme') !== theme) taskbar.setAttribute('theme', theme);
    if (taskbar.dataset.configured === 'true') return;

    const routes = (taskbar.getAttribute('routes') || '').split('|');
    taskbar.setAttribute('live-backdrop', '');
    taskbarSettings.then(settings => taskbar.setSettings(settings));
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
window.addEventListener('hau-theme-change', () => {
    backdropViewport = null;
    drawBackdrop();
    configureTaskbars();
});
window.addEventListener('resize', () => {
    if (backdropResizeFrame) return;
    backdropResizeFrame = requestAnimationFrame(() => { backdropResizeFrame = 0; drawBackdrop(); });
}, { passive: true });
