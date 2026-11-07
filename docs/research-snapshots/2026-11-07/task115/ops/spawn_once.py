from ops_common import *
import modal
AUTH,BOUND=sys.argv[1:3];a,r=inputs(AUTH,BOUND)
assert not(W/'spawn-intent.json').exists(),'previous intent; reconcile only'
d=read('deployment-receipt.json');f=read('current-admission.json')
assert d['root_authority_sha256']==f['root_authority_sha256']==AUTH and d['bound_release_sha256']==f['bound_release_sha256']==BOUND
assert f['status']=='PASS' and time.time()-f['observed_at']<120 and f['remaining_commitment_cents']==2926
fn=modal.Function.from_id(d['function_id']).hydrate();i=fn.info(refresh=True)
assert i.cpu==(32,32) and i.memory_mib in(65536,(65536,65536)) and i.gpus==[('H100!',1)] and i.timeout==9000 and i.max_retries==0 and i.single_use_containers and i.block_network and i.secrets==[] and i.image_info.image_id==r['image_id']
for p,k,ro in [('/sts2-study','source_volume_id',True),('/sts2-parent','parent_volume_id',True),('/sts2-overnight','output_volume_id',False)]:assert(i.volumes[p].volume_id,i.volumes[p].read_only)==(r[k],ro)
s=fn.get_current_stats();assert s.num_running_inputs==s.backlog==s.num_total_runners==0
claims=modal.Dict.from_id(r['claim_dict_id']).hydrate();assert not claims.contains(r['release_id'])
now=time.time();deadline=min(int(now)+9000,r['not_after']);assert deadline-now>=8998
intent={'at':now,'function_id':fn.object_id,'root_authority_sha256':AUTH,'bound_release_sha256':BOUND,'absolute_deadline':deadline,'max_submissions':1,'allocation_cents':1629,'actual_cost':None,'ambiguous_ack_never_reinvoke':True};save('spawn-intent.json',intent)
try:call=fn.spawn(BOUND,deadline)
except BaseException as e:save('spawn-ack-unknown.json',{'at':time.time(),'error_type':type(e).__name__,'do_not_respawn':True});raise SystemExit(1)from None
receipt={**intent,'call_id':call.object_id,'call_url':call.get_dashboard_url(),'output_volume_id':r['output_volume_id'],'output_prefix':r['release_id'],'seed_range':r['segment']['seed_range'],'status':'submitted_not_yet_started_verified'};save('submission-receipt.json',receipt);print(CAN(receipt))
