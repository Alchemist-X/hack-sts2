"""A state_version tick with an unchanged decision is not a stale decision.

Regression for BENCHA0S09 F10: the treasure chest finished opening between
observe and act, so state_version went 108->109 while the decision hash
(can_proceed is excluded from it) stayed the same; the claim was
refused, and a shell-chained ``proceed`` then ran and skipped the relic.
"""
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

CLAIM = {'action': 'claim_treasure_relic', 'relic': 'VENERABLE_TEA_SET'}


def observation(state, version=None):
    obs = {'hash': executor.decision_digest(state), 'state': state, 'legal_actions': refs.observe_actions(state)}
    if version is not None:
        obs['state_version'] = version
    return obs


def probe(version):
    return lambda port, *a, **k: {'state_version': version, 'busy': False}


@pytest.fixture
def chest():
    """Chest mid-opening (v108) and opened (v109): same decision hash, proceed now listed."""
    closed = observation(load('treasure_unopened'), version=108)
    opened = observation(load('treasure_opened_unclaimed'), version=109)
    assert closed['hash'] == opened['hash']
    assert refs.legal_digest(closed['legal_actions']) != refs.legal_digest(opened['legal_actions'])
    return closed, opened


# ----------------------------------------------------------------- digest
def test_legal_digest_is_ref_order_sensitive_and_ignores_wire_fields(chest):
    closed, opened = chest
    actions = opened['legal_actions']
    assert refs.legal_digest(actions) == refs.legal_digest([{'ref': a['ref'], 'index': 99} for a in actions])
    assert refs.legal_digest(actions) != refs.legal_digest(list(reversed(actions)))
    assert refs.legal_digest([]) == refs.legal_digest(None)


def test_act_args_carry_the_legal_digest(chest):
    closed, _ = chest
    _, extra = agent_step.act_args(closed, CLAIM, 'r')
    assert extra == ['--expect-state-version', '108', '--expect-legal-digest', refs.legal_digest(closed['legal_actions'])]
    assert agent_step.act_args({'hash': 'h'}, CLAIM, 'r')[1] == []


# ----------------------------------------------------------- sts2_live guard
def test_live_accepts_moved_version_when_hash_and_legal_unchanged(harness, capsys, monkeypatch):
    out, act = harness
    sequence = {1: load('treasure_claimed')}
    game = FakeGame(load('treasure_opened_unclaimed'), lambda s, a, n: sequence.get(n, s))
    digest = refs.legal_digest(refs.observe_actions(game.state))
    monkeypatch.setattr(executor, 'eval_probe', probe(110))
    code, payload = run(act, capsys, game, json.dumps(CLAIM),
                        extra=('--expect-state-version', '109', '--expect-legal-digest', digest))
    assert code == 0, payload
    assert game.posts and game.posts[0]['action'] == 'claim_treasure_relic'
    assert any('re-bound to 110' in w for w in payload['warnings'])


def test_live_refuses_moved_version_when_legal_actions_changed(harness, capsys, monkeypatch):
    out, act = harness
    game = FakeGame(load('treasure_opened_unclaimed'))
    stale = refs.legal_digest(refs.observe_actions(load('treasure_unopened')))
    monkeypatch.setattr(executor, 'eval_probe', probe(109))
    code, payload = run(act, capsys, game, json.dumps(CLAIM),
                        extra=('--expect-state-version', '108', '--expect-legal-digest', stale))
    assert code == 2 and payload['error'] == 'stale_state_version'
    assert payload['expected_legal_digest'] == stale and payload['observed_legal_digest'] != stale
    assert not game.posts


def test_live_chain_sends_nothing_when_guard_refuses(harness, capsys, monkeypatch):
    out, act = harness
    game = FakeGame(load('treasure_opened_unclaimed'))
    monkeypatch.setattr(executor, 'eval_probe', probe(109))
    code, payload = run(act, capsys, game, json.dumps([CLAIM, {'action': 'proceed'}]),
                        extra=('--expect-state-version', '108', '--expect-legal-digest', 'deadbeef'))
    assert code == 2 and payload['error'] == 'stale_state_version'
    assert game.posts == []          # neither the claim nor the chained proceed


def test_live_without_legal_digest_still_refuses_a_moved_version(harness, capsys, monkeypatch):
    out, act = harness
    game = FakeGame(load('treasure_opened_unclaimed'))
    monkeypatch.setattr(executor, 'eval_probe', probe(109))
    code, payload = run(act, capsys, game, json.dumps(CLAIM), extra=('--expect-state-version', '108'))
    assert code == 2 and payload['error'] == 'stale_state_version' and not game.posts


# ----------------------------------------------------------------- runner
class ScriptedRunner(agent_step.Runner):
    """Runner whose sts2_live calls return scripted results."""

    def __init__(self, a, first_obs, observations, acts):
        super().__init__(a)
        self.observations, self.acts, self.calls = list(observations), list(acts), []
        self.last_obs.write_text(json.dumps(first_obs))

    def live(self, command, *args, extra=()):
        self.calls.append((command, args, tuple(extra)))
        if command == 'act':
            return 0, self.acts.pop(0)
        return 0, (self.observations.pop(0) if len(self.observations) > 1 else self.observations[0])


def make(tmp_path, first_obs, observations, acts):
    a = agent_step.build_parser().parse_args(
        ['--seed-dir', str(tmp_path), '--port', '15621', '--seed', 'BENCHTEST', 'act', 'x', 'y'])
    return ScriptedRunner(a, first_obs, observations, acts)


def stale(observed):
    return {'ok': False, 'error': 'stale_state_version', 'message': 'moved', 'observation': observed}


def records(tmp_path):
    return [json.loads(l) for l in (tmp_path / 'trajectory.jsonl').read_text().splitlines()]


def test_runner_rebinds_once_when_hash_unchanged_and_choice_still_offered(tmp_path, chest, capsys):
    closed, opened = chest
    claimed = observation(load('treasure_claimed'), version=110)
    runner = make(tmp_path, closed, [opened, claimed], [stale(opened), {'ok': True, 'steps': [{}], 'observation': claimed}])
    assert runner.act(json.dumps(CLAIM), 'take the relic') == 0
    acts = [c for c in runner.calls if c[0] == 'act']
    assert len(acts) == 2
    assert acts[1][2][:2] == ('--expect-state-version', '109')
    assert acts[1][2][3] == refs.legal_digest(opened['legal_actions'])
    decision = [r for r in records(tmp_path) if r['type'] == 'decision']
    assert len(decision) == 1 and decision[0]['outcome']['ok']
    assert any('auto-rebound state_version 108->109' in w for w in decision[0]['outcome']['warnings'])
    assert 'blocked_after_refusal' not in runner.s


def test_runner_rebinds_index_to_the_same_ref_not_the_same_position(tmp_path, chest):
    closed, opened = chest
    swift = f'1@{agent_step.listing_token(closed)}'      # discard SWIFT in the closed listing; proceed in opened
    runner = make(tmp_path, closed, [opened], [stale(opened), {'ok': True, 'steps': [{}], 'observation': opened}])
    assert runner.act(swift, 'discard swift potion') == 0
    acts = [c for c in runner.calls if c[0] == 'act']
    assert [json.loads(c[1][0])['action'] for c in acts] == ['discard_potion', 'discard_potion']


def test_runner_does_not_rebind_when_hash_changed(tmp_path, chest):
    closed, _ = chest
    claimed = observation(load('treasure_claimed'), version=109)
    runner = make(tmp_path, closed, [claimed], [stale(claimed)])
    assert runner.act(json.dumps(CLAIM), 'take the relic') == 2
    assert len([c for c in runner.calls if c[0] == 'act']) == 1
    assert runner.s['blocked_after_refusal']['kind'] == 'stale_state_version'


# --------------------------------------------------- chain stop on refusal
def test_refused_act_blocks_a_chained_proceed_until_obs(tmp_path, chest, capsys):
    closed, opened = chest
    claimed = observation(load('treasure_claimed'), version=110)
    seed = ['--seed-dir', str(tmp_path), '--port', '15621', '--seed', 'BENCHTEST']
    holder = {}

    def factory(observations, acts):
        def build(a):
            first = json.loads((tmp_path / 'tools' / 'last_obs.json').read_text()) \
                if (tmp_path / 'tools' / 'last_obs.json').exists() else closed
            holder['r'] = ScriptedRunner(a, first, observations, acts)
            return holder['r']
        return build

    # 1. claim with an unbound index is refused (nothing sent)
    assert agent_step.main([*seed, 'act', '0', 'claim'], runner_cls=factory([opened], [])) == 3
    # 2. the shell-chained proceed is refused too, without calling sts2_live at all
    assert agent_step.main([*seed, 'act', 'proceed', 'leave'], runner_cls=factory([opened], [])) == 3
    assert holder['r'].calls == []
    assert 'blocked_after_refusal' in capsys.readouterr().out
    # 3. an explicit obs clears the block; the next act is sent
    assert agent_step.main([*seed, 'obs'], runner_cls=factory([opened], [])) == 0
    ok = {'ok': True, 'steps': [{}], 'observation': claimed}
    assert agent_step.main([*seed, 'act', json.dumps(CLAIM), 'claim'], runner_cls=factory([claimed], [ok])) == 0
    assert [c[0] for c in holder['r'].calls] == ['act', 'observe']
    kinds = [r.get('kind') for r in records(tmp_path) if r['type'] == 'friction']
    assert kinds == ['index_unbound', 'blocked_after_refusal']


def test_failed_act_also_blocks_until_obs(tmp_path, chest):
    closed, _ = chest
    failed = {'ok': False, 'error': 'unresolvable_action', 'message': 'no such relic', 'observation': closed}
    runner = make(tmp_path, closed, [closed], [failed])
    assert runner.act(json.dumps(CLAIM), 'claim') == 2
    assert runner.act('proceed', 'leave') == 3
    assert [c[0] for c in runner.calls] == ['act', 'observe']
    runner.clear_block()
    assert 'blocked_after_refusal' not in runner.s
