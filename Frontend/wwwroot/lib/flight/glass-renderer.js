import { vertexShader, fragmentShader } from './shaders.js';
import { taskbarIconPaths } from './taskbar-icons.js';

const PADDING = 64;

export function taskbarContentMetrics(width, count) {
  const tabWidth = (width - 8) / count;
  const iconSize = clampSize(tabWidth * 0.34, 20, 24);
  const fontSize = clampSize(tabWidth * 0.17, 9.5, 11);
  const lineHeight = fontSize + 2;
  return { iconSize, fontSize, lineHeight, iconTop: (64 - iconSize - 2 - lineHeight) / 2 };
}

function clampSize(value, min, max) { return Math.min(max, Math.max(min, value)); }

export class GlassRenderer {
  constructor(canvas) {
    this.canvas = canvas;
    this.gl = canvas.getContext('webgl2', {
      alpha: true, premultipliedAlpha: false, antialias: false,
      depth: false, stencil: false, preserveDrawingBuffer: false,
    });
    if (!this.gl) throw new Error('WebGL2 is unavailable.');
    const gl = this.gl;
    this.program = gl.createProgram();
    for (const [type, source] of [[gl.VERTEX_SHADER, vertexShader], [gl.FRAGMENT_SHADER, fragmentShader]]) {
      const shader = gl.createShader(type);
      gl.shaderSource(shader, source);
      gl.compileShader(shader);
      if (!gl.getShaderParameter(shader, gl.COMPILE_STATUS)) {
        const error = gl.getShaderInfoLog(shader);
        gl.deleteShader(shader);
        this.dispose();
        throw new Error(error);
      }
      gl.attachShader(this.program, shader);
      gl.deleteShader(shader);
    }
    gl.linkProgram(this.program);
    if (!gl.getProgramParameter(this.program, gl.LINK_STATUS)) {
      const error = gl.getProgramInfoLog(this.program);
      this.dispose();
      throw new Error(error);
    }
    this.uniforms = new Map();
    this.textures = Array.from({ length: 3 }, () => {
      const texture = gl.createTexture();
      gl.bindTexture(gl.TEXTURE_2D, texture);
      gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MIN_FILTER, gl.LINEAR);
      gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MAG_FILTER, gl.LINEAR);
      gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_S, gl.CLAMP_TO_EDGE);
      gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_T, gl.CLAMP_TO_EDGE);
      return texture;
    });
    this.count = 5;
    this.labels = Array.from({ length: this.count }, (_, index) => `Tab ${index + 1}`);
    this.icons = Array.from({ length: this.count }, () => 'home');
  }

  setTabCount(count) {
    this.count = count;
    this.labels = Array.from({ length: count }, (_, index) => this.labels[index] || `Tab ${index + 1}`);
    this.icons = Array.from({ length: count }, (_, index) => this.icons[index] || 'home');
    if (this.width) this.resize(this.rect, this.dark, this.direction, this.blue);
  }

  setLabels(labels) {
    this.labels = Array.from({ length: this.count }, (_, index) => labels?.[index] || `Tab ${index + 1}`);
    if (this.width) {
      this.makeLabels(1, this.dark ? '#ffffff' : '#000000');
      this.makeLabels(2, '#0088ff');
    }
  }

  setIcons(icons) {
    this.icons = Array.from({ length: this.count }, (_, index) => icons?.[index] || 'home');
    if (this.width) {
      this.makeLabels(1, this.dark || this.blue ? '#eaf7ff' : '#000000');
      this.makeLabels(2, this.blue ? '#aee8ff' : '#0088ff');
    }
  }

  setAvatarInitial(initial) {
    this.avatarInitial = initial;
    if (this.width) {
      this.makeLabels(1, this.dark || this.blue ? '#eaf7ff' : '#000000');
      this.makeLabels(2, this.blue ? '#aee8ff' : '#0088ff');
    }
  }

  uniform(name) {
    if (!this.uniforms.has(name)) this.uniforms.set(name, this.gl.getUniformLocation(this.program, name));
    return this.uniforms.get(name);
  }

  upload(slot, source) {
    const gl = this.gl;
    gl.activeTexture(gl.TEXTURE0 + slot);
    gl.bindTexture(gl.TEXTURE_2D, this.textures[slot]);
    gl.pixelStorei(gl.UNPACK_PREMULTIPLY_ALPHA_WEBGL, false);
    gl.pixelStorei(gl.UNPACK_FLIP_Y_WEBGL, false);
    gl.texImage2D(gl.TEXTURE_2D, 0, gl.RGBA, gl.RGBA, gl.UNSIGNED_BYTE, source);
  }

  resize(rect, dark, direction, blue = false) {
    this.width = rect.width;
    this.dpr = Math.min(window.devicePixelRatio || 1, 2);
    this.rect = rect;
    this.dark = dark;
    this.blue = blue;
    this.direction = direction;
    this.canvas.width = Math.round((this.width + PADDING * 2) * this.dpr);
    this.canvas.height = Math.round((64 + PADDING * 2) * this.dpr);
    this.canvas.style.width = `${this.width + PADDING * 2}px`;
    this.canvas.style.height = `${64 + PADDING * 2}px`;
    this.gl.viewport(0, 0, this.canvas.width, this.canvas.height);
    this.viewport = [window.innerWidth, window.innerHeight];
    this.makeLabels(1, dark || blue ? '#eaf7ff' : '#000000');
    this.makeLabels(2, blue ? '#aee8ff' : '#0088ff');
    this.refreshBackdrop();
  }

  makeLabels(slot, color) {
    const canvas = document.createElement('canvas');
    canvas.width = Math.round(this.width * this.dpr);
    canvas.height = Math.round(64 * this.dpr);
    const ctx = canvas.getContext('2d');
    ctx.scale(this.dpr, this.dpr);
    ctx.fillStyle = color;
    ctx.strokeStyle = color;
    const { iconSize, fontSize, iconTop } = taskbarContentMetrics(this.width, this.count);
    ctx.font = `${fontSize}px Inter, Arial, Helvetica, sans-serif`;
    ctx.textAlign = 'center';
    ctx.textBaseline = 'alphabetic';
    const tabWidth = (this.width - 8) / this.count;
    for (let i = 0; i < this.count; i++) {
      const visualIndex = this.direction === 'rtl' ? this.count - 1 - i : i;
      const x = 4 + (visualIndex + 0.5) * tabWidth;
      ctx.save();
      if (this.icons[i] === 'user' && this.avatarInitial) {
        ctx.fillStyle = '#0093dd';
        ctx.beginPath();
        ctx.arc(x, iconTop + iconSize / 2, iconSize / 2, 0, Math.PI * 2);
        ctx.fill();
        ctx.fillStyle = '#ffffff';
        ctx.font = `600 ${iconSize * 0.52}px Inter, Arial, Helvetica, sans-serif`;
        ctx.textBaseline = 'middle';
        ctx.fillText(this.avatarInitial, x, iconTop + iconSize / 2, iconSize - 4);
      } else {
        ctx.translate(x - iconSize / 2, iconTop);
        ctx.scale(iconSize / 24, iconSize / 24);
        ctx.lineWidth = 1.8;
        for (const path of taskbarIconPaths[this.icons[i]] || taskbarIconPaths.home) ctx.stroke(new Path2D(path));
      }
      ctx.restore();
      ctx.fillText(this.labels[i], x, iconTop + iconSize + 2 + fontSize, tabWidth - 4);
    }
    this.upload(slot, canvas);
  }

  refreshBackdrop(source = this.backdropSource) {
    this.backdropSource = source;
    const canvas = document.createElement('canvas');
    // Background capture is shared across the panel and selection lens, never recursive.
    const scale = Math.min(1, 2048 / Math.max(...this.viewport));
    canvas.width = Math.max(1, Math.round(this.viewport[0] * scale));
    canvas.height = Math.max(1, Math.round(this.viewport[1] * scale));
    const ctx = canvas.getContext('2d');
    const gradient = ctx.createLinearGradient(0, 0, 0, canvas.height);
    gradient.addColorStop(0, '#9ad7ff');
    gradient.addColorStop(0.5, '#daf0ff');
    gradient.addColorStop(1, '#b9d4bc');
    ctx.fillStyle = gradient;
    ctx.fillRect(0, 0, canvas.width, canvas.height);
    if (source) ctx.drawImage(source, 0, 0, canvas.width, canvas.height);
    this.upload(0, canvas);
  }

  render(state, settings) {
    if (this.gl.isContextLost()) return;
    const gl = this.gl;
    gl.useProgram(this.program);
    gl.uniform2f(this.uniform('uResolution'), this.canvas.width, this.canvas.height);
    gl.uniform1f(this.uniform('uDpr'), this.dpr);
    gl.uniform2f(this.uniform('uBarSize'), this.width, 64);
    gl.uniform1f(this.uniform('uTabCount'), this.count);
    gl.uniform2f(this.uniform('uViewport'), ...this.viewport);
    gl.uniform2f(this.uniform('uOrigin'), this.rect.left, this.rect.top);
    gl.uniform1f(this.uniform('uDark'), this.dark ? 1 : 0);
    gl.uniform1f(this.uniform('uIndex'), state.index);
    gl.uniform1f(this.uniform('uPress'), state.press);
    gl.uniform1f(this.uniform('uHighlight'), state.highlight);
    gl.uniform1f(this.uniform('uPanelOffset'), state.panelOffset);
    gl.uniform2f(this.uniform('uScale'), state.scaleX, state.scaleY);
    for (const [name, value] of Object.entries({
      uCornerFraction: settings.cornerRadiusFraction, uBlurRadius: settings.blurRadius,
      uRefractionHeight: settings.refractionHeightFraction * 64 * 0.5,
      uRefractionAmount: settings.refractionAmountFraction * 64,
      uChromaticAberration: settings.chromaticAberration > 0 ? 1 : 0,
      uSurfaceAlpha: settings.surfaceAlpha, uSaturation: settings.saturation,
      uHighlightAlpha: settings.highlightAlpha, uShadowRadius: settings.shadowRadius,
      uDepthEffect: settings.depthEffect ? 1 : 0,
      uBlue: this.blue ? 1 : 0,
      uLiveBackdrop: this.canvas.getRootNode().host?.hasAttribute('live-backdrop') ? 1 : 0,
    })) gl.uniform1f(this.uniform(name), value);
    for (const [slot, name] of ['uBackdrop', 'uLabels', 'uAccentLabels'].entries()) {
      gl.activeTexture(gl.TEXTURE0 + slot);
      gl.bindTexture(gl.TEXTURE_2D, this.textures[slot]);
      gl.uniform1i(this.uniform(name), slot);
    }
    gl.drawArrays(gl.TRIANGLES, 0, 3);
  }

  dispose() {
    for (const texture of this.textures || []) this.gl.deleteTexture(texture);
    if (this.program) this.gl.deleteProgram(this.program);
    this.textures = [];
    this.program = null;
  }
}
