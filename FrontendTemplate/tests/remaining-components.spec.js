import {test,expect} from '@playwright/test';

test.beforeEach(async({page})=>{page.__errors=[];page.on('pageerror',e=>page.__errors.push(e.message));});
test.afterEach(async({page})=>{expect(page.__errors).toEqual([]);});

test('dialog glass theme, modal focus, confirm, Escape and focus restoration',async({page},info)=>{
  await page.goto('/#dialog');const dialog=page.locator('liquid-dialog');const opener=page.getByRole('button',{name:'Mở hộp thoại',exact:true});
  await opener.click();await expect(dialog.locator('dialog')).toBeVisible();
  await expect(dialog.locator('glass-surface')).toHaveAttribute('data-renderer','webgl2');
  expect(await dialog.evaluate(el=>el.glass.settings)).toMatchObject({radius:48,blur:16,brightness:.2,depth:true,refractionAmount:48});
  await page.getByRole('button',{name:'Okay',exact:true}).click();await expect(page.locator('#dialog-status')).toHaveText('Đã chọn: confirm');
  await expect(opener).toBeFocused();await opener.click();await page.keyboard.press('Escape');await expect(page.locator('#dialog-status')).toHaveText('Đã chọn: cancel');
  await dialog.evaluate(el=>el.setAttribute('theme','dark'));await opener.click();expect(await dialog.evaluate(el=>el.glass.settings)).toMatchObject({blur:8,brightness:0});
  await page.screenshot({path:info.outputPath('dialog-dark.png'),fullPage:true});await page.getByRole('button',{name:'Cancel',exact:true}).click();
});

test('original clock texture drives SDF alpha/refraction and pointer/keyboard dragging',async({page,request},info)=>{
  await page.goto('/#lock-screen');const clock=page.locator('liquid-lock-screen');await expect(clock).toHaveAttribute('data-asset','loaded');
  const glass=clock.locator('glass-surface');await expect(glass).toHaveAttribute('data-renderer','webgl2');
  const stats=await clock.evaluate(el=>{
    const s=el.glass;s.draw();const r=s.renderer,gl=r.gl,pixels=new Uint8Array(s.canvas.width*s.canvas.height*4);gl.readPixels(0,0,s.canvas.width,s.canvas.height,gl.RGBA,gl.UNSIGNED_BYTE,pixels);
    let transparent=0,opaque=0;for(let i=3;i<pixels.length;i+=4){if(pixels[i]<10)transparent++;if(pixels[i]>240)opaque++;}
    return {transparent,opaque,enabled:gl.getUniform(r.program,r.uniform('uSdfEnabled')),error:gl.getError()};
  });expect(stats).toMatchObject({enabled:1,error:0});expect(stats.transparent).toBeGreaterThan(500);expect(stats.opaque).toBeGreaterThan(500);
  await glass.focus();await page.keyboard.press('ArrowRight');expect(await clock.evaluate(el=>el.gesture.state.x)).toBe(8);
  const box=await glass.boundingBox();await page.mouse.move(box.x+box.width/2,box.y+box.height/2);await page.mouse.down();await page.mouse.move(box.x+box.width/2+40,box.y+box.height/2+20,{steps:4});await page.mouse.up();
  expect(await clock.evaluate(el=>el.gesture.state.x)).toBeGreaterThan(35);
  const asset=await request.get('/src/assets/clock_sdf.webp');expect(asset.ok()).toBe(true);expect(asset.headers()['content-type']).toBe('image/webp');
  await page.screenshot({path:info.outputPath('lock-screen-desktop.png'),fullPage:true});
});

test('magnifier combines text/cursor capture and really magnifies the cursor 1.5x',async({page},info)=>{
  await page.goto('/#magnifier');const magnifier=page.locator('liquid-magnifier');await expect(magnifier.locator('glass-surface')).toHaveAttribute('data-renderer','webgl2');
  const result=await magnifier.evaluate(el=>{
    el.paint();el.glass.draw();const r=el.glass.renderer,gl=r.gl,pixels=new Uint8Array(el.glass.canvas.width*4);
    gl.readPixels(0,Math.floor(el.glass.canvas.height/2),el.glass.canvas.width,1,gl.RGBA,gl.UNSIGNED_BYTE,pixels);
    let blue=0;for(let i=0;i<pixels.length;i+=4)if(pixels[i]<50 && pixels[i+1]>60 && pixels[i+1]<180 && pixels[i+2]>180)blue++;
    return{blue:blue/r.dpr,scale:gl.getUniform(r.program,r.uniform('uSampleScale')),offset:Array.from(gl.getUniform(r.program,r.uniform('uSampleOffset'))),error:gl.getError()};
  });expect(result).toMatchObject({scale:1.5,offset:[0,80],error:0});expect(result.blue).toBeGreaterThanOrEqual(5);
  await magnifier.getByRole('button',{name:'Kéo con trỏ kính lúp'}).focus();await page.keyboard.press('ArrowLeft');expect(await magnifier.evaluate(el=>el.gesture.state.x)).toBe(-8);
  await page.keyboard.press('Home');expect(await magnifier.evaluate(el=>el.gesture.state.x)).toBe(0);
  await page.screenshot({path:info.outputPath('magnifier-desktop.png'),fullPage:true});
});

test('progressive blur implements original smoothstep alpha mask, not variable radius',async({page},info)=>{
  await page.goto('/#progressive-blur');const blur=page.locator('progressive-blur');await expect(blur.locator('glass-surface')).toHaveAttribute('data-renderer','webgl2');
  const pixels=await blur.evaluate(el=>{
    el.glass.draw();const r=el.glass.renderer,gl=r.gl,w=el.glass.canvas.width,h=el.glass.canvas.height;
    const alpha=[16,64,96,127].map(y=>{const p=new Uint8Array(4);gl.readPixels(Math.floor(w/2),h-1-Math.floor(y*r.dpr),1,1,gl.RGBA,gl.UNSIGNED_BYTE,p);return p[3];});
    return{alpha,blur:gl.getUniform(r.program,r.uniform('uBlur')),mask:gl.getUniform(r.program,r.uniform('uAlphaMask')),error:gl.getError()};
  });expect(pixels).toMatchObject({blur:4,mask:1,error:0});expect(pixels.alpha[0]).toBe(255);expect(pixels.alpha[1]).toBeGreaterThan(250);expect(pixels.alpha[2]).toBeGreaterThan(115);expect(pixels.alpha[2]).toBeLessThan(140);expect(pixels.alpha[3]).toBeLessThan(3);
  await page.screenshot({path:info.outputPath('progressive-desktop.png'),fullPage:true});
});

test('ordinary list has20 cards; lazy100 only mounts visible range, uses one context and releases it',async({page},info)=>{
  await page.goto('/#scroll-container');let list=page.locator('glass-scroll');await expect(list).toHaveAttribute('data-renderer','webgl2');await expect(list.locator('.card')).toHaveCount(20);
  expect(await list.evaluate(el=>el.shadowRoot.querySelectorAll('canvas').length)).toBe(1);
  await page.goto('/#lazy-scroll-container');list=page.locator('glass-scroll');await expect(list).toHaveAttribute('data-renderer','webgl2');
  expect(await list.locator('.card').count()).toBeLessThan(10);
  await list.evaluate(el=>{el.viewport.scrollTop=8500;});await expect.poll(()=>list.evaluate(el=>Math.min(...el.renderedRange))).toBeGreaterThan(45);
  expect(await list.locator('.card').count()).toBeLessThan(10);
  await list.evaluate(el=>{el.viewport.scrollTop=el.viewport.scrollHeight;});await expect.poll(()=>list.evaluate(el=>Math.max(...el.renderedRange))).toBe(99);
  await page.screenshot({path:info.outputPath('lazy-scroll-desktop.png'),fullPage:true});
  await list.evaluate(el=>{window.oldList=el;window.oldGl=el.renderer.gl;});await page.goto('/#buttons');expect(await page.evaluate(()=>({renderer:window.oldList.renderer,frame:window.oldList.frame,lost:window.oldGl.isContextLost()}))).toEqual({renderer:null,frame:0,lost:true});
});

test('playground original geometry/ranges, two-pointer zoom/rotation, inverse sampling, sheet and reset',async({page},info)=>{
  await page.goto('/#glass-playground');const playground=page.locator('glass-playground'),glass=playground.locator('.sample');await expect(glass).toHaveAttribute('data-renderer','webgl2');
  expect(await playground.evaluate(el=>el.glass.settings)).toMatchObject({radius:64,blur:0,refractionHeight:25.6,refractionAmount:51.2,depth:true,chromatic:false});
  await expect(playground.getByRole('slider',{name:'Blur radius'})).toHaveAttribute('max','32');
  await glass.evaluate(el=>{
    const send=(type,id,x,y)=>el.dispatchEvent(new PointerEvent(type,{pointerId:id,clientX:x,clientY:y,button:0,bubbles:true}));
    // Synthetic events cannot capture a physical pointer; mock capture for this unit gesture only.
    el.setPointerCapture=()=>{};send('pointerdown',1,100,100);send('pointerdown',2,200,100);send('pointermove',2,200,200);send('pointerup',2,200,200);send('pointerup',1,100,100);
  });const state=await playground.evaluate(el=>el.gesture.state);expect(state.scale).toBeCloseTo(Math.SQRT2);expect(state.rotation).toBeCloseTo(45);
  const basis=await glass.evaluate(el=>{el.draw();const r=el.renderer;return Array.from(r.gl.getUniform(r.program,r.uniform('uBasis')));});expect(basis[0]).toBeCloseTo(1);expect(basis[1]).toBeCloseTo(1);expect(basis[2]).toBeCloseTo(-1);
  await playground.getByRole('button',{name:'Ẩn bảng',exact:true}).click();await expect(playground.locator('.sheet')).toBeHidden();await playground.getByRole('button',{name:'Hiện bảng',exact:true}).click();
  await playground.getByRole('slider',{name:'Blur radius'}).focus();await page.keyboard.press('End');expect(await playground.evaluate(el=>el.values.blur)).toBe(32);
  await playground.getByRole('button',{name:'Reset',exact:true}).click();await expect.poll(()=>playground.evaluate(el=>el.frame)).toBe(0);expect(await playground.evaluate(el=>el.gesture.state.scale)).toBeCloseTo(1);expect(await playground.evaluate(el=>el.values.blur)).toBe(0);
  await page.screenshot({path:info.outputPath('playground-desktop.png'),fullPage:true});
});

test('ControlCenter has complete11 tiles, manual and opt-in gravity highlights, sensor cleanup',async({page})=>{
  await page.goto('/#control-center');const center=page.locator('liquid-control-center');await expect(center.locator('glass-surface.tile')).toHaveCount(11);
  await expect.poll(()=>center.evaluate(el=>el.shadowRoot.querySelector('glass-surface').frame)).toBe(0);
  const angle=page.getByRole('slider',{name:'Góc sáng Control Center'});await angle.evaluate(el=>{el.value=90;el.dispatchEvent(new Event('input',{bubbles:true}));});
  await expect.poll(()=>center.evaluate(el=>{const s=el.shadowRoot.querySelector('glass-surface');return s.renderer.gl.getUniform(s.renderer.program,s.renderer.uniform('uLightAngle'));}),{timeout:10000}).toBe(90);
  await page.getByRole('button',{name:'Bật phản chiếu theo cảm biến'}).click();
  await expect(page.getByRole('button',{name:'Tắt phản chiếu theo cảm biến'})).toBeVisible();
  await expect.poll(()=>center.evaluate(el=>el.motionEnabled)).toBe(true);
  await page.evaluate(()=>{const e=new Event('devicemotion');Object.defineProperty(e,'accelerationIncludingGravity',{value:{x:9.81,y:0,z:0}});dispatchEvent(e);});
  expect(await center.evaluate(el=>el.highlightAngle)).toBeCloseTo(45);await page.getByRole('button',{name:'Tắt phản chiếu theo cảm biến'}).click();
  await expect.poll(()=>center.evaluate(el=>el.motionEnabled)).toBe(false);
  await page.evaluate(()=>{const e=new Event('devicemotion');Object.defineProperty(e,'accelerationIncludingGravity',{value:{x:9.81,y:0,z:0}});dispatchEvent(e);});expect(await center.evaluate(el=>el.highlightAngle)).toBeCloseTo(45);
});

test('adaptive resamples unchanged canvas over time, uses tween, gestures and stops after unmount',async({page})=>{
  await page.goto('/#adaptive');const glass=page.locator('adaptive-glass');await expect(glass).toHaveAttribute('data-renderer','webgl2');
  await expect.poll(()=>glass.evaluate(el=>el.filterState?.luminance)).toBeCloseTo(180/255);
  // Redraw the same source without notifying: continuous sampler must detect it.
  await glass.evaluate(el=>{const ctx=el.backdrop.getContext('2d');ctx.fillStyle='black';ctx.fillRect(0,0,el.backdrop.width,el.backdrop.height);});
  await expect.poll(()=>glass.evaluate(el=>el.luminanceTarget)).toBe(0);
  const during=await glass.evaluate(el=>el.filterState.luminance);expect(during).toBeGreaterThan(0);
  await expect.poll(()=>glass.evaluate(el=>el.filterState.luminance)).toBeCloseTo(0);
  await glass.focus();await page.keyboard.press('+');expect(await glass.evaluate(el=>el.gesture.state.scale)).toBeCloseTo(1.1);
  await glass.evaluate(el=>{window.oldAdaptive=el;});await page.goto('/#buttons');expect(await page.evaluate(()=>({frame:window.oldAdaptive.frame,renderer:window.oldAdaptive.renderer}))).toEqual({frame:0,renderer:null});
});

test('all new routes work on mobile and CSS fallback; catalog links keep original groups',async({page},info)=>{
  test.setTimeout(60000);await page.setViewportSize({width:360,height:800});
  await page.addInitScript(()=>{const original=HTMLCanvasElement.prototype.getContext;HTMLCanvasElement.prototype.getContext=function(type,...args){return type==='webgl2'?null:original.call(this,type,...args);};});
  for(const route of ['lock-screen','magnifier','progressive-blur','scroll-container','lazy-scroll-container','glass-playground','dialog','home']){
    await page.goto(`/#${route}`);await expect(page.locator(`#resource-page`)).toBeVisible();expect(await page.evaluate(()=>document.documentElement.scrollWidth)).toBe(360);
    if(route!=='home' && route!=='dialog')await expect(page.locator('#resource-page [data-renderer=css]').first()).toBeAttached();
    if(route==='glass-playground')await page.screenshot({path:info.outputPath('playground-mobile-fallback.png'),fullPage:true});
  }
  await expect(page.locator('[data-destination]')).toHaveCount(13);await page.getByRole('button',{name:'Magnifier',exact:true}).click();await expect(page).toHaveURL(/#magnifier$/);
});
