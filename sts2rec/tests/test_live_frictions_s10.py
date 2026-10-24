"""Regression tests for the frictions seen in the S10 A10 benchmark run.

* boss_entry_hp must be the HP observed when each boss combat starts (S10 recorded
  27 for an Act 2 boss entered at 63: the history reconstruction misses Sandpit).
* same-name copies that differ only in cost resolve a bare name to the
  playable/cheapest copy with a warning instead of refusing as ambiguous.
* a single-card ``select_card`` on a multi-pick screen is wrapped as an
  incremental ``{"action":"select","cards":[...],"confirm":false}``.
"""
import copy
import json
import sys
from pathlib import Path

import pytest

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'scripts'))
sys.path.insert(0, str(ROOT / 'scripts' / 'bench'))
sys.path.insert(0, str(Path(__file__).parent))
import metrics  # noqa: E402
import score  # noqa: E402
import sts2_action_refs as refs  # noqa: E402
import sts2_live as live  # noqa: E402
from test_live_frictions import FakeGame, harness, load, run  # noqa: E402,F401

TARGET = 'Scroll of Biting@2'


# ------------------------------------------------------------ boss entry HP
def decision(index, st, hp, act=1, floor=17, rnd=None, post=None):
    return {'type': 'decision', 'index': index, 'state_type': st, 'hp': hp, 'act': act, 'floor': floor,
            'round': rnd, 'outcome': {'ok': True, 'post_state_type': post or st}}


def s10_like_records():
    """Act 1 boss with potion/hand-select sub-screens, won; Act 2 boss lost to Sandpit."""
    return [
        decision(1, 'map', 60, floor=16, post='boss'),
        decision(2, 'boss', 56, rnd=1, post='card_select'),
        decision(3, 'card_select', 56, post='boss'),
        decision(4, 'boss', 56, rnd=1),
        decision(5, 'boss', 17, rnd=3, post='hand_select'),
        decision(6, 'hand_select', 17, rnd=3, post='boss'),
        decision(7, 'boss', 22, rnd=6, post='rewards'),
        decision(8, 'rewards', 28, post='map'),
        decision(9, 'map', 70, act=2, floor=32, post='boss'),
        decision(10, 'boss', 63, act=2, floor=33, rnd=1),
        decision(11, 'boss', 36, act=2, floor=33, rnd=7, post='game_over'),
        {'type': 'run_end', 'end_reason': 'game_over'},
    ]


def test_boss_combats_one_entry_per_combat_at_start_hp():
    combats = metrics.boss_combats(s10_like_records())
    assert [(c['act'], c['floor'], c['hp'], c['killed']) for c in combats] == [(1, 17, 56, True), (2, 33, 63, False)]
    traj = metrics.trajectory_metrics(s10_like_records())
    assert traj['boss_entry_hp'] == [56, 63] and traj['boss_kills'] == 1


def test_back_to_back_act3_bosses_are_separate_combats():
    recs = [decision(1, 'boss', 99, act=3, floor=48, rnd=1), decision(2, 'boss', 27, act=3, floor=48, rnd=13,
                                                                          post='rewards'),
            decision(3, 'rewards', 33, act=3, floor=48, post='map'),
            decision(4, 'boss', 33, act=3, floor=49, rnd=1), decision(5, 'boss', 1, act=3, floor=49, post='game_over')]
    combats = metrics.boss_combats(recs)
    assert [c['hp'] for c in combats] == [99, 33] and [c['killed'] for c in combats] == [True, False]


def test_run_metrics_prefers_observed_boss_entry_over_history_reconstruction():
    # Sandpit kills without "damage": history reconstructs 0 + 27 - 0 = 27.
    history = {'win': False, 'killed_by_encounter': 'ENCOUNTER.THE_INSATIABLE_BOSS', 'map_point_history': [
        [{'map_point_type': 'boss', 'player_stats': [{'current_hp': 28, 'damage_taken': 39, 'hp_healed': 11}]}],
        [{'map_point_type': 'boss', 'player_stats': [{'current_hp': 0, 'damage_taken': 27, 'hp_healed': 0}]}]]}
    out = metrics.run_metrics(s10_like_records(), history, end_reason='game_over')
    assert out['boss_entry_hp'] == [56, 63] and out['hp_at_boss_entry'] == 56
    assert out['boss_entry_hp_history'] == [56, 27] and out['boss_entry_hp_source'] == 'trajectory_combat_start'
    assert out['boss_kills'] == 1  # kills still come from the game history
    no_traj = metrics.run_metrics([], history, end_reason='game_over')
    assert no_traj['boss_entry_hp'] == [56, 27] and no_traj['boss_entry_hp_source'] == 'game_history'


def test_score_refreshes_old_result_from_trajectory(tmp_path, capsys):
    seed = tmp_path / 'BENCHA0S10'
    seed.mkdir()
    (seed / 'trajectory.jsonl').write_text(''.join(json.dumps(r) + '\n' for r in s10_like_records()))
    old = {'seed': 'BENCHA0S10', 'policy': 'p', 'source': 'game_history', 'floor': 33, 'act': 2, 'boss_kills': 1,
           'boss_entry_hp': [56, 27], 'hp_at_boss_entry': 56}
    (seed / 'result.json').write_text(json.dumps(old))
    rows = score.load_results(tmp_path)
    assert rows[0]['boss_entry_hp'] == [56, 63] and rows[0]['boss_entry_hp_history'] == [56, 27]
    assert json.loads((seed / 'result.json').read_text())['boss_entry_hp'] == [56, 27]  # read-only by default
    assert score.load_results(tmp_path, refresh=False)[0]['boss_entry_hp'] == [56, 27]
    assert score.main([str(tmp_path), '--rewrite']) == 0
    assert '56/63' in capsys.readouterr().out
    assert json.loads((seed / 'result.json').read_text())['boss_entry_hp'] == [56, 63]


# ------------------------------------------------- cost-only duplicate copies
def frantic_hand():
    state = load('combat_duplicates')
    state['player']['energy'] = 3
    hand = state['player']['hand']
    escape = {'id': 'FRANTIC_ESCAPE', 'model_id': 'CARD.FRANTIC_ESCAPE', 'name': 'Frantic Escape', 'type': 'Status',
              'description': 'Get farther away. Increase Sandpit by 1. Increase the cost of this card by 1.',
              'is_upgraded': False, 'target_type': 'Self', 'can_play': True, 'unplayable_reason': None,
              'enchantment': None, 'affliction': None, 'star_cost': None}
    for i, cost in ((3, '2'), (6, '1')):
        hand[i] = dict(escape, index=i, cost=cost)
    return state


def test_bare_name_with_cost_only_differences_picks_cheapest_with_warning():
    state = frantic_hand()
    picked = refs.resolve(state, {'action': 'play_card', 'card': 'Frantic Escape'})
    assert picked['wire']['card_index'] == 6  # cost 1, not the cost-2 copy at index 3
    assert any(n.startswith(refs.WARNING_PREFIX) and 'only in cost' in n for n in picked['notes'])
    # Explicit #k still picks exactly that copy.
    assert refs.resolve(state, {'action': 'play_card', 'card': 'Frantic Escape#1'})['wire']['card_index'] == 3


def test_cost_only_prefers_playable_copy():
    state = frantic_hand()
    state['player']['hand'][6].update(cost='0', can_play=False, unplayable_reason='blocked')
    picked = refs.resolve(state, {'action': 'play_card', 'card': 'Frantic Escape'})
    assert picked['wire']['card_index'] == 3


def test_copies_differing_beyond_cost_stay_ambiguous():
    state = frantic_hand()
    state['player']['hand'][6]['enchantment'] = {'id': 'SHARP', 'name': 'Sharp'}
    with pytest.raises(refs.ActionRefError, match='ambiguous'):
        refs.resolve(state, {'action': 'play_card', 'card': 'Frantic Escape'})


def test_cost_only_resolution_surfaces_warning_in_act(harness, capsys):
    out, act = harness

    def transition(state, action, n):
        state = copy.deepcopy(state)
        state['player']['hand'] = [c for c in state['player']['hand'] if c['index'] != action['card_index']]
        for i, card in enumerate(state['player']['hand']):
            card['index'] = i
        return state

    game = FakeGame(frantic_hand(), transition)
    code, payload = run(act, capsys, game, '[{"action":"play_card","card":"Frantic Escape"},'
                                           '{"action":"play_card","card":"Frantic Escape"}]')
    assert code == 0, payload
    assert [p['card_index'] for p in game.posts] == [6, 3]  # cheapest first, then the remaining copy
    assert any('only in cost' in w for w in payload['warnings'])


# ------------------------------------------- single select on a multi-pick
def select_transition(state, action, n):
    state = copy.deepcopy(state)
    block = state['card_select']
    if action['action'] == 'select_card':
        block['selected_indices'] = block.get('selected_indices', []) + [action['index']]
        if len(block['selected_indices']) >= block['min_select']:
            block['can_confirm'] = True
        return state
    assert action['action'] == 'confirm_selection'
    return load('map')


def test_expand_wraps_single_select_card_on_multi_pick():
    state = load('card_select_upgrade')  # min_select 4
    steps = live.expand(state, {'action': 'select_card', 'card': 'Defend'})
    assert len(steps) == 1 and steps[0]['card'] == 'Defend' and steps[0]['_confirm'] is False
    assert 'confirm":false' in steps[0]['_warning']
    # The explicit multi-select form is unchanged: too few cards is still refused.
    with pytest.raises(refs.ActionRefError, match='needs at least'):
        live.expand(state, {'action': 'select', 'cards': ['Defend']})
    with pytest.raises(refs.ActionRefError, match='needs at least'):
        live.expand(state, {'action': 'select_card', 'card': 'Defend', 'confirm': True})


def test_single_select_card_lands_and_keeps_screen_open(harness, capsys):
    out, act = harness
    game = FakeGame(load('card_select_upgrade'), select_transition)
    code, payload = run(act, capsys, game, '{"action":"select_card","card":"Defend"}')
    assert code == 0, payload
    assert [p['action'] for p in game.posts] == ['select_card']  # no confirm sent
    assert any('treated as' in w for w in payload['warnings'])
    # Finishing the pick with the multi-select form confirms as usual.
    code, payload = run(act, capsys, game, '{"action":"select","cards":["Strike","Strike","Strike"]}')
    assert code == 0, payload
    assert game.posts[-1] == {'action': 'confirm_selection'}
