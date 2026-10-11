/* Copyright 2025 Kyant. Apache-2.0. Web adaptation of Shaders.kt/ColorFilter.kt.
 * General glass renderer; native DOM content is not part of the lens texture.
 */
import { vertexShader } from '../shaders.js';
const fragment = `#version 300 es
precision highp float;
uniform vec2 uSize, uViewport, uOrigin;
uniform float uDpr, uRadius, uBlur, uHeight, uAmount, uDepth, uChromatic;
uniform float uBrightness, uContrast, uSaturation, uHighlight, uGlow;
uniform vec4 uTint;
uniform vec4 uTrackRect, uTrackColor, uFillColor;
uniform float uTrackFill, uTrackEnabled, uTrackVertical;
uniform sampler2D uBackdrop;
uniform sampler2D uSdf;
uniform mat2 uBasis;
uniform float uSdfEnabled, uAlphaMask, uSampleScale, uInnerShadow, uLightAngle, uFalloff;
uniform vec2 uSampleOffset;
out vec4 outputColor;
float sdf(vec2 p, vec2 h, float r) {
  vec2 q = abs(p) - (h - r);
  return length(max(q,0.0)) - r + min(max(q.x,q.y),0.0);
}
vec2 grad(vec2 p, vec2 h, float r) {
  vec2 q = abs(p) - (h-r);
  if(q.x>=0.0 || q.y>=0.0) { vec2 v=max(q,0.0); return sign(p)*v/max(length(v),.0001); }
  float x=step(q.y,q.x); return sign(p)*vec2(x,1.0-x);
}
vec3 sampleBackground(vec2 p) {
  vec2 world=uOrigin+uBasis*p;
  vec2 center=uOrigin+uBasis*uSize*.5;
  world=center+(world-center)/uSampleScale+uSampleOffset;
  vec3 color=texture(uBackdrop,clamp(world/uViewport,0.0,1.0)).rgb;
  if(uTrackEnabled>0.0) {
    vec2 halfTrack=uTrackRect.zw*.5;
    float d=sdf(world-uTrackRect.xy-halfTrack,halfTrack,min(halfTrack.x,halfTrack.y));
    float mask=1.0-smoothstep(-.5,.5,d);
    bool filled=uTrackVertical>0.0 ? world.y>uTrackRect.y+uTrackRect.w*(1.0-uTrackFill) : world.x<uTrackRect.x+uTrackRect.z*uTrackFill;
    vec4 tint=filled ? uFillColor : uTrackColor;
    color=mix(color,tint.rgb,tint.a*mask);
  }
  return color;
}
vec3 scene(vec2 p) {
  vec3 color=sampleBackground(p);
  if(uBlur>0.0) {
    color*=4.0;
    color+=2.0*(sampleBackground(p+vec2(uBlur,0))+sampleBackground(p-vec2(uBlur,0))
      +sampleBackground(p+vec2(0,uBlur))+sampleBackground(p-vec2(0,uBlur)));
    color+=sampleBackground(p+vec2(uBlur,uBlur))+sampleBackground(p-vec2(uBlur,uBlur))
      +sampleBackground(p+vec2(uBlur,-uBlur))+sampleBackground(p+vec2(-uBlur,uBlur));
    color/=16.0;
  }
  float luminance=dot(color,vec3(.213,.715,.072));
  color=mix(vec3(luminance),color,uSaturation)*uContrast+(.5-.5*uContrast+uBrightness);
  return mix(clamp(color,0.0,1.0),uTint.rgb,uTint.a);
}
void main() {
  vec2 p=vec2(gl_FragCoord.x,uSize.y*uDpr-gl_FragCoord.y)/uDpr;
  vec2 h=uSize*.5, c=p-h;
  float r=min(uRadius,min(h.x,h.y));
  float distance=sdf(c,h,r);
  float aa=max(.35,.75/uDpr);
  float alpha=1.0-smoothstep(-aa,aa,distance);
  if(alpha<=0.0) { outputColor=vec4(0); return; }
  vec2 offset=vec2(0);
  vec2 sdfNormal=vec2(0); float sdfIntensity=0.0;
  if(uSdfEnabled>0.0) {
    vec4 tex=texture(uSdf,p/uSize);
    float mask=smoothstep(.5,1.0,tex.a);
    if(mask<=0.0) { outputColor=vec4(0); return; }
    float sd=mask<1.0 ? 0.0 : tex.r*2.0-1.0;
    sdfNormal=tex.gb*2.0-1.0;
    sdfNormal/=max(length(sdfNormal),.0001);
    float x=1.0-min(1.0,-sd*1.5);
    sdfIntensity=1.0-sqrt(max(0.0,1.0-x*x));
    offset=-sdfIntensity*uHeight*sdfNormal;
    alpha*=mask;
  }
  else if(uHeight>.001 && uAmount>.001 && -distance<uHeight) {
    float x=clamp(1.0+min(distance,0.0)/uHeight,0.0,1.0);
    vec2 normal=grad(c,h,min(r*1.5,min(h.x,h.y)))+uDepth*c/max(length(c),.0001);
    normal/=max(length(normal),.0001);
    offset=-uAmount*(1.0-sqrt(max(0.0,1.0-x*x)))*normal;
  }
  vec2 point=p+offset;
  vec3 color=scene(point);
  if(uChromatic>0.0) {
    vec2 d=offset*(c.x*c.y/max(h.x*h.y,.001));
    vec3 red=scene(point+d), orange=scene(point+d*2.0/3.0), yellow=scene(point+d/3.0);
    vec3 green=color, cyan=scene(point-d/3.0), blue=scene(point-d*2.0/3.0), purple=scene(point-d);
    color=vec3((red.r+orange.r+yellow.r)/3.5+purple.r/7.0,
      orange.g/7.0+(yellow.g+green.g+cyan.g)/3.5,(cyan.b+blue.b+purple.b)/3.0);
  }
  vec2 light=vec2(cos(radians(uLightAngle)),sin(radians(uLightAngle)));
  if(uSdfEnabled>0.0) {
    color*=1.0+.5*sdfIntensity*clamp(dot(sdfNormal,light),0.0,1.0);
    color*=1.0+.5*clamp(dot(sdfNormal,-light),0.0,1.0)*(1.0-smoothstep(0.0,1.0,abs(sdfIntensity-.25)*6.0));
  }
  float edge=pow(abs(dot(grad(c,h,r),light)),uFalloff)*(1.0-smoothstep(.3,1.1+aa,abs(distance)))*.5*uHighlight*(1.0-uSdfEnabled);
  color+=edge+uGlow*.08;
  if(uInnerShadow>0.0) color*=1.0-.15*(1.0-smoothstep(0.0,uInnerShadow,-distance));
  if(uAlphaMask>0.0) alpha*=1.0-smoothstep(uSize.y*.5,uSize.y,p.y);
  outputColor=vec4(clamp(color,0.0,1.0),alpha);
}`;

export class SurfaceRenderer {
  constructor(canvas) {
    this.canvas = canvas;
    const gl = this.gl = canvas.getContext('webgl2', { alpha: true, premultipliedAlpha: false, antialias: false });
    if (!gl) throw new Error('WebGL2 unavailable');
    this.program = gl.createProgram();
    for (const [type, source] of [[gl.VERTEX_SHADER, vertexShader], [gl.FRAGMENT_SHADER, fragment]]) {
      const shader = gl.createShader(type);
      gl.shaderSource(shader, source); gl.compileShader(shader);
      if (!gl.getShaderParameter(shader, gl.COMPILE_STATUS)) {
        const error = gl.getShaderInfoLog(shader); gl.deleteShader(shader); this.dispose(); throw new Error(error);
      }
      gl.attachShader(this.program, shader); gl.deleteShader(shader);
    }
    gl.linkProgram(this.program);
    if (!gl.getProgramParameter(this.program, gl.LINK_STATUS)) { const error = gl.getProgramInfoLog(this.program); this.dispose(); throw new Error(error); }
    this.texture = gl.createTexture();
    gl.bindTexture(gl.TEXTURE_2D, this.texture);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MIN_FILTER, gl.LINEAR);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MAG_FILTER, gl.LINEAR);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_S, gl.CLAMP_TO_EDGE);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_T, gl.CLAMP_TO_EDGE);
    this.uniforms = new Map();
    this.sdfTexture = gl.createTexture();
    gl.bindTexture(gl.TEXTURE_2D, this.sdfTexture);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MIN_FILTER, gl.LINEAR);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MAG_FILTER, gl.LINEAR);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_S, gl.CLAMP_TO_EDGE);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_T, gl.CLAMP_TO_EDGE);
    gl.texImage2D(gl.TEXTURE_2D,0,gl.RGBA,1,1,0,gl.RGBA,gl.UNSIGNED_BYTE,new Uint8Array([0,0,0,0]));
  }
  uniform(name) {
    if (!this.uniforms.has(name)) this.uniforms.set(name, this.gl.getUniformLocation(this.program, name));
    return this.uniforms.get(name);
  }
  upload(source) {
    const gl = this.gl;
    gl.activeTexture(gl.TEXTURE0);
    gl.bindTexture(gl.TEXTURE_2D, this.texture);
    gl.pixelStorei(gl.UNPACK_PREMULTIPLY_ALPHA_WEBGL, false);
    gl.pixelStorei(gl.UNPACK_FLIP_Y_WEBGL, false);
    gl.texImage2D(gl.TEXTURE_2D, 0, gl.RGBA, gl.RGBA, gl.UNSIGNED_BYTE, source);
  }
  resize(rect) {
    this.dpr = Math.min(devicePixelRatio || 1, 2);
    this.canvas.width = Math.max(1, Math.round(rect.width * this.dpr));
    this.canvas.height = Math.max(1, Math.round(rect.height * this.dpr));
    this.gl.viewport(0, 0, this.canvas.width, this.canvas.height);
  }
  render(rect, settings) {
    const gl = this.gl;
    if (gl.isContextLost()) return;
    gl.useProgram(this.program);
    gl.uniform2f(this.uniform('uSize'), rect.width, rect.height);
    gl.uniform2f(this.uniform('uViewport'), innerWidth, innerHeight);
    gl.uniform2f(this.uniform('uOrigin'), rect.left, rect.top);
    gl.uniformMatrix2fv(this.uniform('uBasis'),false,settings.basis || [1,0,0,1]);
    gl.uniform1f(this.uniform('uSampleScale'),settings.sampleScale || 1);
    gl.uniform2f(this.uniform('uSampleOffset'),...(settings.sampleOffset || [0,0]));
    gl.uniform1f(this.uniform('uAlphaMask'),Number(settings.alphaMask));
    gl.uniform1f(this.uniform('uInnerShadow'),settings.innerShadow || 0);
    gl.uniform1f(this.uniform('uLightAngle'),settings.highlightAngle ?? 45);
    gl.uniform1f(this.uniform('uFalloff'),settings.highlightFalloff || 1);
    gl.uniform1f(this.uniform('uSdfEnabled'),settings.sdf ? 1 : 0);
    gl.activeTexture(gl.TEXTURE1); gl.bindTexture(gl.TEXTURE_2D,this.sdfTexture);
    if(settings.sdf && this.uploadedSdf !== settings.sdf) {
      gl.pixelStorei(gl.UNPACK_COLORSPACE_CONVERSION_WEBGL,gl.NONE);
      gl.texImage2D(gl.TEXTURE_2D,0,gl.RGBA,gl.RGBA,gl.UNSIGNED_BYTE,settings.sdf);
      gl.pixelStorei(gl.UNPACK_COLORSPACE_CONVERSION_WEBGL,gl.BROWSER_DEFAULT_WEBGL);
      this.uploadedSdf=settings.sdf;
    }
    gl.uniform1i(this.uniform('uSdf'),1);
    gl.uniform1f(this.uniform('uDpr'), this.dpr);
    for (const [key, uniform] of Object.entries({ radius: 'uRadius', blur: 'uBlur', refractionHeight: 'uHeight',
      refractionAmount: 'uAmount', depth: 'uDepth', chromatic: 'uChromatic', brightness: 'uBrightness',
      contrast: 'uContrast', saturation: 'uSaturation', highlight: 'uHighlight', glow: 'uGlow' })) {
      gl.uniform1f(this.uniform(uniform), Number(settings[key]));
    }
    gl.uniform4f(this.uniform('uTint'), ...settings.tint);
    const overlay = settings.overlay;
    gl.uniform1f(this.uniform('uTrackEnabled'), overlay ? 1 : 0);
    gl.uniform4f(this.uniform('uTrackRect'), ...(overlay?.rect || [0, 0, 0, 0]));
    gl.uniform4f(this.uniform('uTrackColor'), ...(overlay?.color || [0, 0, 0, 0]));
    gl.uniform4f(this.uniform('uFillColor'), ...(overlay?.fillColor || [0, 0, 0, 0]));
    gl.uniform1f(this.uniform('uTrackFill'), overlay?.fill || 0);
    gl.uniform1f(this.uniform('uTrackVertical'), overlay?.vertical ? 1 : 0);
    gl.activeTexture(gl.TEXTURE0); gl.bindTexture(gl.TEXTURE_2D, this.texture);
    gl.uniform1i(this.uniform('uBackdrop'), 0);
    gl.drawArrays(gl.TRIANGLES, 0, 3);
  }
  dispose() {
    if (this.texture) this.gl.deleteTexture(this.texture);
    if (this.sdfTexture) this.gl.deleteTexture(this.sdfTexture);
    if (this.program) this.gl.deleteProgram(this.program);
    this.texture = this.program = null;
  }
}
