// API fixtures only; no real accounts or business documents are changed.
const assert=require('node:assert/strict'),fs=require('node:fs'),path=require('node:path');
const {chromium}=require(process.env.HAU_PLAYWRIGHT_MODULE||'playwright');
const base=process.env.HAU_TEST_WEB||'http://localhost:5227',actor='aaaaaaaa-0000-0000-0000-000000000001',id='bbbbbbbb-0000-0000-0000-000000000001';
async function check(browser,width){
 const context=await browser.newContext({viewport:{width,height:850},hasTouch:width===300});
 const token=[Buffer.from('{"alg":"none"}').toString('base64url'),Buffer.from(JSON.stringify({sub:actor,role:'Manager',name:'Manager fixture',exp:Math.floor(Date.now()/1000)+3600})).toString('base64url'),'fixture'].join('.');
 await context.addInitScript(({token,id,version})=>{
  if(window!==window.top)return;localStorage.setItem('auth_token',token);
  const nativeFetch=window.fetch;window.fixtureVersion=version;
  window.emitSignedFile=v=>{window.fixtureVersion=v;window.fixtureController?.enqueue(new TextEncoder().encode(`data: ${JSON.stringify({unreadCount:0,documentId:id,documentVersion:v})}\n\n`));};
  window.fetch=(input,init)=>{
   const url=new URL(typeof input==='string'?input:input.url,location.href);
   if(url.pathname!=='/api/notifications/stream')return nativeFetch(input,init);
   const watched=url.searchParams.get('documentId');let controller;window.fixtureWatch=watched;
   const stream=new ReadableStream({start(c){controller=c;window.fixtureController=c;c.enqueue(new TextEncoder().encode(`data: ${JSON.stringify(watched?{unreadCount:0,documentId:watched,documentVersion:window.fixtureVersion}:{unreadCount:0})}\n\n`));},cancel(){}});
   init?.signal?.addEventListener('abort',()=>{try{controller.close();}catch{}},{once:true});
   return Promise.resolve(new Response(stream,{headers:{'Content-Type':'text/event-stream'}}));
  };
 },{token,id,version:actor});
 const doc={id,title:'OCR signing fixture',docTypeName:'CongVanDi',status:'PendingDeptReview',minioPath:'documents/source.jpg',fileContentType:'image/jpeg',canReview:true,concurrencyVersion:actor,processes:[]};
 let signCalls=0,verifyCalls=0,fileReads=0;const errors=[];
 await context.route('**/api/**',async route=>{
  const req=route.request(),p=new URL(req.url()).pathname;
  if(p==='/api/notifications/stream')return route.fulfill({contentType:'text/event-stream',body:'data: '+JSON.stringify({unreadCount:0,documentId:id,documentVersion:doc.concurrencyVersion})+'\n\n'});
  if(p===`/api/documents/${id}/file`){fileReads++;return route.fulfill({contentType:doc.fileContentType,body:doc.signedPdfPath?Buffer.from('%PDF-1.4\n%%EOF'):Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+a7xUAAAAASUVORK5CYII=','base64')});}
  if(p==='/api/signatures/verify-file'){
   assert.equal(req.method(),'POST');assert(req.headers()['content-type'].includes('multipart/form-data'));verifyCalls++;
   return route.fulfill({json:{success:true,data:{isValid:false,signatureCount:2,coversWholeDocument:false,message:'Có thay đổi hoặc nội dung được thêm sau chữ ký cuối.',signatures:[{signerName:'Fixture signer',signedAt:'2026-11-07T01:00:00Z',isIntact:true,isCertificateValid:true,isCertificateTrusted:true}]}}});
  }
  if(p==='/api/signatures/personal-sign') {signCalls++;doc.signedPdfPath='signed.pdf';doc.signingStartedAt='2026-11-07T01:00:00Z';return route.fulfill({json:{success:true,data:{signatureId:actor}}});}
  if(p===`/api/documents/${id}/dept-sign`){doc.status='DeptSigned';return route.fulfill({json:{success:true,data:doc}});}
  let data;
  if(p.startsWith('/api/signatures/document/'))data=signCalls?[{id:actor,signerId:actor,signatureType:'PersonalSignature',signedAt:'2026-11-07T01:00:00Z'}]:[];
  else if(p===`/api/documents/${id}`)data=doc;
  else if(p==='/api/users/me'||p.startsWith('/api/users/'))data={id:actor,fullName:'Manager fixture',role:'Manager',roles:['Manager'],isActive:true};
  else data=[];
  return route.fulfill({json:{success:true,data}});
 });
 const page=await context.newPage();page.setDefaultTimeout(45000);page.on('pageerror',e=>errors.push(e.message));
 try{
  await page.goto(base+'/signatures/'+id);
  await page.getByRole('button',{name:'Ký nháy (Manager)'}).click();
  assert.equal(await page.getByText('Mã PIN chứng thư (nếu có)').count(),0);
  await page.getByRole('button',{name:'Ký số',exact:true}).click();
  await page.waitForFunction(()=>document.body.innerText.includes('Đã ký nháy'));
  assert.equal(signCalls,1);
  await page.getByRole('link',{name:'Kiểm tra PDF bên ngoài'}).click();await page.waitForURL(base+'/verify-signature');
  await page.locator('#signatureVerificationFile').setInputFiles({name:'external.pdf',mimeType:'application/pdf',buffer:Buffer.from('%PDF-1.4\n%%EOF')});
  await page.getByRole('status').filter({hasText:'Có thay đổi'}).waitFor();assert.equal(verifyCalls,1);
  await page.getByRole('button',{name:'Đóng tiến độ tải tệp'}).click();
  assert(await page.getByText('Fixture signer',{exact:true}).isVisible());
  assert(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth+1));assert.deepEqual(errors,[]);
  const geometry=await page.evaluate(()=>Object.fromEntries(['.app-layout','.main-content','.page-container','.page-header','.liquid-sidebar-shell'].map(s=>{const e=document.querySelector(s),r=e.getBoundingClientRect(),c=getComputedStyle(e);return[s,{x:r.x,y:r.y,width:r.width,height:r.height,scrollLeft:e.scrollLeft,scrollTop:e.scrollTop,padding:c.padding,display:c.display,position:c.position}];})));
  if(width>960)assert(geometry['.page-header'].x>=geometry['.liquid-sidebar-shell'].x+geometry['.liquid-sidebar-shell'].width);
  if(process.env.HAU_TEST_ARTIFACTS){fs.mkdirSync(process.env.HAU_TEST_ARTIFACTS,{recursive:true});await page.screenshot({path:path.join(process.env.HAU_TEST_ARTIFACTS,`verify-${width}.png`),fullPage:true});}
  console.log(`PASS ${width}px: image signing and external verification`);
 }finally{await context.close();}
}
(async()=>{const browser=await chromium.launch({headless:true,executablePath:process.env.HAU_TEST_CHROMIUM});try{for(const width of [1366,300])await check(browser,width);}finally{await browser.close();}})().catch(e=>{console.error(e);process.exitCode=1;});
