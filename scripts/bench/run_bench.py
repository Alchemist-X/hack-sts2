#!/usr/bin/env python3
"""Fixed-seed STS2 benchmark runner (Ironclad A0 by default).

Per seed, on a dedicated worker instance:
  stop -> restore the frozen save baseline (sha256-verified) -> launch ->
  menu_select main/singleplayer/standard/<character>/embark(seed) ->
  verify the seed the game actually used -> loop {observe -> policy -> act}
  through ``scripts/sts2_live.py`` (the same harness agents use) ->
  metrics (game history file + trajectory) -> stop.

Trajectory schema: docs/benchmark.md.  Run data stays outside the repo
(default /workspace/private/bench-runs).
"""
import argparse
import json
import os
import queue
import re
import signal
import subprocess
import sys
import threading
import time
import urllib.request
from datetime import datetime, timezone
from pathlib import Path

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[1]
sys.path.insert(0, str(HERE))
import baseline as baseline_mod  # noqa: E402
import metrics as metrics_mod  # noqa: E402
import policies  # noqa: E402

LIVE = ROOT / 'scripts' / 'sts2_live.py'
SCHEMA = 'sts2-bench-trajectory/v1'
DEFAULT_LAUNCHER = '/workspace/hack-sts2-cloud/scripts/headless_launch.sh'
SEED_CHARS = set('0123456789ABCDEFGHJKLMNPQRSTUVWXYZ')
PROFILE_REL = Path('home/.local/share/SlayTheSpire2')
_print_lock = threading.Lock()
STOP = threading.Event()  # set on SIGINT/SIGTERM: finish the current step, stop instances


def log(*parts):
    with _print_lock:
        print(datetime.now().strftime('%H:%M:%S'), *parts, flush=True)


def canonical_seed(seed):
    seed = seed.strip().upper().replace('O', '0').replace('I', '1')
    if not seed or set(seed) - SEED_CHARS:
        raise ValueError(f'invalid seed {seed!r}')
    return seed


class BenchError(RuntimeError):
    pass


# --------------------------------------------------------------- instances
class Instance:
    def __init__(self, pool, number, launcher, env=None):
        self.pool, self.number, self.launcher = Path(pool), int(number), launcher
        self.dir = self.pool / f'inst{self.number}'
        self.port = int((self.dir / 'port').read_text().strip())
        self.env = {**os.environ, **(env or {})}
        if self.port in (15601, 15602, 15603) or 'worker-pool-v2' in str(self.pool):
            raise BenchError('refusing to use worker-pool-v2 / ports 15601-15603 for benchmarks')

    @property
    def name(self):
        return self.dir.name

    def _launcher(self, *extra):
        cmd = ['bash', self.launcher, str(self.number), '--base', str(self.pool), *extra]
        proc = subprocess.run(cmd, capture_output=True, text=True, env=self.env, cwd=Path(self.launcher).parents[1])
        if proc.returncode != 0:
            raise BenchError(f'{" ".join(cmd)} failed: {proc.stderr.strip()[-800:] or proc.stdout.strip()[-800:]}')
        return proc.stdout

    def stop(self):
        self._launcher('--stop')

    def launch(self, wait_s=120):
        self._launcher('--wait-s', str(wait_s))

    def history_dir(self, active_profile):
        return self.dir / PROFILE_REL / active_profile / 'saves' / 'history'

    def current_run(self, active_profile):
        path = self.dir / PROFILE_REL / active_profile / 'saves' / 'current_run.save'
        return json.loads(path.read_text()) if path.exists() else None


def api(port, action=None, timeout=15):
    url = f'http://127.0.0.1:{port}/api/v1/singleplayer'
    data = json.dumps(action).encode() if action is not None else None
    req = urllib.request.Request(url, data=data, method='POST' if action is not None else 'GET',
                                 headers={'Content-Type': 'application/json'})
    with urllib.request.urlopen(req, timeout=timeout) as resp:
        return json.loads(resp.read())


def wait_for(port, predicate, timeout_s, what):
    deadline, last = time.monotonic() + timeout_s, None
    while time.monotonic() < deadline:
        try:
            last = api(port)
            if predicate(last):
                return last
        except OSError:
            pass
        time.sleep(0.5)
    raise BenchError(f'timed out waiting for {what}; last state_type={last and last.get("state_type")} '
                     f'menu_screen={last and last.get("menu_screen")}')


def _option_names(state):
    return [o.get('name') if isinstance(o, dict) else o for o in state.get('options') or []]


def start_seeded_run(port, seed, character='IRONCLAD', ascension=0, timeout_s=90):
    """Drive the real menus to a seeded standard run; returns the embark response."""
    seed = canonical_seed(seed)
    deadline = time.monotonic() + timeout_s
    picked = False
    while time.monotonic() < deadline:
        state = api(port)
        if state.get('state_type') != 'menu':
            raise BenchError(f'expected the main menu, found {state.get("state_type")} (restore the baseline first)')
        screen = state.get('menu_screen')
        if screen == 'main':
            api(port, {'action': 'menu_select', 'option': 'singleplayer'})
        elif screen == 'singleplayer':
            api(port, {'action': 'menu_select', 'option': 'standard'})
        elif screen == 'character_select':
            lobby = state.get('lobby') or {}
            if lobby.get('game_mode') not in (None, 'standard'):
                raise BenchError(f'character select is in {lobby.get("game_mode")} mode, not standard')
            if lobby.get('ascension') not in (None, ascension):
                raise BenchError(f'lobby ascension {lobby.get("ascension")} != {ascension}; baseline drifted?')
            if not picked:
                reply = api(port, {'action': 'menu_select', 'option': character})
                if reply.get('status') != 'ok':
                    raise BenchError(f'could not select {character}: {reply}')
                picked = True
            else:
                reply = api(port, {'action': 'menu_select', 'option': 'embark', 'seed': seed})
                if reply.get('status') != 'ok' or reply.get('seed') != seed:
                    raise BenchError(f'seeded embark refused (needs the seeded-standard-embark MCP patch?): {reply}')
                return reply
        else:
            raise BenchError(f'unexpected menu screen {screen!r} options={_option_names(state)}')
        time.sleep(1.0)
    raise BenchError('timed out starting the seeded run')


def verify_seed(inst, active_profile, seed, log_offset):
    """The seed the game used, from its own log line and the run save."""
    evidence = {}
    game_log = inst.dir / 'game.log'
    if game_log.exists():
        with open(game_log, 'rb') as f:
            f.seek(log_offset)
            text = f.read().decode('utf-8', 'replace')
        found = re.findall(r'Embarking on a singleplayer (\w+) run\. Ascension: (\d+) Seed: (\w+)', text)
        if found:
            evidence['log'] = {'character': found[-1][0], 'ascension': int(found[-1][1]), 'seed': found[-1][2]}
    saved = inst.current_run(active_profile)
    if saved:
        evidence['save'] = {'seed': (saved.get('rng') or {}).get('seed'), 'game_mode': saved.get('game_mode'),
                            'ascension': saved.get('ascension')}
    seeds = {v.get('seed') for v in evidence.values()}
    evidence['verified'] = bool(evidence) and seeds == {seed}
    return evidence


# ------------------------------------------------------------------ harness
def live(command, run_dir, port, *args, extra=()):
    cmd = [sys.executable, str(LIVE), command, *args, '--run-dir', str(run_dir), '--port', str(port), *extra]
    proc = subprocess.run(cmd, capture_output=True, text=True, cwd=ROOT)
    lines = [l for l in proc.stdout.splitlines() if l.strip()]
    payload = None
    if lines:
        try:
            payload = json.loads(lines[-1])
        except json.JSONDecodeError:
            payload = None
    if payload is None:
        raise BenchError(f'sts2_live {command} exit {proc.returncode}: {(proc.stderr or proc.stdout).strip()[-800:]}')
    return proc.returncode, payload


def summarize_state(obs):
    state = obs.get('state') or {}
    run, player, battle = state.get('run') or {}, state.get('player') or {}, state.get('battle') or {}
    enemies = [e.get('id') or e.get('name') for e in battle.get('enemies') or [] if e.get('is_alive', True)]
    return {'state_type': state.get('state_type'), 'act': run.get('act'), 'floor': run.get('floor'),
            'hp': player.get('hp'), 'max_hp': player.get('max_hp'), 'gold': player.get('gold'),
            'round': battle.get('round'), 'enemies': enemies or None}


class Recorder:
    def __init__(self, path):
        self.path = Path(path)
        self.path.parent.mkdir(parents=True, exist_ok=True)
        self.records = []

    def write(self, record):
        record = {'schema': SCHEMA, **record}
        self.records.append(record)
        with open(self.path, 'a', encoding='utf-8') as f:
            f.write(json.dumps(record, ensure_ascii=False) + '\n')


def play(inst, seed, run_dir, rec, policy_name, policy, args, began):
    """Observe/decide/act until game over or a limit; returns the end reason."""
    limits = args.limits
    index, consecutive_errors, avoid, last_error = 0, 0, [], None
    while True:
        if STOP.is_set():
            return 'interrupted'
        if index >= limits['max_decisions']:
            return 'max_decisions'
        if time.monotonic() - began > limits['max_wall_s']:
            return 'max_wall_s'
        code, obs = live('observe', run_dir, inst.port, extra=args.live_flags)
        if obs.get('warning_pending'):
            live('reconcile', run_dir, inst.port, 'bench runner: auto-reconcile after re-observing', extra=args.live_flags)
            rec.write({'type': 'friction', 'kind': 'pending_unresolved', 'index': index})
            continue
        if not obs.get('settled', True):
            rec.write({'type': 'friction', 'kind': 'not_settled', 'index': index, 'stage': 'observe'})
        st = (obs.get('state') or {}).get('state_type')
        if st == 'game_over':
            rec.write({'type': 'terminal', 'index': index, 'state_ref': {'decision_hash': obs.get('hash')},
                       **summarize_state(obs), 't_wall_s': round(time.monotonic() - began, 3)})
            return 'game_over'
        if st == 'menu':
            return 'returned_to_menu'
        if not obs.get('legal_actions'):
            consecutive_errors += 1
            if consecutive_errors > limits['max_consecutive_errors'] * 5:
                return 'no_legal_actions'
            time.sleep(1.0)
            continue
        context = {'seed': seed, 'index': index, 'policy': policy_name, 'avoid': avoid, 'last_error': last_error,
                   'wiki_facts_enabled': bool(args.wiki_facts)}
        try:
            reply, policy_latency = policies.decide(policy, obs, context)
        except (policies.PolicyError, subprocess.TimeoutExpired) as error:
            rec.write({'type': 'friction', 'kind': 'policy_error', 'index': index, 'message': str(error)})
            consecutive_errors += 1
            avoid = []  # let the policy retry from the full action set
            if consecutive_errors >= limits['max_consecutive_errors']:
                return 'policy_error'
            continue
        action_json = json.dumps(reply['action'], ensure_ascii=False)
        t0 = time.monotonic()
        code, result = live('act', run_dir, inst.port, action_json, reply['reason'], obs['hash'], extra=args.live_flags)
        act_latency = time.monotonic() - t0
        post = result.get('observation') or {}
        steps = result.get('steps') or []
        outcome = {'ok': bool(result.get('ok')), 'error': None if result.get('ok') else result.get('error'),
                   'message': None if result.get('ok') else (result.get('message') or '')[:500],
                   'steps': len(steps), 'harness_followups': [s['harness_followup'] for s in steps if s.get('harness_followup')],
                   'warnings': result.get('warnings') or [], 'authority_verified': result.get('authority_verified'),
                   'post_hash': post.get('hash'), 'post_state_type': (post.get('state') or {}).get('state_type'),
                   'post_hp': ((post.get('state') or {}).get('player') or {}).get('hp')}
        rec.write({'type': 'decision', 'seed': seed, 'instance': inst.name, 'index': index,
                   't_wall_s': round(time.monotonic() - began, 3),
                   'state_ref': {'run_dir': str(run_dir), 'observations': 'observations.jsonl',
                                 'decision_hash': obs['hash']},
                   **summarize_state(obs),
                   'legal_actions': [a.get('ref') for a in obs.get('legal_actions') or []],
                   'wiki_facts_chars': len(obs['wiki_facts']) if isinstance(obs.get('wiki_facts'), str) else None,
                   'chosen': reply['action'], 'rationale': reply['reason'], 'policy': policy_name,
                   'outcome': outcome, 'latency_s': {'policy': round(policy_latency, 3), 'act': round(act_latency, 3)}})
        index += 1
        if outcome['ok']:
            consecutive_errors, avoid, last_error = 0, [], None
            continue
        consecutive_errors += 1
        last_error = {'error': outcome['error'], 'message': outcome['message']}
        if outcome['error'] in ('action_unresolved', 'pending_unresolved'):
            live('reconcile', run_dir, inst.port, f'bench runner: auto-reconcile after {outcome["error"]}',
                 extra=args.live_flags)
        if outcome['error'] in ('unresolvable_action', 'action_rejected', 'bad_request', 'selection_incomplete') \
                and post.get('hash') == obs['hash'] and isinstance(reply['action'], dict):
            avoid.append(reply['action'])  # same frame: let the policy pick something else
        if consecutive_errors >= limits['max_consecutive_errors']:
            return 'harness_stuck'


def run_seed(inst, seed, args, policy_name, policy, out_root):
    seed = canonical_seed(seed)
    seed_dir = out_root / seed
    if seed_dir.exists():
        raise BenchError(f'{seed_dir} exists; use a new --out')
    run_dir = seed_dir / 'live'
    rec = Recorder(seed_dir / 'trajectory.jsonl')
    began, wall_start = time.monotonic(), datetime.now(timezone.utc)
    manifest = baseline_mod.load_manifest(args.baseline)
    end_reason, history, seed_evidence, restore_info, error = None, None, None, None, None
    try:
        log(inst.name, seed, 'stop + restore baseline')
        inst.stop()
        restore_info = baseline_mod.restore(inst.dir, args.baseline)
        log_offset = (inst.dir / 'game.log').stat().st_size if (inst.dir / 'game.log').exists() else 0
        epoch_before = time.time()
        inst.launch()
        wait_for(inst.port, lambda s: s.get('state_type') == 'menu' and s.get('menu_screen') == 'main', 120, 'main menu')
        embark = start_seeded_run(inst.port, seed, args.config['character'], args.config['ascension'])
        first = wait_for(inst.port, lambda s: s.get('state_type') not in ('menu', None, 'unknown'), 90, 'first run frame')
        seed_evidence = verify_seed(inst, manifest['active_profile'], seed, log_offset)
        if not seed_evidence['verified']:
            raise BenchError(f'seed not verified: {seed_evidence}')
        rec.write({'type': 'run_start', 'seed': seed, 'instance': inst.name, 'port': inst.port,
                   'started_at': wall_start.isoformat(), 'policy': policy_name, 'wiki_facts': bool(args.wiki_facts),
                   'character': args.config['character'], 'ascension': args.config['ascension'],
                   'benchmark': args.config['name'], 'baseline': restore_info, 'embark': embark,
                   'seed_evidence': seed_evidence, 'first_state_type': first.get('state_type'),
                   'harness': {'mode': args.mode, 'allow_unverified_authority': args.allow_unverified_authority}})
        start = [sys.executable, str(LIVE), 'start', '--run-dir', str(run_dir), '--port', str(inst.port),
                 '--plan', str(args.plan), '--mode', args.mode, *sum((['--memory', str(m)] for m in args.memory), []),
                 *(['--wiki-facts'] if args.wiki_facts else []), *args.live_flags]
        proc = subprocess.run(start, capture_output=True, text=True, cwd=ROOT)
        if proc.returncode != 0:
            raise BenchError(f'sts2_live start failed: {proc.stderr.strip()[-500:]}')
        end_reason = play(inst, seed, run_dir, rec, policy_name, policy, args, began)
        found = metrics_mod.find_history(inst.history_dir(manifest['active_profile']), seed, not_before=epoch_before)
        history = found[1] if found else None
        if found:
            rec.write({'type': 'history', 'file': found[0].name, 'win': history.get('win'),
                       'killed_by_encounter': history.get('killed_by_encounter')})
    except Exception as exc:  # recorded, never silently dropped
        error = f'{type(exc).__name__}: {exc}'
        end_reason = end_reason or 'runner_error'
        rec.write({'type': 'friction', 'kind': 'harness_crash', 'message': error[:1000]})
        log(inst.name, seed, 'ERROR', error[:300])
    finally:
        try:
            inst.stop()
        except BenchError as exc:
            log(inst.name, 'stop failed', exc)
    wall = round(time.monotonic() - began, 1)
    result = {'seed': seed, 'instance': inst.name, 'policy': policy_name, 'wiki_facts': bool(args.wiki_facts),
              'benchmark': args.config['name'], 'error': error, 'seed_evidence': seed_evidence,
              **metrics_mod.run_metrics(rec.records, history, wall_time_s=wall, end_reason=end_reason)}
    rec.write({'type': 'run_end', 'metrics': result})
    (seed_dir / 'result.json').write_text(json.dumps(result, ensure_ascii=False, indent=2) + '\n')
    log(inst.name, seed, f"done: {end_reason} floor={result.get('floor')} act={result.get('act')} "
        f"win={result.get('win')} frictions={result.get('friction_total')} wall={wall}s")
    return result


# -------------------------------------------------------------- determinism
def fingerprint(inst, seed, args, out_dir):
    """Restore, start ``seed`` and capture Neow, the act-1 map and the first combat."""
    manifest = baseline_mod.load_manifest(args.baseline)
    inst.stop()
    baseline_mod.restore(inst.dir, args.baseline)
    log_offset = (inst.dir / 'game.log').stat().st_size if (inst.dir / 'game.log').exists() else 0
    inst.launch()
    wait_for(inst.port, lambda s: s.get('state_type') == 'menu' and s.get('menu_screen') == 'main', 120, 'main menu')
    start_seeded_run(inst.port, seed, args.config['character'], args.config['ascension'])
    wait_for(inst.port, lambda s: s.get('state_type') not in ('menu', None, 'unknown'), 90, 'first run frame')
    evidence = verify_seed(inst, manifest['active_profile'], seed, log_offset)
    run_dir = out_dir / 'live'
    subprocess.run([sys.executable, str(LIVE), 'start', '--run-dir', str(run_dir), '--port', str(inst.port),
                    '--plan', str(args.plan), '--memory', str(args.memory[0]), *args.live_flags],
                   check=True, capture_output=True, cwd=ROOT)
    fp = {'seed_evidence': evidence}
    for _ in range(40):
        _, obs = live('observe', run_dir, inst.port, extra=args.live_flags)
        state = obs.get('state') or {}
        st = state.get('state_type')
        if st == 'event' and 'neow' not in fp:
            fp['neow'] = [o.get('title') for o in (state.get('event') or {}).get('options') or []]
        if st == 'map' and 'map' not in fp:
            fp['map'] = sorted((n['col'], n['row'], n['type']) for n in (state.get('map') or {}).get('nodes') or [])
        if st in metrics_mod.COMBAT_TYPES:
            battle, player = state.get('battle') or {}, state.get('player') or {}
            fp['first_combat'] = {'room': st, 'enemies': [(e.get('id'), e.get('hp')) for e in battle.get('enemies') or []],
                                  'intents': [[i.get('type') for i in e.get('intents') or []] for e in battle.get('enemies') or []],
                                  'hand': [c.get('id') for c in player.get('hand') or []]}
            break
        reply, _ = policies.decide(policies.stub_policy, obs, {'seed': seed})
        live('act', run_dir, inst.port, json.dumps(reply['action']), reply['reason'], obs['hash'], extra=args.live_flags)
    inst.stop()
    return fp


# ---------------------------------------------------------------------- cli
def parse_args(argv=None):
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument('--config', type=Path, default=ROOT / 'config/bench/ironclad-a0-10seed.json')
    p.add_argument('--pool', type=Path, default=Path('/workspace/private/worker-pool-bench'))
    p.add_argument('--instances', default='1', help='comma-separated instance numbers (max 3)')
    p.add_argument('--seeds', help='comma-separated subset of config seeds (default: all)')
    p.add_argument('--policy', default='stub', help='stub | cmd:<command> | py:<module>:<func>')
    p.add_argument('--wiki-facts', action='store_true', help='include wiki_facts in every observation')
    p.add_argument('--baseline', default=baseline_mod.DEFAULT_BASELINE,
                   help=f'save baseline: {" | ".join(baseline_mod.BASELINES)} (default '
                        f'{baseline_mod.DEFAULT_BASELINE}), a baseline dir or a manifest file')
    p.add_argument('--launcher', default=os.environ.get('STS2_BENCH_LAUNCHER', DEFAULT_LAUNCHER))
    p.add_argument('--out', type=Path, help='output dir (default /workspace/private/bench-runs/<timestamp>-<policy>)')
    p.add_argument('--plan', type=Path, default=ROOT / 'config/bench/stub-plan.json')
    p.add_argument('--memory', type=Path, action='append')
    p.add_argument('--mode', choices=['practice', 'evaluation'], default='practice')
    p.add_argument('--require-verified-authority', action='store_true',
                   help='do not pass --allow-unverified-authority (practice mode otherwise allows it, recorded)')
    p.add_argument('--max-decisions', type=int)
    p.add_argument('--max-wall-s', type=float)
    p.add_argument('--determinism', metavar='SEED', help='start SEED twice on the first instance and compare')
    p.add_argument('--starts', type=int, default=2)
    a = p.parse_args(argv)
    a.config = json.loads(a.config.read_text())
    a.memory = a.memory or [ROOT / 'config/bench/bench-memory.md']
    a.limits = dict(a.config.get('limits') or {})
    a.limits.setdefault('max_decisions', 3000)
    a.limits.setdefault('max_wall_s', 5400)
    a.limits.setdefault('max_consecutive_errors', 6)
    if a.max_decisions:
        a.limits['max_decisions'] = a.max_decisions
    if a.max_wall_s:
        a.limits['max_wall_s'] = a.max_wall_s
    a.allow_unverified_authority = a.mode == 'practice' and not a.require_verified_authority
    a.live_flags = ['--allow-unverified-authority'] if a.allow_unverified_authority else []
    a.instance_numbers = [int(x) for x in a.instances.split(',') if x.strip()]
    if not 1 <= len(a.instance_numbers) <= 3:
        raise SystemExit('use 1-3 instances')
    return a


def game_env():
    env = {}
    install = os.environ.get('STS2_GAME_INSTALL_DIR', '/workspace/private/sts2-linux-0.107.1')
    env.setdefault('STS2_GAME_INSTALL_DIR', install)
    env.setdefault('STS2_GAME_EXECUTABLE', os.environ.get('STS2_GAME_EXECUTABLE', f'{install}/SlayTheSpire2'))
    env.setdefault('STS2_GAME_DATA_DIR', os.environ.get('STS2_GAME_DATA_DIR', f'{install}/data_sts2_linuxbsd_x86_64'))
    return env


def check_baseline(config, manifest):
    """The config pins every baseline it accepts (name -> sha256); refuse anything else."""
    pinned = dict(config.get('baselines') or {})
    if config.get('baseline'):
        pinned.setdefault(config['baseline'].get('name'), config['baseline'])
    if not pinned:
        return
    allowed = {b['name']: b['sha256'] for b in pinned.values()}
    want = allowed.get(manifest.get('name'))
    if want is None:
        raise SystemExit(f'config does not allow baseline {manifest.get("name")!r} (allowed: {sorted(allowed)})')
    if want != manifest['sha256']:
        raise SystemExit(f'config expects baseline {manifest.get("name")} {want}, found {manifest["sha256"]}')


def main(argv=None):
    a = parse_args(argv)
    manifest = baseline_mod.load_manifest(a.baseline)
    check_baseline(a.config, manifest)
    instances = [Instance(a.pool, n, a.launcher, game_env()) for n in a.instance_numbers]
    stamp = datetime.now().strftime('%Y%m%d-%H%M%S')
    policy_name, policy = policies.load_policy(a.policy)
    label = re.sub(r'[^A-Za-z0-9_.-]+', '_', policy_name)[:40]
    if a.determinism:
        out = a.out or Path('/workspace/private/bench-runs') / f'{stamp}-determinism'
        prints = []
        for k in range(a.starts):
            log(instances[0].name, a.determinism, f'determinism start {k + 1}/{a.starts}')
            prints.append(fingerprint(instances[0], canonical_seed(a.determinism), a, out / f'start{k + 1}'))
        same = all({k: v for k, v in fp.items() if k != 'seed_evidence'} ==
                   {k: v for k, v in prints[0].items() if k != 'seed_evidence'} for fp in prints)
        report = {'seed': canonical_seed(a.determinism), 'starts': a.starts, 'identical': same, 'fingerprints': prints}
        out.mkdir(parents=True, exist_ok=True)
        (out / 'determinism.json').write_text(json.dumps(report, indent=2) + '\n')
        print(json.dumps({'identical': same, 'report': str(out / 'determinism.json')}))
        return 0 if same else 1
    seeds = [canonical_seed(s) for s in (a.seeds.split(',') if a.seeds else a.config['seeds'])]
    unknown = set(seeds) - {canonical_seed(s) for s in a.config['seeds']}
    if unknown:
        raise SystemExit(f'seeds not in the benchmark config: {sorted(unknown)}')
    out = a.out or Path('/workspace/private/bench-runs') / f'{stamp}-{label}{"-facts" if a.wiki_facts else ""}'
    out.mkdir(parents=True, exist_ok=False)
    (out / 'bench.json').write_text(json.dumps({
        'benchmark': a.config['name'], 'config': a.config, 'policy': policy_name, 'wiki_facts': bool(a.wiki_facts),
        'instances': [i.name for i in instances], 'seeds': seeds,
        'baseline': manifest.get('name'), 'baseline_sha256': manifest['sha256'],
        'limits': a.limits, 'mode': a.mode, 'started_at': datetime.now(timezone.utc).isoformat()}, indent=2) + '\n')
    todo = queue.Queue()
    for seed in seeds:
        todo.put(seed)
    results, lock = [], threading.Lock()

    def worker(inst):
        while True:
            if STOP.is_set():
                return
            try:
                seed = todo.get_nowait()
            except queue.Empty:
                return
            result = run_seed(inst, seed, a, policy_name, policy, out)
            with lock:
                results.append(result)
                with open(out / 'results.jsonl', 'a') as f:
                    f.write(json.dumps(result, ensure_ascii=False) + '\n')

    threads = [threading.Thread(target=worker, args=(i,), name=i.name) for i in instances]
    for t in threads:
        t.start()
    def interrupt(signum, _frame):
        log(f'signal {signum}: finishing current steps and stopping instances')
        STOP.set()
    signal.signal(signal.SIGTERM, interrupt)
    signal.signal(signal.SIGINT, interrupt)
    for t in threads:
        while t.is_alive():
            t.join(1.0)
    log('all done:', out)
    print(json.dumps({'out': str(out), 'runs': len(results)}))
    return 0


if __name__ == '__main__':
    sys.exit(main())
