// All APIs are fixtures. The real sample image is read only; no application document is modified.
const assert=require('node:assert/strict'),fs=require('node:fs'),path=require('node:path');
const {chromium}=require(process.env.HAU_PLAYWRIGHT_MODULE||'playwright');
const base=process.env.HAU_TEST_WEB||'http://localhost:5227';
const id='bbbbbbbb-0000-0000-0000-000000000001',type='aaaaaaaa-0000-0000-0000-000000000002';
const imagePath='photo_2026-10-24_22-56-13.jpg';
async function run(browser,width){
 const context=await browser.newContext({viewport:{width,height:850}});
 const token=[Buffer.from('{"alg":"none"}').toString('base64url'),Buffer.from(JSON.stringify({sub:id,name:'Preview fixture',role:'Specialist',exp:Math.floor(Date.now()/1000)+3600})).toString('base64url'),'fixture'].join('.');
 await context.addInitScript(t=>{if(window===window.top)localStorage.setItem('auth_token',t);},token);
 let doc={id,title:'Preview fixture',status:'Draft',docTypeId:type,docTypeName:'CongVanDi',createdById:id,createdByName:'Preview fixture',minioPath:'',processes:[]},release,uploads=0;
 const errors=[],unexpected=[];
 await context.route('**/api/**',async route=>{
  const req=route.request(),p=new URL(req.url()).pathname;let data;
  if(p==='/api/notifications/stream')return route.fulfill({contentType:'text/event-stream',body:'data: {"unreadCount":0}\n\n'});
  if(p.endsWith('/ocr-jobs/latest'))return route.fulfill({json:{success:true,data:null}});
  if(p==='/api/documents/types')data=[{id:type,typeName:'CongVanDi'}];
  else if(req.method()==='GET'&&p==='/api/documents/'+id)data=doc;
  else if(req.method()==='GET'&&p==='/api/documents/'+id+'/file')return route.fulfill({contentType:'image/jpeg',body:fs.readFileSync(imagePath)});
  else if(req.method()==='POST'&&p==='/api/documents/'+id+'/upload'){
   // CDP omits binary file bodies from intercepted multipart request data.
   // Inspect the File snapshot handed to native FormData, before it is released.
   const captured=await page.evaluate(async()=>{
    const files=[...window.documentFiles.uploadSelections.values()][0];
    return Array.from(new Uint8Array(await files[0].arrayBuffer()));
   });
   assert.deepEqual(Buffer.from(captured),fs.readFileSync(imagePath),'Upload must retain the original File bytes');
   uploads++;await new Promise(resolve=>release=resolve);
   if(uploads===1)return route.fulfill({status:413,json:{success:false}});
   doc={...doc,minioPath:'documents/fixture.jpg',fileName:path.basename(imagePath),fileContentType:'image/jpeg'};data=doc;
  }else{unexpected.push(req.method()+' '+p);return route.abort();}
  return route.fulfill({json:{success:true,data}});
 });
 const page=await context.newPage();page.setDefaultTimeout(60000);page.on('pageerror',e=>errors.push(e.message));
 try{
  await page.goto(base+'/documents/create');await page.locator('#fileInput').setInputFiles(imagePath);
  await page.waitForFunction(()=>document.querySelector('.selected-image-preview img')?.naturalWidth>0);
  await page.locator('#fileInput').setInputFiles({name:'replacement.pdf',mimeType:'application/pdf',buffer:Buffer.from('%PDF-1.4\n%%EOF')});await page.locator('.selected-image-preview').waitFor({state:'hidden'});
  await page.goto(base+'/documents/'+id);await page.getByText('Chưa có tệp đính kèm được lưu cho công văn này.',{exact:true}).waitFor();
  await page.locator('#documentPdfInput').setInputFiles(imagePath);await page.waitForFunction(()=>document.querySelector('.pending-document-image')?.naturalWidth>0);
  assert.equal(await page.locator('.pending-image-card .badge').innerText(),'Chưa lưu');await page.waitForFunction(()=>document.querySelector('.upload-toast')!==null);
  for(let n=0;!release&&n<250;n++)await page.waitForTimeout(20);assert(release,'First upload request did not start');release();release=null;
  await page.locator('.pending-image-card').getByText(/Ảnh chưa được lưu/).waitFor();assert.equal(await page.locator('.pending-document-image').count(),1);
  await page.waitForFunction(()=>{const input=document.getElementById('documentPdfInput');return input&&!input.disabled&&!input.value;});
  await page.locator('#documentPdfInput').setInputFiles(imagePath);await page.waitForFunction(()=>document.querySelector('.pending-document-image')?.naturalWidth>0);for(let n=0;!release&&n<250;n++)await page.waitForTimeout(20);assert(release,'Retry upload request did not start');
  if(process.env.HAU_TEST_ARTIFACTS){fs.mkdirSync(process.env.HAU_TEST_ARTIFACTS,{recursive:true});await page.locator('.pending-image-card').scrollIntoViewIfNeeded();await page.screenshot({path:path.join(process.env.HAU_TEST_ARTIFACTS,`pending-${width}.png`),animations:'disabled'});}
  release();release=null;await page.getByText('Xem ảnh công văn',{exact:true}).waitFor();await page.waitForFunction(()=>document.querySelector('img.document-image:not(.pending-document-image)')?.naturalWidth>0);
  assert.equal(await page.locator('.pending-image-card').count(),0);assert(await page.getByRole('button',{name:'Tải ảnh',exact:true}).isVisible());
  const blobs=await page.evaluate(async()=>{
   const image=document.querySelector('img.document-image'),original=image.closest('.card').querySelector('a[target="_blank"]').href;
   const preview=new Uint8Array(await(await fetch(image.src)).arrayBuffer()),file=new Uint8Array(await(await fetch(original)).arrayBuffer());
   return{separate:image.src!==original,signature:Array.from(preview.slice(0,8)),original:Array.from(file)};
  });
  assert(blobs.separate,'The display preview must have its own URL');assert.deepEqual(blobs.signature,[137,80,78,71,13,10,26,10]);assert.deepEqual(Buffer.from(blobs.original),fs.readFileSync(imagePath));
  const box=await page.locator('img.document-image').boundingBox();assert(box.width<=width);
  await page.reload();await page.waitForFunction(()=>document.querySelector('img.document-image')?.naturalWidth>0);
  assert.deepEqual(errors,[]);assert.deepEqual(unexpected,[]);console.log(`PASS ${width}px create preview, empty attachment, pending/error preview, successful saved image and reload`);
 }catch(error){console.error(JSON.stringify({width,url:page.url(),errors,unexpected,body:(await page.locator('body').innerText()).slice(0,900)}));throw error;}
 finally{if(release)release();await context.close();}
}
(async()=>{const browser=await chromium.launch({headless:true,executablePath:process.env.HAU_TEST_CHROMIUM});try{for(const width of [1366,300])await run(browser,width);}finally{await browser.close();}})().catch(e=>{console.error(e);process.exitCode=1;});
