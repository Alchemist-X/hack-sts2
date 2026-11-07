"""One original-dev samecall readonly observer+hash archive. No models/Volume writes/send."""
from pathlib import Path,PurePosixPath
import modal,time,json,hashlib,os
import sys,re,signal,math
STAGE=Path('/data/raven/hack-sts2-modal-research/task116-finaldev-ops-stage')
W=Path('/data/raven/hack-sts2-modal-research/task116-finaldev-actual')
raw=(STAGE/'submission-receipt.json').read_bytes()
assert re.fullmatch('[0-9a-f]{64}',sys.argv[1]) and hashlib.sha256(raw).hexdigest()==sys.argv[1]
submitted=json.loads(raw); assert submitted['existing_slot_cents']==744 and submitted['new_pledge_cents']==0 and submitted['evaluation_games']==256
CID=submitted['call_id']; VO=submitted['output_volume_id']; DICT=submitted['claim_dict_id']; KEY=submitted['output_prefix']; BOUND=submitted['release_sha256']; END=submitted['absolute_deadline']+600
assert re.fullmatch('fc-[A-Za-z0-9]+',CID) and re.fullmatch('vo-[A-Za-z0-9]+',VO) and re.fullmatch('di-[A-Za-z0-9]+',DICT)
assert KEY=='task116-final-only-M-dev256' and time.time()<END
def deadline_expired(_signum,_frame):
 obj('observer-expired-not-training-terminal.json',{'at':time.time(),'do_not_respawn':True})
 raise SystemExit(124)
signal.signal(signal.SIGALRM,deadline_expired)
W.mkdir(mode=0o700,exist_ok=False)
H=lambda b:hashlib.sha256(b).hexdigest();CAN=lambda x:json.dumps(x,sort_keys=True,separators=(',',':'),allow_nan=False)
def save(n,b):
 p=W/n;p.parent.mkdir(mode=0o700,parents=True,exist_ok=True)
 with p.open('xb')as f:f.write(b);f.flush();os.fsync(f.fileno())
def obj(n,x):save(n,CAN(x).encode())
signal.setitimer(signal.ITIMER_REAL,max(0.001,END-time.time()))
v=modal.Volume.from_id(VO).hydrate();d=modal.Dict.from_id(DICT).hydrate();prefix=None;last=None
while time.time()<END:
 now=time.time();obs={'at':now,'call_id':CID,'model_body_reads':0,'new_compute':0}
 try:obs['result']=modal.FunctionCall.from_id(CID).get(timeout=0);obs['call_state']='returned'
 except (TimeoutError,modal.exception.TimeoutError):obs['call_state']='live_unfinished'
 except Exception as e:obs['call_state']='unknown_exception';obs['exception_type']=type(e).__module__+'.'+type(e).__name__
 if prefix is None:
  try:
   handle=d[KEY];assert handle['call_id']==CID and handle['release_sha256']==BOUND and handle['absolute_deadline']==submitted['absolute_deadline'] and re.fullmatch('[0-9a-f]{32}',handle['attempt']);prefix=KEY+'/'+handle['attempt'];obj('claim-handle.json',handle)
  except KeyError:pass
 obs['prefix']=prefix
 def read(rel,cap):
  data=bytearray()
  for chunk in v.read_file(prefix+'/'+rel):data.extend(chunk);assert len(data)<=cap
  return bytes(data)
 if prefix:
  for n in ['handle.json','entry-status.json','result/ready.json']:
   try:b=read(n,1048576);obs[n]={'sha256':H(b),'body':json.loads(b)}
   except (FileNotFoundError,modal.exception.NotFoundError):obs[n]=None
 key=(obs['call_state'],obs.get('entry-status.json',{}).get('sha256')if obs.get('entry-status.json')else None,obs.get('result/ready.json',{}).get('sha256')if obs.get('result/ready.json')else None)
 if key!=last:obj('observation-'+str(int(now))+'.json',obs);print(CAN({'at':now,'call_state':obs['call_state'],'prefix':prefix,'entry_status':obs['entry-status.json']['body'].get('status')if obs.get('entry-status.json')else None,'ready_present':obs.get('result/ready.json')is not None}),flush=True);last=key
 if obs['call_state']=='unknown_exception':obj('provider-exception-unverified.json',obs);print('UNVERIFIED_PROVIDER_EXCEPTION_NO_RETRY',flush=True);break
 if obs['call_state']=='returned':
  obj('provider-terminal-receipt.json',{'call_id':CID,'at':now,'provider_terminal_verified':True,'observation_class':'FunctionCall_returned','result':obs['result'],'raw_observation_sha256':H(CAN(obs).encode())});assert obs['result']['status']=='completed' and prefix and obs['result']['attempt']==handle['attempt'] and obs['result']['ready']==prefix+'/result/ready.json' and obs.get('result/ready.json') and obs.get('handle.json',{}).get('body')==handle and obs.get('entry-status.json',{}).get('body')=={'status':'completed','handle':handle,'ready':'result/ready.json'};readyraw=read('result/ready.json',1048576);assert H(readyraw)==obs['result/ready.json']['sha256'];ready=json.loads(readyraw);assert ready['status']=='completed'and ready['checkpoint_sha256']=='89641c1e8baf823376a4ed21145bb903e07432835fff53237b4b60f4cc569c9f'and ready['checkpoint_steps']==100007936 and ready['checkpoint_updates']==763 and ready['model_profile']=='M-current-recipe' and ready['base_params']==16253584 and ready['transition_params']==74052 and ready['dev_sha256']=='ee1be1ba52dd1fa597f600df91fbca46f0f291c6c59c4dedb49e81f561c9e340';save('ready.json',readyraw);obj('entry-status.json',obs['entry-status.json']['body']);refs=ready['files'];assert isinstance(refs,dict)and len(refs)==264;total=0;downloaded={}
  core=['evaluation/'+n for n in ('config.json','metrics.json','per_seed.json','preflight.json','replay-audit.json','report.json','report.md','run.json')]
  assert set(core)<=set(refs) and all(re.fullmatch(r'evaluation/games/SILENT-[A-Z0-9]{10}\.summary\.json',n) for n in set(refs)-set(core))
  for n in core+[n for n in refs if n not in core]:
   path=PurePosixPath(n);assert not path.is_absolute()and all(x not in ('','..','.')for x in path.parts)and path.suffix not in ('.pt','.pth');ref=refs[n];assert type(ref['size'])is int and 0<=ref['size']<=1024**3;total+=ref['size'];assert total<=1024**3;b=read('result/'+n,ref['size']);assert len(b)==ref['size']and H(b)==ref['sha256'];save('payload/'+n,b);downloaded[n]={'sha256':H(b),'bytes':len(b)}
   if n==core[-1]:
    obj('core-archive-receipt.json',{'call_id':CID,'prefix':prefix,'ready_sha256':H(readyraw),'files':dict(downloaded),'model_reads':0});print(CAN({'core_complete':True,'core_dir':str(W/'payload'),'ready_sha256':H(readyraw),'provider_terminal':True,'metrics':obs['result'].get('metrics')}),flush=True)
  obj('archive-verification.json',{'call_id':CID,'prefix':prefix,'release_sha256':BOUND,'ready_sha256':H(readyraw),'files':downloaded,'total_bytes':total,'file_count':len(downloaded),'all_hashes_verified':True,'model_body_reads':0,'new_compute':0,'Volume_writes':0});print(CAN({'archive_complete':True,'file_count':len(downloaded),'total_bytes':total,'path':str(W)}),flush=True);break
 time.sleep(min(60,max(0,END-time.time())))
else:obj('observer-expired-not-training-terminal.json',{'at':time.time(),'do_not_respawn':True})
signal.setitimer(signal.ITIMER_REAL,0)
