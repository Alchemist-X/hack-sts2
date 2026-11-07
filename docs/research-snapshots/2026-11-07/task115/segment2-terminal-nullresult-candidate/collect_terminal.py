"""Root-selected one Task115 terminal checkpoint, body to Raven only. No automatic fallback."""
from pathlib import Path,PurePosixPath
import json,hashlib,re,os,time,argparse
H=lambda b:hashlib.sha256(b).hexdigest()
CAN=lambda x:json.dumps(x,sort_keys=True,separators=(',',':'))
CID='fc-01M6SSSTZJRPZSWJNQDBMZTFC6';BOUND='bb6322814aa1590d8669d2ac9219c6107b4bbd93047fc10b83d6e17f80175ab1'
ATTEMPT='5e4a4c4e99a34e709758616860e11694';OUTPUT_VOLUME='vo-90LxK7rxHxxHqfzuDgUhRa'
CLASSES={'completed','resource_censored_with_checkpoint','failed_with_checkpoint','output_censored_with_checkpoint','failed','cancelled','timed_out'}
def sha_file(p):
 with p.open('rb')as f:return hashlib.file_digest(f,'sha256').hexdigest()
def select_metadata(release,selection,terminal,read,probe):
 assert release['output_volume_id']==OUTPUT_VOLUME and release['segment']['index']==2 and release['segment']['beta']==0 and (release['parent']['decisions'],release['parent']['updates'],release['segment']['target_env_steps'],release['segment']['target_updates'])==(595853312,4546,695861248,5309)
 assert release['parent']['model_sha256']=='b7b510d48141fd86afa3c3ec6789a30417bb188d1ec1e63adb2e68431b1edc1a'
 assert terminal['call_id']==CID and terminal['provider_terminal_verified']is True and terminal['observation_class']in{'FunctionCall_returned','FunctionCall_verified_terminal_exception','SDK_verified_terminal_cancel'}
 if terminal['observation_class']=='FunctionCall_returned':assert terminal['result']['status']in CLASSES
 else:assert terminal['status']in CLASSES
 assert selection['root_selected']is True and selection['same_attempt']is True and selection['call_id']==CID and selection['attempt']==ATTEMPT
 attempt=selection['attempt'];result=terminal.get('result')or{}
 if 'attempt'in result:assert result['attempt']==attempt
 prefix=release['release_id']+'/'+attempt
 def optional(n,cap):
  try:return read(prefix+'/'+n,cap)
  except FileNotFoundError:return None
 handle=json.loads(read(prefix+'/handle.json',65536));assert handle['call_id']==CID and handle['attempt']==attempt and handle['release_sha256']==BOUND
 finalraw=optional('run/final.json',1000000);final_pt=probe(prefix+'/run/final.pt');normal_present=finalraw is not None or final_pt is not None
 assert not normal_present or (finalraw is not None and final_pt is not None),'lone finalpayload or finalmetadata is invalidnormalfinal; no latest fallback'
 latest_raw=read(prefix+'/run/latest.json',1000000);latest=json.loads(latest_raw)
 assert latest['parent_model_sha256']==release['parent']['model_sha256'] and 0<latest['bytes']<=134217728
 name=latest['checkpoint'];assert isinstance(name,str)and re.fullmatch(r'checkpoints/ckpt-[0-9]{7}\.pt',name)
 immutable=read(prefix+'/run/'+name.replace('.pt','.manifest.json'),1000000);assert immutable==latest_raw
 update=int(re.search('ckpt-([0-9]+)',name)[1]);assert release['parent']['updates']<update<=release['segment']['target_updates'],'zero-step/older/future checkpoint forbidden'
 if normal_present:
  assert final_pt['bytes']==latest['bytes'] and final_pt['type']=='file'
  assert selection['kind']=='normal_final' and selection['normal_final_present']is True and selection['selected_file']=='final.pt','present final may not be bypassed'
  final=json.loads(finalraw);assert final['final_checkpoint']=='final.pt' and final['final_sha256']==latest['sha256'] and final['latest']==latest
  selected_remote='run/final.pt'
 else:
  assert selection['kind']=='sole_atomic_latest' and selection['normal_final_present']is False and selection['selected_file']==PurePosixPath(name).name
  final=None;selected_remote='run/'+name
 assert selection['selected_sha256']==latest['sha256'] and selection['selected_bytes']==latest['bytes']
 return {'prefix':prefix,'attempt':attempt,'selected_remote':selected_remote,'selected_basename':selection['selected_file'],'latest':latest,'final':final,'final_raw':finalraw,'latest_raw':latest_raw,'immutable_raw':immutable,'normal_final_absence_witnessed':not normal_present,'selected_update':update}
def execute(args):
 def pinned(path,expected):
  raw=path.read_bytes();assert H(raw)==expected;return json.loads(raw)
 auth=pinned(args.authorization,args.authorization_sha)
 assert auth['action']=='EXECUTE_TASK115_EXACT_TERMINAL_COLLECTION_ONCE' and auth['call_id']==CID and auth['bound_release_sha256']==BOUND and auth['allow_once_terminal_collection']is True and time.time()<auth['expires']
 assert H(Path(__file__).read_bytes())==auth['collector_sha256']
 release=pinned(args.release,BOUND);selection=pinned(args.selection,auth['selection_sha256']);terminal=pinned(args.provider_terminal,auth['provider_terminal_receipt_sha256'])
 assert args.output_dir==Path(auth['output_dir']) and args.output_dir.is_absolute()and str(args.output_dir).startswith('/data/raven/')
 import modal
 v=modal.Volume.from_id(release['output_volume_id']).hydrate()
 def read(n,cap):
  b=bytearray()
  for chunk in v.read_file(n):
   b.extend(chunk)
   if len(b)>cap:raise ValueError('bounded terminal metadata exceeded')
  return bytes(b)
 run_prefix=release['release_id']+'/'+selection['attempt']+'/run'
 entries={}
 for i,item in enumerate(v.iterdir(run_prefix,recursive=False)):
  assert i<128,'bounded run metadata listing exceeded'
  name=PurePosixPath(item.path).name
  if name=='final.pt':
   from modal.volume import FileEntryType
   entries[run_prefix+'/final.pt']={'bytes':item.size,'type':'file'if item.type==FileEntryType.FILE else 'not_file'}
 chosen=select_metadata(release,selection,terminal,read,lambda n:entries.get(n))
 D=args.output_dir;D.mkdir(mode=0o700,exist_ok=True);assert not D.is_symlink();os.chmod(D,0o700)
 def save(n,b):
  p=D/n
  if p.exists():assert not p.is_symlink()and p.read_bytes()==b
  else:
   with p.open('xb')as f:f.write(b);f.flush();os.fsync(f.fileno())
  os.chmod(p,0o400)
 refs={};missing=[]
 for n in ['config.json','status.json','summary.json','resume-proof.json']:
  try:b=read(chosen['prefix']+'/run/'+n,1000000)
  except FileNotFoundError:missing.append(n);continue
  save(n,b);refs[n]={'bytes':len(b),'sha256':H(b)}
 for n,b in [('latest.json',chosen['latest_raw']),('selected-checkpoint.manifest.json',chosen['immutable_raw'])]:save(n,b);refs[n]={'bytes':len(b),'sha256':H(b)}
 if chosen['final_raw']is not None:save('final.json',chosen['final_raw']);refs['final.json']={'bytes':len(chosen['final_raw']),'sha256':H(chosen['final_raw'])}
 total=0
 for n in ['metrics.jsonl','episodes.jsonl','cycles.jsonl','cycle_episodes.jsonl','train.log','evals.jsonl']:
  try:b=read(chosen['prefix']+'/run/'+n,268435456-total)
  except FileNotFoundError:missing.append(n);continue
  total+=len(b);save(n,b);refs[n]={'bytes':len(b),'sha256':H(b)}
 assert 'metrics.jsonl'in refs and 'config.json'in refs,'missing core data is engineeringfailure, no alternate model'
 if chosen['final_raw']is not None:assert all(n in refs for n in ['episodes.jsonl','cycles.jsonl','cycle_episodes.jsonl','train.log','evals.jsonl','status.json','summary.json','resume-proof.json']),'normal final closed roles missing; no latest bypass'
 model=D/chosen['selected_basename'];expected=chosen['latest'];intent=D/'model-download-intent.json';assert not model.is_symlink()
 if not model.exists():
  assert not intent.exists(),'prior download attempted; reconcile samecache, never automaticredownload'
  save('model-download-intent.json',CAN({'at':time.time(),'call_id':CID,'attempt':chosen['attempt'],'remote_path':chosen['prefix']+'/'+chosen['selected_remote'],'expected_sha256':expected['sha256'],'expected_bytes':expected['bytes'],'root_authority_sha256':args.authorization_sha}).encode())
  tmp=model.with_suffix(model.suffix+'.tmp');count=0
  with tmp.open('xb')as f:
   for chunk in v.read_file(chosen['prefix']+'/'+chosen['selected_remote']):
    count+=len(chunk);assert count<=expected['bytes'];f.write(chunk)
   f.flush();os.fsync(f.fileno())
  assert count==expected['bytes']and sha_file(tmp)==expected['sha256'];os.replace(tmp,model);os.chmod(model,0o400)
 assert not model.is_symlink()and model.stat().st_size==expected['bytes']and sha_file(model)==expected['sha256']
 refs[model.name]={'bytes':expected['bytes'],'sha256':expected['sha256']}
 save('provider-terminal-receipt.json',args.provider_terminal.read_bytes())
 manifest={'schema':'task115-terminal-artifact-manifest-v1','call_terminal':True,'segment_index':2,'call_id':CID,'attempt':chosen['attempt'],'release_sha256':BOUND,'learning_overlay':release['learning_overlay'],'files':refs,'missing_roles':missing,'selection_sha256':auth['selection_sha256'],'provider_terminal_receipt_sha256':auth['provider_terminal_receipt_sha256'],'model_body_independently_hashed_on_raven':True,'model_body_copied_to_mac':False,'normal_final_absence_witnessed':chosen['normal_final_absence_witnessed'],'selected_update':chosen['selected_update'],'scientific_acceptance':False}
 save('checkpoint-manifest.json',CAN(manifest).encode());print(CAN({'status':'ARCHIVED_ROOT_SELECTED_SAMECALL_TERMINAL','directory':str(D),'manifest_sha256':sha_file(D/'checkpoint-manifest.json'),'model_sha256':expected['sha256'],'model_copied_to_mac':False,'new_compute':0}))
if __name__=='__main__':
 p=argparse.ArgumentParser()
 for n in ['authorization','release','selection','provider-terminal','output-dir']:p.add_argument('--'+n,type=Path,required=True)
 p.add_argument('--authorization-sha',required=True);execute(p.parse_args())
