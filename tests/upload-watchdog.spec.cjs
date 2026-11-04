// Deterministic transport tests: no network, files, application data or cloud calls.
const assert=require('node:assert/strict'),fs=require('node:fs'),vm=require('node:vm');
function fixture(callback=()=>Promise.resolve()){
 const timers=new Map();let counter=0,xhr;
 const context={window:{},document:{getElementById:()=>({files:[{name:'fixture.jpg',size:222395}]})},
  FormData:class{append(){}},Uint8Array,ArrayBuffer,Date,Math,Promise,
  setTimeout:(fn,delay)=>{const id=++counter;timers.set(id,{fn,delay});return id;},clearTimeout:id=>timers.delete(id),
  XMLHttpRequest:class{
   constructor(){xhr=this;this.upload={};this.aborts=0;this.status=200;this.response=new ArrayBuffer(0);}
   open(){}setRequestHeader(){}send(){}getAllResponseHeaders(){return 'Content-Type: application/json\r\n';}
   abort(){this.aborts++;this.onabort?.();}
  }};
 vm.runInNewContext(fs.readFileSync('Frontend/wwwroot/js/document-files.js','utf8'),context);
 const api=context.window.documentFiles,key=api.captureUpload('fixture',52428800);
 const promise=api.upload(key,'https://fixture.invalid/upload','fixture-token','file',{invokeMethodAsync:callback});
 return{timers,promise,get xhr(){return xhr;},fire(delay){const entry=[...timers].find(([,t])=>t.delay===delay);assert(entry,`Missing ${delay} timer`);timers.delete(entry[0]);entry[1].fn();}};
}
(async()=>{
 let f=fixture();const first=[...f.timers.keys()][0];f.xhr.upload.onprogress({loaded:16384,total:222599,lengthComputable:true});assert(!f.timers.has(first));f.fire(45000);
 let r=await f.promise;assert.equal(r.error,'UploadStalled');assert.equal(r.status,0);assert.equal(f.xhr.aborts,1);assert.equal(f.timers.size,0);console.log('PASS stalled 7% upload resolves with a timeout and aborts once');
 f=fixture();f.xhr.upload.onload();assert(![...f.timers.values()].some(t=>t.delay===45000));f.fire(180000);r=await f.promise;assert.equal(r.error,'ProcessingTimeout');assert.equal(f.timers.size,0);console.log('PASS processing uses a separate timeout after bytes are sent');
 f=fixture(name=>name==='Uploaded'?new Promise(()=>{}):Promise.resolve());f.xhr.upload.onload();f.xhr.onload();r=await Promise.race([f.promise,new Promise((_,reject)=>setTimeout(()=>reject(Error('Response blocked on UI progress callback')),100))]);assert.equal(r.status,200);assert.equal(f.timers.size,0);console.log('PASS response completion never waits for a hung UI progress callback');
 f=fixture();f.xhr.onerror();r=await f.promise;assert.equal(r.error,'UploadNetworkError');assert.equal(f.timers.size,0);f.xhr.upload.onprogress({loaded:1,total:2,lengthComputable:true});assert.equal(f.timers.size,0);console.log('PASS network failure clears timers and ignores late progress');
})().catch(e=>{console.error(e);process.exitCode=1;});
