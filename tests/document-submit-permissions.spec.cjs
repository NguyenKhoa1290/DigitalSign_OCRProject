// UI fixtures only: no document creation/submission against the application API.
const assert=require('node:assert/strict');
const {chromium}=require(process.env.HAU_PLAYWRIGHT_MODULE||'playwright');
const base=process.env.HAU_TEST_WEB||'https://hauquanlycongvan.com';
const actor='aaaaaaaa-0000-0000-0000-000000000001';
async function run(browser,role,width){
 const context=await browser.newContext({viewport:{width,height:850}});
 const token=[Buffer.from('{"alg":"none"}').toString('base64url'),Buffer.from(JSON.stringify({sub:actor,name:role,role,exp:Math.floor(Date.now()/1000)+3600})).toString('base64url'),'fixture'].join('.');
 await context.addInitScript(t=>{if(window===window.top)localStorage.setItem('auth_token',t);},token);
 const docs=[{id:'bbbbbbbb-0000-0000-0000-000000000001',title:'Outgoing own',docTypeName:'CongVanDi',createdById:actor},
  {id:'bbbbbbbb-0000-0000-0000-000000000002',title:'Outgoing other',docTypeName:'CongVanDi',createdById:'aaaaaaaa-0000-0000-0000-000000000002'},
  {id:'bbbbbbbb-0000-0000-0000-000000000003',title:'Incoming',docTypeName:'CongVanDen',createdById:actor},
  {id:'bbbbbbbb-0000-0000-0000-000000000004',title:'Returned to author',docTypeName:'CongVanDi',createdById:actor,status:'Rejected'},
  {id:'bbbbbbbb-0000-0000-0000-000000000005',title:'Published',docTypeName:'CongVanDi',createdById:actor,status:'Published'},
  {id:'bbbbbbbb-0000-0000-0000-000000000006',title:'In review',docTypeName:'CongVanDi',createdById:actor,status:'PendingDeptReview'},
  {id:'bbbbbbbb-0000-0000-0000-000000000007',title:'Completed Word',docTypeName:'CongVanDi',createdById:actor,word:true},
  {id:'bbbbbbbb-0000-0000-0000-000000000008',title:'Incoming returned',docTypeName:'CongVanDen',createdById:actor,status:'Rejected'}];
 const errors=[],unexpected=[];
 await context.route('**/api/**',async route=>{
  const req=route.request(),p=new URL(req.url()).pathname;
  if(p==='/api/notifications/stream')return route.fulfill({contentType:'text/event-stream',body:'data: {"unreadCount":0}\n\n'});
  const doc=docs.find(d=>p==='/api/documents/'+d.id);
  if(doc&&req.method()==='GET')return route.fulfill({json:{success:true,data:{...doc,status:doc.status||'Draft',concurrencyVersion:'cccccccc-0000-0000-0000-000000000001',minioPath:doc.word?'documents/ready.docx':'',fileName:doc.word?'ready.docx':null,fileContentType:doc.word?'application/vnd.openxmlformats-officedocument.wordprocessingml.document':null,processes:[]}}});
  if(p.endsWith('/file'))return route.fulfill({body:Buffer.from('word-fixture')});
  if(p.endsWith('/ocr-jobs/latest'))return route.fulfill({json:{success:true,data:null}});
  if(p.startsWith('/api/users/'))return route.fulfill({json:{success:true,data:{id:actor,departmentId:null}}});
  unexpected.push(req.method()+' '+p);return route.abort();
 });
 const page=await context.newPage();page.setDefaultTimeout(60000);page.on('pageerror',e=>errors.push(e.message));
 try{
  for(const [index,doc] of docs.entries()){
   if(index===0)await page.goto(base+'/documents/'+doc.id,{waitUntil:'domcontentloaded'});
   else await page.evaluate(url=>Blazor.navigateTo(url),'/documents/'+doc.id);
   await page.getByRole('heading',{name:doc.title,exact:true}).waitFor();
   const mutable=!doc.status||doc.status==='Draft'||doc.status==='Rejected';
   const canSubmit=mutable&&doc.docTypeName!=='CongVanDen'&&(role==='Admin'||(role==='Specialist'&&doc.createdById===actor));
   assert.equal(await page.getByRole('button',{name:'Nộp trình duyệt',exact:true}).count(),canSubmit?1:0);
   const incoming=doc.docTypeName==='CongVanDen';
   const canEdit=(incoming?(!doc.status||doc.status==='Draft'):mutable)&&(role==='Admin'||(incoming?role==='Clerk':role==='Specialist'&&doc.createdById===actor));
   assert.equal(await page.getByRole('button',{name:'Sửa thông tin',exact:true}).count(),canEdit?1:0);
   assert.equal(await page.getByRole('button',{name:'Tải lên file',exact:true}).count(),canEdit?1:0);
   assert.equal(await page.getByRole('button',{name:'Chạy OCR',exact:true}).count(),!doc.word&&(role==='Admin'||role==='Clerk'&&mutable)?1:0);
   assert.equal(await page.getByRole('button',{name:'Trình BGH chỉ đạo',exact:true}).count(),doc.docTypeName==='CongVanDen'&&(!doc.status||doc.status==='Draft')&&role==='Clerk'?1:0);
   if(doc.word)assert.equal(await page.locator('.gemini-result-card').count(),0);
  }
  assert.deepEqual(errors,[]);assert.deepEqual(unexpected,[]);
  console.log(`PASS ${role} ${width}px: outgoing submit role/owner and separate incoming action`);
 }finally{await context.close();}
}
(async()=>{const browser=await chromium.launch({headless:true,executablePath:process.env.HAU_TEST_CHROMIUM});try{for(const role of ['Clerk','Specialist','Admin','Manager','BoardOfDirectors'])await run(browser,role,1366);await run(browser,'Clerk',300);await run(browser,'Specialist',300);}finally{await browser.close();}})().catch(e=>{console.error(e);process.exitCode=1;});
