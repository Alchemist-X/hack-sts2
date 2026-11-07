"""Exact frozen104/fc287 absolute summary functions; current treatment only."""
import math
import statistics
from collections import Counter

def wilson(k,n=256):
    z=1.959963984540054;p=k/n;den=1+z*z/n
    center=(p+z*z/(2*n))/den;half=z*math.sqrt(p*(1-p)/n+z*z/(4*n*n))/den
    return [max(0,center-half),min(1,center+half)]

def stats(m,rows):
    return {'games':256,'mean_protocol_progress':statistics.mean(x['progress'] for x in rows),'wins':m['wins'],'win_rate':m['wins']/256,'win_rate_wilson95':wilson(m['wins']),'results':m['results'],'act2_reach':sum(x['act']>=2 for x in rows),'act3_reach':sum(x['act']>=3 for x in rows),'terminal_act_histogram':dict(Counter(str(x['act']) for x in rows)),'act1_floor17_terminal':sum(x['act']==1 and x['floor']==17 for x in rows),'floor_termination_histogram':dict(sorted(Counter(f"{x['act']}:{x['floor']}:{x['result']}" for x in rows).items())),'end_kind_status_histogram':dict(Counter(f"{x['end_kind']}:{x['end_status']}" for x in rows)),'replayable':m['replayable'],'illegal':m['illegal_total'],'crashes':m['crashes'],'progress_games':m['progress_games'],'steps_total':m['steps_total'],'steps_mean':m['steps_mean'],'floor_max':m['floor_max']}
