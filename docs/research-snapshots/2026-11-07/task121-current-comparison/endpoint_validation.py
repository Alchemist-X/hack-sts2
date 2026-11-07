"""Accepted114 full256/protocol validator with explicit accepted actual-dose parameter; no data on import."""
from pathlib import Path
import json,hashlib,os
from validation import validate_rows
from eval_summary import stats
DEV_SHA='ee1be1ba52dd1fa597f600df91fbca46f0f291c6c59c4dedb49e81f561c9e340'

def sha(p):return hashlib.sha256(Path(p).read_bytes()).hexdigest()

def read(p):return json.loads(Path(p).read_text())

def location(value,root):
    marker='/artifacts/modal-research/2026-11-04/research/'
    if marker in str(value):return root/str(value).split(marker,1)[1]
    p=Path(value);return p if p.is_absolute()else root/p

def checked(ref,root,cap=2*1024**2,parse=True):
    p=location(ref['path'],root)
    if p.is_symlink()or not p.is_file()or not 0<p.stat().st_size<=cap or p.stat().st_size!=ref['size']or sha(p)!=ref['sha256']:raise ValueError('bound input differs')
    return read(p)if parse else None

def exclusive(p,value):
    with Path(p).open('x')as f:f.write(json.dumps(value,sort_keys=True,indent=2,allow_nan=False)+'\n');f.flush();os.fsync(f.fileno())

def endpoint(ref,root,keys,expected_dose):
    ready=checked(ref['ready'],root,256000)
    if ready.get('status')!='completed'or ready.get('error_type')is not None:raise ValueError('incomplete endpoint cannot replace full dose')
    model=ref['actual_model'];steps,updates=expected_dose
    for k,v in {'checkpoint_sha256':model['checkpoint_file_sha256'],'checkpoint_steps':steps,'checkpoint_updates':updates,'dev_sha256':DEV_SHA,'protocol':'native-original-greedy-dev256-replay'}.items():
        if ready.get(k)!=v:raise ValueError('exact checkpoint/dose/dev protocol differs')
    if (model['decisions'],model['updates'],model['input_arch'],model['critic_arch'])!=(steps,updates,'reward_choices_public_handselect_v1','state_legal_candidates_detached_mean_v1'):raise ValueError('architecture or dose differs')
    preflight=checked(ref['preflight'],root)
    for k,v in {'checkpoint_sha256':model['checkpoint_file_sha256'],'checkpoint_bytes':model['bytes'],'checkpoint_steps':steps,'checkpoint_updates':updates,'source_code_commit':'53dd1d94616a6c1f32c177d8c45e6686571c4188','bundle_sha256':'b2b81e4053200c6d76b3d3a285b08b01a476b14abe96eadeba76783790ffb88b','dev_sha256':DEV_SHA,'model_spec_sha256':'63e9cc460886b2b26836cff57514c24dfb39253b87cd31eea746fb0b061ef10b','precision':'strict-FP32-TF32-off-native-context','policy':'greedy','search':False,'loop_breaker':False,'games':256,'step_cap':1200,'floor_step_cap':80,'max_rows':192,'threads':8,'repeats':1,'training':False,'blind':False}.items():
        if preflight.get(k)!=v:raise ValueError('original formal evaluation preflight differs: '+k)
    rd=location(ref['ready']['path'],root).resolve().parent
    for role in ('metrics','per_seed','preflight'):
        r=ref[role];rel=r['relative_path']
        if rel!='evaluation/'+{'metrics':'metrics.json','per_seed':'per_seed.json','preflight':'preflight.json'}[role]or location(r['path'],root).resolve()!=rd/rel or ready['files'].get(rel)!={'sha256':r['sha256'],'size':r['size']}:raise ValueError('ready payload binding differs')
    m=checked(ref['metrics'],root);rows=checked(ref['per_seed'],root);validation=validate_rows(m,rows,keys)
    for role in ('execution_receipt','independent_acceptance'):checked(ref[role],root,parse=False)
    return rows,stats(m,rows),validation
