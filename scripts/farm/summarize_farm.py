#!/usr/bin/env python3
"""Summarise an E3 coverage farm: numbers only, never trajectories.

Reads the recorder sessions under <farm-root>/worker-*/sessions/ and the
driver's runs.jsonl / rooms.jsonl / events.jsonl, and prints one JSON object:
sessions, runs, wins, floors-reached distribution, rooms by type, combats,
distinct encounters / cards / relics / events / potions touched, anchors
(run_saved .save copies by anchor kind, replay_archived .mcr copies), crashes
and restarts.

usage: summarize_farm.py <farm-root> [--validate validate.json]
"""
import argparse
import collections
import gzip
import json
from pathlib import Path


def jsonl(path):
    if not path.exists() and path.suffix == '.gz' and path.with_suffix('').exists():
        path = path.with_suffix('')  # incomplete (killed) sessions stay uncompressed
    if not path.exists():
        return []
    opener = gzip.open if path.suffix == '.gz' else open
    with opener(path, 'rt') as f:
        return [json.loads(line) for line in f if line.strip()]


def session_stats(sdir):
    man = json.loads((sdir / 'manifest.json').read_text())
    out = {'session': sdir.name, 'run': man.get('run') or {}, 'result': man.get('result'),
           'incomplete': man.get('incomplete'), 'counts': man.get('counts'),
           'degraded_hooks': len(man.get('degraded_hooks') or []),
           'perf': (man.get('perf') or {}).get('snapshot_build_ms')}
    rooms, encounters, events_seen, anchors = collections.Counter(), set(), set(), collections.Counter()
    max_act = max_floor = 0
    win = None
    for ev in jsonl(sdir / 'events.jsonl.gz'):
        entry, data = ev.get('entry'), ev.get('data') or {}
        if entry == 'room_entered':
            rooms[data.get('room_type')] += 1
            mid = str(data.get('room_model_id') or '')
            if mid.startswith('ENCOUNTER.'):
                encounters.add(mid)
            elif mid.startswith('EVENT.'):
                events_seen.add(mid)
        elif entry == 'run_saved':
            anchors[f'save:{data.get("anchor_kind")}'] += 1
        elif entry == 'replay_archived':
            anchors['replay'] += 1
        elif entry == 'run_ended':
            win = data.get('win')
    cards, relics, potions = set(), set(), set()
    states = 0
    for row in jsonl(sdir / 'states.jsonl.gz'):
        states += 1
        st = row.get('state') or {}
        run = st.get('run') or {}
        if isinstance(run.get('act'), int):
            max_act = max(max_act, run['act'])
        if isinstance(run.get('floor'), int):
            max_floor = max(max_floor, run['floor'])
        player = st.get('player') or {}
        for key in ('deck', 'hand', 'draw_pile', 'discard_pile', 'exhaust_pile'):
            for c in player.get(key) or []:
                if isinstance(c, dict) and c.get('id'):
                    cards.add(c['id'])
        for r in player.get('relics') or []:
            if isinstance(r, dict) and r.get('id'):
                relics.add(r['id'])
        for p in player.get('potions') or []:
            if isinstance(p, dict) and p.get('id'):
                potions.add(p['id'])
        for block in ('card_reward', 'shop'):
            b = st.get(block) or {}
            for c in b.get('cards') or []:
                if isinstance(c, dict) and c.get('id'):
                    cards.add(c['id'])
    native = sdir / 'native'
    out.update(rooms=dict(rooms), encounters=encounters, events=events_seen, cards=cards, relics=relics,
               potions=potions, max_act=max_act, max_floor=max_floor, win=win, anchors=dict(anchors),
               save_files=len(list((native / 'saves').glob('*.save'))) if (native / 'saves').exists() else 0,
               replay_files=len(list((native / 'replays').glob('*.mcr'))) if (native / 'replays').exists() else 0)
    return out


def main(argv=None):
    ap = argparse.ArgumentParser()
    ap.add_argument('farm_root')
    ap.add_argument('--validate', help='JSON written by the validate pass: {session: {errors, warnings}}')
    args = ap.parse_args(argv)
    root = Path(args.farm_root)
    sessions = []
    for sdir in sorted(root.glob('worker-*/sessions/*')):
        if (sdir / 'manifest.json').exists():
            s = session_stats(sdir)
            s['worker'] = sdir.parents[1].name
            sessions.append(s)
    runs = jsonl(root / 'runs.jsonl')
    rooms = jsonl(root / 'rooms.jsonl')
    lifecycle = jsonl(root / 'events.jsonl')
    agg_rooms = collections.Counter()
    anchors = collections.Counter()
    sets = {k: set() for k in ('encounters', 'events', 'cards', 'relics', 'potions')}
    floors = collections.Counter()
    acts = collections.Counter()
    by_char = collections.Counter()
    by_asc = collections.Counter()
    wins = 0
    for s in sessions:
        agg_rooms.update(s['rooms'])
        anchors.update(s['anchors'])
        for k in sets:
            sets[k] |= s[k]
        floors[s['max_floor']] += 1
        acts[s['max_act']] += 1
        by_char[s['run'].get('character')] += 1
        by_asc[s['run'].get('ascension')] += 1
        wins += bool(s['win'])
    combats = sum(agg_rooms.get(k, 0) for k in ('Monster', 'Elite', 'Boss'))
    validate = json.loads(Path(args.validate).read_text()) if args.validate else None
    summary = {
        'sessions': len(sessions),
        'sessions_incomplete': sum(1 for s in sessions if s['incomplete']),
        'runs_driver': len(runs),
        'runs_driver_by_result': dict(collections.Counter(r.get('result') for r in runs)),
        'runs_recorded_with_run_ended': sum(1 for s in sessions if s['win'] is not None),
        'wins': wins,
        'sessions_by_character': dict(by_char),
        'sessions_by_ascension': {str(k): v for k, v in by_asc.items()},
        'max_floor_distribution': {str(k): v for k, v in sorted(floors.items())},
        'max_act_distribution': {str(k): v for k, v in sorted(acts.items())},
        'rooms_by_type': dict(agg_rooms),
        'rooms_total': sum(agg_rooms.values()),
        'combats': combats,
        'driver_rooms_logged': len(rooms),
        'distinct': {k: len(v) for k, v in sets.items()},
        'anchors': dict(anchors),
        'save_files': sum(s['save_files'] for s in sessions),
        'replay_files': sum(s['replay_files'] for s in sessions),
        'states': sum((s['counts'] or {}).get('states', 0) for s in sessions),
        'actions': sum((s['counts'] or {}).get('actions', 0) for s in sessions),
        'events': sum((s['counts'] or {}).get('events', 0) for s in sessions),
        'degraded_hooks_sessions': sum(1 for s in sessions if s['degraded_hooks']),
        'snapshot_build_ms_max': max([((s['perf'] or {}).get('max') or 0) for s in sessions] or [0]),
        'lifecycle': dict(collections.Counter(e.get('event') for e in lifecycle)),
        'restart_reasons': dict(collections.Counter(e.get('reason') for e in lifecycle if e.get('event') == 'restart')),
        'driver_decisions': sum(r.get('decisions', 0) for r in runs),
        'driver_action_errors': sum(r.get('errors', 0) for r in runs),
    }
    if validate is not None:
        summary['validate'] = {
            'sessions': len(validate),
            'ok': sum(1 for v in validate.values() if v.get('ok')),
            'errors': sum(v.get('errors', 0) for v in validate.values()),
            'warnings': sum(v.get('warnings', 0) for v in validate.values()),
        }
    print(json.dumps(summary, indent=2, ensure_ascii=False))


if __name__ == '__main__':
    main()
