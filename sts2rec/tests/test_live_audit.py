import copy
import json
import sys
from pathlib import Path
import pytest
ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT/'scripts'))
import sts2_action_executor as executor
import sts2_live as live
from sts2_run_audit import usage_report, run_report
from sts2rec.information import build_information_view
from sts2rec.knowledge import visible_knowledge
from sts2rec.draw_odds import at_least_one, draw_distribution


def selection(indices=(), preview=False):
    return {'state_type':'card_select', 'card_select':{'screen_type':'transform', 'selection_tracking':'available', 'selected_indices':list(indices), 'selected_count':len(indices), 'cards':[{'index':i,'id':'STRIKE','is_selected':i in indices} for i in range(3)], 'preview_showing':preview, 'can_confirm':preview}}


def test_exact_toggle_and_random_preview():
    before, partial, complete = selection(), selection([0]), selection([0,1],True)
    assert executor._action_committed(before,partial,{'action':'select_card','index':0})
    assert not executor._action_committed(before,partial,{'action':'select_card','index':1})
    assert executor._action_committed(partial,complete,{'action':'select_card','index':1})
    assert executor._action_committed(partial,before,{'action':'select_card','index':0})
    a,b=copy.deepcopy(complete),copy.deepcopy(complete)
    a['card_select']['preview_cards']=[{'id':'OFFERING'}]
    b['card_select']['preview_cards']=[{'id':'FEED'}]
    assert executor.digest(a)!=executor.digest(b)
    assert executor.decision_digest(a)==executor.decision_digest(b)
    assert not executor._action_committed(a,b,{'action':'confirm_selection'})
    view=build_information_view(a,'limited')
    assert 'preview_cards' not in view['observation']['card_select']
    assert all(x['action']!='select_card' for x in view['legal_actions'])
    for state in (before,partial):state['card_select'].pop('selection_tracking')
    assert not executor._action_committed(before,partial,{'action':'select_card','index':0})


def test_draw_order_and_card_identity():
    cards=[{'id':'B','name':'乙','index':0,'is_upgraded':True},{'id':'A','name':'甲','index':1}]
    a={'player':{'draw_pile':cards},'seed':'secret'}
    b={'player':{'draw_pile':list(reversed(cards))}}
    assert build_information_view(a,'limited')['observation']==build_information_view(b,'limited')['observation']
    assert a['player']['draw_pile'][0]['index']==0
    pile=live.public_display(a)['state']['player']['draw_pile']
    assert pile[1]['id']=='B' and pile[1]['is_upgraded']


def test_observation_shop_and_provenance(tmp_path):
    state={'state_type':'shop','shop':{'items':[{'index':0,'category':'card','card_id':'ANGER','price':50,'is_stocked':True,'can_afford':True},{'index':1,'category':'card','card_id':'OFFERING','price':200,'is_stocked':True,'can_afford':False}]}}
    root=ROOT/'knowledge/source/v0.107.1'
    live.snapshot(tmp_path,state,root);live.snapshot(tmp_path,state,root)
    rec=json.loads((tmp_path/'observations.jsonl').read_text().splitlines()[0])
    assert len(rec['observation']['shop']['items'])==2
    assert [a['index'] for a in rec['legal_actions'] if a['action']=='shop_purchase']==[0]
    assert len(rec['knowledge_references'])==2
    assert len((tmp_path/'knowledge.jsonl').read_text().splitlines())==2
    assert rec['knowledge_references'][0]['source_sha256']
    assert 'PHROG_PARASITE' in {f['id'] for f in visible_knowledge({'battle':{'enemies':[{'entity_id':'PHROG_PARASITE_0'}]}},root)['facts']}


def prepare(tmp_path):
    plan=tmp_path/'input.json';plan.write_text(json.dumps(dict.fromkeys(('immediate_threat','deck_plan','draw_plan','potion_budget','stop_conditions'),'reviewed')))
    memory=tmp_path/'memory.md';memory.write_text('memory')
    out=tmp_path/'run';live.start_session(out,plan,[memory],'practice',15701,'v0.107.1',None)
    return out,plan,memory


def test_start_preserves_session(tmp_path):
    out,plan,memory=prepare(tmp_path)
    assert (out/'live.log').exists()
    assert json.loads((out/'session.json').read_text())['memory_inputs'][0]['sha256']
    with pytest.raises(SystemExit):live.start_session(out,plan,[memory],'practice',15701,'v0.107.1',None)


def test_timeout_durable_no_duplicate(tmp_path,monkeypatch):
    out,_,_=prepare(tmp_path)
    manifest=json.loads((ROOT/'knowledge/source/v0.107.1/manifest.json').read_text())
    # Exercise the real authority gate without depending on a game installation.
    rules=tmp_path/'knowledge/source/v0.107.1'
    rules.mkdir(parents=True)
    (rules/'manifest.json').write_text(json.dumps(manifest))
    (rules/'rule-evidence.json').write_text(json.dumps({
        'assembly_sha256':manifest['sources']['assembly']['sha256'],
        'decompiled_root':'.','entities':[],'checks':[]}))
    monkeypatch.setattr(live,'ROOT',tmp_path)
    state={'state_type':'map','map':{'next_options':[{'index':0}]},'rules_context':{
        'game_version':manifest['game_version'],'game_commit':manifest['game_commit'],
        'game_assembly_sha256':manifest['sources']['assembly']['sha256']}};calls=[]
    def request(port,action=None):
        if action:calls.append(action)
        return {'status':'ok'} if action else state
    def fail(*args):raise SystemExit('ambiguous')
    monkeypatch.setattr(executor,'request',request);monkeypatch.setattr(executor,'settled_state',fail)
    monkeypatch.setattr(sys,'argv',['sts2_live','act','{"action":"choose_map_node","index":0}','test',executor.decision_digest(state),'--run-dir',str(out)])
    with pytest.raises(SystemExit):live.main()
    with pytest.raises(SystemExit):live.main()
    assert len(calls)==1
    assert '不自动重试' in (out/'live.log').read_text()
    assert json.loads((out/'decisions.jsonl').read_text().splitlines()[-1])['type']=='decision_error'


def test_cost_dedup_and_cutoff(tmp_path):
    def r(response,turn,total,stamp='2026-01-01T00:00:00Z'):
        return {'type':'token_usage_record','timestamp':stamp,'payload':{'response_id':response,'turn_id':turn,'usage':{'input_tokens':total,'cached_input_tokens':total-2,'output_tokens':1,'total_tokens':total+1},'turn_token_usage':{'total_tokens':999999}}}
    rows=[r('a','t1',10),r('a','t1',10),r('b','t2',20),r('c','t2',50,'2026-01-02T00:00:00Z'),{'type':'compacted','timestamp':'2026-01-01T00:00:01Z'}]
    p=tmp_path/'usage.jsonl';p.write_text('\n'.join(json.dumps(r) for r in rows))
    out=usage_report(p,'2026-01-01T12:00:00Z')
    assert out['usage']['total_tokens']==32 and out['requests']==2
    assert out['compaction_count']==1 and out['uncached_input_tokens']==4


def test_reward_dedup_and_scope(tmp_path):
    def intent(key,action,state):return {'type':'decision_intent','decision_id':key,'action':action,'state':state}
    take=intent('a',{'action':'select_card_reward','card_index':0},{'card_reward':{'cards':[{'index':0,'id':'ANGER'}]}})
    rows=[take,take,intent('b',{'action':'skip_card_reward'},{}),intent('c',{'action':'claim_reward','index':0},{'rewards':{'items':[{'index':0,'type':'card'}]}})]
    p=tmp_path/'decisions.jsonl';p.write_text('\n'.join(json.dumps(r) for r in rows))
    assert run_report(p)['card_reward_summary']=={'offers':2,'take':1,'skip':1,'take_rate':0.5}


def test_hypergeometric():
    assert at_least_one(32,1,5)==5/32
    assert at_least_one(32,2,5)==pytest.approx(1-27*26/(32*31))
    assert sum(draw_distribution(10,3,5).values())==pytest.approx(1)
    assert at_least_one(0,0,0)==0
    with pytest.raises(ValueError):at_least_one(4,1,5)
