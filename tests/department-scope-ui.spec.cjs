// Isolated UI fixtures; no changes to real documents, signatures, or accounts.
const assert=require('node:assert/strict'),fs=require('node:fs'),path=require('node:path');
const {chromium}=require(process.env.HAU_PLAYWRIGHT_MODULE||'playwright');
const base=process.env.HAU_TEST_WEB||'http://localhost:5227';
const actor='aaaaaaaa-0000-0000-0000-000000000001';
async function check(browser,width){
 const context=await browser.newContext({viewport:{width,height:850},hasTouch:width===300});
 const token=[Buffer.from('{"alg":"none"}').toString('base64url'),Buffer.from(JSON.stringify({sub:actor,role:'Manager',name:'Trưởng phòng',exp:Math.floor(Date.now()/1000)+3600})).toString('base64url'),'fixture'].join('.');
 await context.addInitScript(token=>{localStorage.setItem('auth_token',token);window.initialLoad=Math.random();},token);
 const docs=[{id:'bbbbbbbb-0000-0000-0000-000000000001',title:'Hồ sơ phòng A',docTypeName:'CongVanDi',status:'PendingDeptReview',departmentName:'Phòng A',canReview:true},
 {id:'bbbbbbbb-0000-0000-0000-000000000002',title:'Hồ sơ chung',docTypeName:'CongVanDi',status:'PendingDeptReview',departmentName:'Trường',isSchoolWide:true,canReview:false},
 {id:'bbbbbbbb-0000-0000-0000-000000000003',title:'Hồ sơ đã ký',docTypeName:'CongVanDi',status:'DeptSigned',departmentName:'Phòng A',canReview:true,mySignedAt:'2026-11-05T02:00:00Z'}];
 const unexpected=[],errors=[];let pendingCount=0,signedCount=0,signCount=0,finishCount=0;
 await context.route('**/api/**',async route=>{
  const req=route.request(),url=new URL(req.url()),p=url.pathname;
  if(req.method()==='POST' && p==='/api/signatures/personal-sign'){
   signCount++;return route.fulfill({json:{success:true,data:{signatureId:actor,docId:docs[0].id,signerId:actor,signatureType:'PersonalSignature'}}});
  }
  if(req.method()==='POST' && p===`/api/documents/${docs[0].id}/dept-sign`){
   finishCount++;if(finishCount===1)return route.fulfill({status:409,json:{success:false,message:'fixture conflict'}});
   docs[0].status='DeptSigned';return route.fulfill({json:{success:true,data:docs[0]}});
  }
  if(req.method()!=='GET'){unexpected.push(req.method()+' '+p);return route.abort();}
  if(p==='/api/notifications/stream')return route.fulfill({contentType:'text/event-stream',body:'data: {"unreadCount":0}\n\n'});
  let data;
  if(p==='/api/documents/types')data=[];
  else if(p==='/api/documents/signing'){
   const signed=url.searchParams.get('view')==='signed';if(signed)signedCount++;else pendingCount++;
   data={items:[signed?docs[2]:docs[0]],totalCount:1,page:1,pageSize:15};
  }else if(p==='/api/documents'){
   const scope=url.searchParams.get('scope');const items=scope==='school'?[docs[1]]:scope==='department'?[docs[0],docs[2]]:docs;
   data={items,totalCount:items.length,page:1,pageSize:15};
  }else if(p.startsWith('/api/documents/') && p.endsWith('/file'))return route.fulfill({contentType:'application/pdf',body:'%PDF-1.4\n%%EOF'});
  else if(p.startsWith('/api/documents/') && p.endsWith('/ocr-jobs/latest')) data=null;
  else if(p.startsWith('/api/signatures/document/'))data=signCount?[{id:actor,docId:docs[0].id,signerId:actor,signatureType:'PersonalSignature',signedAt:'2026-11-05T02:00:00Z'}]:[];
  else if(docs.some(d=>p==='/api/documents/'+d.id))data={...docs.find(d=>p.endsWith(d.id)),minioPath:'file.pdf',fileContentType:'application/pdf',processes:[],concurrencyVersion:actor};
  else if(p==='/api/users/me' || p.startsWith('/api/users/'))data={id:actor,fullName:'Trưởng phòng',role:'Manager',roles:['Manager'],isActive:true};
  else {unexpected.push(p);return route.abort();}
  return route.fulfill({json:{success:true,data}});
 });
 const page=await context.newPage();page.setDefaultTimeout(40000);page.on('pageerror',e=>errors.push(e.message));
 try{
  await page.goto(base+'/signatures',{waitUntil:'domcontentloaded'});
  await page.getByRole('tab',{name:'Chờ ký'}).waitFor();await page.getByText('Hồ sơ phòng A',{exact:true}).first().waitFor();
  assert.equal(await page.getByText('Hồ sơ chung',{exact:true}).count(),0);
  await page.getByRole('tab',{name:'Đã ký'}).click();await page.getByText('Hồ sơ đã ký',{exact:true}).waitFor();
  assert.equal(await page.getByText(/Bạn đã ký:/).count(),1);assert(pendingCount&&signedCount);
  const initial=await page.evaluate(()=>window.initialLoad);
  await page.getByRole('link',{name:'Xem công văn',exact:true}).click();await page.waitForURL(base+'/documents/'+docs[2].id,{waitUntil:'domcontentloaded'});
  assert.equal(await page.evaluate(()=>window.initialLoad),initial);
  await page.evaluate(()=>Blazor.navigateTo('/documents'));
  await page.locator(width===300?'.document-mobile-card':'.document-desktop-table tbody tr').filter({hasText:'Hồ sơ chung'}).waitFor();
  const common=page.locator(width===300?'.document-mobile-card':'.document-desktop-table tbody tr').filter({hasText:'Hồ sơ chung'});
  assert(await common.getByText('Công văn chung',{exact:true}).isVisible());
  assert(await page.getByLabel('Lọc theo phạm vi').isVisible());
  await page.evaluate(()=>Blazor.navigateTo('/documents/'+ 'bbbbbbbb-0000-0000-0000-000000000002'));
  await page.getByRole('heading',{name:'Hồ sơ chung',exact:true}).waitFor();
  assert.equal(await page.getByRole('button',{name:'Ký nháy',exact:true}).count(),0);
  await page.evaluate(()=>Blazor.navigateTo('/signatures/'+ 'bbbbbbbb-0000-0000-0000-000000000001'));
  await page.getByRole('button',{name:'Ký nháy (Manager)'}).click();
  await page.getByRole('button',{name:'Ký số',exact:true}).click();
  await page.getByRole('button',{name:'Hoàn tất bước ký nháy'}).waitFor();
  assert.equal(signCount,1);assert.equal(finishCount,1);
  await page.getByRole('button',{name:'Hoàn tất bước ký nháy'}).click();
  await page.waitForFunction(()=>document.body.innerText.includes('Đã ký nháy'));
  assert.equal(signCount,1);assert.equal(finishCount,2);
  assert.deepEqual(errors,[]);assert.deepEqual(unexpected,[]);assert(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth+1));
  if(process.env.HAU_TEST_ARTIFACTS){fs.mkdirSync(process.env.HAU_TEST_ARTIFACTS,{recursive:true});await page.screenshot({path:path.join(process.env.HAU_TEST_ARTIFACTS,`scope-${width}.png`)});}
  console.log(`PASS ${width}px: signing queue/history, common documents, unit review guard, SPA`);
 }finally{await context.close();}
}
(async()=>{const browser=await chromium.launch({headless:true,executablePath:process.env.HAU_TEST_CHROMIUM});try{for(const width of [1366,300])await check(browser,width);}finally{await browser.close();}})().catch(e=>{console.error(e);process.exitCode=1;});
