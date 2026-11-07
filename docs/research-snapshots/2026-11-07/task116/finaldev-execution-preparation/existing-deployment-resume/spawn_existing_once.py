"""Resume original M dev only at the unsent claim boundary; never deploy."""
from ops_common import *

def main():
 AUTH,BOUND=sys.argv[1:3]
 a,r=inputs(AUTH,BOUND,phase='deploy',gate=False)
 require(a.get('resume_existing_deployment_only')is True and a.get('allow_deploy')is True,'exact existing deployment continuation authority required')
 require(a['ops_file_sha256']['spawn_existing_once.py']==H(Path(__file__).read_bytes()),'exact continuation source required')
 prior=read_bound(W,'root-deploy-authorization.initial.json',a['initial_deploy_authority_sha256'])
 intent=read_bound(W,'deployment-intent.json',a['existing_deployment_intent_sha256'])
 receipt=read_bound(W,'deployment-receipt.json',a['existing_deployment_receipt_sha256'])
 reject=read_bound(W,'initial-claim-rejection.json',a['initial_claim_rejection_sha256'])
 require(intent['root_authority_sha256']==a['initial_deploy_authority_sha256'] and intent['release_sha256']==receipt['release_sha256']==prior['bound_release_sha256']==BOUND,'original single deployment and bound closure')
 require(reject['status']=='FAILED_BEFORE_CLAIM_NO_SPAWN' and reject['canonical_dispatch_event']==DISPATCH and reject['canonical_dispatch_count']==0 and reject['deploy_process_exit_code']==1 and reject['initial_root_authority_sha256']==a['initial_deploy_authority_sha256'] and reject['deployment_intent_sha256']==a['existing_deployment_intent_sha256'] and reject['deployment_receipt_sha256']==a['existing_deployment_receipt_sha256'],'original unsent disposition required')
 for n,k in (('deploy-and-spawn.stderr','original_stderr_sha256'),('send-admission.json','send_admission_sha256')):
  require(H((W/n).read_bytes())==reject[k],'original failure evidence differs '+n)
 require(a['owned_function_ids']['116formaldev_deployed_unsent']==receipt['function_id'],'exact new function included in real quiet inventory')
 require(not any((W/n).exists() for n in ('slot-claim-receipt.json','spawn-intent.json','spawn-acknowledgement.json','spawn-acknowledgement-unknown.json','submission-receipt.json','resume-send-admission.json')),'prior claim, send, or recovery requires reconciliation')
 metadata_continuity(a,r)
 import platform,modal
 require(platform.python_version_tuple()[:2]==('3','11') and modal.__version__=='1.6.3','approved existing toolchain')
 require(modal.Workspace.from_context().hydrate().name=='hycrpg','workspace')
 out=modal.Volume.from_id(r['output_volume_id']).hydrate(); claims=modal.Dict.from_id(r['claim_dict_id']).hydrate()
 require(out._get_metadata().version==2 and out.listdir('/',recursive=True)==[] and claims.len()==0,'actual independent empty output and once Dict')
 fn=modal.Function.from_id(receipt['function_id']).hydrate(); info=fn.info(refresh=True)
 expected={'/sts2-study':(r['source_volume_id'],True),'/sts2-model':(r['model_volume_id'],True),'/sts2-evaluation':(r['output_volume_id'],False)}
 require(info.cpu==(32,32) and info.memory_mib in (65536,(65536,65536)) and info.gpus==[('H100!',1)],'actual H100/32/64')
 require(info.timeout==3600 and info.max_retries==0 and info.block_network is True and info.single_use_containers is True and info.secrets==[] and info.image_info.image_id==r['image_id'],'actual original deployment resources')
 require(set(info.volumes)==set(expected) and all((info.volumes[p].volume_id,info.volumes[p].read_only)==v for p,v in expected.items()),'exact RO source/model and RW output')
 from fresh_admission import refresh
 from claim_slot import claim
 g=refresh(AUTH,BOUND,'deploy',output_name='resume-send-admission.json')
 check_admission(g,a,r,AUTH,BOUND,now=time.time())
 submit=time.time();deadline=min(int(submit)+3600,r['not_after'])
 require(min(a['expires'],r['not_after'])-submit>=3500 and deadline-submit>=3500,'full original explicit admission window')
 claim(a,r,g,AUTH,BOUND)
 save('spawn-intent.json',{'at':submit,'root_authority_sha256':AUTH,'release_sha256':BOUND,'app_id':receipt['app_id'],'function_id':fn.object_id,'absolute_deadline':deadline,'max_submissions':1,'existing_slot_cents':744,'new_pledge_cents':0,'actual_cost':None,'ambiguous_ack_never_reinvoke':True,'existing_deployment_receipt_sha256':a['existing_deployment_receipt_sha256']})
 try:call=fn.spawn(BOUND,deadline)
 except BaseException as exc:
  save('spawn-acknowledgement-unknown.json',{'at':time.time(),'error_class':type(exc).__name__,'release_sha256':BOUND,'new_calls_authorized':0,'do_not_respawn':True});raise SystemExit(1) from None
 save('spawn-acknowledgement.json',{'at':time.time(),'call_id':call.object_id,'function_id':fn.object_id,'absolute_deadline':deadline,'release_sha256':BOUND})
 try:call_url=call.get_dashboard_url()
 except Exception:call_url=None
 save('submission-receipt.json',{'at':time.time(),'status':'submitted_not_yet_started_verified','root_authority_sha256':AUTH,'release_sha256':BOUND,'call_id':call.object_id,'call_url':call_url,'app_id':receipt['app_id'],'function_id':fn.object_id,'submitted_at':submit,'absolute_deadline':deadline,'training_seconds':0,'evaluation_games':256,'hard_timeout_seconds':3600,'output_volume_id':r['output_volume_id'],'output_volume_name':'sts2-task116-final-M-dev256-output-v2','output_prefix':r['release_id'],'claim_dict_id':r['claim_dict_id'],'existing_slot_cents':744,'new_pledge_cents':0,'actual_cost':None,'raven_scheduler_dependency':False})
 print((W/'submission-receipt.json').read_text())
if __name__=='__main__':main()
