// Isolated API fixtures: touch gestures use Chromium's native touch dispatch.
const assert = require('node:assert/strict');
const fs = require('node:fs'), path = require('node:path');
const { chromium } = require(process.env.HAU_PLAYWRIGHT_MODULE || 'playwright');
const base = process.env.HAU_TEST_WEB || 'http://localhost:5227';
const id = 'aaaaaaaa-0000-0000-0000-000000000001';
async function run(browser, width, theme, fallback = false) {
    const context = await browser.newContext({ viewport: { width, height: 844 }, hasTouch: true, isMobile: true });
    const token = [Buffer.from('{"alg":"none"}').toString('base64url'), Buffer.from(JSON.stringify({sub:id,role:'Admin',name:'Taskbar fixture',exp:Math.floor(Date.now()/1000)+3600})).toString('base64url'), 'fixture'].join('.');
    await context.addInitScript(({id, token, theme, fallback}) => {
        localStorage.setItem('auth_token', token);
        localStorage.setItem('hau.theme.account.'+id, theme);
        window.taskbarDocument = Math.random();
        if (fallback) {
            const original = HTMLCanvasElement.prototype.getContext;
            HTMLCanvasElement.prototype.getContext = function(type, ...args) { return type==='webgl2' ? null : original.call(this,type,...args); };
        }
    }, {id, token, theme, fallback});
    const user = { id, username:'taskbar-fixture', fullName:'Taskbar fixture', roles:['Admin'], role:'Admin', isActive:true, theme };
    const unexpected = [], errors = [];
    await context.route('**/api/**', async route => {
        const req = route.request(), p = new URL(req.url()).pathname;
        if(req.method()==='PUT' && p==='/api/users/me/theme') {
            user.theme=req.postDataJSON().Theme;
            return route.fulfill({json:{success:true,data:{theme:user.theme}}});
        }
        if(req.method()!=='GET') { unexpected.push(req.method()+' '+p); return route.abort(); }
        if(p==='/api/notifications/stream') return route.fulfill({contentType:'text/event-stream',body:'data: {"unreadCount":0}\n\n'});
        let data;
        if(p==='/api/users/me') data=user;
        else if(p==='/api/users') data={items:[user],totalCount:1,page:1,pageSize:20};
        else if(p==='/api/documents') data={items:[],totalCount:0,page:1,pageSize:15};
        else if(p==='/api/documents/types' || p==='/api/departments') data=[];
        else { unexpected.push(p); return route.abort(); }
        return route.fulfill({json:{success:true,data}});
    });
    const page = await context.newPage(); page.setDefaultTimeout(20000);
    page.on('pageerror', e=>errors.push(e.message));
    const bar=page.locator('flight-taskbar');
    const touch=await context.newCDPSession(page);
    try {
        await page.goto(base+'/profile',{waitUntil:'domcontentloaded'});
        await page.waitForFunction(()=>document.getElementById('profile-dark-mode')?.disabled===false && document.querySelector('flight-taskbar')?.dataset.configured==='true');
        const identity=await page.evaluate(()=>window.taskbarDocument);
        assert.equal(await page.evaluate(()=>performance.getEntriesByType('resource').filter(e=>new URL(e.name).pathname==='/js/liquid-glass.js').length),1,'Taskbar bootstrap and Blazor must share a single module instance');
        await page.waitForFunction(()=>document.querySelector('flight-taskbar').selectedIndex===4);
        async function selected(route) {
            await page.waitForURL(base+route,{waitUntil:'domcontentloaded',timeout:10000});
            await page.waitForFunction(route=>{
                const b=document.querySelector('flight-taskbar');
                return b.selectedIndex===b.getAttribute('routes').split('|').indexOf(route);
            },route);
            assert.equal(await page.evaluate(()=>window.taskbarDocument),identity,'SPA navigation must not reload');
        }
        async function swipe(target) {
            const current=await bar.evaluate(b=>b.selectedIndex);
            const a=await bar.locator(`[data-index="${current}"]`).boundingBox();
            const b=await bar.locator(`[data-index="${target}"]`).boundingBox();
            const x=a.x+a.width/2,y=a.y+a.height/2,end=b.x+b.width/2;
            await touch.send('Input.dispatchTouchEvent',{type:'touchStart',touchPoints:[{x,y}]});
            for(let i=1;i<=10;i++)await touch.send('Input.dispatchTouchEvent',{type:'touchMove',touchPoints:[{x:x+(end-x)*i/10,y}]});
            await touch.send('Input.dispatchTouchEvent',{type:'touchEnd',touchPoints:[]});
        }
        await swipe(1); await selected('/documents');
        // Simulate a Blazor avatar/icon update between pointerdown and pointerup.
        await bar.evaluate(b=>b.shadowRoot.querySelector('[data-index="2"]').addEventListener('pointerdown',()=>b.updateIcons(),{once:true}));
        await bar.getByRole('tab',{name:'Tổ chức',exact:true}).tap(); await selected('/admin/organization');
        await swipe(4); await selected('/profile');
        const toggle=page.getByRole('switch');
        await page.waitForFunction(()=>document.getElementById('profile-dark-mode')?.disabled===false);
        await toggle.tap();
        const next=theme==='dark'?'light':'dark';
        await page.waitForFunction(next=>document.documentElement.dataset.theme===next && document.querySelector('flight-taskbar').dataset.dark===String(next==='dark'),next);
        await swipe(1); await selected('/documents');
        await page.goBack({waitUntil:'domcontentloaded'}); await selected('/profile');
        await swipe(2); await selected('/admin/organization');
        assert.deepEqual(errors,[]);assert.deepEqual(unexpected,[]);
        if(process.env.HAU_TEST_ARTIFACTS){fs.mkdirSync(process.env.HAU_TEST_ARTIFACTS,{recursive:true});await page.screenshot({path:path.join(process.env.HAU_TEST_ARTIFACTS,`taskbar-${width}-${theme}-${fallback?'css':'webgl'}.png`)});}
        console.log(`PASS ${width}px ${theme} ${fallback?'CSS':'WebGL'}: native swipe, tap, theme switch, Back, SPA routes`);
    } catch(error) {
        console.error(await bar.evaluate(b=>({url:location.pathname,index:b.selectedIndex,theme:b.dataset.dark,renderer:b.dataset.renderer})).catch(()=>null));
        throw error;
    } finally {await context.close();}
}
(async()=>{
    const browser=await chromium.launch({headless:true,executablePath:process.env.HAU_TEST_CHROMIUM});
    try{
        if(process.env.HAU_TEST_SMOKE) await run(browser,390,'dark');
        else {
            for(const width of [390,300])for(const theme of ['dark','light'])await run(browser,width,theme);
            await run(browser,390,'dark',true);
        }
    }finally{await browser.close();}
})().catch(e=>{console.error(e);process.exitCode=1;});
