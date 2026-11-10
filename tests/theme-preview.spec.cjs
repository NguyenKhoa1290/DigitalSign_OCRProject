// Only theme preference writes are allowed; profile/account metadata writes are rejected.
const assert = require('node:assert/strict'), fs = require('node:fs'), path = require('node:path');
const { chromium } = require(process.env.HAU_PLAYWRIGHT_MODULE || 'playwright');
const base = process.env.HAU_TEST_WEB || 'http://localhost:5227';
const id = 'aaaaaaaa-0000-0000-0000-000000000001';
const motionPreference = process.env.HAU_TEST_MOTION === 'normal' ? 'no-preference' : 'reduce';
async function run(browser, width) {
    const context = await browser.newContext({ viewport: { width, height: 900 }, hasTouch:width===300, colorScheme: 'dark', reducedMotion: motionPreference });
    const token = [Buffer.from('{"alg":"none"}').toString('base64url'), Buffer.from(JSON.stringify({ sub: id, name: 'Theme fixture', role: 'Admin', exp: Math.floor(Date.now()/1000)+3600 })).toString('base64url'), 'fixture'].join('.');
    await context.addInitScript(t => {
        if (window !== window.top) return;
        localStorage.setItem('auth_token', t);
        document.addEventListener('DOMContentLoaded', () => { window.themeBeforeBlazor = document.documentElement.dataset.theme; }, { once: true });
    }, token);
    const user = { id, username: 'theme-fixture', fullName: 'Tài khoản thử nghiệm', email: 'theme@example.test', isActive: true, role: 'Admin', roles: ['Admin'], theme:'light' };
    const unexpected = [], errors = [];
    await context.route('**/api/**', async route => {
        const req = route.request(), p = new URL(req.url()).pathname;
        if (req.method()==='PUT' && p==='/api/users/me/theme') {
            const body=req.postDataJSON(); assert(['light','dark'].includes(body.Theme));
            user.theme=body.Theme;return route.fulfill({json:{success:true,data:{theme:user.theme}}});
        }
        if (req.method() !== 'GET') { unexpected.push(req.method()+' '+p); return route.abort(); }
        if (p === '/api/notifications/stream') return route.fulfill({ contentType:'text/event-stream', body:'data: {"unreadCount":0}\n\n' });
        if (p === '/api/users/me') return route.fulfill({ json:{ success:true, data:user } });
        if (p === '/api/users') return route.fulfill({ json:{ success:true, data:{ items:[user], totalCount:1, page:1, pageSize:20 } } });
        if (p === '/api/departments') return route.fulfill({ json:{ success:true, data:[] } });
        unexpected.push(p); return route.abort();
    });
    const page = await context.newPage(); page.setDefaultTimeout(45000); page.on('pageerror', e=>errors.push(e.message));
    const shot = async name => {
        if (!process.env.HAU_TEST_ARTIFACTS) return;
        fs.mkdirSync(process.env.HAU_TEST_ARTIFACTS, { recursive:true });
        await page.screenshot({ path:path.join(process.env.HAU_TEST_ARTIFACTS, `${name}-${width}.png`), animations:'disabled' });
    };
    const assertDark = async selector => {
        await page.waitForFunction(selector => {
            const e = document.querySelector(selector); if (!e) return false;
            const css = getComputedStyle(e), rgb = value => value.match(/[\d.]+/g).slice(0,3).map(Number);
            return Math.max(...rgb(css.backgroundColor)) < 120 && Math.min(...rgb(css.color)) > 140;
        }, selector);
        const style = await page.locator(selector).first().evaluate(e => ({ bg:getComputedStyle(e).backgroundColor, fg:getComputedStyle(e).color }));
        const channels = css => css.match(/[\d.]+/g).slice(0,3).map(Number);
        assert(Math.max(...channels(style.bg)) < 120, `${selector} should have a dark surface: ${style.bg}`);
        assert(Math.min(...channels(style.fg)) > 140, `${selector} should have readable light text: ${style.fg}`);
    };
    try {
        await page.goto(base+'/profile', { waitUntil:'domcontentloaded' });
        const toggle = page.getByRole('switch', { name:/Chế độ tối/ });
        await page.waitForFunction(()=>document.getElementById('profile-dark-mode') && !document.getElementById('profile-dark-mode').disabled);
        assert.equal(await toggle.getAttribute('aria-checked'), 'false'); // OS dark does not opt a user into this experiment.
        assert.equal(await page.evaluate(()=>document.documentElement.dataset.theme), 'light');
        const light = await page.locator('.profile-card').evaluate(e=>getComputedStyle(e).backgroundColor);
        await page.locator('#profile-full-name').fill('Nội dung chưa lưu');
        await toggle.focus(); await toggle.press('Enter');
        await page.waitForFunction(()=>document.documentElement.dataset.theme==='dark');
        assert.equal(await toggle.getAttribute('aria-checked'), 'true');
        await page.waitForFunction(()=>getComputedStyle(document.getElementById('profile-dark-mode')).outlineStyle==='solid');
        assert.equal(await toggle.evaluate(e=>getComputedStyle(e).outlineStyle), 'solid');
        assert.equal(await page.locator('#profile-full-name').inputValue(), 'Nội dung chưa lưu');
        await assertDark('.profile-card'); await assertDark('#profile-full-name');
        const optics = await toggle.locator('glass-surface.theme-switch-thumb').evaluate(e=>({width:parseFloat(getComputedStyle(e).width),height:parseFloat(getComputedStyle(e).height)}));
        assert.deepEqual(optics,{width:36,height:24});
        const box = await toggle.boundingBox(), y = box.y+box.height/2;
        const drag = async (start,end) => {
            await page.mouse.move(box.x+start,y); await page.mouse.down();
            await page.mouse.move(box.x+end,y,{steps:6}); await page.mouse.up();
        };
        await drag(38,14);
        await page.waitForFunction(()=>document.getElementById('profile-dark-mode').getAttribute('aria-checked')==='false');
        await drag(18,42);
        await page.waitForFunction(()=>document.getElementById('profile-dark-mode').getAttribute('aria-checked')==='true');
        await page.mouse.move(box.x+38,y); await page.mouse.down();
        await page.waitForFunction(()=>document.querySelector('.theme-switch-thumb')?.settings.refractionAmount>0);
        if (motionPreference === 'no-preference') await shot('profile-toggle-pressed');
        await page.mouse.move(box.x+14,y,{steps:6});
        await toggle.dispatchEvent('pointercancel',{pointerId:1,isPrimary:true,pointerType:'mouse',bubbles:true});
        await page.mouse.up();
        assert.equal(await toggle.getAttribute('aria-checked'),'true');
        assert.equal(await page.evaluate(()=>document.documentElement.dataset.theme),'dark');
        assert.equal(await toggle.evaluate(e=>getComputedStyle(e).outlineStyle), 'none');
        if(width===300){
            await toggle.tap();await page.waitForFunction(()=>document.getElementById('profile-dark-mode').getAttribute('aria-checked')==='false');
            await toggle.tap();await page.waitForFunction(()=>document.getElementById('profile-dark-mode').getAttribute('aria-checked')==='true');
            assert.equal(await toggle.evaluate(e=>getComputedStyle(e).outlineStyle), 'none');
        }
        if (width === 300) await page.waitForFunction(()=>document.querySelector('flight-taskbar')?.dataset.dark==='true');
        await shot('profile-dark');
        await toggle.press('Tab'); await page.keyboard.press('Shift+Tab');
        assert(await toggle.evaluate(e=>document.activeElement===e));
        assert.equal(await toggle.evaluate(e=>getComputedStyle(e).outlineStyle), 'solid');
        await page.evaluate(()=>Blazor.navigateTo('/admin/organization'));
        await page.getByRole('heading', { name:'Quản lý Người dùng', exact:true }).waitFor();
        await assertDark(width===300 ? '.user-mobile-card' : '.table-wrapper');
        await page.getByRole('button', { name:'Thêm người dùng', exact:true }).click();
        await page.locator('.modal').waitFor(); await assertDark('.modal'); await assertDark('.modal input');
        await shot('organization-dark-modal');
        await page.getByRole('button', { name:'Đóng', exact:true }).click();
        await page.evaluate(()=>Blazor.navigateTo('/profile'));
        await page.waitForFunction(()=>document.getElementById('profile-dark-mode')?.getAttribute('aria-checked')==='true');
        await page.reload({ waitUntil:'domcontentloaded' });
        await page.waitForFunction(()=>document.getElementById('profile-dark-mode')?.getAttribute('aria-checked')==='true');
        assert.equal(await page.evaluate(()=>window.themeBeforeBlazor), 'dark');
        assert.equal(await page.evaluate(id=>localStorage.getItem('hau.theme.account.'+id), id), 'dark');
        await toggle.focus(); await toggle.press('Space');
        await page.waitForFunction(()=>document.documentElement.dataset.theme==='light');
        assert.equal(await toggle.getAttribute('aria-checked'), 'false');
        await page.waitForFunction(light=>getComputedStyle(document.querySelector('.profile-card')).backgroundColor===light, light);
        assert.equal(await page.locator('.profile-card').evaluate(e=>getComputedStyle(e).backgroundColor), light);
        if (width === 300) await page.waitForFunction(()=>document.querySelector('flight-taskbar')?.dataset.dark==='false');
        await shot('profile-light-restored');
        assert(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth+1));
        assert.deepEqual(unexpected, []); assert.deepEqual(errors, []);
        console.log(`PASS ${width}px ${motionPreference}: glass switch, pointer/keyboard focus, theme-only account writes, persistence, restored light`);
    } catch(e) { console.error(JSON.stringify({ width, errors, unexpected, url:page.url() })); throw e; }
    finally { await context.close(); }
}
(async()=>{ const browser = await chromium.launch({ headless:true, executablePath:process.env.HAU_TEST_CHROMIUM });
    try { for(const width of [1366,300]) await run(browser,width); } finally { await browser.close(); }
})().catch(e=>{console.error(e);process.exitCode=1;});
