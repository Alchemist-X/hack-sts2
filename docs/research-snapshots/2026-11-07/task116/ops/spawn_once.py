from ops_common import *
import modal
AUTH,BOUND=sys.argv[1:3];a,r=inputs(AUTH,BOUND);assert a['allow_commit_and_spawn']is True and not(W/'spawn-intent.json').exists(),'previous intent: samehandle reconcile, never retry'
d=read('deployment-receipt.json');f=read('current-admission.json');assert d['root_authority_sha256']==f['root_authority_sha256']==AUTH and d['bound_release_sha256']==f['bound_release_sha256']==BOUND and f['status']=='PASS'and time.time()-f['observed_at']<120
fn=modal.Function.from_id(d['function_id']).hydrate();i=fn.info(refresh=True);assert i.cpu==(32,32)and i.memory_mib in(65536,(65536,65536))and i.gpus==[('H100!',1)]and i.timeout==21600 and i.max_retries==0 and i.block_network and i.single_use_containers and i.image_info.image_id==r['image_id']and i.secrets==[]
expected={'/sts2-study':(r['source_volume_id'],True),'/sts2-overnight':(r['output_volume_id'],False)};assert set(i.volumes)==set(expected)
for key,value in expected.items():assert(i.volumes[key].volume_id,i.volumes[key].read_only)==value
st=fn.get_current_stats();assert st.num_running_inputs==st.backlog==st.num_total_runners==0
claims=modal.Dict.from_id(r['claim_dict_id']).hydrate();assert not claims.contains(r['release_id'])
now=time.time();deadline=min(int(now)+21600,r['not_after']);assert deadline-now>=21598
intent={'at':now,'function_id':fn.object_id,'root_authority_sha256':AUTH,'bound_release_sha256':BOUND,'absolute_deadline':deadline,'max_submissions':1,'allocation_cents':3992,'plan_commitment_cents':5762,'actual_cost':None,'ambiguous_ack_never_reinvoke':True,'platform_admission_policy':POLICY};save('spawn-intent.json',intent)
try:call=fn.spawn(BOUND,deadline)
except BaseException as e:save('spawn-ack-unknown-or-rejected.json',{'at':time.time(),'exception_type':type(e).__module__+'.'+type(e).__name__,'actual_cost':None,'platform_acceptance_unknown_or_rejected':True,'do_not_respawn':True});raise SystemExit(1)from None
receipt={**intent,'call_id':call.object_id,'call_url':call.get_dashboard_url(),'output_volume_id':r['output_volume_id'],'output_prefix':r['release_id'],'seed_namespace':'MODA10PH116MFRESH1','seed_range':r['seed_range'],'status':'submitted_not_yet_started_verified'};save('submission-receipt.json',receipt);print(CAN(receipt))
