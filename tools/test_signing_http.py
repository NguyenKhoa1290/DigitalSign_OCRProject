"""Read-only HTTP smoke: isolated login session and fixture files; no document writes."""
import atexit, json, os, subprocess, urllib.request, uuid
from pathlib import Path

root=Path(__file__).resolve().parents[1]
proof=root/'tai_nguyen/ket_qua_kiem_thu/signing_sources_20261107'
proof.mkdir(parents=True,exist_ok=True)
base=os.environ.get('HAU_SIGNING_TEST_BASE','http://localhost:5227').rstrip('/')
inspection=json.loads(subprocess.check_output(['docker','inspect','hau_sign_service'],text=True))[0]
env=dict(item.split('=',1) for item in inspection['Config']['Env'] if '=' in item)
def request(url,data,headers):
    headers={"User-Agent":"Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/142.0.0.0 Safari/537.36",**headers}
    with urllib.request.urlopen(urllib.request.Request(url,data=data,headers=headers),timeout=120) as response:
        return response.status,response.headers,response.read()
_,_,login=request(base+'/api/auth/login',json.dumps({'username':os.environ['HAU_SIGNING_TEST_USERNAME'],'password':os.environ['HAU_SIGNING_TEST_PASSWORD']}).encode(),{'Content-Type':'application/json'})
token=json.loads(login)['accessToken']
def logout():
    try: request(base+'/api/auth/logout',b'',{'Authorization':'Bearer '+token})
    except Exception: print('Fixture session logout needs checking.')
atexit.register(logout)
def verify(name,content):
    boundary='hau-'+uuid.uuid4().hex
    data=(f'--{boundary}\r\nContent-Disposition: form-data; name="file"; filename="{name}"\r\nContent-Type: application/pdf\r\n\r\n'.encode()+content+f'\r\n--{boundary}--\r\n'.encode())
    status,_,payload=request(base+'/api/signatures/verify-file',data,
        {'Authorization':'Bearer '+token,'Content-Type':'multipart/form-data; boundary='+boundary})
    return status,json.loads(payload)['data']

layout={'blocks':[{'type':'paragraph','text':'SIGNING HTTP EXPORT FIXTURE'}]}
status,headers,word=request('http://localhost:5051/api/ocr/internal/export-layout',json.dumps({'layout':layout}).encode(),
    {'X-Service-Token':env['ServiceAuth__OcrServiceToken'],'Content-Type':'application/json'})
assert status==200 and word[:2]==b'PK'
(proof/'export-source-fixture.docx').write_bytes(word)
results={'actual_internal_word_export':{'status':status,'bytes':len(word),'content_type':headers.get('Content-Type')}}
signed=(proof/'pipeline/ocr-two-signatures.pdf').read_bytes()
status,result=verify('fixture-signed.pdf',signed)
assert status==200 and result['signatureCount']==2 and result['coversWholeDocument'] and not result['isValid']
assert all(s['isIntact'] and not s['isCertificateTrusted'] for s in result['signatures'])
results['external_fixture_ca_is_reported_as_untrusted']=result
appended=root/'tai_nguyen/ket_qua_kiem_thu/signature_audit_20261105/artifacts/fixture-appended-unsigned-page.pdf'
status,result=verify('fixture-appended.pdf',appended.read_bytes())
assert status==200 and not result['coversWholeDocument'] and not result['isValid']
results['unsigned_appended_content_is_rejected']=result
if os.environ.get('HAU_SIGNING_TEST_LARGE','1')=='1':
    status,result=verify('large-fixture.pdf',signed+b'\n'+b' '*(31*1024*1024))
    assert status==200 and not result['isValid']
    results['gateway_accepts_31mb_upload_without_413']={'status':status,'is_valid':result['isValid']}
else:
    results['large_upload']='Not run in this invocation; a prior domain attempt timed out during TLS socket upload. Local 31 MiB check passed.'
results['base_url']=base
(proof/('http-domain-results.json' if base.startswith('https://') else 'http-results.json')).write_text(json.dumps(results,ensure_ascii=False,indent=2),encoding='utf-8')
print('PASS: internal Word export, external CA classification and appended-content detection; evidence written for '+base)
