"""Tests for scripts/bench/map_lookahead.py and its text_state integration."""
import json
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'scripts' / 'bench'))
import map_lookahead as la  # noqa: E402
import text_state  # noqa: E402

TEXT = Path(__file__).parent / 'fixtures' / 'text_state'
CLI = ROOT / 'scripts' / 'bench' / 'map_lookahead.py'


def n(col, row, typ, *kids):
    return {'col': col, 'row': row, 'type': typ, 'children': [list(k) for k in kids]}


def toy_map():
    # (1,1) is a forced corridor: M -> E -> M -> $ ; (3,1) can rest before any elite.
    nodes = [
        n(3, 0, 'Ancient', (1, 1), (3, 1)),
        n(1, 1, 'Monster', (1, 2)), n(3, 1, 'Monster', (2, 2), (3, 2)),
        n(1, 2, 'Elite', (1, 3)), n(2, 2, 'RestSite', (2, 3)), n(3, 2, 'Elite', (2, 3)),
        n(1, 3, 'Monster', (2, 4)), n(2, 3, 'Event', (2, 4), (3, 4)),
        n(2, 4, 'Shop', (3, 5)), n(3, 4, 'Treasure', (3, 5)),
    ]
    return {'current_position': {'col': 3, 'row': 0, 'type': 'Ancient'}, 'nodes': nodes,
            'next_options': [{'index': 0, 'col': 1, 'row': 1, 'type': 'Monster', 'leads_to': []},
                             {'index': 1, 'col': 3, 'row': 1, 'type': 'Monster', 'leads_to': []}],
            'boss': {'col': 3, 'row': 5, 'name': 'Toy Boss'}}


def test_forced_corridor_summary():
    a, b = la.lookahead(toy_map())
    assert a['paths'] == 1 and a['counts'] == {'E': [1, 1], 'R': [0, 0], '$': [1, 1], '?': [0, 0], 'T': [0, 0]}
    assert a['forced_e'] == 2 and a['first_r'] is None and a['e_before_r'] is True
    assert b['paths'] == 4 and b['counts'] == {'E': [0, 1], 'R': [0, 1], '$': [0, 1], '?': [1, 1], 'T': [0, 1]}
    assert b['forced_e'] is None and b['first_r'] == 2 and b['e_before_r'] is False


def test_fmt_and_paths():
    a, b = [la.fmt(s) for s in la.lookahead(toy_map())]
    assert a == 'Look 1,1M: 1p E1 R0 $1 ?0 T0 | no R | forcedE r2 | E<R forced'
    assert b == 'Look 3,1M: 4p E0-1 R0-1 $0-1 ?1 T0-1 | firstR r2'
    paths = la.enumerate_paths(toy_map(), (3, 1))
    assert sorted(' '.join(p) for p in paths) == sorted([
        '3,1M 2,2R 2,3? 2,4$', '3,1M 2,2R 2,3? 3,4T', '3,1M 3,2E 2,3? 2,4$', '3,1M 3,2E 2,3? 3,4T'])
    assert len(la.enumerate_paths(toy_map(), (3, 1), limit=2)) == 2


def test_cycles_and_dangling_edges_are_safe():
    m = toy_map()
    m['nodes'][3]['children'].append([1, 1])  # back edge
    m['nodes'][6]['children'].append([9, 9])  # dangling, treated as terminal
    assert la.lookahead(m)[0]['paths'] == 1
    assert la.lookahead({}) == [] and la.lookahead(None) == []


def obs_for(m, hp):
    return {'state': {'state_type': 'map', 'run': {'act': 1, 'floor': 1, 'ascension': 10},
                      'player': {'hp': hp, 'max_hp': 80, 'block': 0, 'gold': 0}, 'map': m},
            'legal_actions': [{'action': 'choose_map_node', 'index': 0}, {'action': 'choose_map_node', 'index': 1}]}


def test_text_state_shows_lookahead_and_low_hp_warning():
    hi = text_state.encode(obs_for(toy_map(), 70))
    assert 'floor=row+1' in hi and 'Look 1,1M: 1p' in hi and 'avoid' not in hi
    lo = text_state.encode(obs_for(toy_map(), 40))
    assert 'Look 1,1M: 1p E1 R0 $1 ?0 T0 | no R | forcedE r2 | E<R forced | !HP<60% avoid' in lo
    assert 'Look 3,1M: 4p E0-1 R0-1 $0-1 ?1 T0-1 | firstR r2\n' in lo


def test_real_fixture_is_compact():
    obs = json.loads((TEXT / 'map_midrun.json').read_text())
    text = text_state.encode(obs)
    look = [l for l in text.splitlines() if l.startswith('Look ')]
    assert look == ['Look 0,6M: 38p E0-3 R1-3 $0-2 ?1-4 T1 | firstR r7',
                    'Look 1,6E: 44p E1-4 R1-3 $0-2 ?0-4 T1 | firstR r7 | forcedE r6 | E<R forced']
    assert sum(len(l) for l in look) < 200  # token budget: a few dozen tokens per option


def test_lookahead_never_breaks_encoder(monkeypatch):
    monkeypatch.setattr(la, 'lookahead', lambda m: 1 / 0)
    text = text_state.encode(obs_for(toy_map(), 70))
    assert 'Lookahead unavailable: ZeroDivisionError' in text and '## Legal actions' in text


def test_cli(tmp_path):
    f = tmp_path / 'obs.jsonl'
    f.write_text(json.dumps({'other': 1}) + '\n' + json.dumps(obs_for(toy_map(), 70)) + '\n')
    out = subprocess.run([sys.executable, str(CLI), str(f), '--paths', '5'], capture_output=True, text=True, check=True)
    assert out.stdout.splitlines()[0].startswith('Look 1,1M: 1p') and '   3,1M 2,2R 2,3? 2,4$' in out.stdout
    js = subprocess.run([sys.executable, str(CLI), '-', '--json'], input=json.dumps(toy_map()),
                        capture_output=True, text=True, check=True)
    assert [s['forced_e'] for s in json.loads(js.stdout)] == [2, None]
    bad = subprocess.run([sys.executable, str(CLI), '-'], input='{"x":1}', capture_output=True, text=True)
    assert bad.returncode == 2


def test_boss_node_option_prints_boss():
    m = toy_map()
    m['current_position'] = {'col': 3, 'row': 4, 'type': 'Treasure'}
    m['next_options'] = [{'index': 0, 'col': 3, 'row': 5, 'type': 'Boss', 'leads_to': []}]
    (s,) = la.lookahead(m)
    assert s['boss'] is True and la.fmt(s) == 'Look 3,5B: boss'
    lo = text_state.encode(obs_for(m, 40))
    assert 'Look 3,5B: boss' in lo.splitlines() and 'no path to boss' not in lo and 'avoid' not in lo
    m.pop('boss')  # typed Boss node with no explicit boss record
    m['nodes'].append(n(3, 5, 'Boss'))
    assert la.fmt(la.lookahead(m)[0]) == 'Look 3,5B: boss'
