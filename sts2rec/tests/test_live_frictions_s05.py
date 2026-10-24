"""Regression tests for the harness frictions seen in the S01/S02/S04 A10 benchmark runs.

Reuses the trimmed fixtures and the FakeGame/harness helpers of test_live_frictions.
"""
import copy
import json
import sys
from pathlib import Path

import pytest

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'scripts'))
sys.path.insert(0, str(ROOT / 'scripts' / 'bench'))
sys.path.insert(0, str(ROOT / 'scripts' / 'facts'))
sys.path.insert(0, str(Path(__file__).parent))
import metrics  # noqa: E402
import sts2_action_executor as executor  # noqa: E402
import sts2_action_refs as refs  # noqa: E402
import sts2_live as live  # noqa: E402
from lookup import FactDB  # noqa: E402
from test_live_frictions import FakeGame, harness, load, run  # noqa: E402,F401

TARGET = 'Scroll of Biting@2'


def play_and_reindex(state, action, n):
    """Remove the played card and renumber the hand, like the game does."""
    state = copy.deepcopy(state)
    hand = [c for c in state['player']['hand'] if c['index'] != action['card_index']]
    for i, card in enumerate(hand):
        card['index'] = i
    state['player']['hand'] = hand
    state['player']['energy'] = state['player'].get('energy', 3) - 1
    return state


def two_different_strikes():
    state = load('combat_duplicates')
    state['player']['energy'] = 5
    state['player']['hand'][6]['enchantment'] = {'id': 'SHARP', 'name': 'Sharp'}  # plain Strikes at 3 and 6 now differ
    return state


# ------------------------------------------------------------ Name#k refs
def test_hash_zero_reads_as_first_copy():
    state = two_different_strikes()
    first = refs.resolve(state, {'action': 'play_card', 'card': 'Strike#1', 'target': TARGET})
    zero = refs.resolve(state, {'action': 'play_card', 'card': 'Strike#0', 'target': TARGET})
    assert zero['wire'] == first['wire']
    assert any('1-based' in n for n in zero['notes'])


def test_out_of_range_k_resolves_only_for_identical_copies():
    state = load('combat_duplicates')  # two identical plain Strikes
    picked = refs.resolve(state, {'action': 'play_card', 'card': 'Strike#3', 'target': TARGET})
    assert state['player']['hand'][picked['wire']['card_index']]['name'] == 'Strike'
    assert any('identical' in n for n in picked['notes'])
    with pytest.raises(refs.ActionRefError, match='only 2 match'):
        refs.resolve(two_different_strikes(), {'action': 'play_card', 'card': 'Strike#3', 'target': TARGET})


def test_chained_name_k_follows_the_observed_copy(harness, capsys):
    out, act = harness
    game = FakeGame(two_different_strikes(), play_and_reindex)
    chain = json.dumps([{'action': 'play_card', 'card': 'Strike#1', 'target': TARGET},
                        {'action': 'play_card', 'card': 'Strike#2', 'target': TARGET}])
    code, payload = run(act, capsys, game, chain)
    assert code == 0, payload
    # Strike#1 was index 3; Strike#2 (the enchanted copy, index 6) is index 5 after the renumbering.
    assert [p['card_index'] for p in game.posts] == [3, 5]
    assert any('re-resolved' in n for n in payload['notes'])


# ---------------------------------------------------------- reward refs
def two_card_rewards():
    state = load('rewards')
    items = state['rewards']['items']
    items.append({'description': 'Add a card to your deck.', 'index': 3, 'type': 'card'})
    return state


def claim_and_reindex(state, action, n):
    state = copy.deepcopy(state)
    items = [i for i in state['rewards']['items'] if i['index'] != action['index']]
    for i, item in enumerate(items):
        item['index'] = i
    state['rewards']['items'] = items
    return state


def test_reward_chain_survives_renumbering(harness, capsys):
    out, act = harness
    state = load('rewards')
    game = FakeGame(state, claim_and_reindex)
    chain = json.dumps([{'action': 'claim_reward', 'reward': refs.reward_ref(i)} for i in state['rewards']['items']])
    code, payload = run(act, capsys, game, chain)
    assert code == 0, payload
    assert [p['index'] for p in game.posts] == [0, 0, 0]  # each claim shifts the rest down


def test_identical_reward_k_resolves_after_shift():
    state = claim_and_reindex(two_card_rewards(), {'index': 2}, 1)  # one of two card rewards claimed
    picked = refs.resolve(state, {'action': 'claim_reward', 'reward': 'card#2'})
    assert state['rewards']['items'][[i['index'] for i in state['rewards']['items']].index(
        picked['wire']['index'])]['type'] == 'card'
    assert any('shifted' in n for n in picked['notes'])


# ------------------------------------------------------------- treasure
def test_treasure_shorthand_and_hash_ignore_chest_animation():
    unopened, opened = load('treasure_unopened'), load('treasure_opened_unclaimed')
    assert executor.decision_digest(unopened) == executor.decision_digest(opened)
    assert executor.decision_digest(opened) != executor.decision_digest(load('treasure_claimed'))
    for text in ('claim_treasure_relic', 'treasure', '{"action":"claim_treasure_relic"}'):
        request = refs.parse_request(text)[0]
        assert refs.resolve(opened, request)['wire'] == {'action': 'claim_treasure_relic', 'index': 0}
    two = copy.deepcopy(opened)
    two['treasure']['relics'].append({**two['treasure']['relics'][0], 'id': 'OTHER', 'index': 1})
    with pytest.raises(refs.ActionRefError, match='exactly one'):
        refs.resolve(two, {'action': 'claim_treasure_relic'})


def test_treasure_shorthand_end_to_end(harness, capsys):
    out, act = harness
    game = FakeGame(load('treasure_opened_unclaimed'), lambda s, a, n: load('treasure_claimed'))
    # Hash taken on the pre-animation frame still matches.
    code, payload = run(act, capsys, game, 'claim_treasure_relic',
                        expected_hash=executor.decision_digest(load('treasure_unopened')))
    assert code == 0, payload
    assert game.posts == [{'action': 'claim_treasure_relic', 'index': 0}]


# ------------------------------------------------------------- end_turn
def next_round(state):
    state = copy.deepcopy(state)
    state['battle']['round'] += 1
    return state


def test_ignored_end_turn_is_resent_once_and_logged(harness, capsys, monkeypatch):
    out, act = harness
    monkeypatch.setattr(live, 'END_TURN_FIRST_WAIT_S', 0.3)
    game = FakeGame(load('kill_after'), lambda s, a, n: s if n == 1 else next_round(s))
    code, payload = run(act, capsys, game, '{"action":"end_turn"}')
    assert code == 0, payload
    assert game.posts == [{'action': 'end_turn'}] * 2
    assert any(s.get('harness_followup') == 'end_turn_retry' for s in payload['steps'])
    rows = [json.loads(l) for l in (out / 'decisions.jsonl').read_text().splitlines()]
    assert any(r['type'] == 'harness_retry' and r['kind'] == 'end_turn_retry' for r in rows)
    assert 're-sending once' in (out / 'live.log').read_text()


def test_end_turn_is_not_resent_once_the_turn_is_resolving(harness, capsys, monkeypatch):
    out, act = harness
    monkeypatch.setattr(live, 'END_TURN_FIRST_WAIT_S', 0.2)
    calls = {'n': 0}

    def slow_enemy_turn(state, action, n):
        enemy = copy.deepcopy(state)
        enemy['battle']['is_play_phase'] = False
        enemy['battle']['turn'] = 'enemy'
        return enemy

    game = FakeGame(load('kill_after'), slow_enemy_turn)
    real_request = game.__call__

    def request(port, action=None):
        if action is None:
            calls['n'] += 1
            if calls['n'] > 40 and not game.state['battle']['is_play_phase']:
                game.state = next_round(load('kill_after'))
        return real_request(port, action)

    request.state = game.state
    monkeypatch.setattr(executor, 'request', request)
    h = executor.decision_digest(load('kill_after'))
    monkeypatch.setattr(sys, 'argv', ['sts2_live', 'act', '{"action":"end_turn"}', 'end turn', h,
                                      '--run-dir', str(out), '--settle-timeout', '2'])
    try:
        live.main()
    except SystemExit:
        pass
    assert game.posts == [{'action': 'end_turn'}]  # visible change -> never resent


# ------------------------------------------------------------- reasons
def test_short_reason_is_padded_and_logged(harness, capsys, monkeypatch):
    out, _ = harness
    game = FakeGame(load('kill_after'), lambda s, a, n: next_round(s))
    monkeypatch.setattr(executor, 'request', game)

    def act_with(reason):
        h = executor.decision_digest(game.state)
        monkeypatch.setattr(sys, 'argv', ['sts2_live', 'act', '{"action":"end_turn"}', reason, h,
                                          '--run-dir', str(out), '--settle-timeout', '2'])
        return run(lambda: live.main(), capsys)

    code, payload = act_with('end')
    assert code == 0, payload
    assert any('auto-padded' in w for w in payload['warnings'])
    assert 'auto-padded' in (out / 'live.log').read_text()
    code, payload = act_with('   ')
    assert code == 2 and payload['error'] == 'reason_required'


def test_normalize_reason():
    assert live.normalize_reason('end') == ('end' + live.SHORT_REASON_PAD,
                                           "reason 'end' shorter than 4 chars; auto-padded")
    assert live.normalize_reason('end turn, block is enough') == ('end turn, block is enough', None)
    assert live.normalize_reason('') == (None, None)


# ---------------------------------------------------------------- facts
@pytest.mark.parametrize('alias,name', [
    ('STEAM_ERUPTION', 'Steam Eruption'), ('SteamEruption', 'Steam Eruption'),
    ('STEAM_ERUPTION_POWER', 'Steam Eruption'), ('POWER.STEAM_ERUPTION_POWER', 'Steam Eruption'),
    ('Adaptable', 'Adaptable'), ('ADAPTABLE', 'Adaptable'), ('Enrage', 'Enrage'),
    ('PainfulStabs', 'Painful Stabs'), ('Painful Stabs', 'Painful Stabs'), ('PAINFUL_STABS', 'Painful Stabs'),
])
def test_power_aliases(alias, name):
    lines = FactDB.load('0.107.1').lookup_id(alias)
    assert lines and lines[0].startswith(f'[power] {name} ')


def test_boss_power_lines_are_in_enemy_facts():
    db = FactDB.load('0.107.1')
    giant = '\n'.join(db.enemy_lines('WATERFALL_GIANT'))
    assert '[power] Steam Eruption' in giant and 'When killed' in giant
    subject = '\n'.join(db.enemy_lines('TEST_SUBJECT'))
    for name in ('Adaptable', 'Enrage', 'Painful Stabs'):
        assert f'[power] {name} ' in subject
    state = {'state_type': 'boss', 'battle': {'enemies': [{'id': 'WATERFALL_GIANT', 'is_alive': True}]}}
    assert '[power] Steam Eruption' in db.facts_for_state(state, max_chars=None)


# --------------------------------------------------------- find_history
def test_find_history_shape(tmp_path):
    (tmp_path / '1.run').write_text(json.dumps({'seed': 'BENCHA0S05', 'start_time': 100}))
    (tmp_path / '2.run').write_text(json.dumps({'seed': 'BENCHA0S05', 'start_time': 200}))
    (tmp_path / 'bad.run').write_text('[1, 2]')
    match = metrics.find_history(tmp_path, 'benchaos05')  # letter O == zero, like the game
    assert isinstance(match, metrics.HistoryMatch) and isinstance(match, tuple)
    path, data = match
    assert path == match.path and path.name == '2.run' and data is match.data and data['start_time'] == 200
    assert metrics.find_history(tmp_path, 'OTHER') is None
    assert metrics.find_history(tmp_path / 'missing', 'BENCHA0S05') is None
