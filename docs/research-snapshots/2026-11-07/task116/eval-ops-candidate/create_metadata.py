"""Future root-authorized empty metadata only. Default template refuses before SDK."""
from ops_common import *
def main():
 AUTH,BOUND=sys.argv[1:3];a,r=inputs(AUTH,BOUND,phase='metadata',gate=False)
 from fresh_admission import refresh
 refresh(AUTH,BOUND,'metadata')
 import modal
 require(modal.__version__=='1.6.3','SDK pin');ws=modal.Workspace.from_context().hydrate();require(ws.name=='hycrpg','workspace')
 names={'output_volume':'sts2-task116-final-M-dev256-output-v2','claim_dict':'sts2-task116-final-M-dev256-claims-once'}
 save('metadata-intent.json',{'at':time.time(),'root_eval_authority_sha256':AUTH,'bound_release_sha256':BOUND,'names':names,'compute_invocations':0,'new_pledge_cents':0})
 try:v=modal.Volume.from_name(names['output_volume'],environment_name='main',create_if_missing=False).hydrate();vn=False
 except modal.exception.NotFoundError:v=modal.Volume.from_name(names['output_volume'],environment_name='main',create_if_missing=True,version=2).hydrate();vn=True
 require(v._get_metadata().version==2 and v.listdir('/',recursive=True)==[] and v.object_id not in (r['source_volume_id'],r['model_volume_id']),'new empty independent V2')
 try:d=modal.Dict.from_name(names['claim_dict'],environment_name='main',create_if_missing=False).hydrate();dn=False
 except modal.exception.NotFoundError:d=modal.Dict.from_name(names['claim_dict'],environment_name='main',create_if_missing=True).hydrate();dn=True
 require(d.len()==0,'stable once Dict must be empty; never reset')
 save('metadata-receipt.json',{'at':time.time(),'workspace':ws.name,'environment':'main','names':names,'output_volume_id':v.object_id,'output_version':2,'output_empty':True,'output_created':vn,'claim_dict_id':d.object_id,'claim_empty':True,'claim_created':dn,'compute_invocations':0,'root_eval_authority_sha256':AUTH,'bound_release_sha256':BOUND,'new_pledge_cents':0})
 print((W/'metadata-receipt.json').read_text())
if __name__=='__main__':main()
