import modal,json,hashlib,time,shutil
from pathlib import Path
v=modal.Volume.from_id('vo-HbcQICsESY3s4xORGRnIbS').hydrate();prefix='task115-control-continuation-segment1-v1/26d8623c4a28421a9186713f60991bae';out=Path('/data/raven/hack-sts2-modal-research/task115-first-segment-stage/terminal-readonly-meta');out.mkdir(mode=0o700,exist_ok=False)
def read(n):
 b=bytearray()
 for chunk in v.read_file(prefix+'/'+n):
  b.extend(chunk);assert len(b)<=1024**2
 return bytes(b)
refs={}
for n in ['handle.json','entry-status.json','run/final.json','run/latest.json','run/checkpoints/ckpt-0004546.manifest.json']:
 b=read(n);p=out/n;p.parent.mkdir(parents=True,exist_ok=True);p.write_bytes(b);refs[n]={'sha256':hashlib.sha256(b).hexdigest(),'bytes':len(b)}
listing=[]
for i,e in enumerate(v.iterdir(prefix+'/run',recursive=False)):
 assert i<128;listing.append({'path':e.path,'bytes':e.size,'type':str(e.type)})
finals=[x for x in listing if Path(x['path']).name=='final.pt'];assert len(finals)==1 and finals[0]['bytes']==36091713
mem={}
for line in Path('/proc/meminfo').read_text().splitlines():
 key,value=line.split(':',1)
 if key in ['MemTotal','MemAvailable','MemFree','SwapTotal','SwapFree']:mem[key]=value.strip()
disk=shutil.disk_usage('/data/raven');d={'observed_at':time.time(),'call_id':'fc-01M6SFGP9W1T6AGQZP477J8PCZ','attempt':'26d8623c4a28421a9186713f60991bae','files':refs,'run_directory_metadata':listing,'final_pt_exists':True,'final_pt_bytes':finals[0]['bytes'],'model_body_reads':0,'provider_polls':0,'new_compute':0,'data_raven_disk_bytes':{'total':disk.total,'used':disk.used,'free':disk.free},'memory':mem};(out/'receipt.json').write_text(json.dumps(d,sort_keys=True));print((out/'receipt.json').read_text())
