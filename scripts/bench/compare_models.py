#!/usr/bin/env python3
"""Run the same benchmark seeds across several LLMs and compare them.

    export XAI_API_KEY=...            # never printed or logged
    export STS2_GAME_INSTALL_DIR=/workspace/private/sts2-linux-0.107.1
    python3 scripts/bench/compare_models.py --models grok-4.3,grok-4.20-0309-non-reasoning \\
        --instances 1,2 --seeds BENCHA0S01,BENCHA0S02 [--instances-per-model 1] [--wiki-facts]

    # summary only (no game, no API), e.g. after an interrupted comparison
    python3 scripts/bench/compare_models.py --aggregate /workspace/private/bench-runs/<stamp>-models

Every model gets its own ``run_bench.py`` process with ``--policy py:llm_policy:policy`` and
``LLM_MODEL=<model>``, on a disjoint set of instances of ``--pool`` (default
``/workspace/private/worker-pool-llm``, ports 15641+; the other pools are refused), writing to
``<out>/<model>/``. Models run in parallel while free instances last. The summary
(``summary.md`` / ``summary.json`` in ``<out>``) joins score.py's per-run results with the
per-decision ``llm`` telemetry from the trajectories: floor, act, boss kills, wins, decisions,
tokens, latency and $.
"""
import argparse
import json
import os
import queue
import re
import statistics
import subprocess
import sys
import threading
import time
from datetime import datetime
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import score  # noqa: E402

DEFAULT_POOL = Path('/workspace/private/worker-pool-llm')
MIN_PORT = 15641  # STS2_HEADLESS_PORT_BASE=15640; 15601-15633 belong to other pools


def slug(model):
    return re.sub(r'[^A-Za-z0-9._-]+', '_', model)


def check_pool(pool, numbers):
    if 'worker-pool-llm' not in Path(pool).name:
        raise SystemExit(f'refusing pool {pool}: model comparisons use worker-pool-llm only')
    for n in numbers:
        port_file = Path(pool) / f'inst{n}' / 'port'
        if not port_file.exists():
            raise SystemExit(f'{port_file} missing: create the instance first (STS2_HEADLESS_PORT_BASE=15640)')
        port = int(port_file.read_text().strip())
        if port < MIN_PORT:
            raise SystemExit(f'refusing inst{n}: port {port} < {MIN_PORT} belongs to another pool')


def bench_cmd(a, model_dir, instances):
    cmd = [sys.executable, str(HERE / 'run_bench.py'), '--pool', str(a.pool), '--instances',
           ','.join(map(str, instances)), '--policy', 'py:llm_policy:policy', '--out', str(model_dir)]
    for flag, value in (('--seeds', a.seeds), ('--config', a.config), ('--baseline', a.baseline),
                        ('--max-decisions', a.max_decisions), ('--max-wall-s', a.max_wall_s)):
        if value is not None:
            cmd += [flag, str(value)]
    if a.wiki_facts:
        cmd.append('--wiki-facts')
    return cmd


def run_models(a, out):
    numbers = [int(x) for x in a.instances.split(',') if x.strip()]
    k = a.instances_per_model
    if k < 1 or k > 3 or len(numbers) < k:
        raise SystemExit('--instances-per-model must be 1-3 and <= the number of --instances')
    groups = [numbers[i:i + k] for i in range(0, len(numbers) - k + 1, k)]
    if not a.dry_run:
        check_pool(a.pool, numbers)
        key_env = os.environ.get('LLM_API_KEY_ENV') or 'XAI_API_KEY'
        if not os.environ.get(key_env):
            raise SystemExit(f'{key_env} is not set; export it before a model comparison')
    free = queue.Queue()
    for g in groups:
        free.put(g)
    status = {}

    def one(model):
        group = free.get()
        try:
            model_dir = out / slug(model)
            cmd = bench_cmd(a, model_dir, group)
            env = {**os.environ, 'LLM_MODEL': model}
            if a.reasoning_effort:
                env['LLM_REASONING_EFFORT'] = a.reasoning_effort
            print(f'[{model}] instances {group}: {" ".join(cmd)}', flush=True)
            if a.dry_run:
                status[model] = 'dry-run'
                return
            out.mkdir(parents=True, exist_ok=True)
            began = time.monotonic()
            with open(out / f'{slug(model)}.log', 'w') as log:
                rc = subprocess.run(cmd, env=env, stdout=log, stderr=subprocess.STDOUT, cwd=HERE.parents[1]).returncode
            status[model] = f'exit {rc} after {time.monotonic() - began:.0f}s'
            print(f'[{model}] {status[model]}', flush=True)
        finally:
            free.put(group)

    threads = [threading.Thread(target=one, args=(m,), name=m) for m in a.models]
    for t in threads:
        t.start()
    for t in threads:
        t.join()
    return status


def _mean(values):
    values = [v for v in values if isinstance(v, (int, float))]
    return round(statistics.mean(values), 3) if values else None


def llm_totals(model_dir):
    t = {'model': None, 'decisions': 0, 'llm_decisions': 0, 'prompt_tokens': 0, 'cached_tokens': 0,
         'output_tokens': 0, 'reasoning_tokens': 0, 'cost_usd': 0.0, 'cost_known': True, 'retries': 0,
         'policy_errors': 0}
    latencies, policy_lat, act_lat = [], [], []
    for traj in sorted(Path(model_dir).glob('*/trajectory.jsonl')):
        for line in traj.read_text().splitlines():
            if not line.strip():
                continue
            r = json.loads(line)
            if r.get('type') == 'friction' and r.get('kind') == 'policy_error':
                t['policy_errors'] += 1
            if r.get('type') != 'decision':
                continue
            t['decisions'] += 1
            policy_lat.append((r.get('latency_s') or {}).get('policy'))
            act_lat.append((r.get('latency_s') or {}).get('act'))
            llm = r.get('llm')
            if not isinstance(llm, dict):
                continue
            t['llm_decisions'] += 1
            t['model'] = t['model'] or llm.get('model')
            for part in (llm, llm.get('carried_from_failed') or {}):
                for key in ('prompt_tokens', 'cached_tokens', 'output_tokens', 'reasoning_tokens'):
                    t[key] += int(part.get(key) or 0)
                if part.get('cost_usd') is None:
                    t['cost_known'] = t['cost_known'] and part is not llm
                else:
                    t['cost_usd'] += float(part['cost_usd'])
            t['retries'] += max(0, int(llm.get('attempts') or 1) - 1)
            latencies.append(llm.get('latency_s'))
    t['cost_usd'] = round(t['cost_usd'], 4) if t['cost_known'] else None
    t['mean_llm_latency_s'] = _mean(latencies)
    t['p50_llm_latency_s'] = round(statistics.median([x for x in latencies if x is not None]), 3) \
        if any(x is not None for x in latencies) else None
    t['mean_policy_s'] = _mean(policy_lat)
    t['mean_act_s'] = _mean(act_lat)
    return t


def summarize(out):
    rows = []
    for model_dir in sorted(p for p in Path(out).iterdir() if p.is_dir()):
        results = score.load_results(model_dir)
        if not results and not list(model_dir.glob('*/trajectory.jsonl')):
            continue
        agg = (score.aggregate(results) or [{}])[0]
        tel = llm_totals(model_dir)
        rows.append({'model': tel['model'] or model_dir.name, 'dir': model_dir.name, 'runs': agg.get('runs', 0),
                     'runner_errors': agg.get('runner_errors'), 'wins': agg.get('wins'),
                     'mean_floor': agg.get('mean_floor'), 'median_floor': agg.get('median_floor'),
                     'mean_act': agg.get('mean_act'), 'boss_kills': agg.get('boss_kills'),
                     'death_causes': agg.get('death_causes'), **{k: v for k, v in tel.items() if k != 'model'},
                     'tokens_per_decision': round((tel['prompt_tokens'] + tel['output_tokens'])
                                                  / tel['llm_decisions']) if tel['llm_decisions'] else None,
                     'cost_per_run_usd': round(tel['cost_usd'] / agg['runs'], 4)
                     if tel['cost_usd'] is not None and agg.get('runs') else None})
    return rows


COLUMNS = [('model', 'model'), ('runs', 'runs'), ('wins', 'wins'), ('mean_floor', 'floor'),
           ('mean_act', 'act'), ('boss_kills', 'boss kills'), ('decisions', 'decisions'),
           ('policy_errors', 'policy err'), ('retries', 'retries'), ('prompt_tokens', 'in tok'),
           ('cached_tokens', 'cached'), ('output_tokens', 'out tok'), ('tokens_per_decision', 'tok/dec'),
           ('mean_llm_latency_s', 'llm s/dec'), ('mean_act_s', 'act s/dec'), ('cost_usd', '$ total'),
           ('cost_per_run_usd', '$ / run')]


def markdown(rows):
    lines = ['| ' + ' | '.join(h for _, h in COLUMNS) + ' |', '|' + '---|' * len(COLUMNS)]
    for r in rows:
        lines.append('| ' + ' | '.join('-' if r.get(k) is None else str(r.get(k)) for k, _ in COLUMNS) + ' |')
    return '\n'.join(lines) + '\n'


def write_summary(out, status=None):
    rows = summarize(out)
    text = f'# Model comparison: {Path(out).name}\n\n' + markdown(rows)
    if status:
        text += '\n' + '\n'.join(f'- {m}: {s}' for m, s in status.items()) + '\n'
    (Path(out) / 'summary.md').write_text(text)
    (Path(out) / 'summary.json').write_text(json.dumps(rows, indent=2, ensure_ascii=False) + '\n')
    return text


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument('--models', help='comma-separated model ids (see llm_policy.py --list-models)')
    ap.add_argument('--aggregate', type=Path, metavar='OUT', help='only (re)build the summary of OUT')
    ap.add_argument('--pool', type=Path, default=DEFAULT_POOL)
    ap.add_argument('--instances', default='1', help='instance numbers of --pool shared by all models')
    ap.add_argument('--instances-per-model', type=int, default=1)
    ap.add_argument('--seeds', help='comma-separated config seeds (same for every model)')
    ap.add_argument('--config', type=Path)
    ap.add_argument('--baseline')
    ap.add_argument('--wiki-facts', action='store_true')
    ap.add_argument('--reasoning-effort', help='LLM_REASONING_EFFORT for every model')
    ap.add_argument('--max-decisions', type=int)
    ap.add_argument('--max-wall-s', type=float)
    ap.add_argument('--out', type=Path)
    ap.add_argument('--dry-run', action='store_true', help='print the run_bench commands only')
    a = ap.parse_args(argv)
    if a.aggregate:
        print(write_summary(a.aggregate))
        return 0
    if not a.models:
        ap.error('--models or --aggregate is required')
    a.models = [m.strip() for m in a.models.split(',') if m.strip()]
    out = a.out or Path('/workspace/private/bench-runs') / f'{datetime.now():%Y%m%d-%H%M%S}-models'
    status = run_models(a, out)
    if a.dry_run:
        return 0
    print(write_summary(out, status))
    return 0 if all(s.startswith('exit 0') for s in status.values()) else 1


if __name__ == '__main__':
    sys.exit(main())
