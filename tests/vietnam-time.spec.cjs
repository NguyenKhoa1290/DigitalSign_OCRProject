// Fixture timestamps only; no database changes, login, signing or OCR calls.
const assert=require('node:assert/strict'),fs=require('node:fs'),path=require('node:path');
const {chromium}=require(process.env.HAU_PLAYWRIGHT_MODULE||'playwright');
const base=process.env.HAU_TEST_WEB||'https://hauquanlycongvan.com';
const id='bbbbbbbb-0000-0000-0000-000000000001',stamp='2026-10-24T18:30:00Z';
async function run(browser,zone,width){
 const context=await browser.newContext({viewport:{width,height:850},timezoneId:zone});
 const token=[Buffer.from('{"alg":"none"}').toString('base64url'),Buffer.from(JSON.stringify({sub:id,name:'Time fixture',role:'Admin',exp:Math.floor(Date.now()/1000)+3600})).toString('base64url'),'fixture'].join('.');
 await context.addInitScript(t=>{if(window===window.top)localStorage.setItem('auth_token',t);},token);
 const errors=[],unexpected=[];
 const doc={id,title:'Time fixture',docTypeName:'CongVanDi',status:'Draft',createdAt:stamp,issuedDate:'2026-10-24',minioPath:'',
  signatures:[{id,docId:id,signerId:id,signerName:'Time fixture',signatureType:'Personal',signedAt:'2026-10-25T01:30:00+07:00',isValid:true}],
  processes:[{id,action:'Create',timestamp:'2026-10-24T18:30:00',comment:'Time fixture'}],
  ocrDataRaw:JSON.stringify({provider:'gemini',modelId:'gemini-fixture',selectedPages:[1],createdAt:'2026-10-25T01:30:00+07:00',layout:{blocks:[{type:'paragraph',text:'Time fixture result'}]}})};
 await context.route('**/api/**',route=>{
  const p=new URL(route.request().url()).pathname;
  if(p==='/api/notifications/stream')return route.fulfill({contentType:'text/event-stream',body:'data: {"unreadCount":1}\n\n'});
  if(p.endsWith('/ocr-jobs/latest'))return route.fulfill({json:{success:true,data:null}});
  if(p==='/api/documents/'+id)return route.fulfill({json:{success:true,data:doc}});
  if(p==='/api/notifications')return route.fulfill({json:{items:[{id,kind:'DocumentCreated',message:'Time fixture notification',timestamp:stamp,targetUrl:'/documents/'+id}],totalCount:1,page:1,pageSize:20,unreadCount:1}});
  if(p==='/api/admin/system-logs')return route.fulfill({json:{items:[{id,service:'DocumentService',level:'Information',action:'Create',timestamp:'2026-10-25T01:30:00+07:00',actorName:'Time fixture',method:'',path:'',traceId:'fixture'}],totalCount:1,page:1,pageSize:20}});
  unexpected.push(p);return route.abort();
 });
 const page=await context.newPage();page.setDefaultTimeout(60000);page.on('pageerror',e=>errors.push(e.message));
 try{
  await page.goto(base+'/documents/'+id);await page.getByRole('heading',{name:'Time fixture'}).waitFor();
  const created=page.locator('.detail-item').filter({has:page.getByText('Ngày tạo',{exact:true})});
  assert.equal(await created.locator('.detail-value').innerText(),'25/10/2026 01:30');
  assert.equal(await page.locator('.timeline-time').innerText(),'25/10/2026 01:30');
  assert((await page.locator('.sig-meta').innerText()).includes('25/10/2026 01:30'));
  assert((await page.locator('.gemini-result-card').innerText()).includes('25/10/2026 01:30'));
  const issued=page.locator('.detail-item').filter({has:page.getByText('Ngày ban hành',{exact:true})});
  assert.equal(await issued.locator('.detail-value').innerText(),'24/10/2026','DateOnly must remain the entered business date');
  if(process.env.HAU_TEST_ARTIFACTS){fs.mkdirSync(process.env.HAU_TEST_ARTIFACTS,{recursive:true});await page.locator('.timeline-time').scrollIntoViewIfNeeded();await page.screenshot({path:path.join(process.env.HAU_TEST_ARTIFACTS,`time-${zone.replaceAll('/','-')}-${width}.png`),animations:'disabled'});}
  await page.goto(base+'/notifications');await page.locator('.notification-time').waitFor();
  assert.equal(await page.locator('.notification-time').innerText(),'25/10/2026 01:30:00 (Việt Nam)');
  await page.goto(base+'/admin/system-logs');await page.locator('.journal-entry').waitFor();
  assert((await page.locator('.journal-entry').innerText()).includes('25/10/2026 01:30:00'));
  assert.deepEqual(errors,[]);assert.deepEqual(unexpected,[]);console.log(`PASS ${zone} ${width}px: creation/history/signature/Gemini/notification/journal agree in UTC+7; date boundary and DateOnly preserved`);
 }catch(error){console.error(JSON.stringify({zone,width,url:page.url(),errors,unexpected,body:(await page.locator('body').innerText()).slice(-1000)}));throw error;}
 finally{await context.close();}
}
(async()=>{const browser=await chromium.launch({headless:true,executablePath:process.env.HAU_TEST_CHROMIUM});try{for(const zone of ['UTC','America/Los_Angeles'])for(const width of [1366,300])await run(browser,zone,width);}finally{await browser.close();}})().catch(e=>{console.error(e);process.exitCode=1;});
