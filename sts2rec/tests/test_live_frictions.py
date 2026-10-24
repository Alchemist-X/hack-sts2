"""Regression tests for the live-harness frictions seen in the 2026-10-23 runs.

Fixtures under fixtures/live_frictions/ are trimmed observations recorded from
those runs (piles emptied, rules_context removed).
"""
import copy
import json
import sys
from pathlib import Path

import pytest

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'scripts'))
import sts2_action_executor as executor  # noqa: E402
import sts2_action_refs as refs  # noqa: E402
import sts2_live as live  # noqa: E402

FIX = Path(__file__).parent / 'fixtures' / 'live_frictions'


def load(name):
    return json.loads((FIX / f'{name}.json').read_text())


# ---------------------------------------------------------------- friction 5
@pytest.mark.parametrize('expect,card', [
    ('bash', {'name': 'Bash'}),                       # case-insensitive
    ('BASH+', {'name': 'Bash+', 'is_upgraded': True}),
    ('Bash++', {'name': 'Bash+', 'is_upgraded': True}),  # cosmetic "++"
    ('Bash', {'name': 'Bash+', 'is_upgraded': True}),    # upgrade renamed the card
    ('BASH', {'name': 'Bash+', 'id': 'BASH'}),
    ('deal 8 damage', {'name': 'Bash', 'description': 'Deal [blue]8[/blue] damage.'}),
    ("Perfected Strike", {'name': 'Perfected  Strike'}),
])
def test_expect_guard_is_normalized(expect, card):
    assert refs.expect_matches(expect, card)


def test_expect_guard_still_rejects_wrong_card():
    assert not refs.expect_matches('Strike', {'name': 'Defend', 'id': 'DEFEND'})
    state = load('combat_duplicates')
    with pytest.raises(refs.ActionRefError, match='expect'):
        refs.resolve(state, {'action': 'play_card', 'card_index': 1, 'expect': 'Strike'})


def test_card_label_has_single_upgrade_marker():
    assert refs.card_label({'name': 'Bash+', 'is_upgraded': True}) == 'Bash+'
    assert refs.card_label({'name': 'Bash', 'is_upgraded': True}) == 'Bash+'
    assert refs.split_upgrade('Bash++') == ('bash', 1)
    assert refs.split_upgrade('Searing Blow+3') == ('searing blow', 3)


# ---------------------------------------------------------------- friction 2
def test_card_ref_distinguishes_upgrade_tier():
    state = load('combat_duplicates')
    hand = {c['index']: refs.card_label(c) for c in state['player']['hand']}
    plus = refs.resolve(state, {'action': 'play_card', 'card': 'Strike+', 'target': 'Scroll of Biting@2'})
    plain = refs.resolve(state, {'action': 'play_card', 'card': 'Strike', 'target': 'Scroll of Biting@2'})
    assert hand[plus['wire']['card_index']] == 'Strike+'
    assert hand[plain['wire']['card_index']] == 'Strike'
    assert plus['wire']['target'] == '2'  # combat_id, stable across kills


def test_identical_copies_are_listed_once_and_different_copies_need_k():
    state = load('combat_duplicates')
    observed = [json.dumps(a['ref'], sort_keys=True) for a in refs.observe_actions(state)]
    assert len(observed) == len(set(observed))
    hand = state['player']['hand']
    plain = [c for c in hand if refs.card_label(c) == 'Strike']
    plain[1]['description'] = plain[1]['description'] + ' (modified)'
    with pytest.raises(refs.ActionRefError, match='ambiguous'):
        refs.resolve(state, {'action': 'play_card', 'card': 'Strike', 'target': 'Scroll of Biting@2'})
    second = refs.resolve(state, {'action': 'play_card', 'card': 'Strike#2', 'target': 'Scroll of Biting@2'})
    assert second['wire']['card_index'] == plain[1]['index']


def test_entity_renumbering_after_kill_never_retargets():
    before, after = load('kill_before'), load('kill_after')
    observed = refs.entity_map(before)
    assert observed == {'INKLET_0': 1, 'INKLET_1': 2, 'INKLET_2': 3}
    # After INKLET_1 (combat_id 2) died the game renamed combat_id 3 to INKLET_1.
    assert {e['entity_id']: e['combat_id'] for e in after['battle']['enemies']}['INKLET_1'] == 3
    with pytest.raises(refs.ActionRefError, match='combat_id 2'):
        refs.resolve(after, {'action': 'play_card', 'card': 'Strike', 'target': 'INKLET_1'},
                     allow_index=False, observed_entities=observed)
    ok = refs.resolve(after, {'action': 'play_card', 'card': 'Strike', 'target': 'Inklet@3'}, allow_index=False)
    assert ok['wire']['target'] == '3'
    with pytest.raises(refs.ActionRefError):
        refs.resolve(after, {'action': 'play_card', 'card': 'Strike', 'target': 'Twig Slime@3'})


def test_chained_steps_reject_positional_indices():
    state = load('kill_after')
    assert refs.resolve(state, {'action': 'play_card', 'card_index': 0, 'target': 'Inklet@1'})['wire']['card_index'] == 0
    with pytest.raises(refs.ActionRefError, match='positional'):
        refs.resolve(state, {'action': 'play_card', 'card_index': 0, 'target': 'Inklet@1'}, allow_index=False)


def test_shop_refs_survive_index_shift_and_block_unaffordable():
    state = load('shop')
    wire = refs.resolve(state, {'action': 'shop_purchase', 'item': 'Shrug It Off'})['wire']
    item = next(i for i in state['shop']['items'] if i['index'] == wire['index'])
    assert item.get('card_id') == 'SHRUG_IT_OFF'
    shifted = copy.deepcopy(state)
    shifted['shop']['items'] = [dict(i, index=n) for n, i in enumerate(i for i in shifted['shop']['items']
                                                                       if i.get('card_id') != 'HEMOKINESIS')]
    wire2 = refs.resolve(shifted, {'action': 'shop_purchase', 'item': 'card:SHRUG_IT_OFF'})['wire']
    assert wire2['index'] == wire['index'] - 1
    poor = copy.deepcopy(state)
    for i in poor['shop']['items']:
        if i.get('relic_id') == 'LIZARD_TAIL':
            i['can_afford'] = False
    with pytest.raises(refs.ActionRefError, match='cannot_afford'):
        refs.resolve(poor, {'action': 'shop_purchase', 'item': 'relic:LIZARD_TAIL'})


# ---------------------------------------------------------------- friction 6
def test_potion_reward_blocked_when_slots_full_and_discard_offered():
    state = load('rewards_potion_full')
    assert refs.potion_slots_full(state)
    blocked = refs.blocked_actions(state)
    assert blocked and blocked[0]['reason'] == 'potion_slots_full'
    with pytest.raises(refs.ActionRefError, match='potion_slots_full'):
        refs.resolve(state, {'action': 'claim_reward', 'reward': 'potion'})
    actions = refs.observe_actions(state)
    assert {'action': 'discard_potion', 'potion': 'slot:1', 'potion_id': 'DUPLICATOR'} in [a['ref'] for a in actions]
    assert refs.resolve(state, {'action': 'discard_potion', 'potion': 'DUPLICATOR'})['wire'] == {'action': 'discard_potion', 'slot': 1}
    # With a free slot the same reward is claimable.
    assert any(a['ref'] == {'action': 'claim_reward', 'reward': 'potion:LIQUID_BRONZE'}
               for a in refs.observe_actions(load('rewards')))


def test_potion_matching_uses_ids_not_name_substrings():
    state = load('kill_before')
    assert refs.resolve(state, {'action': 'use_potion', 'potion': 'EXPLOSIVE_AMPOULE'})['wire']['slot'] == 0
    with pytest.raises(refs.ActionRefError):
        refs.resolve(state, {'action': 'use_potion', 'potion': 'Explosive'})  # substring is not enough
    with pytest.raises(refs.ActionRefError, match='potion'):
        refs.resolve(state, {'action': 'use_potion', 'potion': 'slot:0', 'potion_id': 'DUPLICATOR'})


# ---------------------------------------------------------------- friction 1
def test_decision_hash_ignores_volatile_execution_block():
    a = load('kill_after')
    b = copy.deepcopy(a)
    b['execution']['request_id'] = 'other'
    b['execution']['queue_idle'] = False
    assert executor.decision_digest(a) == executor.decision_digest(b)


def test_actionable_frames():
    combat = load('kill_after')
    assert executor.actionable(combat)
    busy = copy.deepcopy(combat)
    busy['execution']['queue_idle'] = False
    assert not executor.actionable(busy)
    enemy_turn = copy.deepcopy(combat)
    enemy_turn['battle']['is_play_phase'] = False
    assert not executor.actionable(enemy_turn)
    loading_map = copy.deepcopy(load('map'))
    loading_map['map']['next_options'] = []
    assert not executor.actionable(loading_map)
    assert executor.actionable(load('map'))


def test_map_step_commits_only_when_room_loads():
    before = load('map')
    floor_ticked = copy.deepcopy(before)
    floor_ticked['run']['floor'] = before['run']['floor'] + 1  # race: floor moves first
    action = {'action': 'choose_map_node', 'index': 1}
    assert not executor._action_committed(before, floor_ticked, action)
    assert executor._action_committed(before, load('combat'), action)


def test_end_turn_commit_and_refusal_outside_play_phase():
    turn1 = load('kill_after')
    enemy = copy.deepcopy(turn1)
    enemy['battle']['is_play_phase'] = False
    enemy['battle']['turn'] = 'enemy'
    # The enemy phase is only an intermediate frame; the next player round commits.
    assert not executor._action_committed(turn1, enemy, {'action': 'end_turn'})
    turn2 = copy.deepcopy(turn1)
    turn2['battle']['round'] += 1
    assert executor._action_committed(turn1, turn2, {'action': 'end_turn'})
    assert executor._action_committed(turn1, load('rewards'), {'action': 'end_turn'})
    with pytest.raises(refs.ActionRefError, match='play phase'):
        refs.resolve(enemy, {'action': 'end_turn'})
    with pytest.raises(refs.ActionRefError):
        refs.resolve(load('map'), refs.parse_request('end')[0])


def test_exhaust_detection_is_language_agnostic():
    assert executor._mentions_exhaust('Gain 5 Block. Exhaust.', set())
    assert executor._mentions_exhaust('', {'Exhaust'})
    assert executor._mentions_exhaust('获得5点格挡。消耗。', set())
    assert not executor._mentions_exhaust('Deal 6 damage.', set())


class FakeGame:
    """Minimal stand-in for the MCP endpoint driven by a transition function."""

    def __init__(self, state, transition=None):
        self.state, self.transition, self.posts = state, transition, []

    def __call__(self, port, action=None):
        if action is None:
            return copy.deepcopy(self.state)
        self.posts.append(action)
        if self.transition:
            new = self.transition(self.state, action, len(self.posts))
            if isinstance(new, BaseException):
                raise new
            self.state = new
        return {'status': 'ok'}


def test_wait_settled_reports_unsettled_and_settles(monkeypatch):
    busy = load('kill_after')
    busy['execution']['queue_idle'] = False
    monkeypatch.setattr(executor, 'request', FakeGame(busy))
    state, settled = executor.wait_settled(1, timeout=0.3, quiet_window=0.05, poll=0.02)
    assert not settled
    monkeypatch.setattr(executor, 'request', FakeGame(load('kill_after')))
    state, settled = executor.wait_settled(1, timeout=2, quiet_window=0.05, poll=0.02)
    assert settled and state['state_type'] == load('kill_after')['state_type']


# ------------------------------------------------------- end-to-end harness
@pytest.fixture
def harness(tmp_path, monkeypatch):
    plan = tmp_path / 'input.json'
    plan.write_text(json.dumps(dict.fromkeys(('immediate_threat', 'deck_plan', 'draw_plan', 'potion_budget',
                                              'stop_conditions'), 'reviewed')))
    memory = tmp_path / 'memory.md'
    memory.write_text('memory')
    out = tmp_path / 'run'
    live.start_session(out, plan, [memory], 'practice', 15701, 'v0.107.1', None, True)
    fast_wait = executor.wait_settled
    def quick(port, timeout=20, quiet_window=None, initial=None, poll=None):
        return fast_wait(port, timeout=min(timeout, 2), quiet_window=0.02, poll=0.01, initial=initial)
    monkeypatch.setattr(executor, 'wait_settled', quick)
    monkeypatch.setattr(executor, 'quiet_window_for', lambda action, state: 0.02)
    monkeypatch.setattr(live, 'TREASURE_FIRST_CLICK_S', 0.3)

    def act(game, payload, expected_hash=None, extra=()):
        monkeypatch.setattr(executor, 'request', game)
        h = expected_hash or executor.decision_digest(game.state)
        monkeypatch.setattr(sys, 'argv', ['sts2_live', 'act', payload, 'test reason', h, '--run-dir', str(out),
                                          '--settle-timeout', '2', *extra])
        return live.main()
    return out, act


def run(act, capsys, *args, **kw):
    try:
        act(*args, **kw)
        code = 0
    except SystemExit as stop:
        code = stop.code
    lines = [l for l in capsys.readouterr().out.splitlines() if l.startswith('{')]
    return code, json.loads(lines[-1])


def test_authority_requires_explicit_practice_opt_in(tmp_path, monkeypatch):
    observed = {'decision_context': {'authority': {'status': 'unverified', 'mismatches': ['sha']}}}
    with pytest.raises(live.ActError) as err:
        live.check_authority({'mode': 'practice'}, observed, False)
    assert err.value.payload['error'] == 'rule_authority_unverified'
    assert live.check_authority({'mode': 'practice'}, observed, True) is False
    with pytest.raises(live.ActError):
        live.check_authority({'mode': 'evaluation'}, observed, True)
    assert live.check_authority({'mode': 'evaluation'}, {'decision_context': {'authority': {'status': 'verified'}}}, False)


def test_stale_hash_is_refused_with_fresh_observation(harness, capsys):
    out, act = harness
    game = FakeGame(load('kill_after'))
    code, payload = run(act, capsys, game, '{"action":"end_turn"}', expected_hash='deadbeef')
    assert code == 2 and payload['error'] == 'stale_hash'
    assert payload['observation'] and not game.posts


def test_select_screen_auto_confirms(harness, capsys):
    out, act = harness

    def transition(state, action, n):
        state = copy.deepcopy(state)
        block = state['card_select']
        if action['action'] == 'select_card':
            block['selected_indices'] = block.get('selected_indices', []) + [action['index']]
            for c in block['cards']:
                if c['index'] == action['index']:
                    c['is_selected'] = True
            if len(block['selected_indices']) >= block['min_select']:
                block['preview_showing'] = block['can_confirm'] = True
            return state
        assert action['action'] == 'confirm_selection'
        return load('map')

    start = load('card_select_upgrade')
    game = FakeGame(start, transition)
    code, payload = run(act, capsys, game, '{"action":"select","cards":["Strike","Strike","Defend","Setup Strike"]}')
    assert code == 0, payload
    picked = [p['index'] for p in game.posts if p['action'] == 'select_card']
    assert len(picked) == len(set(picked)) == 4  # "Strike" twice picks two copies
    assert game.posts[-1] == {'action': 'confirm_selection'}
    assert payload['steps'][-1]['harness_followup'] == 'auto_confirm_selection'
    # Too few cards are refused before anything is sent.
    game2 = FakeGame(load('card_select_upgrade'), transition)
    code, payload = run(act, capsys, game2, '{"action":"select","cards":["Strike"]}')
    assert code == 2 and payload['error'] == 'unresolvable_action' and not game2.posts


def test_hand_select_auto_confirms(harness, capsys):
    out, act = harness

    def transition(state, action, n):
        state = copy.deepcopy(state)
        hs = state['hand_select']
        if action['action'] == 'combat_select_card':
            card = next(c for c in hs['cards'] if c['index'] == action['card_index'])
            hs['cards'] = [c for c in hs['cards'] if c is not card]
            hs['selected_cards'] = hs.get('selected_cards', []) + [card]
            hs['can_confirm'] = True
            return state
        assert action['action'] == 'combat_confirm_selection'
        nxt = load('kill_after')
        return nxt

    game = FakeGame(load('hand_select_upgrade'), transition)
    code, payload = run(act, capsys, game, '{"action":"select","card":"Strike"}')
    assert code == 0, payload
    assert [p['action'] for p in game.posts] == ['combat_select_card', 'combat_confirm_selection']


def test_treasure_reclicks_only_while_relic_still_offered(harness, capsys):
    out, act = harness
    sequence = {1: load('treasure_opened_unclaimed'), 2: load('treasure_claimed')}
    game = FakeGame(load('treasure_unopened'), lambda s, a, n: sequence[n])
    code, payload = run(act, capsys, game, '{"action":"claim_treasure_relic","relic":"VENERABLE_TEA_SET"}')
    assert code == 0, payload
    assert game.posts == [{'action': 'claim_treasure_relic', 'index': 0}] * 2
    assert any(s.get('harness_followup') == 'treasure_reclick' for s in payload['steps'])


def test_chain_stops_on_dead_target_and_keeps_audit(harness, capsys):
    out, act = harness
    game = FakeGame(load('kill_before'), lambda s, a, n: load('kill_after'))
    payload_in = json.dumps([{'action': 'play_card', 'card': 'Twin Strike+', 'target': 'INKLET_1'},
                             {'action': 'play_card', 'card': 'Strike', 'target': 'INKLET_1'}])
    code, payload = run(act, capsys, game, payload_in)
    assert code == 2 and payload['error'] == 'unresolvable_action' and payload['failed_step'] == 2
    assert game.posts == [{'action': 'play_card', 'card_index': 2, 'target': '2'}]
    assert not (out / 'pending-action.json').exists()
    rows = [json.loads(l) for l in (out / 'decisions.jsonl').read_text().splitlines()]
    assert [r['type'] for r in rows if r['type'].startswith('decision_')] == ['decision_intent', 'decision_outcome']


def test_rejected_action_clears_pending_and_unresolved_requires_reconcile(harness, capsys, monkeypatch):
    out, act = harness
    game = FakeGame(load('kill_after'), lambda s, a, n: SystemExit('HTTP 400 card not playable'))
    code, payload = run(act, capsys, game, '{"action":"play_card","card":"Defend"}')
    assert code == 2 and payload['error'] == 'action_rejected'
    assert not (out / 'pending-action.json').exists()

    def swallow(state, action, n):  # game moved but never showed the commit
        moved = copy.deepcopy(state)
        moved['battle']['round'] += 0
        moved['player']['energy'] = moved['player'].get('energy', 0) + 1
        return moved
    game = FakeGame(load('kill_after'), swallow)
    monkeypatch.setattr(executor, 'settled_state', lambda *a, **k: (_ for _ in ()).throw(SystemExit('no commit')))
    code, payload = run(act, capsys, game, '{"action":"play_card","card":"Defend"}')
    assert code == 2 and payload['error'] == 'action_unresolved'
    assert (out / 'pending-action.json').exists()
    code, payload = run(act, capsys, game, '{"action":"end_turn"}')
    assert payload['error'] == 'pending_unresolved' and len(game.posts) == 1
    monkeypatch.setattr(sys, 'argv', ['sts2_live', 'reconcile', 'Defend was played (block shown)', '--run-dir',
                                      str(out), '--settle-timeout', '2'])
    live.main()
    assert not (out / 'pending-action.json').exists()
    rows = [json.loads(l) for l in (out / 'decisions.jsonl').read_text().splitlines()]
    assert rows[-1]['type'] == 'decision_outcome_recovered'


# ------------------------------------------- 2026-10-23 benchmark follow-ups
def test_unplayable_hand_cards_are_listed_as_unavailable():
    state = load('combat')
    hand = state['player']['hand']
    hand[0].update(can_play=False, unplayable_reason='NotEnoughEnergy')
    for card in hand:
        if card['name'] == 'Strike':
            card['can_play'] = False  # identical copies, no reason given
    blocked = [b for b in refs.blocked_actions(state) if b['action'] == 'play_card']
    assert {b['ref']: b['reason'] for b in blocked} == {'Bash': 'NotEnoughEnergy', 'Strike': 'can_play=false'}
    assert len(blocked) == 2  # two identical Strikes are one entry
    assert not any(a['action'] == 'play_card' and a.get('card_index') in (0, 2, 3) for a in refs.legal_actions(state))
    # Nothing is listed outside combat or when every card is playable.
    assert not [b for b in refs.blocked_actions(load('combat')) if b['action'] == 'play_card']
    assert not [b for b in refs.blocked_actions(load('map')) if b['action'] == 'play_card']


def test_chain_error_says_when_an_earlier_step_changed_the_screen(harness, capsys):
    out, act = harness
    game = FakeGame(load('kill_before'), lambda s, a, n: load('rewards'))
    payload_in = json.dumps([{'action': 'play_card', 'card': 'Twin Strike+', 'target': 'INKLET_1'},
                             {'action': 'play_card', 'card': 'Strike', 'target': 'INKLET_2'}])
    code, payload = run(act, capsys, game, payload_in)
    assert code == 2 and payload['error'] == 'unresolvable_action' and payload['failed_step'] == 2
    before = load('kill_before')['state_type']
    assert payload['screen_changed'] == {'from': before, 'to': 'rewards', 'after_step': 1}
    assert f"screen changed from {before!r} to 'rewards' after step 1" in payload['message']
    assert len(game.posts) == 1  # the second step was never sent
    # Same-screen failures keep the plain message.
    game = FakeGame(load('kill_before'), lambda s, a, n: load('kill_after'))
    code, payload = run(act, capsys, game, json.dumps([{'action': 'play_card', 'card': 'Twin Strike+',
                                                         'target': 'INKLET_1'},
                                                        {'action': 'play_card', 'card': 'Strike',
                                                         'target': 'INKLET_1'}]))
    assert code == 2 and 'screen_changed' not in payload and 'screen changed' not in payload['message']
