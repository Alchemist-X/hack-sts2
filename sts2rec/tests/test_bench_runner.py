"""Tests for the fixed-seed benchmark tooling in scripts/bench/ (no game needed)."""
import hashlib
import io
import json
import sys
import tarfile
from pathlib import Path

import pytest

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'scripts'))
sys.path.insert(0, str(ROOT / 'scripts' / 'bench'))
import baseline  # noqa: E402
import metrics  # noqa: E402
import policies  # noqa: E402
import run_bench  # noqa: E402
import score  # noqa: E402
import sts2_action_refs as refs  # noqa: E402
import sts2_live as live  # noqa: E402

FIX = Path(__file__).parent / 'fixtures' / 'live_frictions'


def observation(name):
    state = json.loads((FIX / f'{name}.json').read_text())
    return {'hash': 'h', 'state': state, 'legal_actions': refs.observe_actions(state)}


# ------------------------------------------------------------------ policy
def test_stub_plays_first_card_in_combat():
    reply = policies.stub_policy(observation('combat'), {})
    assert reply['action']['action'] == 'play_card'
    assert reply['reason']


def test_stub_ends_turn_when_avoiding_all_cards():
    obs = observation('combat')
    plays = [a['ref'] for a in obs['legal_actions'] if a['ref']['action'] == 'play_card']
    reply = policies.stub_policy(obs, {'avoid': plays})
    assert reply['action'] == {'action': 'end_turn'}


def test_stub_leaves_shop_and_never_touches_potions():
    assert policies.stub_policy(observation('shop'), {})['action'] == {'action': 'proceed'}
    reply = policies.stub_policy(observation('treasure_unopened'), {})
    assert reply['action']['action'] == 'claim_treasure_relic'
    for name in ('rewards_potion_full', 'hand_select_upgrade', 'map', 'card_reward', 'card_select_choose'):
        assert 'potion' not in policies.stub_policy(observation(name), {})['action']['action']


def test_stub_fills_multi_card_selection():
    obs = observation('card_select_choose')
    obs['state']['card_select']['min_select'] = 2
    obs['state']['card_select']['max_select'] = 2
    reply = policies.stub_policy(obs, {})
    assert len(reply['action']['cards']) == 2
    steps = live.expand(obs['state'], reply['action'])
    assert len(steps) == 2 and steps[-1]['_confirm'] is True


def test_cmd_policy_round_trip(tmp_path):
    script = tmp_path / 'p.py'
    script.write_text('import json,sys\nreq=json.load(sys.stdin)\n'
                      'ref=req["observation"]["legal_actions"][-1]["ref"]\n'
                      'print("thinking...")\nprint(json.dumps({"action": ref, "reason": "last legal action"}))\n')
    name, policy = policies.load_policy(f'cmd:{sys.executable} {script}')
    reply, latency = policies.decide(policy, observation('map'), {'seed': 'X', 'index': 0})
    assert reply['action'] == observation('map')['legal_actions'][-1]['ref']
    assert reply['reason'] == 'last legal action' and latency >= 0


def test_decide_rejects_bad_reply_and_flags_missing_reason():
    with pytest.raises(policies.PolicyError):
        policies.decide(lambda o, c: {'no': 'action'}, {}, {})
    reply, _ = policies.decide(lambda o, c: {'action': 'end'}, {}, {})
    assert 'no rationale' in reply['reason']
    with pytest.raises(policies.PolicyError):
        policies.load_policy('nope')


# ----------------------------------------------------------------- metrics
def point(kind, hp, taken=0, healed=0, model='X'):
    return {'map_point_type': kind, 'player_stats': [{'current_hp': hp, 'max_hp': 80, 'damage_taken': taken,
                                                      'hp_healed': healed}],
            'rooms': [{'model_id': model, 'room_type': kind}]}


def test_history_metrics_counts_boss_kills_and_entry_hp():
    hist = {'seed': 'S', 'win': False, 'killed_by_encounter': 'ENCOUNTER.QUEEN_BOSS', 'killed_by_event': 'NONE.NONE',
            'map_point_history': [[point('ancient', 80), point('monster', 70, 10), point('boss', 40, 30, 0)],
                                  [point('monster', 35, 5), point('boss', 0, 50, 15)]], 'run_time': 1234}
    m = metrics.history_metrics(hist)
    assert m['act'] == 2 and m['floor'] == 5 and m['boss_kills'] == 1
    assert m['boss_entry_hp'] == [70, 35]
    assert m['death_cause'] == 'ENCOUNTER.QUEEN_BOSS' and m['win'] is False


def test_find_history_picks_seed_and_time(tmp_path):
    (tmp_path / '1.run').write_text(json.dumps({'seed': 'A', 'start_time': 100}))
    (tmp_path / '2.run').write_text(json.dumps({'seed': 'B', 'start_time': 200}))
    (tmp_path / '3.run').write_text(json.dumps({'seed': 'A', 'start_time': 300}))
    assert metrics.find_history(tmp_path, 'a')[0].name == '3.run'
    assert metrics.find_history(tmp_path, 'A', not_before=1000) is None


def test_trajectory_metrics_frictions_and_fallback():
    recs = [
        {'type': 'decision', 'state_type': 'monster', 'act': 1, 'floor': 3, 'hp': 50, 'enemies': ['A'],
         'outcome': {'ok': False, 'error': 'unresolvable_action', 'post_state_type': 'monster'}},
        {'type': 'decision', 'state_type': 'card_select', 'act': 1, 'floor': 3, 'hp': 50,
         'outcome': {'ok': True, 'harness_followups': ['auto_confirm_selection'], 'post_state_type': 'map'}},
        {'type': 'friction', 'kind': 'not_settled'},
    ]
    m = metrics.run_metrics(recs, None, wall_time_s=12.0, end_reason='game_over')
    assert m['frictions'] == {'unresolvable_action': 1, 'not_settled': 1}
    assert m['harness_followups'] == {'auto_confirm_selection': 1}
    assert m['source'] == 'trajectory' and m['floor'] == 3 and m['death_cause'] == 'combat:A'
    assert m['win'] is False and m['hp_at_boss_entry'] is None


# ------------------------------------------------------------------- score
def test_score_aggregates(tmp_path):
    rows = [{'seed': 'S1', 'policy': 'stub', 'floor': 5, 'act': 1, 'win': False, 'boss_kills': 0,
             'death_cause': 'E1', 'friction_total': 2, 'frictions': {'stale_hash': 2}, 'wall_time_s': 10},
            {'seed': 'S2', 'policy': 'stub', 'floor': 17, 'act': 2, 'win': False, 'boss_kills': 1,
             'boss_entry_hp': [40], 'hp_at_boss_entry': 40, 'death_cause': 'E2', 'friction_total': 0,
             'frictions': {}, 'wall_time_s': 30}]
    (tmp_path / 'results.jsonl').write_text(''.join(json.dumps(r) + '\n' for r in rows))
    agg = score.aggregate(score.load_results(tmp_path))
    assert agg[0]['runs'] == 2 and agg[0]['mean_floor'] == 11 and agg[0]['boss_kills'] == 1
    assert agg[0]['frictions'] == {'stale_hash': 2} and agg[0]['mean_hp_at_boss_entry'] == 40
    assert score.main([str(tmp_path)]) == 0


# ---------------------------------------------------------------- baseline
def make_baseline(tmp_path):
    src = tmp_path / 'src' / 'SlayTheSpire2'
    prof = src / 'default' / '1' / 'modded' / 'profile1' / 'saves'
    prof.mkdir(parents=True)
    (prof / 'progress.save').write_text('{"epochs": ["IRONCLAD3_EPOCH"]}')
    (src / 'default' / '1' / 'settings.save').write_text('{"mod_consent": true}')
    bdir = tmp_path / 'baseline'
    bdir.mkdir()
    tar_path = bdir / 'b.tar.gz'
    with tarfile.open(tar_path, 'w:gz') as tar:
        tar.add(src, arcname='SlayTheSpire2')
    sha = lambda p: hashlib.sha256(p.read_bytes()).hexdigest()  # noqa: E731
    (bdir / 'manifest.json').write_text(json.dumps({
        'name': 't', 'tarball': tar_path.name, 'sha256': sha(tar_path), 'tar_root': 'SlayTheSpire2',
        'active_profile': 'default/1/modded/profile1', 'progress_save_sha256': sha(prof / 'progress.save'),
        'settings_save_sha256': sha(src / 'default' / '1' / 'settings.save')}))
    return bdir


def test_baseline_restore_replaces_save_tree(tmp_path):
    bdir = make_baseline(tmp_path)
    inst = tmp_path / 'worker-pool-test' / 'inst1'
    save = inst / 'home/.local/share/SlayTheSpire2'
    (save / 'default/1/modded/profile1/saves').mkdir(parents=True)
    (save / 'default/1/modded/profile1/saves/current_run.save').write_text('{}')
    info = baseline.restore(inst, bdir)
    assert not (save / 'default/1/modded/profile1/saves/current_run.save').exists()
    assert json.loads((save / 'default/1/settings.save').read_text()) == {'mod_consent': True}
    assert info['tarball_sha256']


def test_baseline_refuses_tampered_tar_and_non_pool_paths(tmp_path):
    bdir = make_baseline(tmp_path)
    with pytest.raises(baseline.BaselineError):
        baseline.restore(tmp_path / 'elsewhere' / 'inst1', bdir)
    with open(bdir / 'b.tar.gz', 'ab') as f:
        f.write(b'x')
    with pytest.raises(baseline.BaselineError):
        baseline.restore(tmp_path / 'worker-pool-test' / 'inst1', bdir)


# ---------------------------------------------------------------- runner
def test_canonical_seed_and_seed_evidence(tmp_path):
    assert run_bench.canonical_seed(' bencha0s01 ') == 'BENCHA0S01'
    assert run_bench.canonical_seed('io') == '10'
    with pytest.raises(ValueError):
        run_bench.canonical_seed('BAD SEED!')


def test_summarize_state_from_combat():
    s = run_bench.summarize_state(observation('combat'))
    assert s['state_type'] == 'monster' and s['enemies'] and s['hp'] is not None


def test_config_seed_list_is_ten_unique_ironclad_a0():
    cfg = json.loads((ROOT / 'config/bench/ironclad-a0-10seed.json').read_text())
    assert cfg['character'] == 'IRONCLAD' and cfg['ascension'] == 0
    seeds = [run_bench.canonical_seed(s) for s in cfg['seeds']]
    assert len(seeds) == 10 == len(set(seeds))


# ------------------------------------------------------------- wiki facts
def test_wiki_facts_flag(tmp_path):
    state = json.loads((FIX / 'combat.json').read_text())
    (tmp_path / 'session.json').write_text(json.dumps({'knowledge_version': 'v0.107.1'}))
    assert live.wiki_facts(tmp_path, state) is None
    (tmp_path / 'session.json').write_text(json.dumps({'knowledge_version': 'v0.107.1', 'wiki_facts': True}))
    facts = live.wiki_facts(tmp_path, state)
    assert isinstance(facts, str) and 0 < len(facts) <= live.WIKI_FACTS_MAX_CHARS


def test_baseline_labels_resolve_to_manifest_files(tmp_path, monkeypatch):
    bdir = make_baseline(tmp_path)
    (bdir / 'manifest.json').rename(bdir / 'manifest-v2-unlocked.json')
    monkeypatch.setattr(baseline, 'BASELINE_ROOT', bdir)
    assert baseline.DEFAULT_BASELINE == 'v2'
    assert baseline.manifest_path('v2') == bdir / 'manifest-v2-unlocked.json'
    assert baseline.manifest_path('v1') == bdir / 'manifest.json'
    assert baseline.manifest_path('v2-a10') == bdir / 'manifest-v2-a10.json'
    assert baseline.manifest_path(bdir / 'manifest-v2-unlocked.json') == bdir / 'manifest-v2-unlocked.json'
    with pytest.raises(baseline.BaselineError):
        baseline.load_manifest('v1')
    inst = tmp_path / 'worker-pool-test' / 'inst1'
    assert baseline.restore(inst)['progress_save_sha256']
    assert baseline.restore(inst, bdir / 'manifest-v2-unlocked.json')['tarball_sha256']


def test_config_pins_each_allowed_baseline():
    config = json.loads((ROOT / 'config/bench/ironclad-a0-10seed.json').read_text())
    assert set(config['baselines']) == {'v1', 'v2'}
    for pinned in config['baselines'].values():
        run_bench.check_baseline(config, {'name': pinned['name'], 'sha256': pinned['sha256']})
    with pytest.raises(SystemExit):
        run_bench.check_baseline(config, {'name': 'sts2-bench-baseline-v1', 'sha256': '0' * 64})
    with pytest.raises(SystemExit):
        run_bench.check_baseline(config, {'name': 'other', 'sha256': config['baselines']['v2']['sha256']})
    run_bench.check_baseline({'baseline': {'name': 'a', 'sha256': 'x'}}, {'name': 'a', 'sha256': 'x'})


def test_a10_config_pins_the_a10_baseline_and_reuses_the_a0_seeds():
    a0 = json.loads((ROOT / 'config/bench/ironclad-a0-10seed.json').read_text())
    a10 = json.loads((ROOT / 'config/bench/ironclad-a10-10seed.json').read_text())
    assert a10['ascension'] == 10 and a10['character'] == 'IRONCLAD'
    assert a10['seeds'] == a0['seeds'] and len(a10['seeds']) == 10
    assert a10['default_baseline'] in a10['baselines'] and a10['default_baseline'] in baseline.BASELINES
    with pytest.raises(SystemExit):  # the A0 baselines are not allowed for the A10 benchmark
        run_bench.check_baseline(a10, {'name': a0['baselines']['v2']['name'], 'sha256': a0['baselines']['v2']['sha256']})


def test_verify_seed_requires_the_expected_ascension(tmp_path):
    class Inst:
        dir = tmp_path
        run = {'rng': {'seed': 'S1'}, 'game_mode': 'standard', 'ascension': 10}

        def current_run(self, _profile):
            return self.run

    (tmp_path / 'game.log').write_text('old\nEmbarking on a singleplayer IRONCLAD run. Ascension: 10 Seed: S1\n')
    inst = Inst()
    assert run_bench.verify_seed(inst, 'p', 'S1', 0)['verified']
    ev = run_bench.verify_seed(inst, 'p', 'S1', 0, 10)
    assert ev['verified'] and ev['ascension_verified']
    ev = run_bench.verify_seed(inst, 'p', 'S1', 0, 0)
    assert not ev['verified'] and not ev['ascension_verified']
    inst.run = dict(inst.run, ascension=0)  # log and save disagree
    assert not run_bench.verify_seed(inst, 'p', 'S1', 0, 10)['verified']
    assert not run_bench.verify_seed(inst, 'p', 'S2', 0, 0)['verified']


# ---------------------------------------------------------- unlocked save
def test_make_unlocked_save_slugify_and_unlock_idempotent():
    import make_unlocked_save as mus
    assert mus.slugify('IroncladEpoch') == 'IRONCLAD_EPOCH'
    assert mus.slugify('Ironclad2Epoch') == 'IRONCLAD2_EPOCH'
    assert mus.slugify('CustomAndSeedsEpoch') == 'CUSTOM_AND_SEEDS_EPOCH'
    ids = {'epochs': ['NEOW_EPOCH', 'CUSTOM_AND_SEEDS_EPOCH'], 'cards': ['CARD.A', 'CARD.B'], 'relics': ['RELIC.A'],
           'potions': ['POTION.A'], 'events': ['EVENT.A'], 'acts': ['ACT.A'], 'monsters': ['MONSTER.A'],
           'encounters': ['ENCOUNTER.A'], 'characters': ['CHARACTER.IRONCLAD', 'CHARACTER.SILENT'],
           'ftues': ['FTUE_A'], 'agnostic_unlock_count': 18}
    before = {'schema_version': 21, 'epochs': [{'id': 'NEOW_EPOCH', 'obtain_date': 5, 'state': 'revealed'},
                                               {'id': 'CUSTOM_AND_SEEDS_EPOCH', 'obtain_date': 0, 'state': 'obtained'}],
              'discovered_cards': ['CARD.A'], 'character_stats': [], 'total_unlocks': 0}
    after = mus.unlock(before, ids)
    assert before['discovered_cards'] == ['CARD.A']  # input untouched
    assert {e['state'] for e in after['epochs']} == {'revealed'}
    assert after['epochs'][0]['obtain_date'] == 5 and after['epochs'][1]['obtain_date'] == mus.FIXED_OBTAIN_DATE
    assert after['discovered_cards'] == ['CARD.A', 'CARD.B']
    assert [c['max_ascension'] for c in after['character_stats']] == [10, 10]
    assert all(c['preferred_ascension'] == 0 for c in after['character_stats'])
    assert after['total_unlocks'] == 18 and after['pending_character_unlock'] == 'NONE.NONE'
    assert mus.unlock(after, ids) == after
    a10 = mus.unlock(after, ids, preferred_ascension=10)
    assert [c['preferred_ascension'] for c in a10['character_stats']] == [10, 10]
    assert {k: v for k, v in a10.items() if k != 'character_stats'} == {k: v for k, v in after.items() if k != 'character_stats'}
    assert mus.unlock(a10, ids, preferred_ascension=10) == a10
    with pytest.raises(SystemExit):
        mus.unlock(after, ids, preferred_ascension=11)
    with pytest.raises(SystemExit):
        mus.unlock({'schema_version': 20}, ids)


def test_observe_retries_transient_http_failures_but_act_never(monkeypatch):
    import run_bench
    calls, retries = [], []

    def flaky(command, *a, **k):
        calls.append(command)
        if len(calls) < 3:
            raise run_bench.BenchError('sts2_live observe exit 1: ... TimeoutError: timed out')
        return 0, {'ok': True}

    monkeypatch.setattr(run_bench, '_live_once', flaky)
    assert run_bench.live('observe', 'x', 1, retry_wait_s=0,
                          on_retry=lambda c, n, e: retries.append(n)) == (0, {'ok': True})
    assert calls == ['observe'] * 3 and retries == [1, 2]

    calls.clear()
    with pytest.raises(run_bench.BenchError):
        run_bench.live('act', 'x', 1, '{}', 'reason', 'hash', retry_wait_s=0)
    assert calls == ['act']  # an act is never re-sent

    def broken(command, *a, **k):
        calls.append(command)
        raise run_bench.BenchError('sts2_live observe exit 1: KeyError: state')

    calls.clear()
    monkeypatch.setattr(run_bench, '_live_once', broken)
    with pytest.raises(run_bench.BenchError):
        run_bench.live('observe', 'x', 1, retry_wait_s=0)
    assert calls == ['observe']  # real bugs are not masked


def test_unusable_instance_returns_its_seed_instead_of_draining_the_queue(tmp_path, monkeypatch):
    import run_bench

    class Inst:
        def __init__(self, name, broken):
            self.name, self.broken, self.unusable, self.ran = name, broken, None, []

        def stop(self):
            if self.broken:
                raise run_bench.BenchError('PID identity changed; refusing to signal')

    good, bad = Inst('inst1', False), Inst('inst2', True)
    monkeypatch.setattr(run_bench, 'Instance', lambda pool, n, *a: {1: good, 2: bad}[int(n)])
    monkeypatch.setattr(run_bench.baseline_mod, 'load_manifest', lambda path: {'name': 'sts2-bench-baseline-v1', 'sha256': '72f1dd96e0edcff817cd6717368701cfe4e38eb2cd13a90277ebedbe9b697846'})

    def fake_run_seed(inst, seed, *a):
        import time
        time.sleep(0.2)  # keep inst1 busy so inst2 claims a seed
        inst.ran.append(seed)
        return {'seed': seed, 'instance': inst.name}

    monkeypatch.setattr(run_bench, 'run_seed', fake_run_seed)
    monkeypatch.setattr(run_bench, 'game_env', lambda: {})
    out = tmp_path / 'out'
    code = run_bench.main(['--instances', '1,2', '--seeds', 'BENCHA0S01,BENCHA0S02,BENCHA0S03', '--out', str(out)])
    assert code == 0
    assert bad.ran == [] and sorted(good.ran) == ['BENCHA0S01', 'BENCHA0S02', 'BENCHA0S03']
    assert bad.unusable
