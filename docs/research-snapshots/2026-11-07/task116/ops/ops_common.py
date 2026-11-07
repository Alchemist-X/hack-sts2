from pathlib import Path
import json,hashlib,os,time,sys
W=Path(__file__).resolve().parent;H=lambda b:hashlib.sha256(b).hexdigest();CAN=lambda x:json.dumps(x,sort_keys=True,separators=(',',':'))
SCIENCE='edf98387857cec63590d6c4f840ef5b3ed6d748b0c188aec4f4548993c8a6871';SCOPE='c687097c1fcb6d4f6156bee0868ff14cf74a073ab2224fd896ba122e179cf382';POLICY='UNKNOWN_USER_DIRECTED_PLATFORM_ENFORCEMENT'
def read(n):return json.loads((W/n).read_bytes())
def save(n,obj):
 with(W/n).open('x')as f:f.write(CAN(obj));f.flush();os.fsync(f.fileno())
 fd=os.open(W,os.O_DIRECTORY);os.fsync(fd);os.close(fd)
def inputs(auth,bound):
 assert H((W/'root-paid-authorization.json').read_bytes())==auth
 raw=(W/'release.bound.json').read_bytes();assert H(raw)==bound and H(CAN(json.loads(raw)).encode())==bound
 a=read('root-paid-authorization.json');r=json.loads(raw)
 assert a['bound_release_sha256']==bound and a['science_plan_sha256']==r['science_plan_sha256']==SCIENCE and r['root_scope_sha256']==SCOPE
 assert a['project_cap_cents']==r['project_cap_cents']==60000 and a['platform_admission_policy']==POLICY and a['max_submissions']==1 and a['total_commitment_cents']==5762
 assert r['parent_checkpoint']is None and r['execution_mode']['start']=='fresh_random' and r['execution_mode']['target_env_steps']==100007936 and r['timeout_seconds']==21600 and r['training_seconds']==21300
 assert r['spec']['env_seed_namespace']=='MODA10PH116MFRESH1' and r['seed_range']==[0,134217728]
 assert time.time()<a['expires'] and time.time()<r['not_after']
 return a,r

def validate_concurrency(stats):
 for key,value in stats.items():
  assert value['backlog']==0 and value['num_running_inputs']<=(1 if key=='115segment1'else 0) and value['num_total_runners']<=(1 if key=='115segment1'else 0),'only exactexistingS115 mayrunparallel'
