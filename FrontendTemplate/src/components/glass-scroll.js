import {SurfaceRenderer} from './surface-renderer.js';
import {getGlassBackdrop} from './backdrop.js';
import {SURFACE_DEFAULTS} from './glass-surface.js';

// ScrollContainerContent / LazyScrollContainerContent: 160px cards, gap16,
// radius32, vibrancy, lens16/32. One GL context shared by all visible cards.
export class GlassScroll extends HTMLElement {
  static observedAttributes=['lazy','count'];
  constructor(){
    super();this.attachShadow({mode:'open'}).innerHTML=`<style>
      :host{display:block;height:480px;min-height:160px;position:relative}
      .viewport{height:100%;overflow:auto;position:relative;overscroll-behavior:contain;border-radius:24px;scrollbar-width:thin}
      .spacer{position:relative;width:100%}.card{position:absolute;left:16px;right:16px;height:160px;border-radius:32px;pointer-events:none;background:rgba(255,255,255,.08);backdrop-filter:saturate(1.5);box-shadow:inset 0 0 0 1px rgba(255,255,255,.5)}
      :host([data-renderer=webgl2]) .card{background:none;backdrop-filter:none;box-shadow:none}
      canvas{position:absolute;left:0;pointer-events:none}.viewport:focus-visible{outline:2px solid #0088ff;outline-offset:3px}
    </style><div class="viewport" tabindex="0" role="list" aria-label="Danh sách thẻ kính"><div class="spacer"></div><canvas aria-hidden="true"></canvas></div>`;
    this.viewport=this.shadowRoot.querySelector('.viewport');this.spacer=this.shadowRoot.querySelector('.spacer');this.canvas=this.shadowRoot.querySelector('canvas');this.cards=new Map();
  }
  get lazy(){return this.hasAttribute('lazy');}
  get count(){return Math.floor(Math.max(1,Math.min(10000,Number(this.getAttribute('count')) || (this.lazy?100:20))));}
  get renderedRange(){return [...this.cards.keys()];}
  connectedCallback(){
    this.abort=new AbortController();const options={signal:this.abort.signal};this.glCanvas=document.createElement('canvas');this.createRenderer();
    this.glCanvas.addEventListener('webglcontextlost',e=>{e.preventDefault();this.dataset.renderer='css';this.refresh();},options);
    this.glCanvas.addEventListener('webglcontextrestored',()=>{this.createRenderer();this.refresh(true);},options);
    this.viewport.addEventListener('scroll',()=>this.refresh(),{...options,passive:true});
    for(const name of ['resize','scroll','glass-backdropchange'])window.addEventListener(name,()=>this.refresh(true),{...options,passive:true});
    document.addEventListener('visibilitychange',()=>{if(document.hidden){cancelAnimationFrame(this.frame);this.frame=0;}else this.refresh(true);},options);
    this.resizeObserver=new ResizeObserver(()=>this.refresh(true));this.resizeObserver.observe(this);this.refresh(true);
  }
  createRenderer(){this.renderer?.dispose();this.uploaded=null;try{this.renderer=new SurfaceRenderer(this.glCanvas);this.dataset.renderer='webgl2';}catch{this.renderer=null;this.dataset.renderer='css';}}
  attributeChangedCallback(){this.refresh();}
  refresh(upload=false){this.needsUpload ||= upload;if(!this.isConnected || this.frame || document.hidden)return;this.frame=requestAnimationFrame(()=>{this.frame=0;this.draw();});}
  draw(){
    const r=this.viewport.getBoundingClientRect(),width=this.viewport.clientWidth,height=this.viewport.clientHeight,top=this.viewport.scrollTop;
    if(width<33 || !height)return;
    this.spacer.style.height=`${32+this.count*176-16}px`;
    const first=Math.max(0,Math.floor((top-16)/176)-1),last=Math.min(this.count-1,Math.ceil((top+height-16)/176)+1);
    const start=this.lazy?first:0,end=this.lazy?last:this.count-1;
    for(const [i,card] of this.cards)if(i<start || i>end){card.remove();this.cards.delete(i);}
    for(let i=start;i<=end;i++)if(!this.cards.has(i)){
      const card=document.createElement('div');card.className='card';card.dataset.index=i;card.style.top=`${16+i*176}px`;
      card.setAttribute('role','listitem');card.setAttribute('aria-label',`Glass card ${i+1}`);card.setAttribute('aria-posinset',i+1);card.setAttribute('aria-setsize',this.count);
      this.spacer.append(card);this.cards.set(i,card);
    }
    const dpr=Math.min(devicePixelRatio,2);this.canvas.width=Math.round(width*dpr);this.canvas.height=Math.round(height*dpr);this.canvas.style.width=`${width}px`;this.canvas.style.height=`${height}px`;this.canvas.style.top=`${top}px`;
    const ctx=this.canvas.getContext('2d');ctx.scale(dpr,dpr);
    if(!this.renderer || this.dataset.renderer!=='webgl2')return;
    try{
      const source=this.backdrop || getGlassBackdrop();if(this.needsUpload || this.uploaded!==source){this.renderer.upload(source);this.uploaded=source;this.needsUpload=false;}
      this.renderer.resize({width:width-32,height:160});
      const settings={...SURFACE_DEFAULTS,radius:32,blur:0,refractionHeight:16,refractionAmount:32,saturation:1.5,tint:[0,0,0,0]};
      for(let i=first;i<=last;i++){
        const y=16+i*176-top;if(y+160<=0 || y>=height)continue;
        this.renderer.render({left:r.left+16,top:r.top+y,width:width-32,height:160},settings);
        ctx.drawImage(this.glCanvas,16,y,width-32,160);
      }
    }catch{this.dataset.renderer='css';this.renderer?.dispose();this.renderer=null;}
  }
  disconnectedCallback(){this.abort?.abort();this.resizeObserver?.disconnect();cancelAnimationFrame(this.frame);this.frame=0;const gl=this.renderer?.gl;this.renderer?.dispose();this.renderer=null;if(gl && !gl.isContextLost())gl.getExtension('WEBGL_lose_context')?.loseContext();}
}
customElements.define('glass-scroll',GlassScroll);
