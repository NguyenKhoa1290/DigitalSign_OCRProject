import './glass-surface.js';
import {getGlassBackdrop} from './backdrop.js';
import {TransformGesture} from './transform-gesture.js';
export const LOREM_IPSUM='Lorem ipsum dolor sit amet, consectetur adipiscing elit, sed do eiusmod tempor incididunt ut labore et dolore magna aliqua. Ut enim ad minim veniam, quis nostrud exercitation ullamco laboris nisi ut aliquip ex ea commodo consequat. Duis aute irure dolor in reprehenderit in voluptate velit esse cillum dolore eu fugiat nulla pariatur. Excepteur sint occaecat cupidatat non proident, sunt in culpa qui officia deserunt mollit anim id est laborum.';

export class LiquidMagnifier extends HTMLElement {
  static observedAttributes=['text','theme'];
  constructor(){
    super();this.attachShadow({mode:'open'}).innerHTML=`<style>
      :host{display:block;position:relative;height:440px;overflow:hidden;border-radius:24px}
      canvas{position:absolute;inset:0;width:100%;height:100%;pointer-events:none}
      .fallback-lens{display:none;border-radius:48px}glass-surface[data-renderer=css] .fallback-lens{display:block}
      button{position:absolute;left:calc(50% - 22px);top:calc(42% - 22px);width:44px;height:44px;background:transparent;border:0;cursor:move;touch-action:none;border-radius:8px}
      button:focus-visible{outline:2px solid #0088ff} glass-surface{position:absolute;left:calc(50% - 64px);top:calc(42% - 80px - 48px);width:128px;height:96px;pointer-events:none;box-shadow:inset 0 0 16px rgba(0,0,0,.15)}
    </style><canvas aria-hidden="true"></canvas><button type="button" aria-label="Kéo con trỏ kính lúp"></button><glass-surface aria-hidden="true"><canvas class="fallback-lens" aria-hidden="true"></canvas></glass-surface>`;
    this.canvas=this.shadowRoot.querySelector('canvas');this.cursor=this.shadowRoot.querySelector('button');this.glass=this.shadowRoot.querySelector('glass-surface');
  }
  connectedCallback(){
    this.abort=new AbortController();const options={signal:this.abort.signal};
    this.glass.configure({radius:48,blur:0,saturation:1,refractionHeight:8,refractionAmount:24,depth:true,chromatic:true,innerShadow:16,sampleScale:1.5,tint:[0,0,0,0]});
    // MagnifierContent translates the capture down to the cursor, 80dp below the lens.
    this.glass.sampleOffset=[0,80];
    this.gesture=new TransformGesture(this.cursor,state=>{
      this.cursor.style.transform=this.glass.style.transform=`translate(${state.x}px,${state.y}px)`;this.paint();
    },{transform:false});
    for(const name of ['resize','scroll','glass-backdropchange']) window.addEventListener(name,()=>this.paint(),{...options,passive:true});
    this.resizeObserver=new ResizeObserver(()=>this.paint());this.resizeObserver.observe(this);
    this.media=matchMedia('(prefers-color-scheme:dark)');this.media.addEventListener('change',()=>this.paint(),options);this.paint();
  }
  attributeChangedCallback(){if(this.isConnected)this.paint();}
  paint(){
    if(!this.isConnected)return;const r=this.getBoundingClientRect(),dpr=Math.min(devicePixelRatio,2);
    if(!r.width || !r.height)return;
    this.canvas.width=Math.round(r.width*dpr);this.canvas.height=Math.round(r.height*dpr);
    const ctx=this.canvas.getContext('2d');ctx.scale(dpr,dpr);
    const dark=this.getAttribute('theme')==='dark' || (!this.hasAttribute('theme') && matchMedia('(prefers-color-scheme:dark)').matches);
    ctx.fillStyle=dark?'rgba(18,18,18,.9)':'rgba(255,255,255,.9)';ctx.beginPath();ctx.roundRect(24,24,r.width-48,r.height-48,32);ctx.fill();
    ctx.fillStyle=dark?'white':'black';ctx.font='16px Inter, Arial, sans-serif';ctx.textBaseline='top';
    let line='',y=48;const available=r.width-96;
    for(const word of (this.getAttribute('text') || LOREM_IPSUM).split(/\s+/)){
      if(line && ctx.measureText(`${line} ${word}`).width>available){if(y<r.height-50)ctx.fillText(line,48,y);y+=24;line=word;}
      else line=line?`${line} ${word}`:word;
    }if(y<r.height-50)ctx.fillText(line,48,y);
    const s=this.gesture?.state || {x:0,y:0};ctx.fillStyle=dark?'#0091ff':'#0088ff';ctx.beginPath();ctx.roundRect(r.width*.5+s.x-2,r.height*.42+s.y-12,4,24,2);ctx.fill();
    const source=document.createElement('canvas');source.width=innerWidth;source.height=innerHeight;
    const global=source.getContext('2d');global.drawImage(getGlassBackdrop(),0,0,innerWidth,innerHeight);global.drawImage(this.canvas,r.left,r.top,r.width,r.height);
    this.glass.backdrop=source;this.glass.refresh(true);
    if(this.glass.dataset.renderer==='css'){
      const lens=this.shadowRoot.querySelector('.fallback-lens');lens.width=128*dpr;lens.height=96*dpr;const fallback=lens.getContext('2d');
      const x=r.left+r.width*.5+s.x,y=r.top+r.height*.42+s.y;
      fallback.drawImage(source,x-128/3,y-96/3,128/1.5,96/1.5,0,0,lens.width,lens.height);
    }
  }
  reset(){this.gesture?.reset();}
  disconnectedCallback(){this.abort?.abort();this.resizeObserver?.disconnect();this.gesture?.dispose();}
}
customElements.define('liquid-magnifier',LiquidMagnifier);
