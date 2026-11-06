"""Semantic equivalent vectorized Task089 labels; reference is transition_aux.targets.

Only public numeric/UID/ownership fields and the actual selected preaction kind are copied.
No afterstate enters Policy. No simulator call, hidden state, action generation or RNG.
"""
from dataclasses import dataclass
import numpy as np
from sts2vec.schema import STATUS
from .transition_aux import BAD,SCALES

GLOBAL_FIELDS=('phase','is_combat','act_index','total_floor','act_floor','map_row','map_col','turn',
               'hp','max_hp','block','energy','enemy_n','prompt_option_n')


@dataclass(frozen=True)
class TransitionFrame:
    public: dict
    enemy_uid: np.ndarray
    enemy_hp: np.ndarray
    enemy_present: np.ndarray
    combats_won: np.ndarray
    cand_n: np.ndarray
    selected_kind: np.ndarray | None
    selected_index: np.ndarray | None
    episode_id: np.ndarray
    step_index: np.ndarray
    status: np.ndarray

    @classmethod
    def capture(cls,spec,obs,episode_id,step_index,status,actions=None):
        gc,ec=spec.cols['global_i'],spec.cols['enemies']
        public={f:obs['global_i'][:,gc[f]].copy() for f in GLOBAL_FIELDS}
        kind=None
        if actions is not None:
            actions=np.asarray(actions)
            rows=obs['cand'].shape[1]
            ok=(actions>=0)&(actions<rows)&(actions<obs['cand_n'])
            safe=np.clip(actions,0,rows-1)
            kind=np.where(ok,obs['cand'][np.arange(len(actions)),safe,spec.cols['cand']['kind']],-1).copy()
        return cls(public,obs['enemies'][:,:,ec['uid']].copy(),obs['enemies'][:,:,ec['hp']].copy(),
                   (obs['enemies'][:,:,ec['present']]!=0).copy(),
                   obs['aux'][:,spec.cols['aux']['combats_won']].copy(),obs['cand_n'].copy(),kind,
                   None if actions is None else np.array(actions,copy=True),
                   np.array(episode_id,copy=True),np.array(step_index,copy=True),np.array(status,copy=True))


def targets(spec,before,after,actions,terminated,truncated,status,valid,*,max_rows=192,
            last_episode=None,terminal_end_kind=None):
    actions=np.asarray(actions);n=len(actions)
    if before.selected_kind is None or before.selected_index is None:
        raise ValueError('pre snapshot missing actual selected kind/index')
    a,b=before.public,after.public
    raw=np.zeros((n,4),np.float64);mask=np.zeros((n,4),bool)
    active=np.ones(n,bool)
    counts={'terminal_hp_labeled':0,'terminal_ownership_rejected':0,'combat_identity_rejected':0,'reasons':{}}
    def reject_count(reason,subset):
        value=int(np.count_nonzero(subset))
        if value:counts['reasons'][reason]=counts['reasons'].get(reason,0)+value
    def keep(condition,reason):
        nonlocal active
        reject_count(reason,active&~condition)
        active=active&condition
    keep(np.asarray(valid,bool)&(actions==before.selected_index)&(actions>=0)&(actions<np.minimum(before.cand_n,max_rows)), 'invalid_sample_or_index')
    names=spec.enum_names['RunActionKind']
    allowed=[names.index(k) for k in ('PlayCard','UsePotion','EndTurn')]
    keep(np.isin(before.selected_kind,allowed),'noncombat_action_kind')
    keep((before.status&(BAD&~STATUS['RESET']))==0,'pre_error_or_native_overflow')
    keep((before.cand_n<=max_rows)&(a['prompt_option_n']<=max_rows),'pre_trainer_width_overflow')
    terminal=active&(np.asarray(terminated)!=0)&(np.asarray(truncated)==0)
    combat=spec.enum_names['RunPhase'].index('Combat')
    owned=np.zeros(n,bool)
    if last_episode is not None and terminal_end_kind is not None:
        lc=spec.cols['last_episode'];le=np.asarray(last_episode)
        hp,mx=le[:,lc['final_hp']],le[:,lc['final_max_hp']]
        owned=(terminal&((np.asarray(status)&(BAD&~STATUS['RESET']))==0)
            &(a['phase']==combat)&(a['is_combat']==1)
            &(before.episode_id!=after.episode_id)&(after.step_index==0)
            &(le[:,lc['end_kind']]==terminal_end_kind)&(le[:,lc['steps']]==before.step_index+1)
            &(le[:,lc['floor']]==a['total_floor'])&(le[:,lc['act']]==a['act_index'])
            &np.isfinite(hp)&np.isfinite(mx)&np.isfinite(a['hp'])&np.isfinite(a['max_hp'])
            &(mx>0)&(hp>=0)&(hp<=mx)&(a['max_hp']>0)&(a['hp']>=0)&(a['hp']<=a['max_hp']))
        raw[owned,0]=hp[owned].astype(np.float64)-a['hp'][owned]
    mask[owned,0]=True
    counts['terminal_hp_labeled']=int(owned.sum())
    rejected=terminal&~owned
    counts['terminal_ownership_rejected']=int(rejected.sum());reject_count('terminal_ownership',rejected)
    active=active&~terminal
    keep((np.asarray(terminated)==0)&(np.asarray(truncated)==0)&((np.asarray(status)&BAD)==0),
         'done_or_post_error_reset_overflow')
    keep((after.cand_n<=max_rows)&(b['prompt_option_n']<=max_rows),'post_trainer_width_overflow')
    keep((before.episode_id==after.episode_id)&(after.step_index==before.step_index+1),'episode_or_step_discontinuity')
    same=np.ones(n,bool)
    for key in ('act_index','total_floor','act_floor','map_row','map_col'):same&=a[key]==b[key]
    keep(same,'room_discontinuity')
    same=(before.combats_won==after.combats_won)&(b['turn']>=a['turn'])
    counts['combat_identity_rejected']=int((active&~same).sum())
    keep(same,'combat_identity_proxy')
    keep((a['phase']==combat)&(b['phase']==combat)&(a['is_combat']==1)&(b['is_combat']==1),'combat_phase')
    for j,key in enumerate(('hp','block','energy')):
        ok=active&np.isfinite(a[key])&np.isfinite(b[key])&(a[key]>=0)&(b[key]>=0)
        if key=='hp':ok&=(a['max_hp']>0)&(b['max_hp']>0)&(a[key]<=a['max_hp'])&(b[key]<=b['max_hp'])
        raw[ok,j]=b[key][ok].astype(np.float64)-a[key][ok]
        mask[ok,j]=True
    # Sort public UIDs, using a sentinel outside native int32 UID range, never slot identity.
    sentinel=np.iinfo(np.int64).max
    u=np.sort(np.where(before.enemy_present,before.enemy_uid.astype(np.int64),sentinel),axis=1)
    v=np.sort(np.where(after.enemy_present,after.enemy_uid.astype(np.int64),sentinel),axis=1)
    duplicate0=((u[:,1:]==u[:,:-1])&(u[:,1:]!=sentinel)).any(1)
    duplicate1=((v[:,1:]==v[:,:-1])&(v[:,1:]!=sentinel)).any(1)
    hp0=np.where(before.enemy_present,before.enemy_hp,0)
    hp1=np.where(after.enemy_present,after.enemy_hp,0)
    enemy=(active&before.enemy_present.any(1)&(u==v).all(1)&~duplicate0&~duplicate1
        &(u>=0).all(1)&(v>=0).all(1)&np.isfinite(hp0).all(1)&np.isfinite(hp1).all(1)
        &(hp0>=0).all(1)&(hp1>=0).all(1)
        &(a['enemy_n']>=0)&(a['enemy_n']<=before.enemy_uid.shape[1])
        &(b['enemy_n']>=0)&(b['enemy_n']<=after.enemy_uid.shape[1]))
    raw[enemy,3]=hp1.sum(1,dtype=np.float64)[enemy]-hp0.sum(1,dtype=np.float64)[enemy]
    mask[enemy,3]=True
    reject_count('enemy_membership_or_invalid',active&~mask[:,3])
    reject_count('player_component_invalid',active&~mask[:,:3].all(1))
    y=(np.sign(raw)*np.log1p(np.abs(raw))/np.log1p(SCALES)).astype(np.float32)
    counts['valid_per_channel']=mask.sum(0).tolist()
    return y,mask,counts
