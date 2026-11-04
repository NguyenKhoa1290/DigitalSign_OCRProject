// Published UI + fixture APIs: no real Word uploads, database changes or AI calls.
const assert=require('node:assert/strict'),fs=require('node:fs'),path=require('node:path');
const {chromium}=require(process.env.HAU_PLAYWRIGHT_MODULE||'playwright');
const base=process.env.HAU_TEST_WEB||'https://hauquanlycongvan.com';
const id='bbbbbbbb-0000-0000-0000-000000000001',v0='cccccccc-0000-0000-0000-000000000001',v1='cccccccc-0000-0000-0000-000000000002';
const editId='dddddddd-0000-0000-0000-000000000001';
const word=Buffer.from('PK\x03\x04edited-word-browser-fixture');
const png=Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aOhkAAAAASUVORK5CYII=','base64');
async function run(browser,width,role='Clerk',docTypeName='CongVanDen',status='Draft',owns=true){
 const context=await browser.newContext({viewport:{width,height:850}});
 const token=[Buffer.from('{"alg":"none"}').toString('base64url'),Buffer.from(JSON.stringify({sub:id,name:'Fixture',role,exp:Math.floor(Date.now()/1000)+3600})).toString('base64url'),'fixture'].join('.');
 await context.addInitScript(t=>{if(window===window.top)localStorage.setItem('auth_token',t);},token);
 const editedDocument={id:editId,fileName:'edited.docx',sourcePath:'documents/original.png',editedById:id,editedByName:role==='Admin'?'System Admin':'Đinh Quốc Bảo',editorRole:role==='Admin'?'Admin':'Clerk',editedAt:'2026-10-25T14:30:00Z',revision:1};
 let result={provider:'gemini',modelId:'gemini-fixture',selectedPages:[1],createdAt:'2026-10-25T14:00:00Z',layout:{blocks:[{type:'paragraph',text:'NỘI DUNG OCR BAN ĐẦU'}]}};
 if(role==='Specialist'||status==='Published')result.editedDocument=editedDocument;
 let doc={id,title:'OCR chỉnh sửa',docTypeName,status,createdById:owns?id:'aaaaaaaa-0000-0000-0000-000000000002',concurrencyVersion:v0,minioPath:'documents/original.png',fileName:'original.png',fileContentType:'image/png',ocrDataRaw:JSON.stringify(result),processes:[]};
 let uploads=0,exports=0,downloads=0,fileReads=0;const unexpected=[],errors=[];
 await context.route('**/api/**',async route=>{
  const req=route.request(),url=new URL(req.url()),p=url.pathname;
  if(p==='/api/notifications/stream')return route.fulfill({contentType:'text/event-stream',body:'data: {"unreadCount":0}\n\n'});
  if(p==='/api/documents/'+id&&req.method()==='GET')return route.fulfill({json:{success:true,data:doc}});
  if(p.endsWith('/ocr-jobs/latest'))return route.fulfill({json:{success:true,data:null}});
  if(p==='/api/documents/'+id+'/file'){fileReads++;return route.fulfill({contentType:'image/png',body:png});}
  if(p==='/api/documents/'+id+'/ocr-edited-file'&&req.method()==='POST'){
   uploads++;assert.equal(url.searchParams.get('expectedVersion'),v0);
   assert(req.postData()?.includes('name="file"'));assert(req.postData()?.includes('edited.docx'));
   if(uploads===1)return route.fulfill({status:409,json:{success:false,message:'Phiên bản đã thay đổi fixture. Tải lại để gửi.'}});
   result={...result,editedDocument};doc={...doc,ocrDataRaw:JSON.stringify(result),concurrencyVersion:v1,processes:[{action:'OcrEdited',timestamp:'2026-10-25T14:30:00Z',comment:'Bản Word đã chỉnh sửa.'}]};
   return route.fulfill({json:{success:true,data:editedDocument}});
  }
  if(p==='/api/documents/'+id+'/ocr-word'&&req.method()==='GET'){
   downloads++;assert.equal(url.searchParams.get('expectedVersion'),doc.concurrencyVersion);
   return route.fulfill({contentType:'application/vnd.openxmlformats-officedocument.wordprocessingml.document',body:word});
  }
  if(p==='/api/ocr/gemini/export-layout'){exports++;return route.fulfill({body:Buffer.from('original-word-fixture')});}
  unexpected.push(req.method()+' '+p);return route.abort();
 });
 const page=await context.newPage();page.setDefaultTimeout(60000);page.on('pageerror',e=>errors.push(e.message));
 try{
  await page.goto(base+'/documents/'+id,{waitUntil:'domcontentloaded'});await page.getByRole('heading',{name:'OCR chỉnh sửa',exact:true}).waitFor();
  const card=page.locator('.gemini-result-card'),input=card.locator('input[type=file]');
  const label=`Đã chỉnh sửa bởi ${role==='Admin'?'Quản trị viên System Admin':'Văn thư Đinh Quốc Bảo'}`;
  if(role==='Specialist'||status==='Published'){
   assert.equal(await card.getByRole('button',{name:'Tải lên bản chỉnh sửa',exact:true}).count(),0);assert.equal(await card.getByRole('button',{name:'Tải Word',exact:true}).count(),role==='Specialist'&&(!owns||docTypeName==='CongVanDen')?0:1);
   await card.getByText(label,{exact:true}).waitFor({timeout:15000});
   if(role==='Clerk'||owns&&docTypeName==='CongVanDi'){
    const [download]=await Promise.all([page.waitForEvent('download'),card.getByRole('button',{name:'Tải Word',exact:true}).click()]);
    assert.equal(download.suggestedFilename(),'edited.docx');assert.equal(downloads,1);assert.equal(exports,0);
   }
  }else{
   assert.equal(await input.isVisible(),false);
   await input.setInputFiles({name:'wrong.pdf',mimeType:'application/pdf',buffer:Buffer.from('%PDF')});
   await card.getByText('Chọn tệp Word .docx, tối đa 50 MB.',{exact:true}).waitFor();assert.equal(uploads,0);
   await input.setInputFiles({name:'edited.docx',mimeType:'application/vnd.openxmlformats-officedocument.wordprocessingml.document',buffer:word});
   await card.getByText('Phiên bản đã thay đổi fixture. Tải lại để gửi.',{exact:true}).waitFor();
   assert.equal(await card.locator('.ocr-edited-summary').count(),0);
   await input.setInputFiles({name:'edited.docx',mimeType:'application/vnd.openxmlformats-officedocument.wordprocessingml.document',buffer:word});
   await card.getByText(label,{exact:true}).waitFor({timeout:15000});assert.equal(uploads,2);assert.equal(fileReads,1);
   assert.equal(await card.locator('.ocr-original-result').getAttribute('open'),null);
   const [download]=await Promise.all([page.waitForEvent('download'),card.getByRole('button',{name:'Tải Word',exact:true}).click()]);assert.equal(download.suggestedFilename(),'edited.docx');
   assert.equal(downloads,1);assert.equal(exports,0);assert.equal(doc.minioPath,'documents/original.png');assert.equal(doc.fileName,'original.png');
   await card.getByText('Nội dung',{exact:true}).click();await card.getByText('NỘI DUNG OCR BAN ĐẦU',{exact:true}).waitFor();
   await page.reload({waitUntil:'domcontentloaded'});await card.getByText(label,{exact:true}).waitFor({timeout:15000});
  }
  assert(await card.getByText(/25\/10\/2026 21:30/).isVisible());
  assert.equal((await card.innerText()).includes('gemini-fixture'),false);
  assert.equal(await card.locator('summary.btn').first().innerText(),'Nội dung');
  const lineSpacing=await card.locator('.layout-paragraph').first().evaluate(p=>{const css=getComputedStyle(p);return parseFloat(css.lineHeight)-1.4*parseFloat(css.fontSize);});
  assert(Math.abs(lineSpacing-2)<0.15);
  assert.deepEqual(unexpected,[]);assert.deepEqual(errors,[]);assert(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth+1));
  const out=process.env.HAU_TEST_ARTIFACTS;if(out){fs.mkdirSync(out,{recursive:true});await card.screenshot({path:path.join(out,`ocr-edit-${width}-${role}-${docTypeName}-${status}-${owns}.png`),animations:'disabled'});}
  console.log(JSON.stringify({width,role,uploads,downloads,exports,result:'PASS'}));
 }catch(error){console.error(JSON.stringify({width,role,uploads,fileReads,errors,unexpected,card:await page.locator('.gemini-result-card').innerText().catch(()=>''),metadata:JSON.parse(doc.ocrDataRaw).editedDocument}));throw error;}finally{await context.close();}
}
(async()=>{const browser=await chromium.launch({headless:true,executablePath:process.env.HAU_TEST_CHROMIUM});try{await run(browser,1366);await run(browser,300);await run(browser,390,'Admin');await run(browser,300,'Specialist');await run(browser,300,'Clerk','CongVanDi');await run(browser,300,'Specialist','CongVanDi');await run(browser,1366,'Specialist','CongVanDi','Draft',false);await run(browser,300,'Clerk','CongVanDi','Published');}finally{await browser.close();}})().catch(e=>{console.error(e);process.exitCode=1;});
