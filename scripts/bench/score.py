#!/usr/bin/env python3
"""Aggregate benchmark results into a table.

    python3 scripts/bench/score.py /workspace/private/bench-runs/<run> [<run> ...] [--json]

Reads ``results.jsonl`` (or per-seed ``*/result.json``) from each run directory
and prints one per-seed table plus an aggregate row per (policy, wiki_facts).
"""
import argparse
import json
import statistics
import sys
from collections import Counter, defaultdict
from pathlib import Path


def load_results(run_dir):
    run_dir = Path(run_dir)
    rows = {}
    for path in sorted(run_dir.glob('*/result.json')):
        r = json.loads(path.read_text())
        rows[r['seed']] = r
    results = run_dir / 'results.jsonl'
    if results.exists():
        for line in results.read_text().splitlines():
            if line.strip():
                r = json.loads(line)
                rows[r['seed']] = r
    for r in rows.values():
        r.setdefault('run', run_dir.name)
    return list(rows.values())


def _mean(values):
    values = [v for v in values if isinstance(v, (int, float))]
    return round(statistics.mean(values), 2) if values else None


def aggregate(rows):
    groups = defaultdict(list)
    for r in rows:
        groups[(r.get('policy'), bool(r.get('wiki_facts')))].append(r)
    out = []
    for (policy, facts), rs in sorted(groups.items(), key=lambda kv: (str(kv[0][0]), kv[0][1])):
        frictions = Counter()
        for r in rs:
            frictions.update(r.get('frictions') or {})
        valid = [r for r in rs if not r.get('error')]
        out.append({
            'policy': policy, 'wiki_facts': facts, 'runs': len(rs), 'runner_errors': len(rs) - len(valid),
            'wins': sum(1 for r in valid if r.get('win')),
            'win_rate': round(sum(1 for r in valid if r.get('win')) / len(valid), 3) if valid else None,
            'mean_floor': _mean(r.get('floor') for r in valid),
            'median_floor': statistics.median([r['floor'] for r in valid if isinstance(r.get('floor'), int)])
            if any(isinstance(r.get('floor'), int) for r in valid) else None,
            'mean_act': _mean(r.get('act') for r in valid),
            'boss_kills': sum(r.get('boss_kills') or 0 for r in valid),
            'mean_hp_at_boss_entry': _mean(r.get('hp_at_boss_entry') for r in valid),
            'death_causes': dict(Counter(r.get('death_cause') for r in valid if not r.get('win'))),
            'frictions': dict(frictions), 'frictions_per_run': _mean(r.get('friction_total') for r in rs),
            'mean_decisions': _mean(r.get('decisions') for r in rs),
            'mean_wall_s': _mean(r.get('wall_time_s') for r in rs),
        })
    return out


def _fmt(v):
    if v is None:
        return '-'
    if isinstance(v, bool):
        return 'yes' if v else 'no'
    if isinstance(v, dict):
        return ', '.join(f'{k}:{n}' for k, n in sorted(v.items(), key=lambda kv: -kv[1])) or '-'
    if isinstance(v, list):
        return '/'.join(str(x) for x in v) or '-'
    return str(v)


def table(headers, rows):
    lines = ['| ' + ' | '.join(h for h, _ in headers) + ' |', '|' + '---|' * len(headers)]
    for r in rows:
        lines.append('| ' + ' | '.join(_fmt(r.get(k)).replace('|', '/') for _, k in headers) + ' |')
    return '\n'.join(lines)


PER_SEED = [('seed', 'seed'), ('policy', 'policy'), ('facts', 'wiki_facts'), ('inst', 'instance'), ('end', 'end_reason'),
            ('act', 'act'), ('floor', 'floor'), ('boss kills', 'boss_kills'), ('win', 'win'),
            ('HP@boss', 'boss_entry_hp'), ('death cause', 'death_cause'), ('frictions', 'friction_total'),
            ('decisions', 'decisions'), ('wall s', 'wall_time_s'), ('source', 'source')]
AGG = [('policy', 'policy'), ('facts', 'wiki_facts'), ('runs', 'runs'), ('errors', 'runner_errors'),
       ('wins', 'wins'), ('win rate', 'win_rate'), ('mean floor', 'mean_floor'), ('median floor', 'median_floor'),
       ('mean act', 'mean_act'), ('boss kills', 'boss_kills'), ('mean HP@boss', 'mean_hp_at_boss_entry'),
       ('death causes', 'death_causes'), ('frictions/run', 'frictions_per_run'), ('frictions', 'frictions'),
       ('mean wall s', 'mean_wall_s')]


def main(argv=None):
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument('runs', nargs='+', type=Path)
    p.add_argument('--json', action='store_true')
    a = p.parse_args(argv)
    rows = [r for d in a.runs for r in load_results(d)]
    if not rows:
        print('no results found', file=sys.stderr)
        return 1
    agg = aggregate(rows)
    if a.json:
        print(json.dumps({'runs': rows, 'aggregate': agg}, ensure_ascii=False, indent=2))
        return 0
    rows.sort(key=lambda r: (str(r.get('policy')), bool(r.get('wiki_facts')), r['seed']))
    print('## Per seed\n')
    print(table(PER_SEED, rows))
    print('\n## Aggregate\n')
    print(table(AGG, agg))
    return 0


if __name__ == '__main__':
    sys.exit(main())
