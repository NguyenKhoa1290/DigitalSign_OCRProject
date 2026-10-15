import './glass-surface.js';
export class ProgressiveBlur extends HTMLElement {
  static observedAttributes=['theme'];
  constructor(){super();this.attachShadow({mode:'open'}).innerHTML=`<style>:host{display:block;height:128px}glass-surface{height:100%}.label{display:grid;place-items:center;height:100%;font:16px Inter,Arial,sans-serif;color:var(--label,#000)}</style><glass-surface><div class="label"><slot>alpha-masked progressive blur</slot></div></glass-surface>`;this.glass=this.shadowRoot.querySelector('glass-surface');}
  connectedCallback(){this.abort=new AbortController();matchMedia('(prefers-color-scheme:dark)').addEventListener('change',()=>this.update(),{signal:this.abort.signal});this.update();}
  attributeChangedCallback(){if(this.glass)this.update();}
  update(){
    const dark=this.getAttribute('theme')==='dark' || (!this.hasAttribute('theme') && matchMedia('(prefers-color-scheme:dark)').matches);
    this.style.setProperty('--label',dark?'white':'black');
    this.glass.configure({radius:0,blur:4,saturation:1,refractionHeight:0,refractionAmount:0,highlight:0,alphaMask:true,tint:dark?[128/255,128/255,128/255,.8]:[1,1,1,.8]});
    const layer=this.glass.shadowRoot.querySelector('.fallback');layer.style.maskImage=layer.style.webkitMaskImage='linear-gradient(to bottom,black 50%,transparent 100%)';
  }
  disconnectedCallback(){this.abort?.abort();}
}
customElements.define('progressive-blur',ProgressiveBlur);
