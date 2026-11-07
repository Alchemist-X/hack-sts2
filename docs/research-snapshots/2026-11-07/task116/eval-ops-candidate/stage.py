"""Pure local staging; no overwrite, SDK, ledger or endpoint fabrication."""
from pathlib import Path
import sys,json,hashlib,shutil
src=Path(__file__).resolve().parent;dest=Path(sys.argv[1]).resolve();assert str(dest).startswith('/data/raven/')and not dest.exists()
pins=json.loads((src/'pins.json').read_bytes())
for n,h in pins['files'].items():
 p=src/n;assert not Path(n).is_absolute()and '..'not in Path(n).parts and not p.is_symlink()and hashlib.sha256(p.read_bytes()).hexdigest()==h
shutil.copytree(src,dest,ignore=shutil.ignore_patterns('__pycache__','history'));print(json.dumps({'status':'staged_disabled','path':str(dest),'SDK_calls':0,'ledger_writes':0,'new_pledge_cents':0}))
