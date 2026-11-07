"""Unchanged accepted110 row/count and descriptive pairing validators."""
from collections import Counter
import math,statistics
from paired_statistics import RESULTS

def validate_rows(metrics,rows,keys):
    """Existing full256 consistency semantics; complete anomalies retained, never dropped."""
    if len(rows)!=256 or len({r['key'] for r in rows})!=256 or {r['key'] for r in rows}!=set(keys):raise ValueError('full exact public256 required')
    for r in rows:
        if r['result'] not in RESULTS or type(r['progress'])not in (int,float) or not math.isfinite(r['progress']) or not 0<=r['progress']<=1:raise ValueError('invalid result/progress')
        if type(r['replayable'])is not bool:raise ValueError('invalid replay flag')
        if type(r['steps'])is not int or not 0<=r['steps']<=1200:raise ValueError('invalid step count')
        if type(r['act'])is not int or not 1<=r['act']<=3 or type(r['floor'])is not int or r['floor']<0:raise ValueError('invalid act/floor')
        if type(r['end_kind'])is not int or type(r['end_status'])is not int:raise ValueError('invalid end fields')
    counts=Counter(r['result'] for r in rows)
    if not set(metrics['results'])<=RESULTS or any(type(v)is not int or v<0 for v in metrics['results'].values()) or {k:v for k,v in metrics['results'].items() if v}!=dict(counts):raise ValueError('summary results differ')
    expected={'games':256,'progress_games':256,'wins':counts['win'],'stuck_games':counts['stuck'],'capped_games':counts['capped'],'crashes':counts['crashed'],'replayable':sum(r['replayable'] for r in rows),'steps_total':sum(r['steps'] for r in rows),'floor_max':max(r['floor'] for r in rows)}
    if any(type(metrics.get(k))is not int or metrics[k]!=v for k,v in expected.items()):raise ValueError('summary count/steps/replay mismatch')
    if metrics['act_reached']!=dict(Counter(str(r['act']) for r in rows)):raise ValueError('summary act counts differ')
    for key,value in [('progress_mean',statistics.mean(r['progress'] for r in rows)),('steps_mean',expected['steps_total']/256)]:
        if type(metrics.get(key))not in (int,float) or not math.isfinite(metrics[key]) or abs(metrics[key]-value)>1e-12:raise ValueError('summary mean differs')
    if type(metrics.get('illegal_total'))is not int or metrics['illegal_total']<0:raise ValueError('invalid reported illegal count')
    return {'full_denominator':256,'formal_quality_validation_eligible':metrics['replayable']==256 and metrics['illegal_total']==0 and not any(counts[k] for k in ('error','crashed','aborted')),'results':dict(counts),'replay_failures':256-metrics['replayable'],'illegal_total_reported':metrics['illegal_total'],'unreported_observation_flags':'unknown; no all-native-flags-zero claim'}

def descriptive_pairs(arm,control,keys):
    a={r['key']:r for r in arm};b={r['key']:r for r in control}
    return {'act_reach_pairs':{str(act):{f'control{int(x)}_treatment{int(y)}':sum((b[k]['act']>=act)==x and (a[k]['act']>=act)==y for k in keys) for x in (False,True) for y in (False,True)} for act in (2,3)},'win_pairs':{f'control{int(x)}_treatment{int(y)}':sum((b[k]['result']=='win')==x and (a[k]['result']=='win')==y for k in keys) for x in (False,True) for y in (False,True)}}
