from ops_common import *
import modal,importlib
AUTH,BOUND=sys.argv[1:3];a,r=inputs(AUTH,BOUND)
assert modal.__version__=='1.6.3' and sys.version_info[:2]==(3,11)
assert a['allow_deploy']is True and not(W/'deployment-intent.json').exists()
f=read('current-admission.json');assert f['root_authority_sha256']==AUTH and f['bound_release_sha256']==BOUND and f['status']=='PASS' and time.time()-f['observed_at']<120
assert f['remaining_commitment_cents']==2926
save('deployment-intent.json',{'at':time.time(),'root_authority_sha256':AUTH,'bound_release_sha256':BOUND,'app_name':r['app_name'],'compute_invocations':0})
sys.path.insert(0,str(W));m=importlib.import_module('function_app');app,fn=m.declare(modal,r);app.deploy(environment_name='main');i=fn.info(refresh=True)
assert i.cpu==(32,32) and i.memory_mib in(65536,(65536,65536)) and i.gpus==[('H100!',1)] and i.timeout==9000 and i.max_retries==0 and i.single_use_containers and i.block_network and i.secrets==[] and i.image_info.image_id==r['image_id']
for p,k,ro in [('/sts2-study','source_volume_id',True),('/sts2-parent','parent_volume_id',True),('/sts2-overnight','output_volume_id',False)]:assert(i.volumes[p].volume_id,i.volumes[p].read_only)==(r[k],ro)
save('deployment-receipt.json',{'at':time.time(),'root_authority_sha256':AUTH,'bound_release_sha256':BOUND,'app_id':app.app_id,'function_id':fn.object_id,'declared_min_containers':0,'declared_max_containers':1,'resources':{'cpu':i.cpu,'memory_mib':i.memory_mib,'gpus':i.gpus,'timeout':i.timeout},'compute_invocations':0});print(CAN(read('deployment-receipt.json')))
