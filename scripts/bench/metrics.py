"""Per-run benchmark metrics from the trajectory and the game's own run history.

The game writes ``saves/history/<start_time>.run`` when a run ends; it is the
authoritative source for win/death/boss outcomes.  The trajectory is the
fallback (and the only source when a run is cut off by a limit).
"""
import collections
import json
from pathlib import Path
from typing import NamedTuple

COMBAT_TYPES = {'monster', 'elite', 'boss'}
FRICTION_ERRORS = ('stale_hash', 'not_settled', 'unresolvable_action', 'selection_incomplete',
                   'action_rejected', 'action_unresolved', 'pending_unresolved', 'rule_authority_unverified',
                   'reason_required', 'hash_required', 'bad_request', 'policy_error', 'harness_crash')


class HistoryMatch(NamedTuple):
    """A run-history file found by :func:`find_history`.

    It is a plain 2-tuple, so ``path, data = find_history(...)`` keeps working,
    and also has names: ``match.path`` (the ``.run`` file) and ``match.data``
    (its parsed JSON dict, the input of :func:`history_metrics`).
    """
    path: Path
    data: dict


def normalize_seed(seed):
    """Seed as the game stores it: upper case, letter O read as zero (BENCHAOS05 == BENCHA0S05)."""
    return str(seed or '').strip().upper().replace('O', '0')


def find_history(history_dir, seed, not_before=None):
    """Newest history file for ``seed``.

    Returns a :class:`HistoryMatch` ``(path, data)`` or ``None`` when the
    directory is missing or nothing matches; it never returns a bare dict or
    path.  ``seed`` is compared with :func:`normalize_seed` on both sides.
    ``not_before`` (epoch seconds, 5 s slack) skips runs started earlier, e.g.
    a previous attempt with the same seed.
    """
    history_dir = Path(history_dir)
    if not history_dir.is_dir():
        return None
    best = None
    for path in history_dir.glob('*.run'):
        try:
            data = json.loads(path.read_text())
        except (OSError, json.JSONDecodeError):
            continue
        if not isinstance(data, dict) or normalize_seed(data.get('seed')) != normalize_seed(seed):
            continue
        if not_before is not None and int(data.get('start_time') or 0) < int(not_before) - 5:
            continue
        if best is None or int(data.get('start_time') or 0) > int(best.data.get('start_time') or 0):
            best = HistoryMatch(path, data)
    return best


def history_metrics(history):
    """Outcome metrics from a game history record."""
    acts = history.get('map_point_history') or []
    floors = sum(len(a) for a in acts)
    boss_kills, boss_entry_hp, last_room = 0, [], None
    for act in acts:
        for point in act:
            stats = (point.get('player_stats') or [{}])[0]
            rooms = point.get('rooms') or []
            if rooms:
                last_room = rooms[-1].get('model_id')
            if point.get('map_point_type') == 'boss':
                hp = stats.get('current_hp')
                if hp is not None:
                    entry = hp + (stats.get('damage_taken') or 0) - (stats.get('hp_healed') or 0)
                    boss_entry_hp.append(entry)
                    if hp > 0:
                        boss_kills += 1
    killed_by = history.get('killed_by_encounter')
    killed_event = history.get('killed_by_event')
    cause = None
    if not history.get('win'):
        if killed_by and killed_by != 'NONE.NONE':
            cause = killed_by
        elif killed_event and killed_event != 'NONE.NONE':
            cause = killed_event
        elif history.get('was_abandoned'):
            cause = 'abandoned'
        else:
            cause = last_room or 'unknown'
    return {'source': 'game_history', 'win': bool(history.get('win')), 'act': len(acts),
            'floor': floors, 'boss_kills': boss_kills, 'boss_entry_hp': boss_entry_hp,
            'death_cause': cause, 'game_run_time_s': history.get('run_time'),
            'acts': history.get('acts'), 'history_seed': history.get('seed'),
            'game_mode': history.get('game_mode'), 'ascension': history.get('ascension')}


def trajectory_metrics(records):
    """Fallback metrics plus harness friction counts from trajectory records."""
    decisions = [r for r in records if r.get('type') == 'decision']
    frictions = collections.Counter()
    followups = collections.Counter()
    floor = act = 0
    boss_entry_hp, boss_kills = [], 0
    in_boss = False
    last_combat_enemies = None
    for r in decisions:
        floor = max(floor, r.get('floor') or 0)
        act = max(act, r.get('act') or 0)
        st = r.get('state_type')
        if st in COMBAT_TYPES:
            last_combat_enemies = r.get('enemies') or last_combat_enemies
        if st == 'boss' and not in_boss:
            in_boss = True
            boss_entry_hp.append(r.get('hp'))
        outcome = r.get('outcome') or {}
        if in_boss and outcome.get('post_state_type') not in (None, 'boss', 'hand_select', 'game_over'):
            in_boss = False
            boss_kills += 1
        if outcome.get('error'):
            frictions[outcome['error']] += 1
        for f in outcome.get('harness_followups') or []:
            followups[f] += 1
        for w in outcome.get('warnings') or []:
            followups['warning:' + w.split(':')[0]] += 1
    for r in records:
        if r.get('type') == 'friction':
            frictions[r.get('kind', 'unknown')] += 1
    return {'decisions': len(decisions), 'floor': floor, 'act': act, 'boss_kills': boss_kills,
            'boss_entry_hp': boss_entry_hp, 'last_combat_enemies': last_combat_enemies,
            'frictions': dict(frictions), 'friction_total': sum(frictions.values()),
            'harness_followups': dict(followups)}


def run_metrics(records, history=None, *, wall_time_s=None, end_reason=None):
    traj = trajectory_metrics(records)
    out = {'end_reason': end_reason, 'wall_time_s': wall_time_s, 'decisions': traj['decisions'],
           'frictions': traj['frictions'], 'friction_total': traj['friction_total'],
           'harness_followups': traj['harness_followups']}
    if history is not None:
        out.update(history_metrics(history))
    else:
        died = end_reason == 'game_over'
        cause = None
        if died:
            enemies = traj['last_combat_enemies'] or []
            cause = 'combat:' + '+'.join(sorted(enemies)) if enemies else 'unknown'
        out.update({'source': 'trajectory', 'win': None if not died else False, 'act': traj['act'],
                    'floor': traj['floor'], 'boss_kills': traj['boss_kills'],
                    'boss_entry_hp': traj['boss_entry_hp'],
                    'death_cause': cause if died else f'not_finished:{end_reason}'})
    out['hp_at_boss_entry'] = out['boss_entry_hp'][0] if out.get('boss_entry_hp') else None
    return out


def load_trajectory(path):
    return [json.loads(l) for l in Path(path).read_text().splitlines() if l.strip()]
