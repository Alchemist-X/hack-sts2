"""Tests for scripts/bench/sim_engine.py (the offline spire-mentor 'sim' engine).

A scripted fake ``sts2_rs`` covers the adapter everywhere; the tests marked
``real`` run against the actual simulator only when ``sts2_rs`` is importable
(it is an external Rust build, see docs/sim-engine.md, so CI skips them).
"""
import json
import sys
import types
from pathlib import Path

import pytest

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'scripts'))
sys.path.insert(0, str(ROOT / 'scripts' / 'bench'))
import compare_models  # noqa: E402
import policies  # noqa: E402
import score  # noqa: E402
import sim_engine as se  # noqa: E402
import text_state  # noqa: E402


class FakeCombat:
    def __init__(self, rm):
        self.rm = rm

    def pending_choice(self):
        return None


class FakeRunManager:
    """Neow -> relic reward -> map -> one fight (Nibbit, 20 HP) -> run won (or lost when hp runs out)."""

    def __init__(self, string_seed, character, ascension_level, **kw):
        self.seed, self.character, self.ascension_level = string_seed, character, ascension_level
        self.kw = kw
        self.stage, self.enemy_hp, self.current_hp, self.max_hp, self.gold = 'neow', 20, 80, 80, 99
        self.player_won, self.act_index, self._relics, self.coord = False, 1, ['RELIC.BURNING_BLOOD'], None
        self.taken = []

    # accessors
    def phase(self):
        return {'neow': 'event', 'relic': 'card_reward', 'map': 'map_choice', 'fight': 'combat'}.get(self.stage, 'over')

    def is_over(self):
        return self.stage == 'over'

    def current_room(self):
        return {'map_coord': self.coord, 'room_type': 'monster' if self.stage == 'fight' else 'event'}

    def current_event_name(self):
        return 'Neow' if self.stage == 'neow' else None

    def event_options(self):
        return [('positive_1', 'NUTRITIOUS_OYSTER'), ('cursed', 'CURSED_PEARL')] if self.stage == 'neow' else None

    def event_option_infos(self):
        return [{'description': 'Gain a positive relic.', 'enabled': True, 'label': 'NUTRITIOUS_OYSTER'},
                {'description': 'Gain a cursed relic.', 'enabled': True, 'label': 'CURSED_PEARL'}]

    def relic_reward_ids(self):
        return ['NUTRITIOUS_OYSTER']

    def act_map(self):
        return [{'col': 3, 'row': 0, 'point_type': 'Ancient', 'children': [{'col': 0, 'row': 1}, {'col': 2, 'row': 1}]},
                {'col': 0, 'row': 1, 'point_type': 'Monster', 'children': []},
                {'col': 2, 'row': 1, 'point_type': 'Monster', 'children': []}]

    def full_map(self):
        return {'boss': {'col': 3, 'row': 16, 'kind': 'WaterfallGiant'}}

    def map_coord(self):
        return self.coord

    def total_floor(self):
        return 0 if self.coord is None else self.coord[1]

    def relics(self):
        return list(self._relics)

    def potions(self):
        return []

    def max_potion_slots(self):
        return 3

    def master_deck_detailed(self):
        return [('StrikeIronclad', False)] * 5 + [('DefendIronclad', False)] * 4 + [('Bash', False)]

    def combat_state(self):
        return FakeCombat(self) if self.stage == 'fight' else None

    def available_actions(self):
        if self.stage == 'neow':
            return [{'action': 'event_choice', 'idx': 0}, {'action': 'event_choice', 'idx': 1}]
        if self.stage == 'relic':
            return [{'action': 'pick_relic_reward'}, {'action': 'skip_relic'}]
        if self.stage == 'map':
            return [{'action': 'move', 'col': 0}, {'action': 'move', 'col': 2}]
        if self.stage == 'fight':
            return [{'action': 'play_card', 'hand_idx': 0, 'target_index': 0},
                    {'action': 'play_card', 'hand_idx': 1, 'target_index': None}, {'action': 'end_turn'}]
        return []

    def take_action(self, action):
        assert action in self.available_actions(), action
        self.taken.append(action)
        kind = action['action']
        if kind == 'event_choice':
            self.stage = 'relic'
        elif kind in ('pick_relic_reward', 'skip_relic'):
            if kind == 'pick_relic_reward':
                self._relics.append('RELIC.NUTRITIOUS_OYSTER')
            self.stage = 'map'
        elif kind == 'move':
            self.coord, self.stage = (action['col'], 1), 'fight'
        elif kind == 'play_card' and action['hand_idx'] == 0:
            self.enemy_hp -= 6
        elif kind == 'end_turn':
            self.current_hp -= 30
        if self.enemy_hp <= 0:
            self.stage, self.player_won = 'over', True
        elif self.current_hp <= 0:
            self.current_hp, self.stage = 0, 'over'


def fake_state_to_dict(cs):
    rm = cs.rm
    card = lambda cid: {'id': cid, 'base_cost': 1, 'cost_override': None, 'upgraded': False}  # noqa: E731
    return {'round': 1, 'side': 'Player',
            'player': {'hp': rm.current_hp, 'max_hp': rm.max_hp, 'block': 0, 'energy': 3, 'powers': []},
            'deck': {'hand': [card('STRIKE_IRONCLAD'), card('DEFEND_IRONCLAD')],
                     'draw_pile': [card('BASH')], 'discard_pile': [], 'exhaust_pile': []},
            'encounter': [{'kind': 'Nibbit', 'hp': rm.enemy_hp, 'max_hp': 20, 'block': 0, 'is_dead': False,
                           'move_state': 'BUTT', 'powers': [{'name': 'STRENGTH', 'amount': 1}],
                           'intents': [{'kind': 'Attack', 'amount': 12, 'effective_amount': 12, 'times': 1}]}]}


FAKE = types.SimpleNamespace(RunManager=FakeRunManager, state_to_dict=fake_state_to_dict)


def game(seed='BENCHA0S01'):
    return se.SimGame(seed, sim=FAKE)


def test_ids_and_names():
    assert se.screaming('FlameBarrierCard') == 'FLAME_BARRIER_CARD'
    assert se.screaming('RELIC.BURNING_BLOOD') == 'BURNING_BLOOD'
    assert se.screaming('StrikeIronclad') == 'STRIKE_IRONCLAD'
    names = se.Names()
    assert names.card('StrikeIronclad')['name'] == 'Strike'
    assert names.card('FlameBarrierCard')['name'] == 'Flame Barrier'  # trailing Card suffix dropped
    assert names.card('Bash', True)['name'] == 'Bash+'
    assert names.relic('RELIC.BURNING_BLOOD')['name'] == 'Burning Blood'
    assert names.card('NotARealCard')['name'] == 'Not A Real Card'


def test_observations_encode_and_refs_round_trip():
    g = game()
    obs = g.observe()
    assert obs['state']['state_type'] == 'event'
    assert [o['title'] for o in obs['state']['event']['options']] == ['Nutritious Oyster', 'Cursed Pearl']
    text = text_state.encode(obs)
    assert 'Event: Neow' in text and 'Nutritious Oyster' in text
    for ref, act in g._refs:
        assert g.resolve(ref) == act
    g.act(obs['legal_actions'][0]['ref'])
    obs = g.observe()
    assert obs['state']['state_type'] == 'rewards'
    g.act('claim_reward')  # unique shorthand resolves
    obs = g.observe()
    assert obs['state']['state_type'] == 'map'
    assert [(o['col'], o['row']) for o in obs['state']['map']['next_options']] == [(0, 1), (2, 1)]
    assert 'Map:' in text_state.encode(obs)
    g.act({'action': 'choose_map_node', 'col': 0})  # unique partial ref resolves
    obs = g.observe()
    st = obs['state']
    assert st['state_type'] == 'monster'
    assert st['battle']['enemies'][0]['name'] == 'Nibbit'
    assert st['battle']['enemies'][0]['intents'][0]['total_damage'] == 12
    refs = [a['ref'] for a in obs['legal_actions']]
    assert refs[0] == {'action': 'play_card', 'card': 'Strike', 'hand_index': 0, 'target': 'Nibbit@0'}
    text = text_state.encode(obs)
    assert 'Nibbit@0' in text and 'Strike' in text
    with pytest.raises(se.SimError):
        g.resolve({'action': 'play_card'})  # ambiguous
    with pytest.raises(se.SimError):
        g.resolve({'action': 'proceed'})


def test_run_seed_with_stub_writes_score_format(tmp_path):
    name, policy = policies.load_policy('stub')
    result = se.run_seed('BENCHA0S01', tmp_path, policy_name=name, policy=policy, sim=FAKE)
    assert result['end_reason'] == 'game_over' and result['win'] is True and result['error'] is None
    assert result['engine'] == 'sim' and result['source'] == 'sim' and result['friction_total'] == 0
    records = [json.loads(line) for line in (tmp_path / 'BENCHA0S01' / 'trajectory.jsonl').read_text().splitlines()]
    decisions = [r for r in records if r['type'] == 'decision']
    assert decisions[0]['state_type'] == 'event' and decisions[0]['outcome']['ok']
    assert decisions[-1]['sim_action']['action'] == 'play_card'
    rows = score.load_results(tmp_path)
    assert rows and rows[0]['seed'] == 'BENCHA0S01'


def test_bad_policy_actions_become_frictions(tmp_path):
    def bad(observation, context):
        return {'action': {'action': 'proceed'}, 'reason': 'always wrong'}
    result = se.run_seed('BENCHA0S02', tmp_path, policy_name='bad', policy=bad, sim=FAKE,
                         limits={'max_consecutive_errors': 3})
    assert result['end_reason'] == 'harness_stuck' and result['frictions'] == {'action_rejected': 3}
    assert result['win'] is None and result['death_cause'] == 'not_finished:harness_stuck'


def test_existing_seed_dir_refused(tmp_path):
    (tmp_path / 'BENCHA0S01').mkdir()
    with pytest.raises(se.SimError):
        se.run_seed('BENCHA0S01', tmp_path, policy_name='stub', policy=policies.stub_policy, sim=FAKE)


def test_missing_simulator_explains_build(monkeypatch):
    monkeypatch.setitem(sys.modules, 'sts2_rs', None)
    with pytest.raises(se.SimError, match='docs/sim-engine.md'):
        se.import_sim()


def test_compare_models_sim_engine_skips_pool(tmp_path, capsys):
    rc = compare_models.main(['--engine', 'sim', '--sim-python', '/opt/simpy', '--models', 'm1,m2',
                              '--seeds', 'BENCHA0S01', '--out', str(tmp_path), '--dry-run'])
    assert rc == 0
    out = capsys.readouterr().out
    assert out.count('sim_engine.py') == 2 and '/opt/simpy' in out and '--pool' not in out


# ------------------------------------------------------------ real simulator (optional)
try:
    se.import_sim()  # honours STS2_SIM_PATH
    HAVE_SIM = True
except se.SimError:
    HAVE_SIM = False

real = pytest.mark.skipif(not HAVE_SIM, reason='sts2_rs (spire-mentor) not built; see docs/sim-engine.md')


@real
def test_real_bencha0s01_matches_live_fingerprint():
    """Live v0.107.1 (bench2-v2-determinism-BENCHA0S01): Neow, first fight and opening hand."""
    g = se.SimGame('BENCHA0S01')
    obs = g.observe()
    assert [o['title'] for o in obs['state']['event']['options']] == ['Nutritious Oyster', 'Phial Holster',
                                                                      'Cursed Pearl']
    g.act(obs['legal_actions'][0]['ref'])
    g.act(g.observe()['legal_actions'][0]['ref'])
    obs = g.observe()
    assert obs['state']['state_type'] == 'map'
    g.act({'action': 'choose_map_node', 'col': 0})
    st = g.observe()['state']
    enemy = st['battle']['enemies'][0]
    assert (enemy['name'], enemy['hp'], enemy['intents'][0]['total_damage']) == ('Nibbit', 44, 12)
    assert [c['name'] for c in st['player']['hand']] == ['Defend', 'Strike', 'Defend', 'Strike', 'Defend']
    assert (st['player']['hp'], st['player']['max_hp'], st['player']['gold']) == (91, 91, 99)


@real
def test_real_stub_run_finishes(tmp_path):
    result = se.run_seed('BENCHA10S00', tmp_path, policy_name='stub', policy=policies.stub_policy, ascension=10)
    assert result['end_reason'] == 'game_over' and result['error'] is None and result['friction_total'] == 0
