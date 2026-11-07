"""One bounded foreground metadata-only observer, no send/modelbody/fees."""
from pathlib import Path
import modal,json,time,hashlib,re,os
CID='fc-01M6SREBKE87QMT2VQKEMC0Q6T';VO='vo-vUK4QYJsqFt1QTrRerH4Of';PREFIX='task116-m-b0-fresh100m-v1/bf5a18d752bf4724943fb6f6f121ab76';END=1794034943+600
W=Path('/data/raven/hack-sts2-modal-research/task116-M-ops/observer');W.mkdir(mode=0o700,exist_ok=False);v=modal.Volume.from_id(VO).hydrate();last=None;last_update=None;last_pointer_at=time.time();warned=False
H=lambda b:hashlib.sha256(b).hexdigest();CAN=lambda x:json.dumps(x,sort_keys=True,separators=(',',':'))
def read(n,cap=1048576):
 b=bytearray()
 try:
  for chunk in v.read_file(PREFIX+'/'+n):b.extend(chunk);assert len(b)<=cap
 except FileNotFoundError:return None
 return bytes(b)
def save(n,d):
 with(W/n).open('x')as f:f.write(CAN(d));f.flush();os.fsync(f.fileno())
while time.time()<END:
 now=time.time();d={'at':now,'call_id':CID,'prefix':PREFIX,'model_body_reads':0,'new_compute':0}
 try:d['call_result']=modal.FunctionCall.from_id(CID).get(timeout=0);d['call_state']='returned'
 except (TimeoutError,modal.exception.TimeoutError):d['call_state']='live_unfinished'
 except Exception as exc:d['call_state']='unknown_exception';d['exception_type']=type(exc).__module__+'.'+type(exc).__name__
 try:
  values={n:read(n,131072 if n.endswith('gpu-gate.json')else 1048576)for n in ['entry-status.json','run/latest.json','run/final.json','run/initialization-proof.json','run/first-transition-gpu-gate.json']}
  for n,b in values.items():d[n]={'sha256':H(b),'body':json.loads(b)}if b else None
  latest=d['run/latest.json'];match=re.search(r'ckpt-(\d+)\.pt$',latest['body']['checkpoint'])if latest else None;u=int(match[1])if match else None;d['checkpoint_update']=u;d['new_env_steps']=u*131072 if u is not None else None
  if u!=last_update and u is not None:last_update=u;last_pointer_at=now;warned=False
  key=tuple(H(b)if b else None for b in values.values())+(d['call_state'],)
  if key!=last:
   d['closed_metadata']={}
   for n in ['run/config.json','run/status.json','run/summary.json']:
    b=read(n);d['closed_metadata'][n]={'sha256':H(b),'body':json.loads(b)}if b else None
   try:d['checkpoint_listing']=[{'path':e.path,'bytes':e.size}for e in v.listdir(PREFIX+'/run/checkpoints',recursive=False)]
   except FileNotFoundError:d['checkpoint_listing']=[]
   filename='observation-'+str(int(now))+'.json';save(filename,d);print(CAN({'at':now,'call_state':d['call_state'],'update':u,'new_env_steps':d['new_env_steps'],'entry_status':d['entry-status.json']['body'].get('status')if d['entry-status.json']else None,'gate_present':d['run/first-transition-gpu-gate.json']is not None,'final_present':d['run/final.json']is not None,'file':str(W/filename)}),flush=True);last=key
  if now-last_pointer_at>=900 and not warned:
   alert={'at':now,'status':'persistence_progress_requires_verification','last_update':last_update,'last_pointer_at':last_pointer_at,'call_state':d['call_state'],'not_job_terminal':True,'do_not_cancel_or_retry':True};save('persistence-alert-'+str(int(now))+'.json',alert);print(CAN(alert),flush=True);warned=True
  if d['call_state']=='returned':save('terminal-observation.json',d);print(CAN({'provider_terminal_observation':str(W/'terminal-observation.json'),'at':time.time()}),flush=True);break
  if d['call_state']=='unknown_exception':save('provider-exception-observation.json',d);print(CAN({'observer_issue':'provider_exception_requires_verification','file':str(W/'provider-exception-observation.json'),'not_automatic_terminal':True}),flush=True);break
 except Exception as exc:
  err={'at':now,'observer_issue':True,'exception_type':type(exc).__module__+'.'+type(exc).__name__,'call_state':d['call_state'],'no_retry_spawn':True};save('read-error-'+str(int(now))+'.json',err);print(CAN(err),flush=True)
 time.sleep(min(90,max(0,END-time.time())))
else:save('observer-expired.json',{'at':time.time(),'call_id':CID,'status':'observer_bound_elapsed_not_training_terminal','do_not_restart':True})
