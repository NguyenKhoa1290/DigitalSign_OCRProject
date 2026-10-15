/*
 * Copyright 2025 Kyant. Licensed under Apache-2.0; see ../LICENSE.
 * Modified: AGSL Shaders.kt translated to GLSL ES 3.00 for WebGL2.
 * Capsule SDF, circleMap refraction, seven-sample chromatic dispersion,
 * 45-degree edge highlight, and saturation weights are from the Android source.
 * Blur, texture composition and antialiasing are browser adaptations.
 */
export const vertexShader = `#version 300 es
void main() {
    vec2 p = vec2(float((gl_VertexID << 1) & 2), float(gl_VertexID & 2));
    gl_Position = vec4(p * 2.0 - 1.0, 0.0, 1.0);
}`;

export const fragmentShader = `#version 300 es
precision highp float;
uniform vec2 uResolution;
uniform float uDpr;
uniform vec2 uBarSize;
uniform vec2 uViewport;
uniform vec2 uOrigin;
uniform sampler2D uBackdrop;
uniform sampler2D uLabels;
uniform sampler2D uAccentLabels;
uniform float uIndex;
uniform float uPress;
uniform float uHighlight;
uniform vec2 uScale;
uniform float uPanelOffset;
uniform float uDark;
uniform float uCornerFraction;
uniform float uBlurRadius;
uniform float uRefractionHeight;
uniform float uRefractionAmount;
uniform float uChromaticAberration;
uniform float uSurfaceAlpha;
uniform float uSaturation;
uniform float uHighlightAlpha;
uniform float uShadowRadius;
uniform float uDepthEffect;
out vec4 outputColor;
const float PAD = 64.0;

float sdRoundedRect(vec2 coord, vec2 halfSize, float radius) {
    vec2 cornerCoord = abs(coord) - (halfSize - vec2(radius));
    float outside = length(max(cornerCoord, 0.0)) - radius;
    float inside = min(max(cornerCoord.x, cornerCoord.y), 0.0);
    return outside + inside;
}

vec2 gradSdRoundedRect(vec2 coord, vec2 halfSize, float radius) {
    vec2 cornerCoord = abs(coord) - (halfSize - vec2(radius));
    if (cornerCoord.x >= 0.0 || cornerCoord.y >= 0.0) {
        vec2 positive = max(cornerCoord, 0.0);
        return sign(coord) * positive / max(length(positive), 0.0001);
    }
    float gradX = step(cornerCoord.y, cornerCoord.x);
    return sign(coord) * vec2(gradX, 1.0 - gradX);
}

vec2 refraction(vec2 coord, vec2 halfSize, float height, float amount) {
    if (height <= 0.001 || amount <= 0.001) return vec2(0.0);
    float radius = min(halfSize.x, halfSize.y) * uCornerFraction;
    float sd = sdRoundedRect(coord, halfSize, radius);
    if (-sd >= height) return vec2(0.0);
    float x = clamp(1.0 + min(sd, 0.0) / height, 0.0, 1.0);
    float circleMap = 1.0 - sqrt(max(0.0, 1.0 - x * x));
    float gradRadius = min(radius * 1.5, min(halfSize.x, halfSize.y));
    vec2 gradient = gradSdRoundedRect(coord, halfSize, gradRadius);
    gradient += uDepthEffect * coord / max(length(coord), 0.0001);
    gradient /= max(length(gradient), 0.0001);
    return -amount * circleMap * gradient;
}

float mask(float distance, float aa) {
    return 1.0 - smoothstep(-aa, aa, distance);
}

vec3 background(vec2 localCoord) {
    return texture(uBackdrop, clamp((uOrigin + localCoord) / uViewport, 0.0, 1.0)).rgb;
}

vec3 blurredBackground(vec2 point) {
    if (uBlurRadius <= 0.0) return background(point);
    // Adjustable blur; same nine-sample approximation as the initial web port.
    vec3 sum = background(point) * 4.0;
    sum += background(point + vec2(uBlurRadius, 0.0)) * 2.0;
    sum += background(point - vec2(uBlurRadius, 0.0)) * 2.0;
    sum += background(point + vec2(0.0, uBlurRadius)) * 2.0;
    sum += background(point - vec2(0.0, uBlurRadius)) * 2.0;
    sum += background(point + vec2(uBlurRadius, uBlurRadius));
    sum += background(point + vec2(-uBlurRadius, uBlurRadius));
    sum += background(point + vec2(uBlurRadius, -uBlurRadius));
    sum += background(point - vec2(uBlurRadius, uBlurRadius));
    return sum / 16.0;
}

vec3 panel(vec2 p, float lensProgress, float height) {
    vec2 halfSize = vec2(uBarSize.x, height) * 0.5;
    vec2 center = vec2(uBarSize.x * 0.5, 32.0);
    vec2 offset = refraction(p - center, halfSize, uRefractionHeight * lensProgress, uRefractionAmount * lensProgress);
    vec3 color = blurredBackground(p + offset);
    float luminance = dot(color, vec3(0.213, 0.715, 0.072));
    color = clamp(mix(vec3(luminance), color, uSaturation), 0.0, 1.0);
    color = mix(color, mix(vec3(250.0 / 255.0), vec3(18.0 / 255.0), uDark), uSurfaceAlpha);
    float tabWidth = (uBarSize.x - 8.0) / 5.0;
    vec2 glowPoint = vec2(4.0 + (uIndex + 0.5) * tabWidth, 32.0);
    float glow = 1.0 - smoothstep(48.0, 96.0, distance(p, glowPoint));
    return clamp(color + vec3((0.08 + 0.15 * glow) * uHighlight), 0.0, 1.0);
}

vec4 labelAt(sampler2D source, vec2 p) {
    if (p.x < 0.0 || p.x > uBarSize.x || p.y < 0.0 || p.y > uBarSize.y) return vec4(0.0);
    return texture(source, p / uBarSize);
}

vec3 accentScene(vec2 p) {
    // CombinedBackdrop contains the raw backdrop plus a clipped 56px tabs row.
    // Preserve this boundary: dispersion samples crossing it produce the
    // coloured rim naturally, rather than painting an artificial rainbow border.
    vec2 halfSize = vec2(uBarSize.x * 0.5, 28.0);
    float rowSdf = sdRoundedRect(p - vec2(halfSize.x, 32.0), halfSize, 28.0 * uCornerFraction);
    float rowMask = mask(rowSdf, max(0.35, 0.75 / uDpr));
    if (rowMask <= 0.0) return background(p);
    vec3 color = panel(p, uPress, 56.0);
    float tabWidth = (uBarSize.x - 8.0) / 5.0;
    float index = clamp(floor((p.x - 4.0) / tabWidth), 0.0, 4.0);
    vec2 center = vec2(4.0 + (index + 0.5) * tabWidth, 32.0);
    vec2 textCoord = center + (p - center) / (1.0 + 0.2 * uPress);
    vec4 label = labelAt(uAccentLabels, textCoord);
    return mix(background(p), mix(color, label.rgb, label.a), rowMask);
}

vec3 dispersedScene(vec2 p, vec2 offset, float strength) {
    vec2 d = offset * strength;
    vec3 red = accentScene(p + d);
    vec3 orange = accentScene(p + d * (2.0 / 3.0));
    vec3 yellow = accentScene(p + d * (1.0 / 3.0));
    vec3 green = accentScene(p);
    vec3 cyan = accentScene(p - d * (1.0 / 3.0));
    vec3 blue = accentScene(p - d * (2.0 / 3.0));
    vec3 purple = accentScene(p - d);
    return vec3(
        (red.r + orange.r + yellow.r) / 3.5 + purple.r / 7.0,
        orange.g / 7.0 + (yellow.g + green.g + cyan.g) / 3.5,
        (cyan.b + blue.b + purple.b) / 3.0
    );
}

float edgeHighlight(vec2 p, vec2 halfSize, float sdf, float aa) {
    vec2 gradient = gradSdRoundedRect(p, halfSize, min(halfSize.x, halfSize.y) * uCornerFraction);
    float intensity = abs(dot(gradient, vec2(0.70710678)));
    return intensity * (1.0 - smoothstep(0.3, 1.1 + aa, abs(sdf))) * 0.5 * uHighlightAlpha;
}

void main() {
    vec2 screen = vec2(gl_FragCoord.x, uResolution.y - gl_FragCoord.y) / uDpr - vec2(PAD);
    float aa = max(0.35, 0.75 / uDpr);
    vec2 halfSize = uBarSize * 0.5;
    float panelScale = 1.0 + 16.0 / uBarSize.x * uPress;
    vec2 panelPoint = (screen - halfSize - vec2(uPanelOffset, 0.0)) / panelScale + halfSize;
    float panelSdf = sdRoundedRect(panelPoint - halfSize, halfSize, 32.0 * uCornerFraction);
    float panelMask = mask(panelSdf, aa);
    float panelShadow = uShadowRadius > 0.0 ? exp(-pow(max(panelSdf - uShadowRadius / 6.0, 0.0) / (uShadowRadius * 0.75), 2.0)) * 0.08 : 0.0;
    vec4 color = vec4(0.0, 0.0, 0.0, panelShadow);
    if (panelMask > 0.0) {
        vec3 glass = panel(panelPoint, 1.0, 64.0);
        vec4 labels = labelAt(uLabels, panelPoint);
        glass = mix(glass, labels.rgb, labels.a);
        glass += vec3(edgeHighlight(panelPoint - halfSize, halfSize, panelSdf, aa));
        color = vec4(clamp(glass, 0.0, 1.0), panelMask + panelShadow * (1.0 - panelMask));
    }

    float tabWidth = (uBarSize.x - 8.0) / 5.0;
    vec2 selectorHalfSize = vec2(tabWidth * 0.5, 28.0);
    vec2 selectorCenter = vec2(4.0 + (uIndex + 0.5) * tabWidth + uPanelOffset, 32.0);
    vec2 centered = (screen - selectorCenter) / uScale;
    float selectorRadius = min(selectorHalfSize.x, 28.0) * uCornerFraction;
    float selectorSdf = sdRoundedRect(centered, selectorHalfSize, selectorRadius);
    float selectorMask = mask(selectorSdf, aa);
    vec2 shadowCoord = (screen - selectorCenter - vec2(0.0, uShadowRadius / 6.0)) / uScale;
    float shadowSdf = sdRoundedRect(shadowCoord, selectorHalfSize, selectorRadius);
    float selectorShadow = uShadowRadius > 0.0 ? exp(-pow(max(shadowSdf, 0.0) / uShadowRadius, 2.0)) * 0.1 * uPress : 0.0;
    float combinedAlpha = color.a + selectorShadow * (1.0 - color.a);
    color.rgb *= color.a * (1.0 - selectorShadow) / max(combinedAlpha, 0.00001);
    color.a = combinedAlpha;
    if (selectorMask > 0.0) {
        // Retain selector-to-panel ratios 10/24 and 14/24 from LiquidBottomTabs.
        vec2 refracted = refraction(centered, selectorHalfSize, uRefractionHeight * (10.0 / 24.0) * uPress, uRefractionAmount * (14.0 / 24.0) * uPress);
        // LayerBackdrop.inverseTransform cancels the selector layer's scale
        // before sampling. Scale the displacement, NOT the captured labels.
        // The icon retains only LocalLiquidBottomTabScale (1 -> 1.2).
        vec2 screenRefraction = refracted * uScale;
        vec2 samplePoint = screen + screenRefraction - vec2(uPanelOffset, 0.0);
        float dispersion = centered.x * centered.y / (selectorHalfSize.x * selectorHalfSize.y);
        vec3 lens = uPress > 0.001 && uChromaticAberration > 0.0 ? dispersedScene(samplePoint, screenRefraction, dispersion) : accentScene(samplePoint);
        float tint = mix(0.1 * (1.0 - uPress), 0.0, uDark) + 0.03 * uPress;
        lens *= (1.0 - tint);
        if (uDark > 0.5) lens += vec3(0.1 * (1.0 - uPress));
        float edge = edgeHighlight(centered, selectorHalfSize, selectorSdf, aa) * uPress;
        float inner = exp(-max(-selectorSdf, 0.0) / max(8.0 * uPress, 0.001)) * 0.15 * uPress;
        lens = clamp(lens * (1.0 - inner) + edge, 0.0, 1.0);
        color = vec4(mix(color.rgb, lens, selectorMask), max(color.a, selectorMask));
    }
    outputColor = color;
}`;
