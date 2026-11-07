"""Bounded foreground observer of the one authorized control call. No send/commit/modelread."""
from pathlib import Path
import modal,json,time,hashlib,re,os
CID='fc-01M6SFGP9W1T6AGQZP477J8PCZ';VO='vo-HbcQICsESY3s4xORGRnIbS';W=Path('/data/raven/hack-sts2-modal-research/task115-first-segment-stage/observer');W.mkdir(mode=0o700,exist_ok=True)
claims=modal.Dict.from_id('di-IDu5UOJzIdhEuY6xOakpby').hydrate();key='task115-control-continuation-segment1-v1';handle=None
for _ in range(4):
 try:handle=claims[key];break
 except KeyError:time.sleep(30)
if handle is None:raise RuntimeError('claim not yet visible; do not resubmit')
assert handle['call_id']==CID
PREFIX=key+'/'+handle['attempt'];print(json.dumps({'claimed_handle':handle,'prefix':PREFIX}),flush=True)
END=1794012982+600;v=modal.Volume.from_id(VO).hydrate();last=None;seen25=False
H=lambda b:hashlib.sha256(b).hexdigest();CAN=lambda x:json.dumps(x,sort_keys=True,separators=(',',':'))
def read(n,cap=1000000):
 b=bytearray()
 try:
  for x in v.read_file(PREFIX+'/'+n):
   b.extend(x)
   if len(b)>cap:raise ValueError('boundedmetadata exceeded')
 except FileNotFoundError:return None
 return bytes(b)
def save(name,obj):
 p=W/name
 with p.open('x')as f:f.write(CAN(obj));f.flush();os.fsync(f.fileno())
while time.time()<END:
 now=time.time();r={'at':now,'call_id':CID,'volume_id':VO,'prefix':PREFIX,'model_body_reads':0,'new_compute':0}
 try:r['call_result']=modal.FunctionCall.from_id(CID).get(timeout=0);r['call_state']='returned'
 except (TimeoutError,modal.exception.TimeoutError):r['call_state']='live_unfinished'
 except Exception as e:r['call_state']='unknown';r['exception_type']=type(e).__module__+'.'+type(e).__name__
 try:
  latest=read('run/latest.json');entry=read('entry-status.json');final=read('run/final.json');r['latest']=json.loads(latest)if latest else None;r['entry']=json.loads(entry)if entry else None;r['final']=json.loads(final)if final else None
  key=(H(latest)if latest else None,H(entry)if entry else None,H(final)if final else None,r['call_state'])
  match=re.search(r'ckpt-(\d+)\.pt$',r['latest']['checkpoint'])if r['latest']else None;u=int(match[1])if match else None;r['checkpoint_update']=u;r['derived_additional_steps']=(u-3783)*131072 if u is not None else None
  changed=key!=last;cross25=not seen25 and r['derived_additional_steps']is not None and r['derived_additional_steps']>=25034752
  if changed or cross25:
   r['documents']={}
   for n in ['run/config.json','run/status.json','run/summary.json','run/resume-proof.json']:
    b=read(n);r['documents'][n]={'sha256':H(b),'body':json.loads(b)}if b else None
   try:r['checkpoint_listing']=[{'path':x.path,'size':x.size}for x in v.listdir(PREFIX+'/run/checkpoints',recursive=False)]
   except FileNotFoundError:r['checkpoint_listing']=[]
   r['milestone25_additional']=cross25;name='observation-'+str(int(now))+'.json';save(name,r)
   event={'at':now,'call_state':r['call_state'],'checkpoint_update':u,'derived_additional_steps':r['derived_additional_steps'],'milestone25_additional':cross25,'file':str(W/name),'entry_status':r['entry'].get('status')if r['entry']else None,'final_status':r['final'].get('status')if r['final']else None};print(CAN(event),flush=True)
   if cross25:seen25=True
   last=key
  if final or r['call_state']=='returned':
   save('terminal-observation.json',r);print(CAN({'terminal_observation':str(W/'terminal-observation.json'),'at':time.time()}),flush=True);break
 except Exception as e:
  err={'at':now,'exception_type':type(e).__module__+'.'+type(e).__name__,'call_state':r['call_state'],'same_call_only':True};save('read-error-'+str(int(now))+'.json',err);print(CAN(err),flush=True)
 time.sleep(min(90,max(0,END-time.time())))
else:save('observer-expired.json',{'at':time.time(),'call_id':CID,'status':'observer_bound_elapsed_not_job_failure','do_not_restart':True})
