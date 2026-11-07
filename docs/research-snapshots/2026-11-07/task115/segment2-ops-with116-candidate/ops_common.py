from pathlib import Path
import json,hashlib,time,os,sys
W=Path(__file__).resolve().parent
H=lambda b:hashlib.sha256(b).hexdigest()
CAN=lambda x:json.dumps(x,sort_keys=True,separators=(',',':'))
def read(n):return json.loads((W/n).read_bytes())
def save(n,x):
 with(W/n).open('x')as f:f.write(CAN(x));f.flush();os.fsync(f.fileno())
 fd=os.open(W,os.O_DIRECTORY);os.fsync(fd);os.close(fd)
def accepted_parent(proof_sha,release):
 raw=(W/'first-terminal-accepted.json').read_bytes();assert H(raw)==proof_sha
 p=json.loads(raw)
 assert release['segment']['index']==2 and release['segment']['target_env_steps']==695861248 and release['segment']['target_updates']==5309 and release['segment']['beta']==0
 assert p['status']=='accepted_full_endpoint' and p['provider_terminal_verified']is True and p['model_body_hash_verified']is True
 assert p['call_id']=='fc-01M6SFGP9W1T6AGQZP477J8PCZ'
 assert p['parent']==release['parent'] and p['parent']['decisions']==595853312 and p['parent']['updates']==4546
 assert p['model_body_sha256']==release['parent']['model_sha256']
 assert p['parent_volume_id']==release['parent_volume_id']=='vo-HbcQICsESY3s4xORGRnIbS' and p['parent_path']==release['parent_path']
 assert p['parent_metadata_evidence_sha256']==release['parent_evidence_sha256']
 for k in ['root_final_acceptance_sha256','independent_final_acceptance_sha256','provider_terminal_receipt_sha256']:
  assert isinstance(p.get(k),str) and len(p[k])==64 and all(x in '0123456789abcdef'for x in p[k])
 assert p['parent']['cursor']<=p['parent']['issued'] and p['parent']['namespace']=='MODA10PH104FRESH1'
 start,end=release['segment']['seed_range'];assert end-start==2**27 and start>=max(1099783413606,release['old_reserved_end'],p['parent']['issued'],p['actual_prior_reserved_end'])
 return p
def inputs(auth,bound):
 assert H((W/'root-authorization.json').read_bytes())==auth
 raw=(W/'release.bound.json').read_bytes();assert H(raw)==bound and H(CAN(json.loads(raw)).encode())==bound
 a=read('root-authorization.json');r=json.loads(raw)
 assert a['forward_admission_policy_sha256']=='6241b01cbd1238cbdc569e80af4e8e525077f0c2ae41342404c00fbda13432eb'
 assert a['bound_release_sha256']==bound and a['science_plan_sha256']==r['science_plan_sha256']=='548320e5ba99de37c8cfbf93031164ab948259003f66c2394d1eff7b152de2fb'
 assert a['authorized_segment']==r['segment']['index']==2 and a['max_submissions']==1
 assert a['segment_scope_sha256']==r['segment_authority_sha256'] and len(a['segment_scope_sha256'])==64
 assert r['segment']['target_env_steps']==695861248 and r['segment']['target_updates']==5309 and r['segment']['beta']==0
 accepted_parent(a['first_parent_proof_sha256'],r)
 assert time.time()<a['expires'] and time.time()<r['not_after']
 return a,r
