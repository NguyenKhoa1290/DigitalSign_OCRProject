// Read-only fixture APIs; no application documents are created.
const assert = require('node:assert/strict');
const { chromium } = require(process.env.HAU_PLAYWRIGHT_MODULE || 'playwright');
async function run(browser,role,width) {
  const context=await browser.newContext({viewport:{width,height:850}});
  const token=[Buffer.from('{"alg":"none"}').toString('base64url'),Buffer.from(JSON.stringify({sub:'bbbbbbbb-0000-0000-0000-000000000001',name:role,role,exp:Math.floor(Date.now()/1000)+3600})).toString('base64url'),'fixture'].join('.');
  await context.addInitScript(value=>{if(window===window.top)localStorage.setItem('auth_token',value);},token);
  const errors=[],unexpected=[];
  await context.route('**/api/**',async route=>{
    if (new URL(route.request().url()).pathname === "/api/notifications/stream") return route.fulfill({ contentType: "text/event-stream", body: 'data: {"unreadCount":0}\n\n' });
    const req=route.request(),url=new URL(req.url());let data;
    if(req.method()==='GET'&&url.pathname==='/api/documents/types')data=[{id:'aaaaaaaa-0000-0000-0000-000000000001',typeName:'CongVanDen'},{id:'aaaaaaaa-0000-0000-0000-000000000002',typeName:'CongVanDi'}];
    else if(req.method()==='GET'&&url.pathname==='/api/documents')data={items:[],totalCount:0,page:1,pageSize:15};
    else{unexpected.push(req.method()+' '+url.pathname);return route.abort();}
    return route.fulfill({json:{success:true,data}});
  });
  const page=await context.newPage();page.setDefaultTimeout(60000);page.on('pageerror',e=>errors.push(e.message));
  try{
    await page.goto((process.env.HAU_TEST_WEB||'http://localhost:5227')+'/documents',{waitUntil:'domcontentloaded'});
    const canCreate=['Admin','Clerk','Specialist'].includes(role);
    await page.getByRole('heading',{name:'Danh sách Công văn',exact:true}).waitFor();
    assert.equal(await page.getByRole('link',{name:'Tạo mới',exact:true}).count(),canCreate?1:0);
    if(canCreate){
      await page.getByRole('link',{name:'Tạo mới',exact:true}).click();
      await page.getByRole('heading',{name:'Tạo Công văn mới',exact:true}).waitFor();
      await page.locator('#document-type-select').click();
      assert.equal(await page.getByRole('option',{name:'Công văn đến',exact:true}).count(),role==='Specialist'?0:1);
      assert.equal(await page.getByRole('option',{name:'Công văn đi',exact:true}).count(),role==='Clerk'?0:1);
    }
    assert.deepEqual(errors,[]);assert.deepEqual(unexpected,[]);console.log(`PASS ${role} ${width}px create permissions and document types`);
  }finally{await context.close();}
}
(async()=>{const browser=await chromium.launch({headless:true,executablePath:process.env.HAU_TEST_CHROMIUM});try{for(const role of ['Admin','Clerk','Specialist','Manager','BoardOfDirectors'])await run(browser,role,1366);for(const role of ['Admin','Clerk','Specialist'])await run(browser,role,300);}finally{await browser.close();}})().catch(e=>{console.error(e);process.exitCode=1;});
