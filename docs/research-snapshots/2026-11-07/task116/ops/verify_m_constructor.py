"""Authorized once bounded Raven-only fresh M CPU constructor; no forward/native/game/old model."""
from pathlib import Path
import hashlib,json,resource,sys,time
P=Path(__file__).parent;sys.path.insert(0,str(P/'frozen_source'))
import torch
torch.set_num_threads(1);torch.set_num_interop_threads(1)
from sts2vec.schema import Schema
from sts2train.features import ModelSpec,PUBLIC_HAND_INPUT_ARCH
from sts2train.model import Policy
from sts2train.transition_aux import TransitionHead
expected={'features.py':'316e969717284df7aefc1ac459b02b508097db0e2b755476ba42337476529030','model.py':'1c0f48bbe45154417f5952d734673aa96874098c57010b765cdfa0b190becb21'}
for name,want in expected.items():assert hashlib.sha256((P/'frozen_source/sts2train'/name).read_bytes()).hexdigest()==want
schema=Schema.from_json((P/'public-schema.json').read_bytes());spec=ModelSpec.from_schema(schema,input_arch=PUBLIC_HAND_INPUT_ARCH)
assert hashlib.sha256(json.dumps(spec.to_json(),sort_keys=True,separators=(',',':')).encode()).hexdigest()=='63e9cc460886b2b26836cff57514c24dfb39253b87cd31eea746fb0b061ef10b'
torch.manual_seed(1457924769);t=time.monotonic();model=Policy(spec,d=384,s=768,depth=6,heads=8,ff_mult=4,aux_heads=True,deck_counts=True,critic_arch='state_legal_candidates_detached_mean_v1');head=TransitionHead(768,384,seed=89)
base=sum(p.numel()for p in model.parameters());aux=sum(p.numel()for p in head.parameters());assert(base,aux)==(16253584,74052)
result={'status':'PASS_CPU_CONSTRUCTOR_ONLY','base_parameters':base,'transition_parameters':aux,'total_training_parameters':base+aux,'value_candidate_proj_shape':list(model.value_candidate_proj.weight.shape),'head_shapes':{k:list(v.shape)for k,v in head.state_dict().items()},'constructor_s':time.monotonic()-t,'peak_RSS_KiB':resource.getrusage(resource.RUSAGE_SELF).ru_maxrss,'Torch_threads':torch.get_num_threads(),'Torch_interop_threads':torch.get_num_interop_threads(),'device':str(next(model.parameters()).device),'forwards':0,'updates':0,'native_loads':0,'games':0,'old_checkpoint_reads':0,'source_sha256':expected,'spec_sha256':'63e9cc460886b2b26836cff57514c24dfb39253b87cd31eea746fb0b061ef10b'}
print(json.dumps(result,sort_keys=True));(P/'constructor-receipt.json').write_text(json.dumps(result,indent=2,sort_keys=True)+'\n')
