"""Map lookahead: summarize every path from each legal next map node to the act boss.

Pure function of the observation's ``map`` block (``nodes`` with ``children``,
``next_options``, ``boss``), so it works on live observations, saved
``observations.jsonl`` records, and fixtures alike.

Per option (all counts include the option node itself, exclude the boss):

* ``paths``      number of distinct paths to the boss
* ``E R $ ? T``  min-max count of elites / rests / shops / unknowns / treasures over those paths
* ``forced_e``   first row where *every* path is on an elite (unavoidable), else None
* ``first_r``    earliest row a rest can be reached (best path), else None
* ``e_before_r`` True when *every* path meets an elite before its first rest
                 (i.e. you cannot reach a rest first); this is the risky case

Text form (one line per option, ~25 tokens)::

    Look 3,1M: 12p E1-3 R1-2 $0-1 ?1-4 T1 | firstR r5 | forcedE r6 | E<R forced

CLI::

    python3 scripts/bench/map_lookahead.py OBS.json|observations.jsonl|- [--paths N] [--json]
"""
from __future__ import annotations

import argparse
import json
import sys
from functools import lru_cache

KINDS = (('E', 'Elite'), ('R', 'RestSite'), ('$', 'Shop'), ('?', 'Unknown'), ('T', 'Treasure'))
SHORT = {'Monster': 'M', 'Elite': 'E', 'RestSite': 'R', 'Shop': '$', 'Treasure': 'T', 'Unknown': '?',
         'Event': '?', 'Boss': 'B', 'Ancient': 'A'}


def _kind(t):
    return 'Unknown' if t == 'Event' else t


def _key(x):
    if isinstance(x, dict):
        return (x.get('col'), x.get('row'))
    if isinstance(x, (list, tuple)) and len(x) >= 2:
        return (x[0], x[1])
    return None


def find_map(obj):
    """Return the map block from an observation, a state, or a bare map block."""
    if not isinstance(obj, dict):
        return None
    if 'nodes' in obj and ('next_options' in obj or 'current_position' in obj):
        return obj
    for k in ('state', 'map', 'observation', 'obs', 'display'):
        r = find_map(obj.get(k))
        if r:
            return r
    return None


class Graph:
    def __init__(self, m):
        self.m = m
        self.types = {}
        self.kids = {}
        for n in m.get('nodes') or []:
            k = _key(n)
            if k is None:
                continue
            self.types[k] = _kind(n.get('type'))
            self.kids[k] = [c for c in (_key(c) for c in n.get('children') or []) if c is not None]
        boss = m.get('boss') or (m.get('bosses') or [{}])[0] or {}
        self.boss = _key(boss)
        self.boss_row = boss.get('row') if isinstance(boss, dict) else None
        for o in m.get('next_options') or []:  # options may be missing from nodes in partial maps
            k = _key(o)
            if k and k not in self.types:
                self.types[k] = _kind(o.get('type'))
                self.kids[k] = [c for c in (_key(x) for x in o.get('leads_to') or []) if c]

    def terminal(self, k):
        return (k == self.boss or self.types.get(k) == 'Boss' or k not in self.types
                or (self.boss_row is not None and isinstance(k[1], int) and k[1] >= self.boss_row))

    def is_boss(self, k):
        return k == self.boss or self.types.get(k) == 'Boss' or (
            self.boss_row is not None and isinstance(k[1], int) and k[1] >= self.boss_row)

    def children(self, k):
        # map edges always go up a row; ignoring anything else makes cycles impossible
        return [c for c in self.kids.get(k, []) if isinstance(c[1], int) and isinstance(k[1], int) and c[1] > k[1]]


def summarize(m, key):
    g = Graph(m)

    @lru_cache(maxsize=None)
    def agg(k):
        """(paths, {kind: (min,max)}, rows_by_depth frozenset map, first_rest, e_before_r_all)."""
        if g.terminal(k):
            return 0, None
        t = g.types.get(k)
        mine = {s: int(t == full) for s, full in KINDS}
        kids = g.children(k)
        if not kids or all(g.terminal(c) for c in kids):
            return 1, {'rng': {s: (v, v) for s, v in mine.items()},
                       'rows': {k[1]: frozenset({t})},
                       'first_r': k[1] if t == 'RestSite' else None,
                       'risky': t == 'Elite'}
        sub = [s for s in (agg(c) for c in kids if not g.terminal(c)) if s[1] is not None]
        if not sub:
            return 0, None
        paths = sum(s[0] for s in sub)
        rng = {s: (mine[s] + min(x[1]['rng'][s][0] for x in sub), mine[s] + max(x[1]['rng'][s][1] for x in sub))
               for s, _ in KINDS}
        rows = {}
        for x in sub:
            for r, ts in x[1]['rows'].items():
                rows[r] = rows.get(r, frozenset()) | ts
        rows[k[1]] = frozenset({t})
        if t == 'RestSite':
            first_r, risky = k[1], False
        else:
            frs = [x[1]['first_r'] for x in sub if x[1]['first_r'] is not None]
            first_r = min(frs) if frs else None
            risky = True if t == 'Elite' else all(x[1]['risky'] for x in sub)
        return paths, {'rng': rng, 'rows': rows, 'first_r': first_r, 'risky': risky}

    paths, info = agg(key)
    if info is None:
        out = {'node': list(key), 'type': g.types.get(key), 'paths': 0}
        if g.is_boss(key):
            out['boss'] = True
        return out
    forced = sorted(r for r, ts in info['rows'].items() if ts == frozenset({'Elite'}))
    return {'node': list(key), 'type': g.types.get(key), 'paths': paths,
            'counts': {s: list(v) for s, v in info['rng'].items()},
            'forced_e': forced[0] if forced else None, 'forced_e_rows': forced,
            'first_r': info['first_r'], 'e_before_r': info['risky']}


def enumerate_paths(m, key, limit=50):
    g = Graph(m)
    out = []

    def walk(k, acc):
        if len(out) >= limit:
            return
        if g.terminal(k):
            out.append(acc)
            return
        acc = acc + [f'{k[0]},{k[1]}{SHORT.get(g.types.get(k), "?")}']
        kids = g.children(k)
        if not kids:
            out.append(acc)
        for c in kids:
            walk(c, acc)
    walk(key, [])
    return out


def lookahead(m):
    if not m:
        return []
    return [summarize(m, _key(o)) for o in m.get('next_options') or [] if _key(o)]


def fmt(s):
    n = s['node']
    head = f'Look {n[0]},{n[1]}{SHORT.get(s.get("type"), "?")}:'
    if s.get('boss'):
        return head + ' boss'
    if not s.get('paths'):
        return head + ' no path to boss'
    c = s['counts']
    parts = [f'{s["paths"]}p ' + ' '.join(f'{k}{a}' if a == b else f'{k}{a}-{b}' for k, (a, b) in c.items())]
    parts.append(f'firstR r{s["first_r"]}' if s['first_r'] is not None else 'no R')
    if s['forced_e'] is not None:
        parts.append('forcedE r' + ','.join(map(str, s['forced_e_rows'])))
    if s['e_before_r']:
        parts.append('E<R forced')
    return head + ' ' + ' | '.join(parts)


def lines(m):
    out = [fmt(s) for s in lookahead(m)]
    if out:
        out.insert(0, 'Lookahead (per next node, all paths to boss; E/R/$/?/T min-max, row numbers):')
    return out


def _load(path):
    text = sys.stdin.read() if path == '-' else open(path, encoding='utf-8').read()
    try:
        return [json.loads(text)]
    except json.JSONDecodeError:
        return [json.loads(l) for l in text.splitlines() if l.strip()]


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__.split('\n')[0])
    ap.add_argument('obs', help='observation json, observations.jsonl (last map record used), or -')
    ap.add_argument('--paths', type=int, default=0, help='also list up to N paths per option')
    ap.add_argument('--json', action='store_true')
    a = ap.parse_args(argv)
    m = None
    for rec in reversed(_load(a.obs)):
        m = find_map(rec)
        if m:
            break
    if not m:
        print('no map block found', file=sys.stderr)
        return 2
    res = lookahead(m)
    if a.json:
        print(json.dumps(res))
        return 0
    for s in res:
        print(fmt(s))
        if a.paths:
            for p in enumerate_paths(m, tuple(s['node']), a.paths):
                print('   ' + ' '.join(p))
    return 0


if __name__ == '__main__':
    sys.exit(main())
