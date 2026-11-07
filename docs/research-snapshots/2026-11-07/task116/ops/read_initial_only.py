import modal,json,time,hashlib
from pathlib import Path
CID='fc-01M6SREBKE87QMT2VQKEMC0Q6T';release_id='task116-m-b0-fresh100m-v1';d=modal.Dict.from_id('di-KeDVgFU923sIFXoy52dVxp').hydrate();handle=d[release_id];assert handle['call_id']==CID
v=modal.Volume.from_id('vo-vUK4QYJsqFt1QTrRerH4Of').hydrate();prefix=release_id+'/'+handle['attempt'];out=Path('/data/raven/hack-sts2-modal-research/task116-M-ops/initial-readonly');out.mkdir(mode=0o700,exist_ok=False);result={'at':time.time(),'call_id':CID,'handle':handle,'prefix':prefix,'model_body_reads':0,'new_compute':0,'files':{}}
try:result['get0_result']=modal.FunctionCall.from_id(CID).get(timeout=0);result['call_state']='returned'
except (TimeoutError,modal.exception.TimeoutError):result['call_state']='live_unfinished'
except Exception as e:result.update(call_state='unknown_exception',exception_type=type(e).__module__+'.'+type(e).__name__)
for n,cap in [('handle.json',65536),('entry-status.json',65536),('run/initialization-proof.json',262144),('run/config.json',1048576),('run/first-transition-gpu-gate.json',131072),('run/latest.json',65536),('run/final.json',262144)]:
 b=bytearray()
 try:
  for chunk in v.read_file(prefix+'/'+n):b.extend(chunk);assert len(b)<=cap
 except FileNotFoundError:continue
 p=out/n;p.parent.mkdir(parents=True,exist_ok=True);p.write_bytes(b);result['files'][n]={'sha256':hashlib.sha256(b).hexdigest(),'bytes':len(b),'body':json.loads(b)}
(out/'receipt.json').write_text(json.dumps(result,sort_keys=True));print((out/'receipt.json').read_text())
