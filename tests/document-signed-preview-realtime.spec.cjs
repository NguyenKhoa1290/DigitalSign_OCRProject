// Real published UI with fixture SSE/APIs; no user data, storage or AI calls.
const assert=require('node:assert/strict'),fs=require('node:fs'),path=require('node:path');
const {chromium}=require(process.env.HAU_PLAYWRIGHT_MODULE||'playwright');
const base=process.env.HAU_TEST_WEB||'http://localhost:5227';
const id='bbbbbbbb-0000-0000-0000-000000000001',otherId='bbbbbbbb-0000-0000-0000-000000000002';
const v0='cccccccc-0000-0000-0000-000000000001',v1='cccccccc-0000-0000-0000-000000000002',v2='cccccccc-0000-0000-0000-000000000003';
const writer='dddddddd-0000-0000-0000-000000000001';
const png=Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aOhkAAAAASUVORK5CYII=','base64');
const result=text=>JSON.stringify({provider:'gemini',modelId:'gemini-fixture',selectedPages:[1],sourceFileNames:['fixture.png'],createdAt:'2026-10-25T09:00:00Z',layout:{blocks:[{type:'paragraph',text}]}});
async function run(browser,width){
 let doc={id,title:'Công văn realtime',status:'Draft',docTypeName:'CongVanDi',createdById:writer,createdByName:'Văn thư fixture',minioPath:'documents/fixture.png',fileName:'fixture.png',fileContentType:'image/png',concurrencyVersion:v0,ocrDataRaw:null,processes:[]};
 const other={...doc,id:otherId,title:'Công văn khác'};const sessions=[];
 try{
  for(const [index,role] of ['Specialist','Manager','BoardOfDirectors'].entries()){
   const user=`eeeeeeee-0000-0000-0000-00000000000${index+1}`;
   const context=await browser.newContext({viewport:{width,height:850}});
   const token=[Buffer.from('{"alg":"none"}').toString('base64url'),Buffer.from(JSON.stringify({sub:user,name:role,role,exp:Math.floor(Date.now()/1000)+3600})).toString('base64url'),'fixture'].join('.');
   await context.addInitScript(({token,id,initial})=>{
    if(window!==window.top)return;localStorage.setItem('auth_token',token);
    const nativeFetch=window.fetch;window.fixtureVersion=initial;window.fixtureWatch=null;window.fixtureConnections=0;
    window.emitDocument=(documentId,documentVersion)=>{if(documentId===id)window.fixtureVersion=documentVersion;window.fixtureController?.enqueue(new TextEncoder().encode(`data: ${JSON.stringify({unreadCount:0,documentId,documentVersion})}\n\n`));};
    window.disconnectFixture=()=>window.fixtureController?.close();
    window.fetch=(input,init)=>{
     const url=new URL(typeof input==='string'?input:input.url,location.href);
     if(url.pathname!=='/api/notifications/stream')return nativeFetch(input,init);
     const watched=url.searchParams.get('documentId');let controller;
     window.fixtureWatch=watched;window.fixtureConnections++;
     const stream=new ReadableStream({start(c){controller=c;window.fixtureController=c;
      c.enqueue(new TextEncoder().encode(`data: ${JSON.stringify(watched?{unreadCount:0,documentId:watched,documentVersion:watched===id?window.fixtureVersion:initial}:{unreadCount:0})}\n\n`));},cancel(){}});
     init?.signal?.addEventListener('abort',()=>{try{controller.close();}catch{}},{once:true});
     return Promise.resolve(new Response(stream,{headers:{'Content-Type':'text/event-stream'}}));
    };
   },{token,id,initial:v0});
   const requests=[],unexpected=[],errors=[];let fileReads=0;
   await context.route('**/api/**',async route=>{
    const req=route.request(),p=new URL(req.url()).pathname;requests.push(req.method()+' '+p);
    if(req.method()==='GET'&&p==='/api/documents/'+id)return route.fulfill({json:{success:true,data:doc}});
    if(req.method()==='GET'&&p==='/api/documents/'+otherId)return route.fulfill({json:{success:true,data:other}});
    if(req.method()==='GET'&&(p==='/api/documents/'+id+'/file'||p==='/api/documents/'+otherId+'/file')){fileReads++;return route.fulfill({contentType:p==='/api/documents/'+id+'/file'?doc.fileContentType:'image/png',body:p==='/api/documents/'+id+'/file'&&doc.signedPdfPath?fs.readFileSync(path.join(__dirname,'../tai_nguyen/ket_qua_kiem_thu/signing_sources_20261107/pipeline/ocr-two-signatures.pdf')):png});}
    unexpected.push(req.method()+' '+p);return route.abort();
   });
   const page=await context.newPage();page.setDefaultTimeout(60000);page.on('pageerror',e=>errors.push(e.message));
   sessions.push({context,page,role,requests,unexpected,errors,get fileReads(){return fileReads;}});
   await page.goto(base+'/documents/'+id);await page.getByRole('heading',{name:doc.title,exact:true}).waitFor();
   await page.waitForFunction(id=>window.fixtureWatch===id,id);
   assert.equal(await page.getByRole('button',{name:'Chạy OCR',exact:true}).count(),0);
  }
  for(const s of sessions){
   await s.page.evaluate(()=>document.querySelector('.page-container').scrollTop=100);
   s.before=await s.page.evaluate(()=>({scroll:document.querySelector('.page-container').scrollTop,src:document.querySelector('.document-preview-card img')?.src}));
   assert.equal(s.fileReads,1);
  }
  // Another user's OCR completes while all three readers remain on their open detail page.
  doc={...doc,concurrencyVersion:v1,ocrDataRaw:result('KẾT QUẢ VĂN THƯ VỪA OCR')};
  for(const s of sessions)await s.page.evaluate(({id,v})=>window.emitDocument(id,v),{id,v:v1});
  for(const s of sessions){
   await s.page.getByText('KẾT QUẢ VĂN THƯ VỪA OCR',{exact:true}).waitFor();
   assert.equal(await s.page.locator('.notification-count').count(),0);
   assert.equal(await s.page.getByRole('button',{name:'Tải Word',exact:true}).count(),0);
   assert.equal(s.fileReads,1);assert.equal(s.requests.filter(x=>x==='GET /api/documents/'+id).length,2);
   const after=await s.page.evaluate(()=>({scroll:document.querySelector('.page-container').scrollTop,src:document.querySelector('.document-preview-card img')?.src}));
   assert.deepEqual(after,s.before);
   // The same snapshot does not repeatedly fetch the document or announce the result.
   await s.page.evaluate(({id,v})=>window.emitDocument(id,v),{id,v:v1});
  }
  // Reconnect reconciles a newer result even when the completion event was missed.
  const leader=sessions[1];doc={...doc,concurrencyVersion:v2,ocrDataRaw:result('KẾT QUẢ SAU KẾT NỐI LẠI')};
  await leader.page.evaluate(v=>{window.fixtureVersion=v;window.disconnectFixture();},v2);
  await leader.page.getByText('KẾT QUẢ SAU KẾT NỐI LẠI',{exact:true}).waitFor();assert(leader.fileReads===1);
  // A signature changes the displayed PDF while preserving the original image path.
  const signedVersion='cccccccc-0000-0000-0000-000000000004';
  doc={...doc,status:'DeptSigned',signedPdfPath:'signed-fixture.pdf',signingStartedAt:'2026-11-07T01:00:00Z',fileName:'signed-fixture.pdf',fileContentType:'application/pdf',concurrencyVersion:signedVersion};
  for(const s of sessions)await s.page.evaluate(({id,v})=>window.emitDocument(id,v),{id,v:signedVersion});
  for(const s of sessions){await s.page.locator('iframe.pdf-frame').waitFor();await s.page.getByRole('button',{name:'Tải PDF đã ký'}).waitFor();assert.equal(s.fileReads,2);}
  // Changing documents releases the old watch; an old document event must not replace the new page.
  const specialist=sessions[0];await specialist.page.evaluate(url=>Blazor.navigateTo(url),'/documents/'+otherId);
  await specialist.page.getByRole('heading',{name:'Công văn khác',exact:true}).waitFor();
  await specialist.page.waitForFunction(id=>window.fixtureWatch===id,otherId);
  await specialist.page.evaluate(({id,v})=>window.emitDocument(id,v),{id,v:v2});
  await specialist.page.waitForTimeout(200);
  assert.equal(await specialist.page.getByText('KẾT QUẢ SAU KẾT NỐI LẠI',{exact:true}).count(),0);
  for(const s of sessions){
   assert.deepEqual(s.unexpected,[]);assert.deepEqual(s.errors,[]);
   assert.equal(s.requests.some(x=>x.includes('/ocr-jobs')||x.startsWith('POST')||x.startsWith('PATCH')),false);
   assert(await s.page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth+1));
   const out=process.env.HAU_TEST_ARTIFACTS;if(out){fs.mkdirSync(out,{recursive:true});await s.page.locator('.gemini-result-card').screenshot({path:path.join(out,`ocr-viewer-${width}-${s.role}.png`),animations:'disabled'});}
  }
  console.log(`PASS ${width}px: Specialist/Manager/Board signed PDF preview, original image preserved, reconnect and document isolation`);
 }finally{for(const s of sessions)await s.context.close();}
}
(async()=>{const browser=await chromium.launch({headless:true,executablePath:process.env.HAU_TEST_CHROMIUM});try{await run(browser,1366);await run(browser,300);}finally{await browser.close();}})().catch(e=>{console.error(e);process.exitCode=1;});
