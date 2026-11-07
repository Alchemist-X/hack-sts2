from ops_common import *
import modal,importlib
AUTH,BOUND=sys.argv[1:3];a,r=inputs(AUTH,BOUND);assert a['allow_deploy']is True and not(W/'deployment-intent.json').exists()
assert modal.__version__=='1.6.3'and sys.version_info[:2]==(3,11)
f=read('current-admission.json');assert f['status']=='PASS'and f['root_authority_sha256']==AUTH and f['bound_release_sha256']==BOUND and f['remaining_commitment_cents']==5762 and time.time()-f['observed_at']<120
save('deployment-intent.json',{'at':time.time(),'root_authority_sha256':AUTH,'bound_release_sha256':BOUND,'app_name':r['app_name'],'compute_invocations':0});sys.path.insert(0,str(W));m=importlib.import_module('function_app');app,fn=m.declare(modal,r);app.deploy(environment_name='main');i=fn.info(refresh=True)
assert i.cpu==(32,32)and i.memory_mib in(65536,(65536,65536))and i.gpus==[('H100!',1)]and i.timeout==21600 and i.max_retries==0 and i.block_network and i.single_use_containers and i.image_info.image_id==r['image_id']and i.secrets==[]
expected={'/sts2-study':(r['source_volume_id'],True),'/sts2-overnight':(r['output_volume_id'],False)};assert set(i.volumes)==set(expected)
for key,value in expected.items():assert(i.volumes[key].volume_id,i.volumes[key].read_only)==value
save('deployment-receipt.json',{'at':time.time(),'root_authority_sha256':AUTH,'bound_release_sha256':BOUND,'app_id':app.app_id,'function_id':fn.object_id,'resources':{'cpu':i.cpu,'memory_mib':i.memory_mib,'gpus':i.gpus,'timeout':i.timeout},'source_RO_output_RW_no_parent':True,'declared_min_containers':0,'declared_max_containers':1,'compute_invocations':0});print(CAN(read('deployment-receipt.json')))
