"""Harness side of the MCP eval API (/api/v1/eval): start_run, readback checks, busy gate (no game needed)."""
import json
import sys
from pathlib import Path

import pytest

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'scripts'))
sys.path.insert(0, str(ROOT / 'scripts' / 'bench'))
import run_bench  # noqa: E402
import sts2_action_executor as executor  # noqa: E402

FIX = Path(__file__).parent / 'fixtures' / 'live_frictions'
SETTINGS = {'fast_mode': 'fast', 'screenshake': 0, 'text_effects': False, 'skip_tutorials': True}


def ok_reply(character='IRONCLAD', ascension=10, seed='BENCHA0S01', **extra):
    return {'status': 'ok', 'verified': True, 'error': None, 'steps': [],
            'readback': {'character': character, 'ascension': ascension, 'seed': seed, 'game_mode': 'standard'},
            'settings': SETTINGS, **extra}


@pytest.fixture
def fake_eval(monkeypatch):
    calls = []

    def install(reply, available=True):
        def eval_api(port, body=None, timeout=200, query=''):
            calls.append(body)
            if body is None:
                if not available:
                    raise OSError('HTTP 404')
                return {'schema': 'sts2-eval-snapshot', 'busy': False}
            return reply(body) if callable(reply) else reply
        monkeypatch.setattr(run_bench, 'eval_api', eval_api)
        return calls
    return install


def test_start_run_uses_eval_api_and_checks_the_readback(fake_eval):
    calls = fake_eval(ok_reply())
    reply, method = run_bench.start_run(1, 'bencha0s01', 'IRONCLAD', 10, 'auto', SETTINGS)
    assert method == 'eval' and reply['verified']
    body = calls[-1]
    assert body['action'] == 'start_run' and body['seed'] == 'BENCHA0S01' and body['ascension'] == 10
    assert body['mode'] == 'standard' and body['settings'] == SETTINGS


@pytest.mark.parametrize('reply', [
    ok_reply(ascension=0),                                  # mod says ok but readback disagrees
    ok_reply(seed='OTHER'),
    ok_reply(character='SILENT'),
    dict(ok_reply(), verified=False),
    {'status': 'error', 'error': 'A run is already in progress', 'verified': False},
])
def test_start_run_refuses_unverified_or_mismatched_readback(fake_eval, reply):
    fake_eval(reply)
    with pytest.raises(run_bench.BenchError):
        run_bench.start_run(1, 'BENCHA0S01', 'IRONCLAD', 10, 'eval')


def test_start_run_falls_back_to_menu_only_when_allowed(fake_eval, monkeypatch):
    fake_eval(ok_reply(), available=False)
    monkeypatch.setattr(run_bench, 'start_seeded_run', lambda *a: {'embarked': a})
    reply, method = run_bench.start_run(1, 'S1', 'IRONCLAD', 0, 'auto')
    assert method == 'menu' and reply['embarked'][1] == 'S1'
    with pytest.raises(run_bench.BenchError, match='no /api/v1/eval'):
        run_bench.start_run(1, 'S1', 'IRONCLAD', 0, 'eval')
    with pytest.raises(run_bench.BenchError, match='game_settings'):
        run_bench.start_run(1, 'S1', 'IRONCLAD', 0, 'auto', SETTINGS)
    _, method = run_bench.start_run(1, 'S1', 'IRONCLAD', 0, 'menu')
    assert method == 'menu'


def test_run_result_is_none_while_the_run_is_still_in_progress(fake_eval):
    fake_eval({'status': 'in_progress', 'run_result': None})
    assert run_bench.run_result(1) is None
    fake_eval({'status': 'ok', 'run_result': {'victory': False, 'cause': 'encounter:NIBBIT_WEAK', 'score': 12}})
    assert run_bench.run_result(1)['cause'] == 'encounter:NIBBIT_WEAK'


def make_inst(tmp_path, save):
    class Inst:
        dir = tmp_path

        def current_run(self, _profile):
            return save
    return Inst()


def test_verify_seed_cross_checks_the_mcp_readback(tmp_path):
    (tmp_path / 'game.log').write_text('Embarking on a singleplayer IRONCLAD run. Ascension: 10 Seed: S1\n')
    inst = make_inst(tmp_path, {'rng': {'seed': 'S1'}, 'game_mode': 'standard', 'ascension': 10})
    ev = run_bench.verify_seed(inst, 'p', 'S1', 0, 10, readback=ok_reply(seed='S1')['readback'])
    assert ev['verified'] and ev['mcp_readback']['seed'] == 'S1'
    ev = run_bench.verify_seed(inst, 'p', 'S1', 0, 10, readback=ok_reply(seed='S1', ascension=9)['readback'])
    assert not ev['verified']


def test_verify_seed_reads_custom_run_log_lines(tmp_path):
    (tmp_path / 'game.log').write_text('Embarking on a CUSTOM DEFECT run. Ascension: 3 Seed: CUST0M1 Modifiers: X\n')
    ev = run_bench.verify_seed(make_inst(tmp_path, None), 'p', 'CUST0M1', 0, 3)
    assert ev['verified'] and ev['log']['character'] == 'DEFECT'


def test_cli_and_config_select_start_method_and_settings(tmp_path):
    cfg = json.loads((ROOT / 'config/bench/ironclad-a10-10seed.json').read_text())
    path = tmp_path / 'c.json'
    path.write_text(json.dumps(dict(cfg, start_method='eval', game_settings=SETTINGS)))
    a = run_bench.parse_args(['--config', str(path)])
    assert a.start_method == 'eval' and a.game_settings == SETTINGS
    a = run_bench.parse_args(['--config', str(path), '--start-method', 'menu', '--game-settings', '{}'])
    assert a.start_method == 'menu' and a.game_settings == {}
    a = run_bench.parse_args(['--config', str(ROOT / 'config/bench/ironclad-a10-10seed.json')])
    assert a.start_method == 'auto' and a.game_settings is None
    with pytest.raises(SystemExit):
        run_bench.parse_args(['--config', str(path), '--game-settings', '[1]'])


# ------------------------------------------------------------ busy gate
def test_wait_settled_waits_while_the_mod_reports_busy(monkeypatch):
    state = json.loads((FIX / 'kill_after.json').read_text())
    monkeypatch.setattr(executor, 'request', lambda port, action=None: state)
    probes = iter([{'busy': True, 'busy_reason': 'combat_ending_awaiting_rewards'}] * 3 + [{'busy': False}] * 99)
    seen = []

    def probe(port, *a, **k):
        seen.append(next(probes))
        return seen[-1]
    monkeypatch.setattr(executor, 'eval_probe', probe)
    _, settled = executor.wait_settled(1, timeout=3, quiet_window=0.02, poll=0.01)
    assert settled and len(seen) == 4
    monkeypatch.setattr(executor, 'eval_probe', lambda *a, **k: {'busy': True})
    _, settled = executor.wait_settled(1, timeout=0.2, quiet_window=0.02, poll=0.01)
    assert not settled


def test_eval_probe_without_the_endpoint_is_skipped_for_the_process(monkeypatch):
    monkeypatch.setattr(executor, '_EVAL_UNAVAILABLE', set())
    attempts = []

    def urlopen(url, timeout=0):
        attempts.append(url)
        raise executor.urllib.error.URLError('refused')
    monkeypatch.setattr(executor.urllib.request, 'urlopen', urlopen)
    assert executor.eval_probe(9) is None and executor.eval_probe(9) is None
    assert len(attempts) == 1 and 'include_state=false' in attempts[0]


# ------------------------------------------------ busy while awaiting our pick
def test_wait_settled_accepts_player_choice_despite_busy_queue(monkeypatch):
    """S05 F13: True Grit+ 'Choose a card to Exhaust' deadlocked on busy."""
    state = json.loads((FIX / 'hand_select_upgrade.json').read_text())
    assert state['execution']['game_action_state'] == 'GatheringPlayerChoice'
    monkeypatch.setattr(executor, 'request', lambda port, action=None: state)
    monkeypatch.setattr(executor, 'eval_probe',
                        lambda *a, **k: {'busy': True, 'busy_reason': 'action_queue_not_empty'})
    got, settled = executor.wait_settled(1, timeout=1, quiet_window=0.02, poll=0.01)
    assert settled and got['state_type'] == 'hand_select'


@pytest.mark.parametrize('mutate', [
    lambda s: s['execution'].update(game_action_state='Executing'),
    lambda s: s.update(state_type='monster'),
])
def test_busy_still_blocks_without_the_player_choice_witness(monkeypatch, mutate):
    state = json.loads((FIX / 'hand_select_upgrade.json').read_text())
    mutate(state)
    assert not executor.awaiting_player_choice(state, {'busy': True, 'busy_reason': 'action_queue_not_empty'})


def test_other_busy_reasons_still_block_a_player_choice():
    state = json.loads((FIX / 'hand_select_upgrade.json').read_text())
    assert not executor.awaiting_player_choice(state, {'busy': True, 'busy_reason': 'scene_transition'})
