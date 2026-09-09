import copy
import hashlib
import json
from pathlib import Path
import sys

import pytest

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT/'scripts'))
import sts2_live as live
from sts2rec.knowledge import authority, visible_knowledge
from sts2rec.legal_actions import derive_legal_actions

RULES = ROOT/'knowledge/source/v0.107.1'
LOCAL_SOURCES = RULES/json.loads((RULES/'rule-evidence.json').read_text())['decompiled_root']
requires_game_source = pytest.mark.skipif(
    not LOCAL_SOURCES.is_dir(),
    reason='integration check requires a local v0.107.1 decompilation; see docs/sts2-live-protocol.md',
)


def fingerprint():
    m = json.loads((RULES/'manifest.json').read_text())
    return {'game_version': 'v'+m['game_version'], 'game_commit': m['game_commit'],
            'game_assembly_sha256': m['sources']['assembly']['sha256']}


def test_build_identity_fails_closed():
    assert authority({}, RULES)['status'] == 'unverified'
    state = {'rules_context': fingerprint()}
    assert authority(state, RULES)['status'] == 'verified'
    for field in ('game_version', 'game_commit', 'game_assembly_sha256'):
        wrong = copy.deepcopy(state); wrong['rules_context'][field] = 'wrong'
        assert authority(wrong, RULES)['status'] == 'mismatch'


@requires_game_source
def test_encounter_and_decorations_reach_decision_context(tmp_path):
    state = {'rules_context': fingerprint(), 'state_type': 'combat',
             'battle': {'enemies': [{'id': 'PHROG_PARASITE', 'hp': 68}]},
             'player': {'status': [{'id': 'TAINTED_POWER', 'amount': 2}],
                        'hand': [{'id': 'STRIKE_IRONCLAD', 'enchantment': {'id': 'INSTINCT'}}]}}
    result = live.snapshot(tmp_path, state, RULES)
    context = result['decision_context']
    assert context['authority']['status'] == 'verified'
    facts = {f['id']: f for f in context['active_facts']}
    assert {'PHROG_PARASITE', 'INFESTED_POWER', 'WRIGGLER', 'INFECTION', 'TAINTED_POWER', 'INSTINCT'} <= facts.keys()
    assert 'INFECT_MOVE' in facts['PHROG_PARASITE']['move_state_machine_source']
    assert 'ModifyDamageAdditive' in facts['TAINTED_POWER']['rule_source']
    assert {'PHROG_PARASITE_CYCLE', 'TAINTED_PER_HIT', 'INSTINCT_BEFORE_STRENGTH'} <= {c['id'] for c in context['rules']}
    assert all(f['source_sha256'] for f in context['active_facts'])
    assert json.loads((tmp_path/'observations.jsonl').read_text())['rule_authority']['status'] == 'verified'


@requires_game_source
def test_aeonglass_cycle_and_wither_are_retrieved():
    result = visible_knowledge({'rules_context': fingerprint(), 'battle': {'enemies': [{'id':'AEONGLASS'}]}}, RULES)
    facts = {f['id']: f for f in result['decision_context']['active_facts']}
    assert {'AEONGLASS', 'WITHER', 'WITHERING_PRESENCE_POWER'} <= facts.keys()
    assert 'EYE_LASERS_MOVE' in facts['AEONGLASS']['move_state_machine_source']
    assert 'AEONGLASS_CYCLE' in {c['id'] for c in result['decision_context']['rules']}


@pytest.mark.parametrize('mutation', ['changed', 'removed'])
def test_source_dependencies_and_fail_closed_validation(tmp_path, mutation):
    """Portable contract test using small synthetic rules, not game source."""
    manifest = json.loads((RULES/'manifest.json').read_text())
    (tmp_path/'manifest.json').write_text(json.dumps(manifest))
    entities = []
    for kind, identity, model, filename, code in [
        ('powers', 'SPAWNER_POWER', 'POWER.SPAWNER_POWER', 'SpawnerPower.cs',
         'PowerCmd.Apply<ChildPower>();'),
        ('powers', 'CHILD_POWER', 'POWER.CHILD_POWER', 'ChildPower.cs',
         'CardCmd.Create<CardFromPower>();'),
        ('cards', 'CARD_FROM_POWER', 'CARD.CARD_FROM_POWER', 'CardFromPower.cs',
         'public class CardFromPower {}'),
    ]:
        path = tmp_path/filename
        path.write_text(code)
        entities.append({'kind':kind, 'id':identity, 'model_id':model, 'source':{
            'decompiled_path':filename, 'sha256':hashlib.sha256(path.read_bytes()).hexdigest(),
            'rule_source':code}})
    check = {'id':'SPAWNER_CHAIN', 'related_models':['POWER.SPAWNER_POWER'],
             'sources':[{'decompiled_path':'SpawnerPower.cs',
                         'sha256':entities[0]['source']['sha256']}]}
    (tmp_path/'rule-evidence.json').write_text(json.dumps({
        'assembly_sha256':fingerprint()['game_assembly_sha256'], 'decompiled_root':'.',
        'entities':entities, 'checks':[check]}))
    state = {'rules_context':fingerprint(), 'player':{'status':[{'id':'SPAWNER_POWER'}]}}
    result = live.snapshot(tmp_path/'run', state, tmp_path)
    assert result['decision_context']['authority']['status'] == 'verified'
    assert {f['model_id'] for f in result['decision_context']['active_facts']} == {
        'POWER.SPAWNER_POWER', 'POWER.CHILD_POWER', 'CARD.CARD_FROM_POWER'}
    assert result['decision_context']['rules'] == [check]
    source = tmp_path/'ChildPower.cs'
    if mutation == 'changed':
        source.write_text('changed rule')
    else:
        source.unlink()
    invalid = visible_knowledge(state, tmp_path)
    assert invalid['authority']['status'] == 'mismatch'
    assert {'kind':'powers', 'id':'CHILD_POWER',
            'reason':'local_source_missing_or_changed'} in invalid['authority']['source_errors']


def test_changed_source_is_reported_not_silently_trusted(tmp_path):
    manifest = json.loads((RULES/'manifest.json').read_text())
    (tmp_path/'manifest.json').write_text(json.dumps(manifest))
    source = tmp_path/'Power.cs'; source.write_text('reviewed rule')
    row = {'kind':'powers','id':'TEST_POWER','model_id':'POWER.TEST_POWER','source':{
        'decompiled_path':'Power.cs','sha256':hashlib.sha256(source.read_bytes()).hexdigest(),'rule_source':'reviewed rule'}}
    (tmp_path/'rule-evidence.json').write_text(json.dumps({'assembly_sha256':fingerprint()['game_assembly_sha256'],
        'decompiled_root':'.','entities':[row],'checks':[]}))
    state = {'rules_context':fingerprint(),'player':{'status':[{'id':'TEST_POWER'}]}}
    assert visible_knowledge(state,tmp_path)['authority']['status']=='verified'
    source.write_text('changed rule')
    assert visible_knowledge(state,tmp_path)['authority']['status']=='mismatch'


def test_piles_keep_current_decorations_and_unknowns_are_explicit():
    state = {'player': {'draw_pile': [
        {'id':'STRIKE_IRONCLAD','description':'6','enchantment':None},
        {'id':'STRIKE_IRONCLAD','description':'12','enchantment':{'id':'INSTINCT'}}],
        'status':[{'id':'NEW_UNSUPPORTED_POWER'}]}}
    assert len(live.public_display(state)['state']['player']['draw_pile']) == 2
    assert {'kind':'powers','id':'NEW_UNSUPPORTED_POWER'} in visible_knowledge(state,RULES)['missing']


def test_inactive_and_unhittable_enemies_are_not_attack_targets():
    state = {'state_type':'monster','battle':{'is_play_phase':True,'enemies':[
        {'entity_id':'GHOST_0','hp':20,'is_targetable':False}],
        'inactive_enemies':[{'entity_id':'REVIVER_0','hp':0}]},'player':{'hand':[
        {'index':0,'can_play':True,'target_type':'AnyEnemy'}]}}
    assert not [a for a in derive_legal_actions(state) if a['action']=='play_card']
    state['battle']['enemies'][0]['is_targetable'] = True
    assert len([a for a in derive_legal_actions(state) if a['action']=='play_card']) == 1


def test_visible_menu_options_are_preserved():
    assert [a['option'] for a in derive_legal_actions({'state_type':'menu','options':[
        'continue',{'name':'singleplayer','enabled':False}]})] == ['continue']
