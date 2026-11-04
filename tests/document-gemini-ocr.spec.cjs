// All APIs are fixtures: no real documents, Paddle runs or Gemini charges.
const assert=require('node:assert/strict'),fs=require('node:fs'),path=require('node:path');
const {chromium}=require(process.env.HAU_PLAYWRIGHT_MODULE||'playwright');
const base=process.env.HAU_TEST_WEB||'https://hauquanlycongvan.com';
const id='bbbbbbbb-0000-0000-0000-000000000001',version='cccccccc-0000-0000-0000-000000000001';
const layout={font_name:'Times New Roman',font_size:12,blocks:[
 {type:'paragraph',text:'NỘI DUNG GEMINI',bold:true,align:'center'},
 {type:'table',headers:['Nội dung','Kết quả'],rows:[],blank_rows:7},
 {type:'paragraph',text:'<img src=x onerror="window.ocrInjected=true">'}],uncertain_fields:[]};
const result={provider:'gemini',modelId:'gemini-fixture',selectedPages:[2,6],sourceFileNames:['saved.pdf'],createdAt:'2026-10-25T07:00:00Z',layout};

async function run(browser,width,{fail=false,role='Clerk',missing=false,stay=false}={}){
 const context=await browser.newContext({viewport:{width,height:850}});
 const token=[Buffer.from('{"alg":"none"}').toString('base64url'),Buffer.from(JSON.stringify({sub:id,name:'OCR fixture',role,exp:Math.floor(Date.now()/1000)+3600})).toString('base64url'),'fixture'].join('.');
 await context.addInitScript(t=>{if(window===window.top)localStorage.setItem('auth_token',t);},token);
 let doc={id,title:'OCR fixture',docTypeName:'CongVanDen',status:'Draft',createdById:id,createdByName:'OCR fixture',concurrencyVersion:version,minioPath:missing?'':'documents/saved.pdf',fileName:'saved.pdf',fileContentType:'application/pdf',docNumber:'KEEP-123',issuedDate:'2026-10-20',ocrDataRaw:role==='Specialist'?JSON.stringify(result):null,processes:[]};
 let job=null,notice=null,queued=0,exports=0,inspections=0,reads=0,streamCount=0;
 const unexpected=[],errors=[];
 await context.route('**/api/**',async route=>{
  const req=route.request(),p=new URL(req.url()).pathname;
  if(p==='/api/notifications/stream'){streamCount++;return route.fulfill({contentType:'text/event-stream',body:`data: {"unreadCount":${notice&&!notice.readAt?1:0}}\n\n`});}
  if(req.method()==='GET'&&p==='/api/documents/'+id)return route.fulfill({json:{success:true,data:doc}});
  if(p==='/api/documents/'+id+'/file')return route.fulfill({contentType:'application/pdf',body:Buffer.from('%PDF-1.4\n%%EOF')});
  if(req.method()==='GET'&&p==='/api/documents/'+id+'/ocr-jobs/latest')return route.fulfill({json:{success:true,data:job}});
  if(p==='/api/ocr/gemini/documents/'+id+'/input-info'){inspections++;assert.equal(req.postDataJSON().expectedVersion,version);return route.fulfill({json:{totalPages:89}});}
  if(p==='/api/ocr/gemini/models')return route.fulfill({json:[{id:'gemini-fixture',displayName:'Fixture'}]});
  if(req.method()==='POST'&&p==='/api/documents/'+id+'/ocr-jobs'){
   queued++;assert.deepEqual(req.postDataJSON(),{expectedVersion:version,modelId:'gemini-fixture',pages:'2,6'});
   job={id:'dddddddd-0000-0000-0000-000000000001',documentId:id,status:'Queued',modelId:'gemini-fixture',pages:'2,6',createdAt:'2026-10-25T07:00:00Z'};
   return route.fulfill({status:202,json:{success:true,data:job}});
  }
  if(p==='/api/notifications')return route.fulfill({json:{success:true,data:{items:notice?[notice]:[],totalCount:notice?1:0,unreadCount:notice&&!notice.readAt?1:0,page:1,pageSize:20}}});
  if(p==='/api/notifications/eeeeeeee-0000-0000-0000-000000000001/read'){
   reads++;notice.readAt='2026-10-25T07:02:00Z';return route.fulfill({json:{success:true,data:true}});
  }
  if(p==='/api/ocr/gemini/export-layout'){
   exports++;assert.deepEqual(req.postDataJSON().layout,layout);
   return route.fulfill({contentType:'application/vnd.openxmlformats-officedocument.wordprocessingml.document',body:Buffer.from('fixture-docx')});
  }
  unexpected.push(req.method()+' '+p);return route.abort();
 });
 const page=await context.newPage();page.setDefaultTimeout(60000);page.on('pageerror',e=>errors.push(e.message));
 try{
  await page.goto(base+'/documents/'+id);await page.getByRole('heading',{name:'OCR fixture',exact:true}).waitFor();
  const runButton=page.getByRole('button',{name:'Chạy OCR',exact:true});
  if(role==='Specialist'){
   assert.equal(await runButton.count(),0);assert.equal(await page.locator('.gemini-result-card').getByRole('button',{name:'Tải Word',exact:true}).count(),0);
   await page.getByText('NỘI DUNG GEMINI',{exact:true}).waitFor();
  }else if(missing){
   await runButton.click();await page.getByText('Vui lòng tải tệp lên trước khi chạy OCR.',{exact:true}).waitFor();assert.equal(inspections,0);
  }else{
   await runButton.click();const panel=page.locator('#document-gemini-ocr');
   await panel.getByText('Tài liệu có 89 trang.',{exact:true}).waitFor();assert.equal(await panel.locator('input[type=file]').count(),0);
   await panel.getByRole('button',{name:'Chạy OCR',exact:true}).click();
   await page.locator('#ocr-pages').fill('1-6');assert.equal(await page.getByRole('button',{name:'Gửi yêu cầu OCR',exact:true}).isDisabled(),true);
   await page.locator('#ocr-pages').fill('2,6');await page.getByRole('button',{name:'Gửi yêu cầu OCR',exact:true}).click();
   await page.getByText('Đã xếp hàng OCR.',{exact:false}).waitFor();
   assert.equal(queued,1);assert(await runButton.isDisabled());
   assert(await page.getByRole('button',{name:'Sửa thông tin',exact:true}).isEnabled());
   assert(await page.getByRole('button',{name:'Tải lên file',exact:true}).isEnabled());
   // Reload must recover the durable job without replaying the request.
   job.status='Running';await page.reload();await page.getByText('Đang nhận diện OCR.',{exact:false}).waitFor();assert.equal(queued,1);
   // Leave detail while AI is still pending; the global notification bell remains active.
   if(!stay){await page.getByRole('link',{name:'Thông báo',exact:true}).click();
    await page.getByRole('heading',{name:'Trung tâm thông báo',exact:true}).waitFor();}
   job.status=fail?'Failed':'Succeeded';job.completedAt='2026-10-25T07:01:00Z';job.error=fail?'Model tạm hết hạn mức fixture.':null;
   if(!fail)doc={...doc,ocrDataRaw:JSON.stringify(result),concurrencyVersion:'cccccccc-0000-0000-0000-000000000002'};
   notice={id:'eeeeeeee-0000-0000-0000-000000000001',kind:fail?'OcrFailed':'OcrCompleted',message:fail?'OCR fixture không thành công. Model hết hạn mức.':'OCR hoàn tất công văn fixture.',timestamp:job.completedAt,targetUrl:'/documents/'+id+'#document-ocr-result',readAt:null};
   if(stay){await page.locator('.gemini-result-card').getByText('NỘI DUNG GEMINI',{exact:true}).waitFor();assert(await runButton.isEnabled());
    await page.getByRole('link',{name:'Thông báo',exact:false}).click();}
   await page.getByText(notice.message,{exact:true}).waitFor();await page.locator('.notification-count').getByText('1',{exact:true}).waitFor();assert(streamCount>1);
   if(fail)await page.getByText('OCR thất bại',{exact:true}).waitFor();
   await page.getByRole('button',{name:'Xem chi tiết',exact:true}).click();
   await page.getByRole('heading',{name:'OCR fixture',exact:true}).waitFor();assert.equal(reads,1);
   await page.waitForFunction(()=>!document.querySelector('.notification-count'));
   assert(await runButton.isEnabled());
   if(fail){await page.getByText('OCR không thành công.',{exact:false}).waitFor();assert.equal(await page.getByText('NỘI DUNG GEMINI',{exact:true}).count(),0);}
   else{
    const viewer=page.locator('.gemini-result-card');await viewer.getByText('NỘI DUNG GEMINI',{exact:true}).waitFor();
    assert.equal(await viewer.locator('tbody tr').count(),7);assert.equal(await page.evaluate(()=>!!window.ocrInjected),false);
    await viewer.getByRole('button',{name:'Tải Word',exact:true}).click();await page.waitForFunction(()=>!document.querySelector('.gemini-result-card button[disabled]'));
    assert.equal(exports,1);assert.equal(queued,1);
   }
  }
  assert.equal(queued,role==='Specialist'||missing?0:1);assert.deepEqual(unexpected,[]);assert.deepEqual(errors,[]);
  assert(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth+1));
  const out=process.env.HAU_TEST_ARTIFACTS;if(out){fs.mkdirSync(out,{recursive:true});await page.screenshot({path:path.join(out,`ocr-background-${width}-${role}-${fail?'failed':missing?'missing':'success'}.png`),animations:'disabled'});}
  console.log(JSON.stringify({width,role,fail,missing,stay,queued,exports,reads,streamCount,result:'PASS'}));
 }finally{await context.close();}
}
(async()=>{const browser=await chromium.launch({headless:true,executablePath:process.env.HAU_TEST_CHROMIUM});try{await run(browser,1366,{stay:true});await run(browser,300);await run(browser,390,{fail:true});await run(browser,1366,{role:'Specialist'});await run(browser,300,{missing:true});}finally{await browser.close();}})().catch(e=>{console.error(e);process.exitCode=1;});
