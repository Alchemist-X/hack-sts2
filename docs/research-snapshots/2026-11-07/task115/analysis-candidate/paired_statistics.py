"""Task115's narrow114/110/104-kernel reuse. No artifact reads or real-data execution on import."""
import hashlib
import json
import math

RESULTS={'win','loss','stuck','capped','error','crashed','aborted'}
REPLICATES=20000
SEED=115114
ENDPOINTS=('progress','win_indicator')

def canonical(x):return json.dumps(x,sort_keys=True,separators=(',',':'),ensure_ascii=False,allow_nan=False).encode()
def digest(x):return hashlib.sha256(canonical(x)).hexdigest()

def validate_statistics_parameters(replicates=REPLICATES,seed=SEED,endpoints=ENDPOINTS):
    if type(replicates)is not int or replicates!=20000 or type(seed)is not int or seed!=115114 or endpoints!=ENDPOINTS:
        raise ValueError('exact115 paired20000/RNG115114/two endpoints required')
    return {'replicates':replicates,'seed':seed,'prng':'PCG64','n':256,'endpoints':list(endpoints),'quantile_method':'linear','confidence':[.025,.975]}

def paired_deltas(arm,control,expected_keys):
    """Same104 keyed join; reject missing/duplicate/nonfinite, retain all valid failure rows."""
    if len(expected_keys)!=256 or len(set(expected_keys))!=256:raise ValueError('exact public256 key binding required')
    if len(arm)!=256 or len(control)!=256:raise ValueError('full256 denominators required')
    a={x['key']:x for x in arm};b={x['key']:x for x in control};keys=set(expected_keys)
    if len(a)!=256 or len(b)!=256 or set(a)!=keys or set(b)!=keys:raise ValueError('duplicate/missing/nonpublic keys')
    result=[]
    for key in sorted(keys):
        av,bv=a[key]['progress'],b[key]['progress']
        if any(type(x)not in (int,float) or not math.isfinite(x) or not 0<=x<=1 for x in (av,bv)):raise ValueError('finite protocol progress required')
        if a[key]['result'] not in RESULTS or b[key]['result'] not in RESULTS:raise ValueError('known results required; never drop or impute')
        result.append({'key':key,'progress_delta':av-bv,'win_indicator_delta':int(a[key]['result']=='win')-int(b[key]['result']=='win')})
    return result

def bootstrap_contrasts(contrasts,*,replicates=REPLICATES,seed=SEED):
    """104's PCG64/float64/linear kernel, with one index shared across planned contrasts."""
    policy=validate_statistics_parameters(replicates,seed)
    if not contrasts or any(len(pairs)!=256 for pairs in contrasts.values()):raise ValueError('exact paired256 before RNG')
    key_order=None
    for pairs in contrasts.values():
        keys=[p['key'] for p in pairs]
        if len(set(keys))!=256 or keys!=sorted(keys):raise ValueError('canonical unique paired256 before RNG')
        if key_order is not None and keys!=key_order:raise ValueError('same public256 order across contrasts')
        key_order=keys
        for p in pairs:
            if type(p['progress_delta'])not in (int,float) or not math.isfinite(p['progress_delta']) or not -1<=p['progress_delta']<=1:raise ValueError('finite progress delta')
            if type(p['win_indicator_delta'])is not int or p['win_indicator_delta']not in (-1,0,1):raise ValueError('win indicator delta')
    import numpy as np
    rng=np.random.default_rng(seed);idx=rng.integers(0,256,size=(replicates,256))
    if type(rng.bit_generator).__name__!='PCG64':raise ValueError('PCG64 required')
    receipt=dict(policy,numpy_version=np.__version__,index_sha256_little_endian_int64=hashlib.sha256(idx.astype('<i8').tobytes(order='C')).hexdigest(),contrasts={})
    for name,pairs in contrasts.items():
        endpoints={}
        for endpoint in ENDPOINTS:
            values=[x[endpoint+'_delta'] for x in pairs];d=np.array(values,dtype=np.float64);means=d[idx].mean(axis=1,dtype=np.float64)
            endpoints[endpoint]={'mean_delta':float(d.mean()),'ci95':np.quantile(means,[.025,.975],method='linear').tolist(),'delta_sha256':digest(values),'improved':sum(x>0 for x in values),'worse':sum(x<0 for x in values),'tied':sum(x==0 for x in values)}
        receipt['contrasts'][name]={'n':256,'endpoints':endpoints}
    return receipt
