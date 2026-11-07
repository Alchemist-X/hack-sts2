from ops_common import *
import modal,copy
AUTH=sys.argv[1];raw=(W/'root-metadata-authorization.json').read_bytes();assert H(raw)==AUTH;a=json.loads(raw)
assert a['allow_metadata_only']is True and a['compute_invocations_authorized']==0 and time.time()<a['expires']
draft=(W/'release.segment2.DISABLED-TEMPLATE.json').read_bytes();assert H(draft)==a['draft_release_sha256']=='644f849869feb589e5713e893b46020f2605f7ca1bb6ace817e48fc5fb95941c'
r=json.loads(draft);binding=a['release_binding'];allowed={'root_selection_sha256','science_plan_sha256','segment_authority_sha256','funding_receipt_sha256','parent_evidence_sha256','budget_cap_cents','project_cap_cents','source_volume_id','parent_volume_id','not_after','status','release_id','parent','parent_path','parent_strategy','old_reserved_end','segment'}
assert set(binding)<=allowed and binding['budget_cap_cents']==1629 and binding['science_plan_sha256']=='548320e5ba99de37c8cfbf93031164ab948259003f66c2394d1eff7b152de2fb'
assert r['project_cap_cents']==60000 and binding.get('project_cap_cents',60000)==60000
r.update(binding);accepted_parent(a['first_parent_proof_sha256'],r);assert not(W/'metadata-intent.json').exists(),'unknown metadata intent: reconcile same names only'
assert modal.__version__=='1.6.3' and modal.Workspace.from_context().hydrate().name=='hycrpg'
names={'output_volume':'sts2-task115-control-continuation-segment2-output-v2','claim_dict':'sts2-task115-control-continuation-segment2-claims-once'}
save('metadata-intent.json',{'at':time.time(),'root_metadata_authority_sha256':AUTH,'names':names,'compute_invocations':0})
try:v=modal.Volume.from_name(names['output_volume'],environment_name='main',create_if_missing=False).hydrate();vc=False
except modal.exception.NotFoundError:v=modal.Volume.from_name(names['output_volume'],environment_name='main',create_if_missing=True,version=2).hydrate();vc=True
assert v._get_metadata().version==2 and v.listdir('/',recursive=True)==[] and v.object_id not in(r['source_volume_id'],r['parent_volume_id'])
try:d=modal.Dict.from_name(names['claim_dict'],environment_name='main',create_if_missing=False).hydrate();dc=False
except modal.exception.NotFoundError:d=modal.Dict.from_name(names['claim_dict'],environment_name='main',create_if_missing=True).hydrate();dc=True
assert d.len()==0;r.update(output_volume_id=v.object_id,claim_dict_id=d.object_id)
b=CAN(r).encode()
with(W/'release.bound.json').open('xb')as f:f.write(b);f.flush();os.fsync(f.fileno())
save('metadata-receipt.json',{'at':time.time(),'root_metadata_authority_sha256':AUTH,'bound_release_sha256':H(b),'draft_release_sha256':H(draft),'output_volume_id':v.object_id,'output_version':2,'output_empty':True,'output_created':vc,'claim_dict_id':d.object_id,'claim_empty':True,'claim_created':dc,'names':names,'binding_diff':{k:{'before':json.loads(draft).get(k),'after':r[k]}for k in r if r[k]!=json.loads(draft).get(k)},'compute_invocations':0,'ledger_mutations':0});print(CAN(read('metadata-receipt.json')))
