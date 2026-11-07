from ops_common import *
import modal
AUTH=sys.argv[1];raw=(W/'root-metadata-authorization.json').read_bytes();assert H(raw)==AUTH;a=json.loads(raw);assert a['allow_metadata_only']is True and a['compute_invocations_authorized']==0 and time.time()<a['expires']
raw=(W/'release.DISABLED-DRAFT.json').read_bytes();assert H(raw)==a['draft_release_sha256']=='f19852a0740d32492cdb138e18e824ed6811354634e90c2e31f4b15b69f5e44e';r=json.loads(raw)
binding=a['release_binding'];assert set(binding)<= {'source_volume_id','not_after','funding_receipt_sha256','status'};r.update(binding)
assert r['root_scope_sha256']==SCOPE and r['science_plan_sha256']==SCIENCE and r['project_cap_cents']==60000 and r['budget_cap_cents']==3992 and r['parent_checkpoint']is None and r['spec']['env_seed_namespace']=='MODA10PH116MFRESH1' and r['source_volume_id']=='vo-NWLwwK5soAAAaKiLjay1Ek'and r['seed_range']==[0,134217728]
assert not(W/'metadata-intent.json').exists(),'existing metadataintent: same-name reconcile only'
assert modal.__version__=='1.6.3'and modal.Workspace.from_context().hydrate().name=='hycrpg'
names={'output_volume':'sts2-task116-m-fresh100m-output-v2','claim_dict':'sts2-task116-m-fresh100m-claims-once'};save('metadata-intent.json',{'at':time.time(),'authority_sha256':AUTH,'names':names,'compute_invocations':0})
try:v=modal.Volume.from_name(names['output_volume'],environment_name='main',create_if_missing=False).hydrate();vc=False
except modal.exception.NotFoundError:v=modal.Volume.from_name(names['output_volume'],environment_name='main',create_if_missing=True,version=2).hydrate();vc=True
assert v._get_metadata().version==2 and v.listdir('/',recursive=True)==[] and v.object_id!=r['source_volume_id']
try:d=modal.Dict.from_name(names['claim_dict'],environment_name='main',create_if_missing=False).hydrate();dc=False
except modal.exception.NotFoundError:d=modal.Dict.from_name(names['claim_dict'],environment_name='main',create_if_missing=True).hydrate();dc=True
assert d.len()==0;r.update(output_volume_id=v.object_id,claim_dict_id=d.object_id);b=CAN(r).encode()
with(W/'release.bound.json').open('xb')as f:f.write(b);f.flush();os.fsync(f.fileno())
save('metadata-receipt.json',{'at':time.time(),'root_metadata_authority_sha256':AUTH,'bound_release_sha256':H(b),'draft_release_sha256':H(raw),'output_volume_id':v.object_id,'output_version':2,'output_empty':True,'output_created':vc,'claim_dict_id':d.object_id,'claim_empty':True,'claim_created':dc,'names':names,'binding_diff':{k:{'before':json.loads(raw).get(k),'after':r[k]}for k in r if r[k]!=json.loads(raw).get(k)},'compute_invocations':0,'ledger_mutations':0});print(CAN(read('metadata-receipt.json')))
