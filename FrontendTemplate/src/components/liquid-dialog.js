import './glass-surface.js';
import {getGlassBackdrop} from './backdrop.js';

// DialogContent.kt light/dark palette and exact glass parameters.
export class LiquidDialog extends HTMLElement {
  static observedAttributes=['theme'];
  constructor() {
    super();this.attachShadow({mode:'open'}).innerHTML=`<style>
      :host{display:block;width:min(100%,420px)} *{box-sizing:border-box}
      dialog{padding:0;border:0;background:none;color:inherit;width:min(420px,calc(100vw - 48px));max-height:calc(100dvh - 48px);overflow:auto}
      dialog:not([open]){display:none} dialog::backdrop{background:var(--dim,rgba(41,41,58,.23))}
      glass-surface{color:var(--content,#000)} h2{margin:0;padding:24px 28px 12px;font:500 24px Inter,Arial,sans-serif}
      p{padding:12px 24px;margin:0;font:15px/1.5 Inter,Arial,sans-serif;opacity:.68;display:-webkit-box;-webkit-line-clamp:5;-webkit-box-orient:vertical;overflow:hidden}
      .actions{display:flex;gap:16px;padding:12px 24px 24px} button{height:48px;flex:1;border:0;border-radius:99px;font:16px Inter,Arial,sans-serif;cursor:pointer}
      .cancel{color:inherit;background:var(--cancel,rgba(250,250,250,.2))}.confirm{color:white;background:var(--accent,#0088ff)}
      button:focus-visible{outline:2px solid var(--accent,#0088ff);outline-offset:3px}
    </style><dialog aria-labelledby="title"><glass-surface><h2 id="title"><slot name="title">Dialog Title</slot></h2><p><slot></slot></p><div class="actions"><button class="cancel" type="button">Cancel</button><button class="confirm" type="button">Okay</button></div></glass-surface></dialog>`;
    this.dialog=this.shadowRoot.querySelector('dialog');this.glass=this.shadowRoot.querySelector('glass-surface');
  }
  get open(){return this.dialog.open;}
  connectedCallback(){
    this.abort=new AbortController();const options={signal:this.abort.signal};
    this.shadowRoot.querySelector('.cancel').addEventListener('click',()=>this.close('cancel'),options);
    this.shadowRoot.querySelector('.confirm').addEventListener('click',()=>this.close('confirm'),options);
    this.dialog.addEventListener('cancel',e=>{e.preventDefault();this.close('cancel');},options);
    this.dialog.addEventListener('click',e=>{if(e.target===this.dialog){const r=this.dialog.getBoundingClientRect();if(e.clientX<r.left || e.clientX>r.right || e.clientY<r.top || e.clientY>r.bottom)this.close('cancel');}},options);
    this.media=matchMedia('(prefers-color-scheme:dark)');this.media.addEventListener('change',()=>this.updateTheme(),options);this.updateTheme();
    for(const name of ['resize','scroll','glass-backdropchange'])window.addEventListener(name,()=>{if(this.open)this.capture();},{...options,passive:true});
  }
  attributeChangedCallback(){if(this.glass)this.updateTheme();}
  updateTheme(){
    const dark=this.getAttribute('theme')==='dark' || (!this.hasAttribute('theme') && matchMedia('(prefers-color-scheme:dark)').matches);
    this.style.setProperty('--dim',dark?'rgba(18,18,18,.56)':'rgba(41,41,58,.23)');
    this.style.setProperty('--content',dark?'white':'black');this.style.setProperty('--accent',dark?'#0091ff':'#0088ff');
    this.style.setProperty('--cancel',dark?'rgba(18,18,18,.2)':'rgba(250,250,250,.2)');
    this.glass.configure({radius:48,brightness:dark?0:.2,saturation:1.5,blur:dark?8:16,refractionHeight:24,refractionAmount:48,depth:true,tint:dark?[18/255,18/255,18/255,.4]:[250/255,250/255,250/255,.6]});
    if(this.open)this.capture();
  }
  capture(){const canvas=document.createElement('canvas');canvas.width=innerWidth;canvas.height=innerHeight;const ctx=canvas.getContext('2d');ctx.drawImage(getGlassBackdrop(),0,0,innerWidth,innerHeight);ctx.fillStyle=this.style.getPropertyValue('--dim');ctx.fillRect(0,0,innerWidth,innerHeight);this.glass.backdrop=canvas;}
  show(){if(this.open)return;this.dialog.showModal();this.capture();this.glass.refresh(true);}
  close(reason='cancel'){
    if(!this.open)return;this.dialog.close(reason);
    this.dispatchEvent(new CustomEvent('dialogaction',{bubbles:true,composed:true,detail:{action:reason}}));
  }
  disconnectedCallback(){if(this.open)this.dialog.close();this.abort?.abort();}
}
customElements.define('liquid-dialog',LiquidDialog);
