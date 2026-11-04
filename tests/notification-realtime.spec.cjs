// Mock streaming fetch and fixture APIs: no writes to application users/documents.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { chromium } = require(process.env.HAU_PLAYWRIGHT_MODULE || 'playwright');
async function run(browser,width) {
  const context=await browser.newContext({viewport:{width,height:850}});
  const token=[Buffer.from('{"alg":"none"}').toString('base64url'),Buffer.from(JSON.stringify({sub:'bbbbbbbb-0000-0000-0000-000000000001',name:'Notification fixture',role:'Admin',exp:Math.floor(Date.now()/1000)+3600})).toString('base64url'),'fixture'].join('.');
  await context.addInitScript(token=>{
    if(window!==window.top)return;
    localStorage.setItem('auth_token',token);
    const nativeFetch=window.fetch;
    window.streamCount=0;window.fixtureUnread=0;window.streamRequests=0;
    window.emitUnread=count=>{window.fixtureUnread=count;window.fixtureStream?.enqueue(new TextEncoder().encode(`data: ${JSON.stringify({unreadCount:count})}\n\n`));};
    window.fetch=(input,init)=>{
      const url=new URL(typeof input==='string'?input:input.url,location.href);
      if(url.pathname!=='/api/notifications/stream')return nativeFetch(input,init);
      window.streamRequests++;
      window.fixtureHasBearer=new Headers(init?.headers).get('Authorization')?.startsWith('Bearer ');
      const body=new ReadableStream({start(controller){window.fixtureStream=controller;window.emitUnread(window.fixtureUnread);},cancel(){window.fixtureStream=null;}});
      init?.signal?.addEventListener('abort',()=>{try{window.fixtureStream?.close();}catch{}window.fixtureStream=null;},{once:true});
      return Promise.resolve(new Response(body,{headers:{'Content-Type':'text/event-stream'}}));
    };
  },token);
  let unread=0;const unexpected=[],errors=[];
  const publish=async count=>{unread=count;await Promise.all(context.pages().map(p=>p.evaluate(n=>window.emitUnread(n),count)));};
  await context.route('**/api/**',async route=>{
    const req=route.request(),url=new URL(req.url());let data;
    if(req.method()==='GET'&&url.pathname==='/api/documents/types')data=[];
    else if(req.method()==='GET'&&url.pathname==='/api/documents')data={items:[],totalCount:0,page:1,pageSize:15};
    else if(req.method()==='GET'&&url.pathname==='/api/notifications')data={items:Array.from({length:Math.min(unread,3)},(_,i)=>({id:`bbbbbbbb-0000-0000-0000-00000000000${i+2}`,kind:'Assignment',message:`Thông báo fixture ${i+1}`,targetUrl:'/documents',timestamp:'2026-10-24T10:00:00Z',readAt:null})),totalCount:unread,unreadCount:unread,page:1,pageSize:20};
    else if(req.method()==='PATCH'&&url.pathname.endsWith('/read')){await publish(unread-1);data=true;}
    else if(req.method()==='POST'&&url.pathname==='/api/notifications/read-all'){await publish(0);data=true;}
    else{unexpected.push(req.method()+' '+url.pathname);return route.abort();}
    return route.fulfill({json:{success:true,data}});
  });
  const first=await context.newPage(),second=await context.newPage();
  for(const page of [first,second]){page.setDefaultTimeout(15000);page.on('pageerror',e=>errors.push(e.message));}
  try{
    await first.goto('http://localhost:5227/documents');await second.goto('http://localhost:5227/documents');
    for(const page of [first,second])await page.waitForFunction(()=>window.fixtureStream&&window.fixtureHasBearer);
    assert.equal(await first.locator('.notification-count').count(),0);
    await publish(3);
    for(const page of [first,second])await page.locator('.notification-count').filter({hasText:'3'}).waitFor();
    await first.getByRole('link',{name:'Thông báo: 3 chưa đọc',exact:true}).click();
    await first.getByRole('heading',{name:'Trung tâm thông báo',exact:true}).waitFor();
    await first.getByRole('button',{name:'Đánh dấu đã đọc',exact:true}).first().click();
    for(const page of [first,second])await page.locator('.notification-count').filter({hasText:'2'}).waitFor();
    await first.getByRole('button',{name:'Đánh dấu tất cả đã đọc',exact:true}).click();
    for(const page of [first,second])await page.locator('.notification-count').waitFor({state:'hidden'});
    unread=7;await second.evaluate(()=>{window.fixtureUnread=7;window.fixtureStream.close();window.fixtureStream=null;});
    await second.locator('.notification-count').filter({hasText:'7'}).waitFor();
    assert(await second.evaluate(()=>window.streamRequests>=2));
    await publish(120);await second.locator('.notification-count').filter({hasText:'99+'}).waitFor();
    await publish(1);await first.getByRole('button',{name:'Xem chi tiết',exact:true}).first().click();
    await first.waitForURL('**/documents');
    await second.locator('.notification-count').waitFor({state:'hidden'});
    if(process.env.HAU_TEST_ARTIFACTS){await publish(3);await first.locator('.notification-count').waitFor();fs.mkdirSync(process.env.HAU_TEST_ARTIFACTS,{recursive:true});await first.screenshot({path:path.join(process.env.HAU_TEST_ARTIFACTS,`badge-${width}.png`),animations:'disabled'});}
    assert.deepEqual(unexpected,[]);assert.deepEqual(errors,[]);
    console.log(`PASS ${width}px streaming badge, cross-tab read/all, details marks read, reconnect, 99+ and navigation`);
  }finally{await context.close();}
}
(async()=>{const browser=await chromium.launch({headless:true,executablePath:process.env.HAU_TEST_CHROMIUM});try{for(const width of [1366,300])await run(browser,width);}finally{await browser.close();}})().catch(e=>{console.error(e);process.exitCode=1;});
