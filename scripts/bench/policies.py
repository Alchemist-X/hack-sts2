"""Pluggable decision policies for the benchmark runner.

A policy maps one observation (the JSON printed by ``sts2_live.py observe``,
including ``legal_actions[*].ref`` and, when enabled, ``wiki_facts``) to
``{"action": <ref or list of refs>, "reason": <public rationale>}``.

Specs accepted by :func:`load_policy`:

* ``stub``                 deterministic baseline (no model, no search)
* ``cmd:<shell command>``  external process / LLM CLI hook: the request JSON is
  written to stdin and one JSON object is read from stdout
* ``py:<module>:<func>``   in-process hook; ``func(observation, context)``
"""
import importlib
import json
import os
import shlex
import subprocess
import time

COMBAT_TYPES = {'monster', 'elite', 'boss'}
NEVER = {'use_potion', 'discard_potion'}
SELECT_SCREENS = {'card_select', 'hand_select'}
LEAVE = ('proceed', 'leave_shop', 'leave', 'close_shop')


class PolicyError(RuntimeError):
    pass


def _refs(observation):
    return [a['ref'] for a in observation.get('legal_actions') or [] if isinstance(a.get('ref'), dict)]


def stub_policy(observation, context):
    """Trivial, deterministic baseline.

    Combat: first playable card (first legal target), otherwise end_turn.
    Shop: leave. Everything else: first legal action. Potions are never used.
    ``context['avoid']`` (refs that just failed on this same frame) are skipped.
    """
    state = observation.get('state') or {}
    st = state.get('state_type')
    avoid = [json.dumps(r, sort_keys=True) for r in context.get('avoid') or []]
    refs = [r for r in _refs(observation)
            if r.get('action') not in NEVER and json.dumps(r, sort_keys=True) not in avoid]
    if not refs:
        raise PolicyError(f'no legal action on {st!r}')
    block = state.get(st) if isinstance(state.get(st), dict) else {}
    minimum = block.get('min_select')
    if st in SELECT_SCREENS and isinstance(minimum, int) and minimum > 1:
        picks = [r for r in refs if r.get('card')]
        names = list(dict.fromkeys(r['card'] for r in picks))
        while picks and len(names) < minimum:  # identical copies collapse to one ref; repeat names
            names.append(picks[len(names) % len(picks)]['card'])
        if picks:
            return {'action': {'action': picks[0]['action'], 'cards': names[:minimum]},
                    'reason': f'stub: first {minimum} cards for a {minimum}-card selection'}
    if st in COMBAT_TYPES:
        for ref in refs:
            if ref.get('action') == 'play_card':
                return {'action': ref, 'reason': 'stub: first playable card'}
        for ref in refs:
            if ref.get('action') == 'end_turn':
                return {'action': ref, 'reason': 'stub: nothing playable, end turn'}
    if st == 'shop':
        for ref in refs:
            if ref.get('action') in LEAVE:
                return {'action': ref, 'reason': 'stub: never buys, leave shop'}
    return {'action': refs[0], 'reason': f'stub: first legal action on {st}'}


def cmd_policy(command, timeout_s=600):
    argv = shlex.split(command)

    def policy(observation, context):
        request = {'schema': 'sts2-bench-policy-request/v1', 'context': context, 'observation': observation}
        env = {**os.environ, 'BENCH_SEED': str(context.get('seed', '')),
               'BENCH_DECISION_INDEX': str(context.get('index', ''))}
        proc = subprocess.run(argv, input=json.dumps(request, ensure_ascii=False), capture_output=True,
                              text=True, timeout=timeout_s, env=env)
        if proc.returncode != 0:
            raise PolicyError(f'policy command exit {proc.returncode}: {proc.stderr.strip()[-500:]}')
        lines = [l for l in proc.stdout.splitlines() if l.strip()]
        if not lines:
            raise PolicyError('policy command printed nothing')
        try:
            reply = json.loads(lines[-1])
        except json.JSONDecodeError as error:
            raise PolicyError(f'policy reply is not JSON: {error}') from error
        return reply
    return policy


def load_policy(spec):
    if spec == 'stub':
        return 'stub', stub_policy
    if spec.startswith('cmd:'):
        return spec, cmd_policy(spec[4:])
    if spec.startswith('py:'):
        _, module, func = spec.split(':', 2)
        return spec, getattr(importlib.import_module(module), func)
    raise PolicyError(f'unknown policy spec {spec!r} (stub | cmd:<command> | py:<module>:<func>)')


def decide(policy, observation, context):
    """Call a policy and validate its reply shape; returns (reply, latency_s)."""
    began = time.monotonic()
    reply = policy(observation, context)
    latency = time.monotonic() - began
    if not isinstance(reply, dict) or 'action' not in reply:
        raise PolicyError(f'policy reply must be an object with "action": {reply!r}')
    if not isinstance(reply['action'], (dict, list, str)):
        raise PolicyError('policy "action" must be a ref object, a list of refs or a shorthand string')
    reason = str(reply.get('reason') or '').strip()
    if len(reason) < 4:
        reason = f'{reason or "no rationale"} (policy gave no rationale)'
    out = {'action': reply['action'], 'reason': reason}
    if isinstance(reply.get('llm'), dict):  # model/tokens/latency/cost telemetry (llm_policy.py)
        out['llm'] = reply['llm']
    return out, latency
