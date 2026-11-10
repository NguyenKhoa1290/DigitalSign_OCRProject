// Shared fixture server state, independent browser contexts. No application users are changed.
const assert=require('node:assert/strict');
const {chromium}=require(process.env.HAU_PLAYWRIGHT_MODULE||'playwright');
const base=process.env.HAU_TEST_WEB||'http://localhost:5227';
const accounts={alpha:{id:'aaaaaaaa-0000-0000-0000-000000000001',theme:'light'},beta:{id:'bbbbbbbb-0000-0000-0000-000000000002',theme:'light'}};
const unexpected=[],errors=[];let failSave=false,saves=0;
const tokenFor=user=>[Buffer.from('{"alg":"none"}').toString('base64url'),Buffer.from(JSON.stringify({sub:user.id,name:user.username,role:'Specialist',exp:Math.floor(Date.now()/1000)+3600})).toString('base64url'),'fixture'].join('.');
async function context(browser){
 const ctx=await browser.newContext({viewport:{width:390,height:844},reducedMotion:'reduce'});
 await ctx.route('**/api/**',async route=>{
  const r=route.request(),p=new URL(r.url()).pathname;
  if(p==='/api/auth/login'&&r.method()==='POST'){
   const username=r.postDataJSON().username,account=accounts[username];assert(account);
   return route.fulfill({json:{accessToken:tokenFor({...account,username}),refreshToken:'fixture',userId:account.id,username,fullName:username,roles:['Specialist'],theme:account.theme,mustChangePassword:false}});
  }
  if(p==='/api/auth/logout'&&r.method()==='POST')return route.fulfill({json:{success:true}});
  if(p==='/api/notifications/stream')return route.fulfill({contentType:'text/event-stream',body:'data: {"unreadCount":0}\n\n'});
  let sub;try{sub=JSON.parse(Buffer.from(r.headers().authorization.split('.')[1],'base64url')).sub;}catch{}
  const account=Object.values(accounts).find(a=>a.id===sub);
  if(p==='/api/users/me'&&r.method()==='GET'){
   assert(account);return route.fulfill({json:{success:true,data:{...account,username:account===accounts.alpha?'alpha':'beta',fullName:'Theme account',isActive:true,roles:['Specialist']}}});
  }
  if(p==='/api/users/me/theme'&&r.method()==='PUT'){
   assert(account);const body=r.postDataJSON();assert.deepEqual(Object.keys(body),['Theme']);assert(['light','dark'].includes(body.Theme));
   if(failSave){failSave=false;return route.fulfill({status:503,json:{success:false,message:'Save unavailable fixture'}});}
   saves++;account.theme=body.Theme;return route.fulfill({json:{success:true,data:{theme:account.theme}}});
  }
  if(p==='/api/documents/stats'&&r.method()==='GET')return route.fulfill({json:{success:true,data:{totalDocuments:0,todayDocuments:0}}});
  unexpected.push(r.method()+' '+p);return route.abort();
 });return ctx;
}
async function pageFor(ctx){const p=await ctx.newPage();p.setDefaultTimeout(45000);p.on('pageerror',e=>errors.push(e.message));return p;}
async function login(p,username){
 console.log('Checking login',username);
 await p.goto(base+'/login',{waitUntil:'domcontentloaded'});await p.locator('#username').fill(username);await p.locator('#password').fill('fixture-password');
 await p.locator('button[type=submit]').click();await p.waitForURL(base+'/',{waitUntil:'domcontentloaded'});
 console.log('Logged in',username);
}
async function profile(p){await p.evaluate(()=>Blazor.navigateTo('/profile'));await p.waitForFunction(()=>document.getElementById('profile-dark-mode')&&!document.getElementById('profile-dark-mode').disabled);return p.getByRole('switch',{name:/Chế độ tối/});}
async function logout(p){
 await p.getByRole('button',{name:'Đăng xuất',exact:true}).click();
 await p.waitForURL(base+'/login',{waitUntil:'domcontentloaded'});await p.locator('#username').waitFor();
 await p.waitForFunction(()=>!localStorage.getItem('auth_token')&&document.documentElement.dataset.theme==='light');
}
(async()=>{const browser=await chromium.launch({headless:true,executablePath:process.env.HAU_TEST_CHROMIUM});let first,second;try{
 first=await context(browser);const a=await pageFor(first);await login(a,'alpha');let toggle=await profile(a);console.log('First profile loaded');
 await toggle.click();await a.waitForFunction(()=>document.documentElement.dataset.theme==='dark');assert.equal(accounts.alpha.theme,'dark');
 second=await context(browser);const b=await pageFor(second);await login(b,'alpha');
 assert.equal(await b.evaluate(()=>document.documentElement.dataset.theme),'dark');toggle=await profile(b);assert.equal(await toggle.getAttribute('aria-checked'),'true');
 await logout(b);assert.equal(await b.evaluate(()=>document.documentElement.dataset.theme),'light');
 await login(b,'beta');assert.equal(await b.evaluate(()=>document.documentElement.dataset.theme),'light');toggle=await profile(b);assert.equal(await toggle.getAttribute('aria-checked'),'false');
 failSave=true;await toggle.click();await b.getByText('Không lưu được giao diện. Hãy thử lại.',{exact:true}).waitFor();
 assert.equal(accounts.beta.theme,'light');assert.equal(await toggle.getAttribute('aria-checked'),'false');assert.equal(await b.evaluate(()=>document.documentElement.dataset.theme),'light');
 await toggle.click();await b.waitForFunction(()=>document.documentElement.dataset.theme==='dark');assert.equal(accounts.beta.theme,'dark');assert.equal(saves,2);
 await logout(b);await login(b,'alpha');assert.equal(await b.evaluate(()=>document.documentElement.dataset.theme),'dark');
 assert.deepEqual(unexpected,[]);assert.deepEqual(errors,[]);
 console.log('PASS account theme: independent browser login, persisted preference, logout/reset, account isolation, failed save retains previous theme, retry succeeds');
}catch(e){console.error(JSON.stringify({errors,unexpected}));throw e;}finally{await browser.close();}})().catch(e=>{console.error(e);process.exitCode=1});
