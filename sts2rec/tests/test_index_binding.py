"""Positional action numbers must stay bound to the listing they were read from.

Regression for BENCHA0S07 F25: after claiming the first reward the agent sent
``1`` chosen from the pre-claim listing (0 relic, 1 card, 2 proceed); against
the fresh listing ``1`` was ``proceed`` and the card reward was skipped.
"""
import copy
import json
import sys
from pathlib import Path

import pytest

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'scripts'))
sys.path.insert(0, str(ROOT / 'scripts' / 'bench'))
import agent_step  # noqa: E402
import sts2_action_executor as executor  # noqa: E402
import sts2_action_refs as refs  # noqa: E402
from tests.test_live_frictions import FakeGame, harness, load, run  # noqa: E402,F401


def observation(state, version=None):
    obs = {'hash': executor.decision_digest(state), 'state': state, 'legal_actions': refs.observe_actions(state)}
    if version is not None:
        obs['state_version'] = version
    return obs


def claimed_first(state):
    state = copy.deepcopy(state)
    items = state['rewards']['items'][1:]
    for i, item in enumerate(items):
        item['index'] = i
    state['rewards']['items'] = items
    return state


@pytest.fixture
def listings():
    before = observation(load('rewards'), version=41)
    after = observation(claimed_first(load('rewards')), version=42)
    assert before['legal_actions'][1]['ref'] == {'action': 'claim_reward', 'reward': 'potion:LIQUID_BRONZE'}
    assert after['legal_actions'][2]['ref'] == {'action': 'proceed'}
    return before, after


def test_index_bound_to_current_listing_resolves_to_its_ref(listings):
    before, _ = listings
    token = agent_step.listing_token(before)
    assert agent_step.bind(before, f'1@{token}') == {'action': 'claim_reward', 'reward': 'potion:LIQUID_BRONZE'}
    assert agent_step.bind(before, f'[0, 2]@{token.upper()}') == [
        {'action': 'claim_reward', 'reward': 'gold'}, {'action': 'claim_reward', 'reward': 'card'}]
    assert agent_step.bind(before, f'#3@{before["hash"]}') == {'action': 'proceed'}


def test_index_from_pre_claim_listing_is_refused_after_the_claim(listings):
    before, after = listings
    stale = f'2@{agent_step.listing_token(before)}'      # "card" in the listing the agent read
    with pytest.raises(agent_step.IndexBindingError) as err:
        agent_step.bind(after, stale)                     # would be "proceed" now
    assert err.value.kind == 'stale_index'
    assert agent_step.listing_token(after) in err.value.message


@pytest.mark.parametrize('text', ['1', '[0,1]', ' #2 '])
def test_bare_index_is_refused(listings, text):
    _, after = listings
    with pytest.raises(agent_step.IndexBindingError) as err:
        agent_step.bind(after, text)
    assert err.value.kind == 'index_unbound'
    assert f'@{agent_step.listing_token(after)}' in err.value.message


def test_out_of_range_index_is_refused(listings):
    _, after = listings
    with pytest.raises(agent_step.IndexBindingError) as err:
        agent_step.bind(after, f'7@{agent_step.listing_token(after)}')
    assert err.value.kind == 'index_out_of_range'


def test_refs_and_shorthands_need_no_token(listings):
    _, after = listings
    assert agent_step.bind(after, 'proceed') is None
    assert agent_step.parse_action(after, '{"action":"claim_reward","reward":"card"}') == {
        'action': 'claim_reward', 'reward': 'card'}
    assert agent_step.parse_action(after, 'end_turn') == 'end_turn'


def test_act_args_carry_hash_and_state_version(listings):
    before, _ = listings
    args, extra = agent_step.act_args(before, {'action': 'proceed'}, 'leave')
    assert args == ['{"action": "proceed"}', 'leave', before['hash']]
    assert extra[:2] == ['--expect-state-version', '41']
    no_version = {k: v for k, v in before.items() if k != 'state_version'}
    assert agent_step.act_args(no_version, 'proceed', 'leave')[1] == []
    assert 'state_version 41' in agent_step.listing_header(before)


@pytest.mark.parametrize('argv', [['obs'], ['observe'], ['act', 'obs'], ['act', 'obs', ''], ['act', ' OBSERVE ', 'x']])
def test_obs_is_routed_as_an_observation(argv):
    a = agent_step.build_parser().parse_args(['--seed-dir', '/tmp/x', '--port', '1', '--seed', 'S', *argv])
    assert agent_step.route(a) == 'observe'


def test_act_without_reason_is_refused_before_anything_is_sent():
    a = agent_step.build_parser().parse_args(['--seed-dir', '/tmp/x', '--port', '1', '--seed', 'S', 'act', 'proceed'])
    with pytest.raises(SystemExit, match='REASON'):
        agent_step.route(a)


class FakeRunner(agent_step.Runner):
    """Runner with the sts2_live subprocess replaced by an in-memory game."""

    def __init__(self, a, listings):
        super().__init__(a)
        self.listings, self.calls = list(listings), []
        self.last_obs.write_text(json.dumps(self.listings[0]))

    def live(self, command, *args, extra=()):
        self.calls.append((command, args, tuple(extra)))
        current = self.listings[-1]
        if command == 'act':
            return 0, {'ok': True, 'steps': [{'step': 1}], 'observation': current}
        return 0, current


def make_runner(tmp_path, listings, argv):
    a = agent_step.build_parser().parse_args(
        ['--seed-dir', str(tmp_path), '--port', '15621', '--seed', 'BENCHTEST', *argv])
    return a, FakeRunner(a, listings)


def test_runner_refuses_stale_index_without_acting(tmp_path, listings, capsys):
    before, after = listings
    stale = f'2@{agent_step.listing_token(before)}'
    a, runner = make_runner(tmp_path, [before, after], ['act', stale, 'take the card'])
    runner.last_obs.write_text(json.dumps(after))     # the claim's own act output already re-observed
    assert runner.act(a.action, a.reason) == 3
    assert [c[0] for c in runner.calls] == ['observe']   # re-listed, never acted
    out = capsys.readouterr().out
    assert 'ACT REFUSED (stale_index)' in out and f'@{agent_step.listing_token(after)}' in out
    frictions = [json.loads(l) for l in (tmp_path / 'trajectory.jsonl').read_text().splitlines()]
    assert [f['kind'] for f in frictions] == ['stale_index']


def test_runner_sends_bound_index_with_hash_and_version(tmp_path, listings, capsys):
    before, after = listings
    a, runner = make_runner(tmp_path, [before, after], ['act', f'1@{agent_step.listing_token(before)}', 'potion'])
    assert runner.act(a.action, a.reason) == 0
    command, args, extra = runner.calls[0]
    assert command == 'act'
    assert json.loads(args[0]) == {'action': 'claim_reward', 'reward': 'potion:LIQUID_BRONZE'}
    assert args[2] == before['hash'] and extra[:2] == ('--expect-state-version', '41')
    assert extra[2:] == ('--expect-legal-digest', refs.legal_digest(before['legal_actions']))
    record = json.loads((tmp_path / 'trajectory.jsonl').read_text().splitlines()[0])
    assert record['type'] == 'decision' and record['state_ref']['state_version'] == 41
    assert 'Index token: @' + agent_step.listing_token(after) in capsys.readouterr().out


def test_main_routes_act_obs_to_observe_without_recording_a_decision(tmp_path, listings):
    before, _ = listings
    holder = {}

    def factory(a):
        holder['runner'] = FakeRunner(a, [before])
        return holder['runner']
    argv = ['--seed-dir', str(tmp_path), '--port', '15621', '--seed', 'BENCHTEST', 'act', 'obs']
    assert agent_step.main(argv, runner_cls=factory) == 0
    assert [c[0] for c in holder['runner'].calls] == ['observe']
    assert not (tmp_path / 'trajectory.jsonl').exists()


# ------------------------------------------------- sts2_live state_version guard
def test_live_act_refuses_when_state_version_moved(harness, capsys, monkeypatch):
    out, act = harness
    game = FakeGame(load('map'))
    monkeypatch.setattr(executor, 'eval_probe', lambda port, *a, **k: {'state_version': 9, 'busy': False})
    code, payload = run(act, capsys, game, 'proceed', extra=('--expect-state-version', '8'))
    assert code == 2 and payload['error'] == 'stale_state_version'
    assert payload['expected_state_version'] == 8 and payload['observed_state_version'] == 9
    assert payload['observation'] and not game.posts


def test_live_act_state_version_guard_passes_and_degrades(harness, capsys, monkeypatch):
    out, act = harness
    sts2_live = sys.modules['sts2_live']
    monkeypatch.setattr(executor, 'eval_probe', lambda port, *a, **k: None)
    warning = sts2_live.check_state_version(1, 8, 'h', {}, out, None)
    assert warning and 'decision hash guard only' in warning
    assert sts2_live.check_state_version(1, None, 'h', {}, out, None) is None
    monkeypatch.setattr(executor, 'eval_probe', lambda port, *a, **k: {'state_version': 8, 'busy': False})
    assert sts2_live.check_state_version(1, 8, 'h', {}, out, None) is None
