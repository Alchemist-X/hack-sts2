#!/usr/bin/env python3
"""One observe or act for an interactive LLM agent playing a benchmark seed.

    agent_step.py --seed-dir DIR --port P --seed S obs [--facts]
    agent_step.py --seed-dir DIR --port P --seed S act ACTION REASON [--facts]

ACTION is a stable ref / shorthand / JSON request (passed to sts2_live as is),
or legal-action numbers from a listing.  Numbers are positional, and positions
shift after every action (claiming one reward renumbers the rest, so yesterday's
``1`` becomes ``proceed``).  They are therefore only accepted bound to the
listing they were read from: ``3@<token>`` or ``[1,4]@<token>``, where the
token is printed with each listing.  The token is checked against the last
observation, and the act is then guarded by that observation's decision hash
and, when the eval API is available, its ``state_version``; a mismatch
anywhere refuses the act without sending anything.

``act obs`` (or ``act observe``) is an observation, never an action.
"""
from __future__ import annotations

import argparse
import json
import re
import sys
import time
from pathlib import Path
from typing import Any

OBSERVE_WORDS = frozenset({'obs', 'observe'})
TOKEN_LEN = 8
INDEX_RE = re.compile(r'^\s*(?P<body>#?\d+|\[\s*#?\d+(?:\s*,\s*#?\d+)*\s*\])\s*'
                      r'(?:@\s*(?P<token>[0-9a-fA-F]{6,64}))?\s*$')


class IndexBindingError(Exception):
    def __init__(self, kind: str, message: str):
        super().__init__(message)
        self.kind = kind
        self.message = message


def is_observe(text: str | None) -> bool:
    return str(text or '').strip().casefold() in OBSERVE_WORDS


def listing_token(obs: dict[str, Any]) -> str:
    return str(obs.get('hash') or '')[:TOKEN_LEN]


def listing_header(obs: dict[str, Any]) -> str:
    token = listing_token(obs)
    version = obs.get('state_version')
    extra = f', state_version {version}' if version is not None else ''
    return (f'Index token: @{token}{extra} (number acts must be written N@{token} or [i,j]@{token}; '
            'refs need no token)')


def parse_index(text: str) -> tuple[list[int], str | None, bool] | None:
    """``(indices, token, is_list)`` for a positional request, or None for anything else."""
    match = INDEX_RE.match(str(text))
    if not match:
        return None
    body = match.group('body')
    nums = [int(x.strip().lstrip('#')) for x in body.strip('[]').split(',')]
    token = match.group('token')
    return nums, token.lower() if token else None, body.lstrip().startswith('[')


def bind(obs: dict[str, Any], text: str) -> Any:
    """Turn ``N@token`` into the listing's stable ref(s); None when ``text`` is not positional.

    Raises IndexBindingError (nothing may be sent) when the numbers are not
    bound to a listing, are bound to a different one, or do not exist in it.
    """
    parsed = parse_index(text)
    if parsed is None:
        return None
    nums, token, is_list = parsed
    current = str(obs.get('hash') or '').lower()
    if token is None:
        raise IndexBindingError('index_unbound', (
            f'positional action {text!r} has no listing token; numbers shift after every action, so write '
            f'{text.strip()}@{listing_token(obs)} if you chose it from the listing marked @{listing_token(obs)}, '
            'or use a stable ref'))
    if not current or not current.startswith(token):
        raise IndexBindingError('stale_index', (
            f'{text!r} was chosen from listing @{token}, but the current listing is @{listing_token(obs)} '
            '(the screen changed, e.g. a reward was claimed); nothing was sent; choose again from the '
            'listing below'))
    actions = obs.get('legal_actions') or []
    refs = []
    for n in nums:
        if not 0 <= n < len(actions):
            raise IndexBindingError('index_out_of_range', f'action {n} is not in listing @{token} '
                                    f'(0..{len(actions) - 1})')
        ref = actions[n].get('ref')
        if ref is None:
            raise IndexBindingError('index_has_no_ref', f'action {n} has no stable ref; send its JSON request')
        refs.append(ref)
    return refs if is_list else refs[0]


def parse_action(obs: dict[str, Any], text: str) -> Any:
    bound = bind(obs, text)
    if bound is not None:
        return bound
    return json.loads(text) if text[:1] in '{["' else text


def act_args(obs: dict[str, Any], action: Any, reason: str) -> tuple[list[str], list[str]]:
    """Positional args and extra flags for ``sts2_live act``, guarded by the observed hash/version."""
    payload = action if isinstance(action, str) else json.dumps(action, ensure_ascii=False)
    extra = []
    if isinstance(obs.get('state_version'), int):
        extra = ['--expect-state-version', str(obs['state_version'])]
    return [payload, reason, obs['hash']], extra


def discard_guard(action: Any, reason: str) -> str | None:
    acts = action if isinstance(action, list) else [action]
    if any(isinstance(x, dict) and x.get('action') == 'discard_potion' for x in acts) \
            and 'discard' not in reason.lower():
        return 'discard_potion without "discard" in the reason; refusing'
    return None


# ----------------------------------------------------------------------- CLI
class Runner:
    def __init__(self, a):
        sys.path.insert(0, str(Path(__file__).resolve().parent))
        import metrics  # noqa: PLC0415
        import run_bench  # noqa: PLC0415
        import text_state  # noqa: PLC0415
        self.rb, self.ts, self.a = run_bench, text_state, a
        self.seed_dir = a.seed_dir.resolve()
        self.run_dir = self.seed_dir / 'live'
        self.tools = self.seed_dir / 'tools'
        self.tools.mkdir(parents=True, exist_ok=True)
        self.state_path, self.last_obs = self.tools / 'state.json', self.tools / 'last_obs.json'
        self.s = json.loads(self.state_path.read_text()) if self.state_path.exists() else {}
        self.s.setdefault('index', 0)
        self.s.setdefault('began_wall', time.time())
        self.rec = run_bench.Recorder(self.seed_dir / 'trajectory.jsonl')
        if (self.seed_dir / 'trajectory.jsonl').exists():
            self.rec.records = metrics.load_trajectory(self.seed_dir / 'trajectory.jsonl')

    def save(self):
        self.state_path.write_text(json.dumps(self.s, indent=2))

    def live(self, command, *args, extra=()):
        return self.rb.live(command, self.run_dir, self.a.port, *args, extra=[*self.a.live_flag, *extra],
                            on_retry=lambda c, n, e: self.rec.write(
                                {'type': 'friction', 'kind': 'transient_observe_retry', 'attempt': n, 'message': e}))

    def observe(self):
        _, obs = self.live('observe')
        if obs.get('warning_pending'):
            print('WARNING pending unresolved -> need reconcile')
        if not obs.get('settled', True):
            self.rec.write({'type': 'friction', 'kind': 'not_settled', 'index': self.s['index'], 'stage': 'observe'})
        self.last_obs.write_text(json.dumps(obs))
        if (obs.get('state') or {}).get('state_type') == 'game_over' and not self.s.get('terminal'):
            self.rec.write({'type': 'terminal', 'index': self.s['index'], 'state_ref': {'decision_hash': obs.get('hash')},
                            **self.rb.summarize_state(obs), 't_wall_s': round(time.time() - self.s['began_wall'], 3)})
            self.s['terminal'] = True
        self.save()
        return obs

    def show(self, obs):
        try:
            print(self.ts.encode(obs, facts=self.a.facts))
        except Exception as error:  # noqa: BLE001 - still show something usable
            print('encode failed', error)
            print(json.dumps(obs.get('state'))[:4000])
        print(listing_header(obs))

    def refuse(self, kind, message):
        self.rec.write({'type': 'friction', 'kind': kind, 'index': self.s['index'], 'message': message[:300]})
        print(f'ACT REFUSED ({kind}): {message}')
        self.show(self.observe())
        return 3

    def act(self, text, reason):
        obs = json.loads(self.last_obs.read_text())
        try:
            action = parse_action(obs, text)
        except IndexBindingError as error:
            return self.refuse(error.kind, error.message)
        guard = discard_guard(action, reason)
        if guard:
            print('GUARD:', guard)
            return 3
        args, extra = act_args(obs, action, reason)
        t0 = time.time()
        _, result = self.live('act', *args, extra=extra)
        latency = time.time() - t0
        post = result.get('observation') or {}
        steps = result.get('steps') or []
        outcome = {'ok': bool(result.get('ok')), 'error': None if result.get('ok') else result.get('error'),
                   'message': None if result.get('ok') else (result.get('message') or '')[:500],
                   'steps': len(steps),
                   'harness_followups': [x['harness_followup'] for x in steps if x.get('harness_followup')],
                   'warnings': result.get('warnings') or [], 'authority_verified': result.get('authority_verified'),
                   'post_hash': post.get('hash'), 'post_state_type': (post.get('state') or {}).get('state_type'),
                   'post_hp': ((post.get('state') or {}).get('player') or {}).get('hp')}
        self.rec.write({'type': 'decision', 'seed': self.a.seed, 'instance': self.a.instance, 'index': self.s['index'],
                        't_wall_s': round(time.time() - self.s['began_wall'], 3),
                        'state_ref': {'run_dir': str(self.run_dir), 'observations': 'observations.jsonl',
                                      'decision_hash': obs['hash'], 'state_version': obs.get('state_version')},
                        **self.rb.summarize_state(obs),
                        'legal_actions': [x.get('ref') for x in obs.get('legal_actions') or []],
                        'chosen': action, 'chosen_text': text, 'rationale': reason,
                        'policy': 'llm-agent-interactive', 'outcome': outcome,
                        'latency_s': {'policy': None, 'act': round(latency, 3)}})
        self.s['index'] += 1
        self.save()
        if not outcome['ok']:
            print('ACT FAILED:', outcome['error'], outcome['message'])
            if outcome['error'] in ('action_unresolved', 'pending_unresolved'):
                self.live('reconcile', f'agent: auto-reconcile after {outcome["error"]}')
                print('reconciled')
        if outcome['warnings']:
            print('WARNINGS:', outcome['warnings'])
        self.show(self.observe())
        return 0 if outcome['ok'] else 2


def build_parser():
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument('--seed-dir', type=Path, required=True)
    p.add_argument('--port', type=int, required=True)
    p.add_argument('--seed', required=True)
    p.add_argument('--instance', default='inst1')
    p.add_argument('--live-flag', action='append', default=[], help='extra sts2_live flag (repeatable)')
    p.add_argument('--facts', action='store_true', help='include wiki facts in the text view')
    p.add_argument('command', choices=['obs', 'observe', 'act'])
    p.add_argument('action', nargs='?')
    p.add_argument('reason', nargs='?')
    return p


def route(a) -> str:
    """'observe' or 'act'.  ``act obs`` is an observation, not an action with an empty reason."""
    if a.command in OBSERVE_WORDS or is_observe(a.action):
        return 'observe'
    if not a.action:
        raise SystemExit('act needs ACTION and REASON')
    if not (a.reason or '').strip():
        raise SystemExit('act needs a non-empty REASON (nothing was sent)')
    return 'act'


def main(argv=None, runner_cls=Runner):
    a = build_parser().parse_args(argv)
    kind = route(a)
    runner = runner_cls(a)
    if kind == 'observe':
        runner.show(runner.observe())
        return 0
    return runner.act(a.action, a.reason)


if __name__ == '__main__':
    sys.exit(main())
