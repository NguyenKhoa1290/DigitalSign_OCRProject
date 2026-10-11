// Pointer equivalent of detectTransformGestures; no DOM/global listeners survive disposal.
export class TransformGesture {
  constructor(element, onChange, { transform = true } = {}) {
    this.element=element; this.onChange=onChange; this.allowTransform=transform;
    this.state={x:0,y:0,scale:1,rotation:0}; this.pointers=new Map();
    this.abort=new AbortController(); const options={signal:this.abort.signal};
    element.style.touchAction='none';
    element.addEventListener('pointerdown',e=>{
      if(e.button!==0) return;
      this.pointers.set(e.pointerId,{x:e.clientX,y:e.clientY});
      element.setPointerCapture(e.pointerId); this.previous=this.measure();
    },options);
    element.addEventListener('pointermove',e=>{
      if(!this.pointers.has(e.pointerId)) return;
      this.pointers.set(e.pointerId,{x:e.clientX,y:e.clientY});
      const next=this.measure(), before=this.previous;
      if(before && next) {
        const delta=Math.atan2(Math.sin(next.angle-before.angle),Math.cos(next.angle-before.angle));
        const scale=this.allowTransform ? this.state.scale*(before.distance>0 ? next.distance/before.distance : 1) : 1;
        const rotation=this.allowTransform ? this.state.rotation+delta*180/Math.PI : 0;
        const angle=rotation*Math.PI/180, dx=next.x-before.x, dy=next.y-before.y;
        // Kotlin rotates local pan by targetRotation and multiplies targetZoom.
        this.set({x:this.state.x+(dx*Math.cos(angle)-dy*Math.sin(angle))*scale,
          y:this.state.y+(dx*Math.sin(angle)+dy*Math.cos(angle))*scale,scale,rotation});
      }
      this.previous=next;
    },options);
    const end=e=>{this.pointers.delete(e.pointerId);this.previous=this.measure();};
    for(const name of ['pointerup','pointercancel','lostpointercapture']) element.addEventListener(name,end,options);
    window.addEventListener('blur',()=>{this.pointers.clear();this.previous=null;},options);
    element.addEventListener('keydown',e=>{
      const dx={ArrowLeft:-8,ArrowRight:8}[e.key] || 0,dy={ArrowUp:-8,ArrowDown:8}[e.key] || 0;
      if(dx || dy) {e.preventDefault();this.set({x:this.state.x+dx,y:this.state.y+dy});}
      else if(this.allowTransform && ['+','=','-','[',']'].includes(e.key)) {
        e.preventDefault(); this.set(['[',']'].includes(e.key) ? {rotation:this.state.rotation+(e.key===']' ? 5 : -5)} : {scale:this.state.scale*(e.key==='-' ? .9 : 1.1)});
      } else if(e.key==='Home') {e.preventDefault();this.reset();}
    },options);
    element.addEventListener('wheel',e=>{
      if(!this.allowTransform || !(e.ctrlKey || e.shiftKey)) return;
      e.preventDefault();this.set(e.shiftKey ? {rotation:this.state.rotation+e.deltaY*.1} : {scale:this.state.scale*Math.exp(-e.deltaY*.005)});
    },{...options,passive:false});
  }
  measure() {
    const p=[...this.pointers.values()]; if(!p.length) return null;
    return p.length===1 ? {...p[0],distance:0,angle:0} : {x:(p[0].x+p[1].x)/2,y:(p[0].y+p[1].y)/2,
      distance:Math.hypot(p[1].x-p[0].x,p[1].y-p[0].y),angle:Math.atan2(p[1].y-p[0].y,p[1].x-p[0].x)};
  }
  set(patch) {Object.assign(this.state,patch);this.state.scale=Math.max(.2,Math.min(4,this.state.scale));this.onChange({...this.state});}
  reset() {this.set({x:0,y:0,scale:1,rotation:0});}
  dispose() {this.abort.abort();this.pointers.clear();}
}
