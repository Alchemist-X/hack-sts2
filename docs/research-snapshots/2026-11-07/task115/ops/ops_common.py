from pathlib import Path
import json,hashlib,time,os,sys
W=Path(__file__).resolve().parent
H=lambda b:hashlib.sha256(b).hexdigest()
CAN=lambda x:json.dumps(x,sort_keys=True,separators=(',',':'))
def read(n):return json.loads((W/n).read_bytes())
def save(n,x):
 with(W/n).open('x')as f:f.write(CAN(x));f.flush();os.fsync(f.fileno())
 fd=os.open(W,os.O_DIRECTORY);os.fsync(fd);os.close(fd)
def inputs(auth,bound):
 assert H((W/'root-authorization.json').read_bytes())==auth
 raw=(W/'release.bound.json').read_bytes();assert H(raw)==bound and H(CAN(json.loads(raw)).encode())==bound
 a=read('root-authorization.json');r=json.loads(raw)
 assert a['bound_release_sha256']==bound and a['science_plan_sha256']==r['science_plan_sha256']=='548320e5ba99de37c8cfbf93031164ab948259003f66c2394d1eff7b152de2fb'
 assert a['authorized_segment']==r['segment']['index']==1 and a['max_submissions']==1
 assert r['parent']['model_sha256']=='7f4de995ae7a0434078e73ef96f76ae3b27a77e746054c7b4ef61822ee543a6c'
 assert r['segment']['seed_range']==[1099649195878,1099783413606] and r['segment']['target_env_steps']==595853312
 assert time.time()<a['expires'] and time.time()<r['not_after']
 return a,r
