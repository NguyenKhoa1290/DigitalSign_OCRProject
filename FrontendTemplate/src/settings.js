// Defaults/ranges researched from the complete, untrimmed AndroidLiquidGlass-kmp
// source. Playground ranges come from GlassPlaygroundContent.kt; taskbar defaults
// come from LiquidBottomTabs.kt and its original effects/animation classes.
export const CONTROL_GROUPS = [
  { id: 'glass', title: 'Kính & khúc xạ', controls: [
    { key: 'cornerRadiusFraction', label: 'Bo góc', kotlin: 'Corner radius', min: 0, max: 1, step: 0.01, default: 1, unit: 'fraction', hint: '0: góc vuông · 1: capsule.' },
    { key: 'blurRadius', label: 'Độ mờ nền', kotlin: 'Blur radius', min: 0, max: 32, step: 0.1, default: 8, unit: 'px', hint: 'Làm mờ hình nền phía sau kính.' },
    { key: 'refractionHeightFraction', label: 'Vùng khúc xạ', kotlin: 'Refraction height', min: 0, max: 1, step: 0.01, default: 0.75, unit: 'height', hint: 'Độ dày vùng bẻ cong hình ở viền kính.' },
    { key: 'refractionAmountFraction', label: 'Độ lệch khúc xạ', kotlin: 'Refraction amount', min: 0, max: 1, step: 0.005, default: 0.375, unit: 'amount', hint: 'Mức dịch chuyển hình nền qua kính.' },
    { key: 'chromaticAberration', label: 'Tách màu', kotlin: 'Chromatic aberration', min: 0, max: 1, step: 0.01, default: 1, unit: 'chromatic', hint: '0: tắt · >0: bật. Giữ và kéo ô đang chọn để thử.' },
  ] },
  { id: 'surface', title: 'Bề mặt & ánh sáng', controls: [
    { key: 'surfaceAlpha', label: 'Độ phủ lớp kính', kotlin: 'containerColor.alpha', min: 0, max: 1, step: 0.01, default: 0.4, unit: 'percent', hint: 'Giảm để nhìn rõ nền; tăng để lớp kính đặc hơn.' },
    { key: 'saturation', label: 'Độ bão hoà màu', kotlin: 'colorControls.saturation', min: 0, max: 3, step: 0.05, default: 1.5, unit: 'ratio', hint: '0: đơn sắc · 1: màu nền gốc · 1.5: vibrancy gốc.' },
    { key: 'highlightAlpha', label: 'Độ sáng viền', kotlin: 'Highlight.alpha', min: 0, max: 1, step: 0.01, default: 1, unit: 'percent', hint: 'Cường độ viền sáng của kính.' },
    { key: 'shadowRadius', label: 'Bán kính bóng', kotlin: 'Shadow.radius', min: 0, max: 48, step: 1, default: 24, unit: 'px', hint: '0: bỏ bóng đổ. Bóng của ô chọn xuất hiện khi nhấn.' },
  ] },
  { id: 'motion', title: 'Chuyển động khi nhấn & kéo', controls: [
    { key: 'pressedScale', label: 'Phóng to khi nhấn', kotlin: 'pressedScale', min: 1, max: 1.8, step: 0.01, default: 78 / 56, unit: 'ratio', hint: 'Tỷ lệ phóng to của ô kính đang chọn.' },
    { key: 'valueStiffness', label: 'Độ cứng chuyển tab', kotlin: 'valueAnimationSpec.stiffness', min: 100, max: 2000, step: 10, default: 1000, unit: 'number', hint: 'Tăng để ô kính theo tab nhanh hơn.' },
    { key: 'valueDamping', label: 'Giảm chấn chuyển tab', kotlin: 'valueAnimationSpec.dampingRatio', min: 0.2, max: 1, step: 0.01, default: 1, unit: 'ratio', hint: 'Giảm để có độ nảy; 1 để về đích không rung.' },
    { key: 'scaleDampingX', label: 'Giảm chấn co giãn ngang', kotlin: 'scaleXAnimationSpec.dampingRatio', min: 0.2, max: 1, step: 0.01, default: 0.6, unit: 'ratio', hint: 'Độ nảy của chiều ngang sau khi nhấn/thả.' },
    { key: 'scaleDampingY', label: 'Giảm chấn co giãn dọc', kotlin: 'scaleYAnimationSpec.dampingRatio', min: 0.2, max: 1, step: 0.01, default: 0.7, unit: 'ratio', hint: 'Độ nảy của chiều dọc sau khi nhấn/thả.' },
  ] },
];

export const CONTROL_DEFINITIONS = CONTROL_GROUPS.flatMap(group => group.controls);
export const DEFAULT_SETTINGS = Object.freeze({
  ...Object.fromEntries(CONTROL_DEFINITIONS.map(control => [control.key, control.default])),
  depthEffect: false,
});

// GlassPlaygroundContent.kt defaults, with fractions applied to this 64px bar
// instead of the original 256dp square. This is distinct from taskbar defaults.
export const PLAYGROUND_SETTINGS = Object.freeze({
  ...DEFAULT_SETTINGS, cornerRadiusFraction: 0.5, blurRadius: 0,
  refractionHeightFraction: 0.2, refractionAmountFraction: 0.2,
  chromaticAberration: 0, depthEffect: true,
});

export function normalizeSettings(patch, current = DEFAULT_SETTINGS) {
  const settings = { ...current };
  if (!patch || typeof patch !== 'object') return settings;
  for (const control of CONTROL_DEFINITIONS) {
    if (!(control.key in patch)) continue;
    const value = Number(patch[control.key]);
    if (Number.isFinite(value)) settings[control.key] = Math.max(control.min, Math.min(control.max, value));
  }
  if (typeof patch.depthEffect === 'boolean') settings.depthEffect = patch.depthEffect;
  return settings;
}
