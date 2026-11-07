import modal,json,hashlib,time
from pathlib import Path
v=modal.Volume.from_id('vo-HbcQICsESY3s4xORGRnIbS').hydrate();prefix='task115-control-continuation-segment1-v1/26d8623c4a28421a9186713f60991bae';out=Path('/data/raven/hack-sts2-modal-research/task115-first-segment-stage/first-periodic-metadata');out.mkdir(mode=0o700,exist_ok=False)
files=['handle.json','entry-status.json','release.json','run/latest.json','run/resume-proof.json','run/config.json','run/status.json','run/metrics.jsonl','run/train.log','run/checkpoints/ckpt-0003812.manifest.json'];refs={};total=0
for n in files:
 b=bytearray()
 try:
  for chunk in v.read_file(prefix+'/'+n):
   b.extend(chunk);assert len(b)<=16*1024**2
 except FileNotFoundError:continue
 total+=len(b);assert total<=32*1024**2;p=out/n;p.parent.mkdir(parents=True,exist_ok=True);p.write_bytes(b);refs[n]={'sha256':hashlib.sha256(b).hexdigest(),'bytes':len(b)}
(out/'receipt.json').write_text(json.dumps({'observed_at':time.time(),'call_id':'fc-01M6SFGP9W1T6AGQZP477J8PCZ','attempt':'26d8623c4a28421a9186713f60991bae','files':refs,'model_body_reads':0,'model_independent_bodyhash':False,'new_compute':0},sort_keys=True));print((out/'receipt.json').read_text())
