"""Regression: same-name copies that differ only in cost (S03r2 Frantic Escape).

The S03r2 (A10, 2026-10-24) final turn had Frantic Escape [3] at hand index 0
and Frantic Escape [2] at index 2 with 2 energy.  The listing offered
``play_card card=Frantic Escape#2`` (the cost-2 copy) as action 1, but both
``Frantic Escape#2`` and ``1@token`` were refused with "cannot be played now"
because the live act path re-resolved the ref by a cost-blind content anchor
and landed on the first (cost-3) copy.  ``Name#k`` and ``N@token`` must hit
the exact instance shown; a bare name must pick an affordable copy.

Fixture: fixtures/live_frictions/combat_frantic_escape_s03r2.json (trimmed
observation from that turn: piles emptied, rules_context removed).
"""
import copy
import json
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'scripts'))
sys.path.insert(0, str(ROOT / 'scripts' / 'bench'))
sys.path.insert(0, str(Path(__file__).parent))
import agent_step  # noqa: E402
import sts2_action_executor as executor  # noqa: E402
import sts2_action_refs as refs  # noqa: E402
from test_live_frictions import FakeGame, harness, load, run  # noqa: E402,F401


def s03r2():
    return load('combat_frantic_escape_s03r2')


def listing(state):
    return {'hash': executor.decision_digest(state), 'legal_actions': refs.observe_actions(state)}


def escape_action(obs, cost_index):
    return next(n for n, a in enumerate(obs['legal_actions'])
                if a['action'] == 'play_card' and a.get('card_index') == cost_index)


def remove_played(state, action, n):
    state = copy.deepcopy(state)
    hand = state['player']['hand']
    played = next(c for c in hand if c['index'] == action['card_index'])
    state['player']['energy'] -= int(played['cost'])
    state['player']['hand'] = [c for c in hand if c['index'] != action['card_index']]
    for i, card in enumerate(state['player']['hand']):
        card['index'] = i
        card['can_play'] = int(card['cost']) <= state['player']['energy']
    return state


def test_fixture_shape():
    hand = s03r2()['player']['hand']
    escapes = [(c['index'], c['cost'], c['can_play']) for c in hand if c['name'] == 'Frantic Escape']
    assert escapes == [(0, '3', False), (2, '2', True)]
    assert s03r2()['player']['energy'] == 2


def test_listing_ref_names_the_affordable_instance():
    state = s03r2()
    obs = listing(state)
    n = escape_action(obs, 2)
    assert obs['legal_actions'][n]['ref'] == {'action': 'play_card', 'card': 'Frantic Escape#2'}
    assert all(a.get('card_index') != 0 for a in obs['legal_actions'])  # cost-3 copy is not offered


def test_hash_k_resolves_exact_instance_after_anchoring():
    state = s03r2()
    request = refs.anchor_request(state, {'action': 'play_card', 'card': 'Frantic Escape#2'})
    assert refs.resolve(state, request)['wire']['card_index'] == 2
    request = refs.anchor_request(state, {'action': 'play_card', 'card': 'Frantic Escape#1'})
    try:
        refs.resolve(state, request)
        raise AssertionError('Frantic Escape#1 costs 3 with 2 energy and must be refused')
    except refs.ActionRefError as error:
        assert 'cannot be played' in str(error)


def test_bare_name_picks_affordable_copy():
    state = s03r2()
    request = refs.anchor_request(state, {'action': 'play_card', 'card': 'Frantic Escape'})
    assert refs.resolve(state, request)['wire']['card_index'] == 2
    assert refs.resolve(state, {'action': 'play_card', 'card': 'Frantic Escape'})['wire']['card_index'] == 2


def test_live_act_hash_k_plays_the_observed_copy(harness, capsys):
    out, act = harness
    game = FakeGame(s03r2(), remove_played)
    code, payload = run(act, capsys, game, '{"action":"play_card","card":"Frantic Escape#2"}')
    assert code == 0, payload
    assert [p['card_index'] for p in game.posts] == [2]


def test_live_act_index_token_plays_the_observed_copy(harness, capsys):
    out, act = harness
    state = s03r2()
    obs = listing(state)
    n = escape_action(obs, 2)
    bound = agent_step.bind(obs, f'{n}@{agent_step.listing_token(obs)}')
    game = FakeGame(state, remove_played)
    code, payload = run(act, capsys, game, json.dumps(bound))
    assert code == 0, payload
    assert [p['card_index'] for p in game.posts] == [2]


def test_chained_plays_keep_the_instance_after_renumbering(harness, capsys):
    # Strike (index 5) first shifts nothing before index 2, but Twin Strike (index 1)
    # renumbers Frantic Escape [2] to index 1; the anchored instance must still be played.
    out, act = harness
    state = s03r2()
    state['player']['energy'] = 3
    for card in state['player']['hand']:
        card['can_play'] = int(card['cost']) <= 3
    game = FakeGame(state, remove_played)
    code, payload = run(act, capsys, game, '[{"action":"play_card","card":"Twin Strike","target":"%s"},'
                                           '{"action":"play_card","card":"Frantic Escape#2"}]'
                        % refs.enemy_ref(next(e for e in state['battle']['enemies'] if e.get('hp', 0) > 0)))
    assert code == 0, payload
    assert [p['card_index'] for p in game.posts] == [1, 1]  # Twin Strike, then the cost-2 copy (now index 1)
    assert game.state['player']['hand'][0]['cost'] == '3'  # the cost-3 copy stayed in hand
