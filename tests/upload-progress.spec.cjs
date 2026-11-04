// Fixture uploads only; no application documents, MinIO files, or cloud APIs are changed.
const assert=require('node:assert/strict');
const fs=require('node:fs');const path=require('node:path');const http=require('node:http');
const {chromium}=require(process.env.HAU_PLAYWRIGHT_MODULE||'playwright');
const id='bbbbbbbb-0000-0000-0000-000000000001',type='aaaaaaaa-0000-0000-0000-000000000001';
async function ui(browser,width){
 const context=await browser.newContext({viewport:{width,height:850}});
 const token=[Buffer.from('{"alg":"none"}').toString('base64url'),Buffer.from(JSON.stringify({sub:id,name:'Upload fixture',role:'Clerk',exp:Math.floor(Date.now()/1000)+3600})).toString('base64url'),'fixture'].join('.');
 await context.addInitScript(t=>{
  if(window!==window.top)return;localStorage.setItem('auth_token',t);
  const Native=window.XMLHttpRequest;
  window.XMLHttpRequest=function(){
   const xhr=new Native(),open=xhr.open;
   xhr.open=function(method,url){
    if(!/\/api\/documents\/[^/]+\/upload/.test(url))return open.apply(xhr,arguments);
    const fake={upload:{},headers:{},status:0,response:null,setRequestHeader(k,v){this.headers[k]=v;},
     send(form){window.activeUpload=this;window.uploadFiles=Array.from(form.values()).map(f=>({name:f.name,size:f.size}));},
     getAllResponseHeaders(){return 'Content-Type: application/json\r\n';}};
    window.pendingUpload=fake;
    for(const key of ['setRequestHeader','send','getAllResponseHeaders'])xhr[key]=fake[key].bind(fake);
    Object.defineProperty(xhr,'upload',{value:fake.upload});
    Object.defineProperty(xhr,'status',{get:()=>fake.status});Object.defineProperty(xhr,'response',{get:()=>fake.response});
    window.progressUpload=percent=>fake.upload.onprogress?.({loaded:percent,total:100,lengthComputable:true});
    window.uploadSent=()=>fake.upload.onload?.();
    window.finishUpload=(status,body)=>{fake.status=status;fake.response=new TextEncoder().encode(JSON.stringify(body)).buffer;xhr.onload?.();};
   };return xhr;
  };
 },token);
 let doc={id,title:'Upload fixture',status:'Draft',docTypeId:type,minioPath:'',processes:[]};const unexpected=[],errors=[];
 await context.route('**/api/**',route=>{const req=route.request(),p=new URL(req.url()).pathname;let data;
  if(p==='/api/notifications/stream')return route.fulfill({contentType:'text/event-stream',body:'data: {"unreadCount":0}\n\n'});
  if(p.endsWith('/ocr-jobs/latest'))return route.fulfill({json:{success:true,data:null}});
  if(p==='/api/documents/types')data=[{id:type,typeName:'CongVanDi'}];
  else if(req.method()==='POST'&&p==='/api/documents')data=doc;
  else if(req.method()==='GET'&&p==='/api/documents/'+id)data=doc;
  else{unexpected.push(req.method()+' '+p);return route.abort();}
  return route.fulfill({json:{success:true,data}});
 });
 const page=await context.newPage();page.setDefaultTimeout(15000);page.on('pageerror',e=>errors.push(e.message));
 try{
  await page.goto('http://localhost:5227/documents/create');await page.locator('input[placeholder="Nhập tiêu đề công văn..."]').fill('Upload fixture');await page.locator('#document-type-select').click();await page.getByRole('option',{name:'Công văn đi',exact:true}).click();
  await page.locator('#fileInput').setInputFiles({name:'fixture.pdf',mimeType:'application/pdf',buffer:Buffer.from('%PDF-1.4\n%%EOF')});
  await page.getByRole('button',{name:'Tạo Công văn',exact:true}).click();await page.waitForFunction(()=>window.activeUpload);
  const popup=page.locator('.upload-toast');await popup.waitFor();await page.evaluate(()=>window.progressUpload(25));await page.waitForFunction(()=>document.querySelector('.upload-toast progress')?.value===25);
  const box=await popup.boundingBox();assert(box.x>=0&&box.x+box.width<=width+1);assert(box.y<30);assert(box.x+box.width>width-25);
  assert(await page.locator('#fileInput').isDisabled());
  if(process.env.HAU_TEST_ARTIFACTS){fs.mkdirSync(process.env.HAU_TEST_ARTIFACTS,{recursive:true});await page.screenshot({path:path.join(process.env.HAU_TEST_ARTIFACTS,`progress-${width}.png`),animations:'disabled'});}
  await page.evaluate(()=>{window.progressUpload(100);window.uploadSent();});await popup.getByText('Đã tải lên, máy chủ đang xử lý...', {exact:true}).waitFor();assert.equal(await popup.getByText('Tải lên thành công',{exact:true}).count(),0);
  await page.evaluate(()=>window.finishUpload(413,{message:'Too large fixture'}));await popup.getByText('Tải lên thất bại. Vui lòng thử lại.',{exact:true}).waitFor();await page.waitForURL('**/documents/'+id);
  await popup.getByRole('button',{name:'Đóng tiến độ tải tệp'}).click();await popup.waitFor({state:'hidden'});
  const chooser=page.waitForEvent('filechooser');await page.getByRole('button',{name:'Tải lên file',exact:true}).click();await(await chooser).setFiles({name:'retry.pdf',mimeType:'application/pdf',buffer:Buffer.from('%PDF-1.4\n%%EOF')});
  await popup.waitFor();await page.waitForFunction(()=>window.uploadFiles?.[0]?.name==='retry.pdf');await page.evaluate(()=>{window.progressUpload(70);window.uploadSent();});await popup.getByText('Đã tải lên, máy chủ đang xử lý...', {exact:true}).waitFor();
  await page.evaluate(d=>window.finishUpload(200,{success:true,data:d}),doc);await popup.getByText('Tải lên thành công',{exact:true}).waitFor();
  assert.deepEqual(await page.evaluate(()=>window.uploadFiles.map(f=>f.name)),['retry.pdf']);
  await page.waitForFunction(()=>window.documentFiles.uploadSelections.size===0);await popup.waitFor({state:'hidden',timeout:10000});
  assert.deepEqual(errors,[]);assert.deepEqual(unexpected,[]);console.log(`PASS ${width}px upload progress, processing, 413, persistent error, retry, success and cleanup`);
 }finally{await context.close();}
}
async function nativeTransport(browser){
 let bytes=0;const server=http.createServer((req,res)=>{
  res.setHeader('Access-Control-Allow-Origin','http://localhost:5227');res.setHeader('Access-Control-Allow-Headers','Authorization');res.setHeader('Access-Control-Allow-Methods','POST, OPTIONS');
  if(req.method==='OPTIONS'){res.writeHead(204).end();return;}
  assert.equal(req.headers.authorization,'Bearer fixture-only');req.on('data',b=>bytes+=b.length);req.on('end',()=>setTimeout(()=>{res.setHeader('Content-Type','application/json');res.end('{"success":true}');},200));
 });await new Promise(r=>server.listen(0,'127.0.0.1',r));const port=server.address().port;
 const context=await browser.newContext(),page=await context.newPage();
 try{
  await page.goto('http://localhost:5227/login');await page.evaluate(()=>{const input=document.createElement('input');input.type='file';input.id='native-upload-fixture';document.body.appendChild(input);});
  await page.locator('#native-upload-fixture').setInputFiles({name:'native.pdf',mimeType:'application/pdf',buffer:Buffer.alloc(1024*1024,65)});
  const result=await page.evaluate(async port=>{const events=[],key=window.documentFiles.captureUpload('native-upload-fixture',2*1024*1024);try{const response=await window.documentFiles.upload(key,`http://127.0.0.1:${port}/upload`,'fixture-only','file',{invokeMethodAsync:(name,...args)=>{events.push({name,args});return Promise.resolve();}});return{status:response.status,body:new TextDecoder().decode(response.body),events};}finally{window.documentFiles.releaseUpload(key);}},port);
  assert.equal(result.status,200);assert.equal(JSON.parse(result.body).success,true);assert(result.events.some(e=>e.name==='Report'&&e.args[0]>0&&e.args[2]));assert(result.events.some(e=>e.name==='Uploaded'));assert(bytes>1024*1024);console.log('PASS native XHR upload: actual multipart bytes, Authorization, real progress events and binary response');
 }finally{await context.close();await new Promise(r=>server.close(r));}
}
(async()=>{const browser=await chromium.launch({headless:true,executablePath:process.env.HAU_TEST_CHROMIUM});try{for(const width of [1366,300])await ui(browser,width);await nativeTransport(browser);}finally{await browser.close();}})().catch(e=>{console.error(e);process.exitCode=1;});
