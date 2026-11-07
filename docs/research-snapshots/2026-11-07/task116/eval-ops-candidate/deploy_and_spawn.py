"""Future final-only deploy/one spawn. No imports or side effects until exact future gates."""
from ops_common import *
def main():
 AUTH,BOUND=sys.argv[1:3];auth,release=inputs(AUTH,BOUND,phase='deploy',gate=False)
 from fresh_admission import refresh
 refresh(AUTH,BOUND,'deploy')
 import platform,modal
 CODE=W
 metadata_continuity(auth,release)
 assert platform.python_version_tuple()[:2]==('3','11')and modal.__version__=='1.6.3'
 for n,h in release['overlay_sha256'].items():assert H((CODE/n).read_bytes())==h
 assert not(W/'spawn-intent.json').exists()and not(W/'deployment-intent.json').exists(),'prior attempt requires reconciliation; never automatic re-invoke'
 preflight=json.loads((W/'deploy-admission.json').read_bytes());assert preflight['status']=='PASS'and time.time()-preflight['observed_at']<120
 assert preflight['existing_slot_cents']==744 and preflight['new_pledge_cents']==0 and preflight['inventory_within_authorized_scope']is True and preflight['quote_cents']<=744
 ws=modal.Workspace.from_context().hydrate();assert ws.name=='hycrpg';out=modal.Volume.from_id(release['output_volume_id']).hydrate();claims=modal.Dict.from_id(release['claim_dict_id']).hydrate();assert out._get_metadata().version==2 and out.listdir('/',recursive=True)==[]and claims.len()==0
 sys.path.insert(0,str(CODE));import function_app
 app,fn=function_app.declare(modal,release)
 serialized=fn._get_source_info().serialized_function();save('serialization.json',{'bytes':len(serialized),'sha256':H(serialized),'python':platform.python_version(),'sdk':modal.__version__,'compute_invocations':0})
 save('deployment-intent.json',{'at':time.time(),'root_authority_sha256':AUTH,'release_sha256':BOUND,'app_name':release['app_name'],'compute_invocations':0})
 app.deploy(environment_name='main');info=fn.info(refresh=True)
 assert info.cpu==(32,32)and info.memory_mib in (65536,(65536,65536))and info.gpus==[('H100!',1)]
 assert info.timeout==3600 and info.max_retries==0 and info.block_network is True and info.single_use_containers is True and info.secrets==[]and info.image_info.image_id==release['image_id']
 expected={'/sts2-study':(release['source_volume_id'],True),'/sts2-model':(release['model_volume_id'],True),'/sts2-evaluation':(release['output_volume_id'],False)}
 assert set(info.volumes)==set(expected)
 for path,(vid,ro)in expected.items():assert(info.volumes[path].volume_id,info.volumes[path].read_only)==(vid,ro)
 save('deployment-receipt.json',{'at':time.time(),'app_id':app.app_id,'function_id':fn.object_id,'app_url':app.get_dashboard_url(),'release_sha256':BOUND,'resources':{'cpu':info.cpu,'memory_mib':info.memory_mib,'gpus':info.gpus,'timeout':info.timeout,'max_retries':info.max_retries,'single_use':info.single_use_containers,'network_block':info.block_network,'volumes':expected},'declared_min_containers':0,'declared_max_containers':1,'declared_startup_timeout':80,'info_http_startup_timeout':info.startup_timeout,'compute_invocations':0})
 # Final same-control-plane metadata checks; never stop or recreate an existing run.
 assert claims.len()==0 and out.listdir('/',recursive=True)==[]
 preflight=refresh(AUTH,BOUND,'deploy',output_name='send-admission.json')
 check_admission(preflight,auth,release,AUTH,BOUND,now=time.time())
 submit=time.time();deadline=min(int(submit)+3600,release['not_after']);assert deadline-submit>=3500 and submit<auth['expires']
 from claim_slot import claim
 claim(auth,release,preflight,AUTH,BOUND)
 save('spawn-intent.json',{'at':submit,'root_authority_sha256':AUTH,'release_sha256':BOUND,'app_id':app.app_id,'function_id':fn.object_id,'absolute_deadline':deadline,'max_submissions':1,'existing_slot_cents':744,'new_pledge_cents':0,'actual_cost':None,'ambiguous_ack_never_reinvoke':True})
 try:call=fn.spawn(BOUND,deadline)
 except BaseException as exc:
  save('spawn-acknowledgement-unknown.json',{'at':time.time(),'error_class':type(exc).__name__,'release_sha256':BOUND,'new_calls_authorized':0,'do_not_respawn':True});raise SystemExit(1)from None
 save('spawn-acknowledgement.json',{'at':time.time(),'call_id':call.object_id,'function_id':fn.object_id,'absolute_deadline':deadline,'release_sha256':BOUND})
 try:call_url=call.get_dashboard_url()
 except Exception:call_url=None
 save('submission-receipt.json',{'at':time.time(),'status':'submitted_not_yet_started_verified','root_authority_sha256':AUTH,'release_sha256':BOUND,'call_id':call.object_id,'call_url':call_url,'app_id':app.app_id,'function_id':fn.object_id,'submitted_at':submit,'absolute_deadline':deadline,'training_seconds':0,'evaluation_games':256,'hard_timeout_seconds':3600,'output_volume_id':release['output_volume_id'],'output_volume_name':'sts2-task116-final-M-dev256-output-v2','output_prefix':release['release_id'],'claim_dict_id':release['claim_dict_id'],'existing_slot_cents':744,'new_pledge_cents':0,'actual_cost':None,'raven_scheduler_dependency':False})
 print((W/'submission-receipt.json').read_text())
if __name__=='__main__':main()
