"""Task114 learner-only forward KL(U_legal || policy); no forward/RNG/native calls."""
import numpy as np
import torch
METHOD='task114-map-rest-forward-kl-uniform-v1'
PHASES=('Map','RestSite')

def context_of(spec):
 names=spec.enum_names['RunPhase']
 return (spec.cols['global_i']['phase'],names.index('Map'),names.index('RestSite'))

def penalty_and_metrics(logp_all,cand_n,phase,map_id,rest_id):
 """Input is the original masked learner log_softmax, valid PPO rows only.

 Each minibatch phase has weight1/2, including when the other phase is absent.
 All first cand_n legal rows enter U; single-choice and all other phases excluded.
 Metrics retain phase sums/counts, never aggregate means of minibatch means.
 """
 if logp_all.ndim!=2 or cand_n.shape!=logp_all.shape[:1]or phase.shape!=cand_n.shape:raise ValueError('strategic learner shapes differ')
 legal=torch.arange(logp_all.shape[1],device=logp_all.device)[None,:]<cand_n[:,None]
 safe=torch.where(legal,logp_all,torch.zeros_like(logp_all))
 n=cand_n.to(logp_all.dtype).clamp_min(1)
 kl=-n.log()-safe.sum(-1)/n
 p=torch.where(legal,logp_all.exp(),torch.zeros_like(logp_all))
 hn=-(p*safe).sum(-1)/n.clamp_min(2).log()
 near=(p.amax(-1)>.999).to(logp_all.dtype)
 eligible=(cand_n>=2)&(cand_n<=logp_all.shape[1]);stats=[];means=[]
 for phase_id in (map_id,rest_id):
  take=eligible&(phase==phase_id);zero=torch.zeros_like(kl)
  count=take.sum().to(logp_all.dtype);ks=torch.where(take,kl,zero).sum()
  stats.extend((count,ks,torch.where(take,hn,zero).sum(),torch.where(take,near,zero).sum()))
  means.append(ks/count.clamp_min(1))
 penalty=.5*(means[0]+means[1])
 return penalty,torch.stack((*stats,penalty))

def record(beta):
 if type(beta)not in (int,float)or beta not in (0.,.02):raise ValueError('Task114 strategic beta must be0 or.02')
 return {'method':METHOD,'beta':float(beta),'phase_weights':{'Map':.5,'RestSite':.5},'normalization':'within each learner minibatch, valid phase frames with cand_n>=2; absent phase0, no renormalization','direction':'KL(Ulegal||policy)','same_forward':True,'old_global_entropy_unchanged':.02}

def summarize(rows,beta):
 r=record(beta);a=np.asarray([row.cpu().numpy()for row in rows],dtype=np.float64)
 if not len(a):return {**r,'minibatches':0,'phases':{name:{'frames':0,'KL_sum':0.,'KL_mean':None,'normalized_entropy_sum':0.,'normalized_entropy_mean':None,'near_deterministic_count':0,'near_deterministic_share':None}for name in PHASES},'weighted_KL_minibatch_mean':None,'added_loss_minibatch_mean':None}
 if a.ndim!=2 or a.shape[1]!=9 or not np.isfinite(a).all():raise FloatingPointError('nonfinite or invalid strategic observer')
 total=a.sum(0,dtype=np.float64);phases={}
 for index,name in enumerate(PHASES):
  n,kl,h,near=total[index*4:index*4+4];n=int(n);near=int(near)
  phases[name]={'frames':n,'KL_sum':float(kl),'KL_mean':float(kl/n)if n else None,'normalized_entropy_sum':float(h),'normalized_entropy_mean':float(h/n)if n else None,'near_deterministic_count':near,'near_deterministic_share':float(near/n)if n else None}
 mean=float(a[:,-1].mean(dtype=np.float64))
 return {**r,'minibatches':len(a),'phases':phases,'weighted_KL_minibatch_mean':mean,'added_loss_minibatch_mean':float(beta)*mean,'measurement':'learner minibatch same-forward pre-step policies; repeated learner frames, not holdout/rollout-policy samples'}
