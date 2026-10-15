import './glass-surface.js';
import './liquid-slider.js';
import './liquid-button.js';
import {TransformGesture} from './transform-gesture.js';
import {Spring} from '../spring.js';

const CONTROLS=[['corner','Corner radius',0,1,.001,.5],['blur','Blur radius',0,32,.01,0],['height','Refraction height',0,1,.001,.2],['amount','Refraction amount',0,1,.001,.2],['chromatic','Chromatic aberration',0,1,.001,0]];
export class GlassPlayground extends HTMLElement {
  constructor(){
    super();this.attachShadow({mode:'open'}).innerHTML=`<style>
      :host{display:block;position:relative} *{box-sizing:border-box}.stage{height:360px;position:relative;overflow:hidden;border-radius:24px}
      .sample{position:absolute;left:calc(50% - 128px);top:48px;width:256px;height:256px;cursor:grab;outline:none}.sample:focus-visible{outline:2px solid #0088ff;outline-offset:4px}
      .sheet{padding:24px;margin:16px 0;border-radius:32px}.fields{display:grid;gap:16px}.field{min-width:0}.caption{display:flex;justify-content:space-between;font:12px Inter,Arial,sans-serif;margin-bottom:8px}output{font-variant-numeric:tabular-nums;color:#006cbb}
      .actions{display:flex;justify-content:space-between;gap:16px;margin:16px 0}.actions liquid-button{font:13px Inter,Arial,sans-serif} [hidden]{display:none!important}
    </style><div class="stage"><glass-surface class="sample" tabindex="0" role="img" aria-label="Kính thử nghiệm, kéo để di chuyển, hai ngón để thu phóng và xoay"></glass-surface></div>
    <glass-surface class="sheet"><div class="fields">${CONTROLS.map(([key,label,min,max,step,value])=>`<div class="field"><div class="caption"><label>${label}</label><output data-output="${key}">${value}</output></div><liquid-slider data-key="${key}" label="${label}" min="${min}" max="${max}" step="${step}" value="${value}"></liquid-slider></div>`).join('')}</div></glass-surface>
    <div class="actions"><liquid-button class="sheet-toggle" tint="#ff8d28">Ẩn bảng</liquid-button><liquid-button class="reset" tint="#ff8d28">Reset</liquid-button></div>`;
    this.glass=this.shadowRoot.querySelector('.sample');this.sheet=this.shadowRoot.querySelector('.sheet');this.values=Object.fromEntries(CONTROLS.map(([key,,,,,value])=>[key,value]));
  }
  connectedCallback(){
    this.abort=new AbortController();const options={signal:this.abort.signal};
    this.gesture=new TransformGesture(this.glass,state=>{cancelAnimationFrame(this.frame);this.frame=0;this.applyTransform(state);});
    this.sheet.configure({radius:32,blur:4,refractionHeight:16,refractionAmount:32,tint:[1,1,1,.5]});
    this.shadowRoot.querySelectorAll('liquid-slider').forEach(slider=>slider.addEventListener('input',e=>{this.values[slider.dataset.key]=e.detail.value;this.sync();},options));
    this.shadowRoot.querySelector('.sheet-toggle').addEventListener('activate',()=>{this.sheet.hidden=!this.sheet.hidden;this.shadowRoot.querySelector('.sheet-toggle').textContent=this.sheet.hidden?'Hiện bảng':'Ẩn bảng';this.sheet.refresh();},options);
    this.shadowRoot.querySelector('.reset').addEventListener('activate',()=>this.reset(),options);this.sync();
  }
  applyTransform(state){this.glass.style.transform=`translate(${state.x}px,${state.y}px) rotate(${state.rotation}deg) scale(${state.scale})`;this.glass.refresh();this.dispatchEvent(new CustomEvent('transformchange',{bubbles:true,composed:true,detail:state}));}
  sync(){
    const v=this.values;this.glass.configure({radius:128*v.corner,blur:v.blur,refractionHeight:128*v.height,refractionAmount:256*v.amount,chromatic:v.chromatic>0,depth:true,saturation:1.5,tint:[0,0,0,0]});
    for(const [key,label] of CONTROLS){this.shadowRoot.querySelector(`[data-output="${key}"]`).value=String(Number(v[key].toFixed(3)));}
  }
  reset(){
    this.values=Object.fromEntries(CONTROLS.map(([key,,,,,value])=>[key,value]));
    for(const slider of this.shadowRoot.querySelectorAll('liquid-slider'))slider.value=this.values[slider.dataset.key];this.sync();
    cancelAnimationFrame(this.frame);const springs=Object.fromEntries(Object.entries(this.gesture.state).map(([key,value])=>{const s=new Spring(value,1500,.5);s.target=key==='scale'?1:0;return[key,s];}));
    let previous=performance.now();const tick=now=>{this.frame=0;const dt=Math.min(.05,(now-previous)/1000);previous=now;const reduced=matchMedia('(prefers-reduced-motion:reduce)').matches;
      for(const [key,s] of Object.entries(springs)){if(reduced)s.snap(s.target);else s.step(dt);this.gesture.state[key]=s.value;}
      this.applyTransform({...this.gesture.state});if(Object.values(springs).some(s=>!s.settled))this.frame=requestAnimationFrame(tick);};this.frame=requestAnimationFrame(tick);
  }
  disconnectedCallback(){this.abort?.abort();this.gesture?.dispose();cancelAnimationFrame(this.frame);this.frame=0;}
}
customElements.define('glass-playground',GlassPlayground);
