"""Future sole-writer nonfinancial claim of existing744. Never adds pledge/cost row."""
from ops_common import *

def claim_in_transaction(db,append_event,a,r,g,auth_sha,release_sha,*,now):
 from plan_liabilities import liabilities
 from fresh_admission import current_shared_liabilities
 check_admission(g,a,r,auth_sha,release_sha,now=now)
 require(min(a['expires'],r['not_after'])-now>=3500,'insufficient explicit finaldev admission window; no claim')
 require(H(db.serialize())==g['authoritative_ledger_state_sha256'],'ledger changed since dynamic fresh guard')
 s=current_shared_liabilities(db,liabilities);require(s['M116_active_allocations']=={'final_dev':744,'shared_blind_increment':1026}and s['M116_historical_allocations'].get('train')==3992 and s['shared_blind_base_in_S115_cents']==1075,'original M744 slot/S1075/M1026 unavailable')
 require(not db.execute('SELECT 1 FROM events WHERE id=?',(DISPATCH,)).fetchone(),'single116finaldev already claimed; never retry unknown')
 payload={'plan_event':PLAN,'slot':'final_dev','existing_slot_cents':744,'new_pledge_cents':0,'financial_representation':'original M5762 remaining744 slot unchanged; this is nonfinancial once-dispatch metadata','root_eval_authority_sha256':auth_sha,'bound_release_sha256':release_sha,'root_terminal_decision_sha256':r['root_terminal_decision_sha256'],'checkpoint_sha256':r['checkpoint_sha256'],'actual_cost':None,'unknown_submit_blocks_reinvoke':True}
 append_event(db,DISPATCH,'operator_existing_plan_slot_dispatch_claim',payload,now);return payload

def claim(a,r,g,auth_sha,release_sha):
 root=Path('/var/lib/sts2-modal-production');sys.path.insert(0,str(root/'control-047-m/sim/tools'));from modal_research.store import Store
 require(a['allow_consume_existing_slot']is True,'future exact root claim permission required')
 store=Store.__new__(Store);store.path=root/'operation.sqlite3';store.clock=time.time
 with store.transaction()as db:payload=claim_in_transaction(db,Store.append_event,a,r,g,auth_sha,release_sha,now=time.time())
 save('slot-claim-receipt.json',payload);return payload
