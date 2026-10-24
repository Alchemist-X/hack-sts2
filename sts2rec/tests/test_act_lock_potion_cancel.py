"""Regression tests for two S08-replay frictions.

* ``parallel_act_race``: two act calls sent concurrently to one instance
  interleaved (one play landed, the other was left pending).  A per-port lock
  now serializes them (``wait_s``) or refuses the second with ``act_in_flight``
  before anything is sent.
* ``potion_cancel_consumed``: cancelling the card choice a Colorless Potion opened
  consumed the potion.  Cancel/skip of a potion-opened selection is refused
  unless ``"allow_potion_cancel": true`` / ``--allow-potion-cancel`` is given.
"""
import json
import os
import subprocess
import sys
import textwrap
import time
from pathlib import Path

import pytest

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'scripts'))
sys.path.insert(0, str(ROOT / 'scripts' / 'bench'))
sys.path.insert(0, str(Path(__file__).parent))
import agent_step  # noqa: E402
import sts2_act_lock as act_lock  # noqa: E402
import sts2_action_executor as executor  # noqa: E402
import sts2_live as live  # noqa: E402
from test_live_frictions import FakeGame, harness, load, run  # noqa: E402,F401

PORT = 15701


@pytest.fixture
def holder():
    """Run a separate process that holds the port lock until told to stop."""
    procs = []

    def start(port=PORT, hold_s=30.0):
        code = textwrap.dedent(f"""
            import sys, time
            sys.path.insert(0, {str(ROOT / 'scripts')!r})
            import sts2_act_lock as l
            with l.act_lock({port}, command='holder-test'):
                print('ready', flush=True)
                time.sleep({hold_s})
        """)
        proc = subprocess.Popen([sys.executable, '-c', code], stdout=subprocess.PIPE, text=True, env=os.environ.copy())
        assert proc.stdout.readline().strip() == 'ready'
        procs.append(proc)
        return proc
    yield start
    for proc in procs:
        proc.kill()
        proc.wait()


# ------------------------------------------------------------------ lock unit
def test_second_act_on_same_port_fails_fast_naming_holder(holder):
    proc = holder()
    began = time.monotonic()
    with pytest.raises(act_lock.ActInFlight) as err:
        with act_lock.act_lock(PORT):
            pytest.fail('lock should not be acquired')
    assert time.monotonic() - began < 1.0
    assert err.value.holder['pid'] == proc.pid and err.value.holder['command'] == 'holder-test'
    assert 'another act in flight on port 15701' in str(err.value)


def test_other_port_is_independent(holder):
    holder(port=PORT)
    with act_lock.act_lock(PORT + 1) as held:
        assert held['pid'] == os.getpid()


def test_wait_serializes_behind_holder(holder):
    proc = holder(hold_s=0.4)
    began = time.monotonic()
    with act_lock.act_lock(PORT, wait_s=10) as held:
        assert held is not None
    assert 0.2 < time.monotonic() - began < 5
    proc.wait(timeout=5)


def test_lock_released_after_body_and_on_error():
    with pytest.raises(RuntimeError):
        with act_lock.act_lock(PORT):
            raise RuntimeError('boom')
    with act_lock.act_lock(PORT):
        pass
    assert os.environ.get(act_lock.HELD_ENV) is None


def test_child_of_holder_reenters_without_deadlock():
    with act_lock.act_lock(PORT):
        assert os.environ[act_lock.HELD_ENV] == str(PORT)
        child = subprocess.run([sys.executable, '-c', textwrap.dedent(f"""
            import sys; sys.path.insert(0, {str(ROOT / 'scripts')!r})
            import sts2_act_lock as l
            with l.act_lock({PORT}) as h:
                print('reentered' if h is None else 'acquired')
        """)], capture_output=True, text=True, env=os.environ.copy(), timeout=10)
        assert child.stdout.strip() == 'reentered'


def test_crashed_holder_leaves_no_stale_lock(holder):
    proc = holder()
    proc.kill()
    proc.wait()
    with act_lock.act_lock(PORT) as held:
        assert held is not None


# ------------------------------------------------------------ sts2_live act
def test_live_act_refused_while_other_act_in_flight(harness, capsys, holder):
    out, act = harness
    holder()
    game = FakeGame(load('kill_before'), lambda s, a, n: load('kill_after'))
    code, payload = run(act, capsys, game, '{"action":"end_turn"}')
    assert code == 2 and payload['error'] == 'act_in_flight'
    assert 'another act in flight' in payload['message']
    assert game.posts == []
    assert not (out / 'pending-action.json').exists()


def test_live_act_lock_wait_serializes(harness, capsys, holder):
    out, act = harness
    holder(hold_s=0.4)
    game = FakeGame(load('kill_before'), lambda s, a, n: load('kill_after'))
    code, payload = run(act, capsys, game, '{"action":"end_turn"}', extra=('--lock-wait', '10'))
    assert payload.get('error') != 'act_in_flight'
    assert game.posts, 'the action is sent once the earlier act finished'


# ------------------------------------------------------------ agent_step act
def test_agent_step_refuses_without_touching_state(tmp_path, holder, capsys):
    seed_dir = tmp_path / 'seed'
    seed_dir.mkdir()
    holder()

    class Boom:
        def __init__(self, a):
            raise AssertionError('runner must not be built while another act holds the lock')
    code = agent_step.main(['--seed-dir', str(seed_dir), '--port', str(PORT), '--seed', 'S', 'act',
                            '{"action":"end_turn"}', 'end the turn'], runner_cls=Boom)
    assert code == 3
    assert 'act_in_flight' in capsys.readouterr().out
    friction = json.loads((seed_dir / 'trajectory.jsonl').read_text().splitlines()[-1])
    assert friction['type'] == 'friction' and friction['kind'] == 'act_in_flight'
    assert not (seed_dir / 'tools' / 'state.json').exists()


def test_agent_step_act_holds_lock_for_its_duration(tmp_path):
    seen = {}

    class Probe:
        def __init__(self, a):
            pass

        def act(self, text, reason):
            seen['env'] = os.environ.get(act_lock.HELD_ENV)
            with pytest.raises(act_lock.ActInFlight):
                os.environ.pop(act_lock.HELD_ENV)  # look like an unrelated process
                try:
                    with act_lock.act_lock(PORT):
                        pass
                finally:
                    os.environ[act_lock.HELD_ENV] = str(PORT)
            return 0
    assert agent_step.main(['--seed-dir', str(tmp_path), '--port', str(PORT), '--seed', 'S', 'act',
                            'end', 'end the turn'], runner_cls=Probe) == 0
    assert seen['env'] == str(PORT)


# ------------------------------------------------------- potion cancel guard
def opened_by(out, action):
    executor.append_jsonl(out / 'decisions.jsonl', {'type': 'decision_intent', 'decision_id': 'x#s1',
                                                    'action': {'action': action}})


@pytest.mark.parametrize('payload', ['{"action":"cancel"}', '{"action":"cancel_selection"}',
                                     '{"action":"skip"}'])
def test_cancel_of_potion_selection_is_refused(harness, capsys, payload):
    out, act = harness
    opened_by(out, 'use_potion')
    game = FakeGame(load('card_select_choose'), lambda s, a, n: load('kill_after'))
    code, result = run(act, capsys, game, payload)
    assert code == 2 and result['error'] == 'potion_selection_cancel'
    assert 'allow_potion_cancel' in result['message']
    assert game.posts == []


def test_cancel_of_potion_selection_with_explicit_flag(harness, capsys):
    out, act = harness
    opened_by(out, 'use_potion')
    game = FakeGame(load('card_select_choose'), lambda s, a, n: load('kill_after'))
    code, result = run(act, capsys, game, '{"action":"cancel","allow_potion_cancel":true}')
    assert result.get('ok') is True
    assert [p['action'] for p in game.posts] == ['cancel_selection']
    assert 'allow_potion_cancel' not in game.posts[0]


def test_cancel_of_potion_selection_with_cli_flag(harness, capsys):
    out, act = harness
    opened_by(out, 'use_potion')
    game = FakeGame(load('card_select_choose'), lambda s, a, n: load('kill_after'))
    code, result = run(act, capsys, game, '{"action":"skip"}', extra=('--allow-potion-cancel',))
    assert result.get('ok') is True and [p['action'] for p in game.posts] == ['skip_card_selection']


def test_cancel_of_non_potion_selection_is_allowed(harness, capsys):
    out, act = harness
    opened_by(out, 'choose_event_option')
    game = FakeGame(load('card_select_choose'), lambda s, a, n: load('kill_after'))
    code, result = run(act, capsys, game, '{"action":"cancel"}')
    assert result.get('ok') is True and [p['action'] for p in game.posts] == ['cancel_selection']


def test_selecting_a_card_after_potion_is_unaffected(harness, capsys):
    out, act = harness
    opened_by(out, 'use_potion')
    state = load('card_select_choose')
    name = state['card_select']['cards'][0]['name']
    game = FakeGame(state, lambda s, a, n: load('kill_after'))
    code, result = run(act, capsys, game, json.dumps({'action': 'select_card', 'card': name}))
    assert result.get('error') != 'potion_selection_cancel'
    assert game.posts and game.posts[0]['action'] == 'select_card'


def test_in_chain_potion_then_cancel_is_refused(tmp_path):
    out = tmp_path
    with pytest.raises(live.ActError) as err:
        live.potion_selection_guard(out, {'action': 'cancel_selection'}, {}, {'action': 'use_potion', 'slot': 1},
                                    False)
    assert err.value.payload['error'] == 'potion_selection_cancel'
    # previous step in the chain was not a potion: decisions.jsonl is not consulted
    opened_by(out, 'use_potion')
    live.potion_selection_guard(out, {'action': 'cancel_selection'}, {}, {'action': 'play_card'}, False)
