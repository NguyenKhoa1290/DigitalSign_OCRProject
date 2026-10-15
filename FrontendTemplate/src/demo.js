import './flight-taskbar.js';
import { CONTROL_GROUPS, CONTROL_DEFINITIONS, DEFAULT_SETTINGS, PLAYGROUND_SETTINGS } from './settings.js';

// Like rememberSaveable in BottomTabsContent: preserve this tab's selection.
const taskbar = document.querySelector('flight-taskbar');
try {
  const saved = sessionStorage.getItem('flight-taskbar:selected-index');
  if (saved !== null) taskbar.selectedIndex = Number(saved);
} catch { /* Storage can be unavailable in private or embedded contexts. */ }

taskbar.addEventListener('tabchange', ({ detail }) => {
  try { sessionStorage.setItem('flight-taskbar:selected-index', String(detail.index)); }
  catch { /* The component works independently of storage. */ }
});

const STORAGE_KEY = 'flight-taskbar:playground-settings';
let background = 'gradient';
try {
  const saved = JSON.parse(localStorage.getItem(STORAGE_KEY) || 'null');
  if (saved) {
    taskbar.settings = saved.settings;
    background = saved.background === 'stripes' ? 'stripes' : 'gradient';
  }
} catch { /* A missing/malformed saved state falls back to original taskbar values. */ }

function createGroup(group) {
  return `<fieldset class="control-group"><legend>${group.title}</legend><div class="control-grid">${group.controls.map(control => `
    <div class="control">
      <div class="control-heading">
        <label for="${control.key}">${control.label}<span class="kotlin-label">${control.kotlin}</span></label>
        <output id="${control.key}-value" for="${control.key}"></output>
      </div>
      <input type="range" id="${control.key}" min="${control.min}" max="${control.max}" step="${control.step}"
        aria-describedby="${control.key}-hint" ${['refractionHeightFraction', 'refractionAmountFraction', 'chromaticAberration'].includes(control.key) ? 'data-requires-webgl' : ''} />
      <p class="control-hint" id="${control.key}-hint">${control.hint}</p>
    </div>`).join('')}</div></fieldset>`;
}

document.querySelector('#glass-controls').innerHTML = createGroup(CONTROL_GROUPS[0]);
document.querySelector('#advanced-controls').innerHTML = CONTROL_GROUPS.slice(1).map(createGroup).join('');

function formattedValue(control, value) {
  switch (control.unit) {
    case 'fraction': return `${value.toFixed(2)} · ${(32 * value).toFixed(1)}px`;
    case 'height': return `${value.toFixed(2)} · ${(32 * value).toFixed(1)}px`;
    case 'amount': return `${value.toFixed(3)} · ${(64 * value).toFixed(1)}px`;
    case 'chromatic': return `${value.toFixed(2)} · ${value > 0 ? 'Bật' : 'Tắt'}`;
    case 'percent': return `${Math.round(value * 100)}%`;
    case 'px': return `${Number(value.toFixed(1))}px`;
    case 'ratio': return `${value.toFixed(2)}×`;
    default: return String(value);
  }
}

function syncControls() {
  const settings = taskbar.settings;
  for (const control of CONTROL_DEFINITIONS) {
    const input = document.getElementById(control.key);
    const value = settings[control.key];
    input.value = value;
    const text = formattedValue(control, value);
    document.getElementById(`${control.key}-value`).value = text;
    input.setAttribute('aria-valuetext', text);
    input.style.setProperty('--fill', `${(value - control.min) / (control.max - control.min) * 100}%`);
  }
  document.querySelector('#depth-effect').checked = settings.depthEffect;
  updateCapabilities();
}

function saveSettings() {
  try { localStorage.setItem(STORAGE_KEY, JSON.stringify({ settings: taskbar.settings, background })); }
  catch { /* Interactions do not depend on persistent storage. */ }
}

for (const control of CONTROL_DEFINITIONS) {
  document.getElementById(control.key).addEventListener('input', event => {
    taskbar.setSettings({ [control.key]: Number(event.currentTarget.value) });
    syncControls();
    saveSettings();
  });
}

document.querySelector('#depth-effect').addEventListener('change', event => {
  taskbar.setSettings({ depthEffect: event.currentTarget.checked });
  saveSettings();
});

function setBackground(value) {
  background = value;
  for (const button of document.querySelectorAll('[data-background]')) {
    button.setAttribute('aria-pressed', String(button.dataset.background === value));
  }
  if (value === 'gradient') {
    document.body.style.removeProperty('background-image');
    document.documentElement.style.removeProperty('--preview-background');
    taskbar.backdrop = null;
  } else {
    const canvas = document.createElement('canvas');
    canvas.width = innerWidth;
    canvas.height = innerHeight;
    const ctx = canvas.getContext('2d');
    const gradient = ctx.createLinearGradient(0, 0, 0, canvas.height);
    gradient.addColorStop(0, '#9ad7ff');
    gradient.addColorStop(0.5, '#daf0ff');
    gradient.addColorStop(1, '#b9d4bc');
    ctx.fillStyle = gradient;
    ctx.fillRect(0, 0, canvas.width, canvas.height);
    for (let x = 0; x < canvas.width; x += 36) {
      ctx.fillStyle = x % 72 === 0 ? 'rgba(48,72,109,.55)' : 'rgba(255,255,255,.6)';
      ctx.fillRect(x, 0, 18, canvas.height);
    }
    const image = `url(${canvas.toDataURL()})`;
    document.body.style.backgroundImage = image;
    document.body.style.backgroundSize = '100vw 100vh';
    document.documentElement.style.setProperty('--preview-background', image);
    taskbar.backdrop = canvas;
  }
  taskbar.refresh();
}

for (const button of document.querySelectorAll('[data-background]')) {
  button.addEventListener('click', () => { setBackground(button.dataset.background); saveSettings(); });
}
window.addEventListener('resize', () => { if (background === 'stripes') setBackground('stripes'); });

document.querySelector('#reset-settings').addEventListener('click', () => {
  taskbar.setSettings(DEFAULT_SETTINGS);
  setBackground('gradient');
  syncControls();
  saveSettings();
});
document.querySelector('#playground-preset').addEventListener('click', () => {
  taskbar.setSettings(PLAYGROUND_SETTINGS);
  syncControls();
  saveSettings();
});

document.querySelector('#download-config').addEventListener('click', () => {
  const config = {
    format: 'flight-taskbar-web/config',
    version: 1,
    component: 'flight-taskbar',
    taskbar: {
      selectedIndex: taskbar.selectedIndex,
      settings: taskbar.settings,
    },
    preview: { background },
    apply: 'taskbar.selectedIndex = config.taskbar.selectedIndex; taskbar.setSettings(config.taskbar.settings);',
  };
  const blob = new Blob([`${JSON.stringify(config, null, 2)}\n`], { type: 'application/json' });
  const url = URL.createObjectURL(blob);
  const link = document.createElement('a');
  link.href = url;
  link.download = 'flight-taskbar-config.json';
  link.hidden = true;
  document.body.append(link);
  link.click();
  link.remove();
  setTimeout(() => URL.revokeObjectURL(url), 1_000);
  document.querySelector('#config-export-status').textContent = 'Đã tải flight-taskbar-config.json.';
});

function updateCapabilities() {
  const shaderAvailable = taskbar.dataset.renderer === 'webgl2';
  for (const input of document.querySelectorAll('[data-requires-webgl]')) input.disabled = !shaderAvailable;
  document.querySelector('#fallback-note').hidden = shaderAvailable;
}
new MutationObserver(updateCapabilities).observe(taskbar, { attributes: true, attributeFilter: ['data-renderer'] });
syncControls();
setBackground(background);
await import('./gallery.js');
