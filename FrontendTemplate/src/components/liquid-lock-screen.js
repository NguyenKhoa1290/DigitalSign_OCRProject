import './glass-surface.js';
import {getGlassBackdrop} from './backdrop.js';
import {TransformGesture} from './transform-gesture.js';

export class LiquidLockScreen extends HTMLElement {
  constructor(){
    super();this.attachShadow({mode:'open'}).innerHTML=`<style>
      :host{display:block;height:420px;position:relative;border-radius:24px;overflow:hidden;background:rgba(0,0,0,.3)}
      glass-surface{position:absolute;top:22%;left:12%;width:76%;max-width:400px;aspect-ratio:1599/515;cursor:grab;outline:none}
      glass-surface:focus-visible{outline:2px solid white;outline-offset:8px} glass-surface:active{cursor:grabbing}
      .fallback-clock{display:none;width:100%;height:100%}
      glass-surface[data-renderer=css] .fallback-clock{display:block} glass-surface[data-renderer=css]{--glass-tint:transparent!important;--glass-filter:none!important}
    </style><glass-surface tabindex="0" role="img" aria-label="Đồng hồ kính 12:45, kéo hoặc dùng phím mũi tên"><canvas class="fallback-clock" aria-hidden="true"></canvas></glass-surface>`;
    this.glass=this.shadowRoot.querySelector('glass-surface');
  }
  connectedCallback(){
    this.abort=new AbortController();const options={signal:this.abort.signal};
    this.glass.configure({radius:0,brightness:-.1,contrast:.75,saturation:1.5,blur:2,refractionHeight:48,refractionAmount:0,highlight:0,tint:[1,1,1,.25]});
    const image=new Image();this.image=image;
    image.addEventListener('load',()=>{this.glass.style.aspectRatio=`${image.naturalWidth}/${image.naturalHeight}`;this.glass.sdf=image;this.dataset.asset='loaded';this.paint();},options);
    image.addEventListener('error',()=>{this.dataset.asset='error';},options);
    image.src=new URL('../assets/clock_sdf.webp',import.meta.url).href;
    this.gesture=new TransformGesture(this.glass,state=>{this.glass.style.transform=`translate(${state.x}px,${state.y}px)`;this.paint();},{transform:false});
    for(const name of ['resize','scroll','glass-backdropchange']) window.addEventListener(name,()=>this.paint(),{...options,passive:true});
    this.resizeObserver=new ResizeObserver(()=>this.paint());this.resizeObserver.observe(this);this.paint();
  }
  paint(){
    if(!this.isConnected)return;
    const source=getGlassBackdrop(),canvas=document.createElement('canvas');canvas.width=innerWidth;canvas.height=innerHeight;
    const ctx=canvas.getContext('2d');ctx.drawImage(source,0,0,innerWidth,innerHeight);
    const r=this.getBoundingClientRect();ctx.fillStyle='rgba(0,0,0,.3)';ctx.fillRect(r.left,r.top,r.width,r.height);
    this.glass.backdrop=canvas;this.glass.refresh(true);
    if(this.dataset.asset==='loaded' && this.glass.dataset.renderer==='css'){
      const target=this.shadowRoot.querySelector('.fallback-clock'),g=this.glass.getBoundingClientRect();target.width=Math.round(g.width);target.height=Math.round(g.height);
      const mask=target.getContext('2d',{willReadFrequently:true});mask.drawImage(this.image,0,0,target.width,target.height);
      const pixels=mask.getImageData(0,0,target.width,target.height);for(let i=3;i<pixels.data.length;i+=4){const x=Math.max(0,Math.min(1,(pixels.data[i]/255-.5)*2));pixels.data[i]=255*x*x*(3-2*x);}mask.putImageData(pixels,0,0);
      mask.globalCompositeOperation='source-in';mask.drawImage(canvas,g.left,g.top,g.width,g.height,0,0,target.width,target.height);mask.globalCompositeOperation='source-over';
    }
  }
  reset(){this.gesture?.reset();}
  disconnectedCallback(){this.abort?.abort();this.resizeObserver?.disconnect();this.gesture?.dispose();}
}
customElements.define('liquid-lock-screen',LiquidLockScreen);
