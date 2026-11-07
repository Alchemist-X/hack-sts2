#!/usr/bin/env python3
"""LLM policy for run_bench: any OpenAI-compatible chat-completions API (default: xAI) or the
native Anthropic Messages API (``LLM_PROVIDER=anthropic``).

Use it from run_bench as an in-process policy::

    XAI_API_KEY=... LLM_MODEL=grok-4.3 python3 scripts/bench/run_bench.py \\
        --pool /workspace/private/worker-pool-llm --policy py:llm_policy:policy ...
    ANTHROPIC_API_KEY=... LLM_PROVIDER=anthropic LLM_MODEL=claude-opus-5-5 python3 scripts/bench/run_bench.py ...

or as a ``cmd:`` policy (``cmd:python3 scripts/bench/llm_policy.py``), which reads
the ``sts2-bench-policy-request/v1`` JSON on stdin and prints one reply line.

Each decision: the observation is encoded by :mod:`text_state`, the model must
answer ``{"action": <number> | [numbers], "reason": "..."}``; the number is mapped
back to the exact legal ``ref``. An unparsable / out-of-range answer is sent back
with the validation error (up to ``LLM_MAX_RETRIES`` times). The reply carries an
``llm`` block (provider, model, tokens, latency, cost, attempts) that run_bench
writes into the trajectory's decision record. Both providers fill the same fields.

Configuration (environment):

  LLM_PROVIDER           openai (default; any OpenAI-compatible POST {base}/chat/completions)
                         | anthropic (native Messages API, POST {base}/messages)
  LLM_MODEL              model id (required unless --model); e.g. grok-4.3, claude-opus-5-5
  LLM_BASE_URL           default https://api.x.ai/v1 (openai) / https://api.anthropic.com/v1 (anthropic)
  LLM_API_KEY_ENV        name of the env var holding the key (default XAI_API_KEY / ANTHROPIC_API_KEY)
  LLM_TIMEOUT_S          per-request timeout, default 120
  LLM_MAX_RETRIES        validation retries per decision, default 2
  LLM_HTTP_RETRIES       retries on 408/409/429/5xx (incl. Anthropic 529)/network errors, default 3
  LLM_REASONING_EFFORT   optional effort (low|medium|high|xhigh|max), model permitting: sent as
                         reasoning_effort (openai) or output_config.effort (anthropic)
  LLM_MAX_TOKENS         optional max_tokens for the answer (anthropic: always sent, default 16000)
  LLM_TEMPERATURE        optional temperature (openai only: anthropic-version 2026-10-21 rejects it)
  LLM_THINKING           anthropic only: thinking type (adaptive|disabled|between_tools); unset = model default
  LLM_ANTHROPIC_VERSION  anthropic only: anthropic-version header, default 2026-10-21
  LLM_HISTORY            recent decisions shown to the model, default 8
  LLM_PRICES_JSON        optional {"model": [in, cached_in, out(, cache_write)]} USD per 1M tokens override

The API key is read from the environment only, sent only in the Authorization
header (openai) or the x-api-key header (anthropic), never printed, logged or
written to a trajectory, and scrubbed from error text.

``python3 scripts/bench/llm_policy.py [--provider anthropic] --list-models`` lists the models the key can use.
"""
import argparse
import collections
import json
import os
import re
import sys
import time
import urllib.error
import urllib.request
from pathlib import Path

HERE = Path(__file__).resolve().parent
if str(HERE) not in sys.path:
    sys.path.insert(0, str(HERE))

import text_state  # noqa: E402

try:
    from policies import PolicyError as _Base  # run_bench turns PolicyError into a policy_error friction
except Exception:  # noqa: BLE001 - standalone use
    _Base = RuntimeError

DEFAULT_BASE_URL = 'https://api.x.ai/v1'
ANTHROPIC_BASE_URL = 'https://api.anthropic.com/v1'
# Raw-HTTP API version. From 2026-10-21 temperature/top_p/top_k are a 400 on every model.
ANTHROPIC_VERSION = '2026-10-21'
# The Messages API requires max_tokens; adaptive thinking counts against it (only what is generated is billed).
ANTHROPIC_MAX_TOKENS = 16000
CACHE_WRITE_MULTIPLIER = 1.25  # 5-minute cache writes, for a price without an explicit 4th element
PROVIDERS = {  # provider -> defaults for LLM_BASE_URL and LLM_API_KEY_ENV
    'openai': {'base_url': DEFAULT_BASE_URL, 'key_env': 'XAI_API_KEY'},
    'anthropic': {'base_url': ANTHROPIC_BASE_URL, 'key_env': 'ANTHROPIC_API_KEY'},
}
PROVIDER_ALIASES = {'openai-compatible': 'openai', 'openai_compatible': 'openai'}
# USD per 1M tokens: (input, cached input, output[, cache write]); xAI and Anthropic ids never collide.
# xAI: docs.x.ai/developers/models, checked 2026-10-24. Requests above 200k context are billed at 2x;
# this harness stays far below that.
PRICES = {
    'grok-4.7': (2.00, 0.50, 6.00),
    'grok-4.6': (2.00, 0.50, 6.00),
    'grok-4.5': (2.00, 0.30, 6.00),
    'grok-4.3': (1.25, 0.20, 2.50),
    'grok-4.20-0309-reasoning': (1.25, 0.20, 2.50),
    'grok-4.20-0309-non-reasoning': (1.25, 0.20, 2.50),
    'grok-4.20-multi-agent-0309': (1.25, 0.20, 2.50),
    'grok-build-0.1': (1.00, 0.20, 2.00),
    # Anthropic: platform.claude.com/docs/en/about-claude/pricing, checked 2026-10-30. Cached input =
    # cache hits; 4th element = 5-minute cache writes. Haiku 5.5: rates for prompts up to 100k tokens.
    'claude-fable-5-1': (10.00, 0.25, 50.00, 12.50),
    'claude-fable-5': (10.00, 1.00, 50.00, 12.50),
    'claude-opus-5-5': (4.00, 0.20, 20.00, 5.00),
    'claude-opus-5': (5.00, 0.50, 25.00, 6.25),
    'claude-opus-4-8': (5.00, 0.50, 25.00, 6.25),
    'claude-opus-4-7': (5.00, 0.50, 25.00, 6.25),
    'claude-opus-4-6': (5.00, 0.50, 25.00, 6.25),
    'claude-sonnet-5-5': (2.00, 0.10, 10.00, 2.50),
    'claude-sonnet-5': (2.00, 0.20, 10.00, 2.50),
    'claude-sonnet-4-6': (3.00, 0.30, 15.00, 3.75),
    'claude-haiku-5-5': (0.10, 0.01, 0.50, 0.125),
    'claude-haiku-4-5': (1.00, 0.10, 5.00, 1.25),
}
# Suggested comparison sets (check against --list-models for the key in use).
CANDIDATES = ['grok-4.3', 'grok-4.20-0309-non-reasoning', 'grok-4.20-0309-reasoning', 'grok-4.5', 'grok-4.7']
ANTHROPIC_CANDIDATES = ['claude-haiku-5-5', 'claude-sonnet-5-5', 'claude-opus-5-5']

SYSTEM_PROMPT = """You are playing Slay the Spire 2 (game v0.107.1) to win the run (beat the Act 3 boss).
Each turn you get the full visible game state as text and a numbered list of legal actions.
Card lines read: Name [energy cost] Type/->target: text. Enemy lines show HP, block, intent
(Attack N or per-hit x hits = total) and powers. "(engine: dmg @id=N)" is the game's own damage
preview against each target. Map rows list nodes as col+type>child cols in the next row.
Think briefly, then reply with ONLY a JSON object, no markdown:
{"action": <number of one legal action>, "reason": "<one short sentence>"}
When the state says Multi-select, "action" is a list of numbers instead."""

SECRET_PATTERN = re.compile(r'(Bearer\s+)\S+|(xai-|sk-ant-|sk-)[A-Za-z0-9_-]{8,}')


class LLMPolicyError(_Base):
    pass


def _mask(secret):
    match = SECRET_PATTERN.fullmatch(secret)
    return ((match.group(1) or match.group(2)) if match else '') + '***'


def redact(text, *secrets):
    """Scrub key-shaped tokens and, when given, the exact configured key(s) from ``text``."""
    text = str(text)
    for secret in secrets:
        if secret and len(secret) >= 8:  # never blank out short, common substrings
            text = text.replace(secret, _mask(secret))
    return SECRET_PATTERN.sub(lambda m: (m.group(1) or m.group(2)) + '***', text)


def provider_name(value):
    name = (value or 'openai').strip().lower()
    name = PROVIDER_ALIASES.get(name, name)
    if name not in PROVIDERS:
        raise LLMPolicyError(f'unknown LLM_PROVIDER {value!r}: use {" or ".join(PROVIDERS)}')
    return name


def config(env=None, **override):
    env = os.environ if env is None else env
    provider = provider_name(override.pop('provider', None) or env.get('LLM_PROVIDER'))
    defaults = PROVIDERS[provider]
    cfg = {
        'provider': provider,
        'model': env.get('LLM_MODEL'),
        'base_url': (env.get('LLM_BASE_URL') or defaults['base_url']).rstrip('/'),
        'key_env': env.get('LLM_API_KEY_ENV') or defaults['key_env'],
        'timeout_s': float(env.get('LLM_TIMEOUT_S') or 120),
        'max_retries': int(env.get('LLM_MAX_RETRIES') or 2),
        'http_retries': int(env.get('LLM_HTTP_RETRIES') or 3),
        'reasoning_effort': env.get('LLM_REASONING_EFFORT') or None,
        'max_tokens': int(env['LLM_MAX_TOKENS']) if env.get('LLM_MAX_TOKENS') else None,
        'temperature': float(env['LLM_TEMPERATURE']) if env.get('LLM_TEMPERATURE') else None,
        'thinking': env.get('LLM_THINKING') or None,
        'anthropic_version': env.get('LLM_ANTHROPIC_VERSION') or ANTHROPIC_VERSION,
        'history': int(env.get('LLM_HISTORY') or 8),
        'prices': dict(PRICES),
    }
    if env.get('LLM_PRICES_JSON'):
        cfg['prices'].update({k: tuple(v) for k, v in json.loads(env['LLM_PRICES_JSON']).items()})
    cfg.update({k: v for k, v in override.items() if v is not None})
    return cfg


def check_config(cfg):
    """Refuse settings that would otherwise fail every decision with an HTTP 400/404."""
    model = cfg.get('model')
    if not model:
        raise LLMPolicyError('LLM_MODEL is not set (e.g. LLM_MODEL=grok-4.3, or LLM_PROVIDER=anthropic '
                             'LLM_MODEL=claude-opus-5-5)')
    if cfg['provider'] == 'openai' and model.startswith('claude-') and cfg['base_url'] == DEFAULT_BASE_URL:
        raise LLMPolicyError(f'{model} is an Anthropic model but LLM_PROVIDER=openai would send it to '
                             f'{DEFAULT_BASE_URL}: set LLM_PROVIDER=anthropic (compare_models: anthropic:{model})')
    if cfg['provider'] == 'anthropic' and cfg.get('temperature') is not None \
            and cfg['anthropic_version'] >= ANTHROPIC_VERSION:
        raise LLMPolicyError(f'LLM_TEMPERATURE is rejected by the Anthropic Messages API from anthropic-version '
                             f'{ANTHROPIC_VERSION} (HTTP 400 on every model): unset it for anthropic models')


def api_key(cfg, env=None):
    env = os.environ if env is None else env
    key = env.get(cfg['key_env'])
    if not key:
        raise LLMPolicyError(f'{cfg["key_env"]} is not set: export it (the key is read from the environment only) '
                             f'or point LLM_API_KEY_ENV at the variable that holds it')
    return key


def _headers(cfg, key):
    if cfg['provider'] == 'anthropic':
        return {'x-api-key': key, 'anthropic-version': cfg['anthropic_version'], 'Accept': 'application/json'}
    return {'Authorization': f'Bearer {key}', 'Accept': 'application/json'}


def _request(cfg, key, path, payload=None):
    """One API call with the shared timeout/backoff policy for both providers."""
    url = cfg['base_url'] + path
    data = None if payload is None else json.dumps(payload).encode()
    headers = _headers(cfg, key)
    if data is not None:
        headers['Content-Type'] = 'application/json'
    last = None
    for attempt in range(cfg['http_retries'] + 1):
        req = urllib.request.Request(url, data=data, headers=headers, method='POST' if data is not None else 'GET')
        try:
            with urllib.request.urlopen(req, timeout=cfg['timeout_s']) as resp:
                return json.loads(resp.read().decode())
        except urllib.error.HTTPError as error:
            body = redact(error.read().decode(errors='replace')[:4000], key)[:300]  # scrub before truncating
            last = f'HTTP {error.code} from {url}: {body}'
            if error.code not in (408, 409, 429) and error.code < 500:  # 5xx includes Anthropic's 529 overloaded
                raise LLMPolicyError(last) from None
        except (urllib.error.URLError, TimeoutError, OSError) as error:
            last = f'{type(error).__name__} calling {url}: {redact(getattr(error, "reason", error), key)}'
        if attempt < cfg['http_retries']:
            time.sleep(min(30.0, 2.0 * 2 ** attempt))
    raise LLMPolicyError(last)


def list_models(cfg=None, env=None):
    cfg = cfg or config(env)
    path = '/models?limit=1000' if cfg['provider'] == 'anthropic' else '/models'  # Anthropic pages by 20
    data = _request(cfg, api_key(cfg, env), path)
    return sorted(m.get('id') for m in data.get('data') or [] if isinstance(m, dict) and m.get('id'))


def usage_of(resp):
    u = resp.get('usage') or {}
    prompt = int(u.get('prompt_tokens') or 0)
    completion = int(u.get('completion_tokens') or 0)
    cached = int((u.get('prompt_tokens_details') or {}).get('cached_tokens') or 0)
    reasoning = int((u.get('completion_tokens_details') or {}).get('reasoning_tokens') or 0)
    total = u.get('total_tokens')
    # xAI reports reasoning outside completion_tokens (total = prompt + completion + reasoning);
    # OpenAI-style servers include it. Bill output = completion (+ reasoning when separate).
    separate = reasoning and isinstance(total, int) and total >= prompt + completion + reasoning
    out = {'prompt_tokens': prompt, 'cached_tokens': cached, 'completion_tokens': completion,
           'reasoning_tokens': reasoning, 'output_tokens': completion + (reasoning if separate else 0)}
    if u.get('cost_in_usd_ticks') is not None:  # xAI's authoritative per-request cost (1e-10 USD)
        out['cost_usd_reported'] = int(u['cost_in_usd_ticks']) / 1e10
    return out


def anthropic_usage_of(resp):
    """Messages API usage in the same telemetry fields as :func:`usage_of`.

    ``input_tokens`` excludes cache reads and writes, so prompt_tokens is the sum of the
    three. ``output_tokens`` already includes thinking, which the API does not report
    separately, so reasoning_tokens stays 0.
    """
    u = resp.get('usage') or {}
    fresh = int(u.get('input_tokens') or 0)
    read = int(u.get('cache_read_input_tokens') or 0)
    write = int(u.get('cache_creation_input_tokens') or 0)
    out = int(u.get('output_tokens') or 0)
    return {'prompt_tokens': fresh + read + write, 'cached_tokens': read, 'cache_write_tokens': write,
            'completion_tokens': out, 'reasoning_tokens': 0, 'output_tokens': out}


def cost_of(cfg, model, usage):
    if usage.get('cost_usd_reported') is not None:
        return usage['cost_usd_reported']
    price = cfg['prices'].get(model)
    if not price:
        return None
    inp, cached_p, outp = price[:3]
    write_p = price[3] if len(price) > 3 else inp * CACHE_WRITE_MULTIPLIER
    written = usage.get('cache_write_tokens', 0)
    fresh = max(0, usage['prompt_tokens'] - usage['cached_tokens'] - written)
    return (fresh * inp + usage['cached_tokens'] * cached_p + written * write_p + usage['output_tokens'] * outp) / 1e6


def parse_answer(text, n_actions, size):
    """Return a list of action numbers or raise ValueError with a model-facing message."""
    body = (text or '').strip()
    body = re.sub(r'^```(?:json)?\s*|\s*```$', '', body)
    match = re.search(r'\{.*\}', body, re.S)
    if not match:
        raise ValueError('reply must be a JSON object like {"action": 3, "reason": "..."}')
    try:
        obj = json.loads(match.group(0))
    except json.JSONDecodeError as error:
        raise ValueError(f'reply is not valid JSON ({error.msg})') from None
    action = obj.get('action')
    picks = action if isinstance(action, list) else [action]
    nums = []
    for p in picks:
        if isinstance(p, str) and p.strip().lstrip('#').isdigit():
            p = int(p.strip().lstrip('#'))
        if not isinstance(p, int) or isinstance(p, bool) or not 0 <= p < n_actions:
            raise ValueError(f'"action" must be a legal action number 0..{n_actions - 1}, got {p!r}')
        nums.append(p)
    if size:
        lo, hi = size
        if not lo <= len(nums) <= hi:
            raise ValueError(f'this screen needs a list of {lo}' + (f'-{hi}' if hi != lo else '')
                             + f' action numbers, got {len(nums)}')
    elif len(nums) != 1:
        raise ValueError('choose exactly one action number')
    reason = str(obj.get('reason') or '').strip()[:300]
    return nums, reason


def to_action(refs, nums, size):
    if not size:
        return refs[nums[0]]
    chosen = [refs[n] for n in nums]
    names = [r.get('card') for r in chosen]
    if all(names) and len({r.get('action') for r in chosen}) == 1:
        return {'action': chosen[0]['action'], 'cards': names}
    return chosen


_HISTORY = collections.defaultdict(lambda: collections.deque(maxlen=64))
_PENDING = collections.Counter()  # usage of failed decisions, charged to the next reply


def build_messages(text, context, history_n):
    parts = []
    hist = list(_HISTORY[context.get('seed')])[-history_n:] if history_n else []
    if hist:
        parts.append('## Your recent decisions\n' + '\n'.join(hist))
    if context.get('last_error'):
        err = context['last_error']
        parts.append(f'## Last action failed: {err.get("error")}: {str(err.get("message") or "")[:200]}')
    if context.get('avoid'):
        parts.append('## Do not repeat (rejected in this exact state): '
                     + '; '.join(text_state.action_label(a) for a in context['avoid']))
    parts.append(text)
    return [{'role': 'system', 'content': SYSTEM_PROMPT}, {'role': 'user', 'content': '\n'.join(parts)}]


def anthropic_payload(cfg, messages):
    """Messages API body: system prompt top-level, max_tokens always set, effort under output_config."""
    system = '\n\n'.join(m['content'] for m in messages if m['role'] == 'system')
    payload = {'model': cfg['model'], 'max_tokens': cfg.get('max_tokens') or ANTHROPIC_MAX_TOKENS,
               'messages': [m for m in messages if m['role'] != 'system']}
    if system:
        payload['system'] = system
    if cfg.get('reasoning_effort'):
        payload['output_config'] = {'effort': cfg['reasoning_effort']}
    if cfg.get('thinking'):
        payload['thinking'] = {'type': cfg['thinking']}
    if cfg.get('temperature') is not None:  # only reachable with LLM_ANTHROPIC_VERSION before 2026-10-21
        payload['temperature'] = cfg['temperature']
    return payload


def complete(cfg, key, messages):
    """One model call. Returns the answer text, normalized usage, the served model id, the
    assistant turn to replay before a validation re-ask (None: re-ask without one) and a
    note appended to the validation error."""
    if cfg['provider'] == 'anthropic':
        resp = _request(cfg, key, '/messages', anthropic_payload(cfg, messages))
        blocks = [b for b in resp.get('content') or [] if isinstance(b, dict)]
        text = ''.join(str(b.get('text') or '') for b in blocks if b.get('type') == 'text')
        stop, note = resp.get('stop_reason'), ''
        if stop not in (None, 'end_turn', 'stop_sequence'):  # max_tokens, refusal, ...
            category = (resp.get('stop_details') or {}).get('category')
            note = f' (stop_reason={stop}' + (f', category={category}' if category else '') + ')'
        # Replay the reply unchanged (thinking blocks included): the API checks replayed thinking.
        # A reply without text cannot be an assistant turn; the re-ask is then a user turn only.
        turn = {'role': 'assistant', 'content': blocks} if text.strip() else None
        return {'text': text, 'usage': anthropic_usage_of(resp), 'served_model': resp.get('model'),
                'retry_turn': turn, 'note': note}
    payload = {'model': cfg['model'], 'messages': messages}
    for k in ('reasoning_effort', 'max_tokens', 'temperature'):
        if cfg.get(k) is not None:
            payload[k] = cfg[k]
    resp = _request(cfg, key, '/chat/completions', payload)
    text = ((resp.get('choices') or [{}])[0].get('message') or {}).get('content') or ''
    return {'text': text, 'usage': usage_of(resp), 'served_model': resp.get('model'),
            'retry_turn': {'role': 'assistant', 'content': text[:2000]}, 'note': ''}


def policy(observation, context, *, cfg=None, env=None):
    """run_bench policy entry point (``py:llm_policy:policy``)."""
    cfg = cfg or config(env)
    check_config(cfg)
    key = api_key(cfg, env)
    refs = text_state.legal_refs(observation)
    if not refs:
        raise LLMPolicyError('observation has no legal actions')
    size = text_state.selection_size(observation)
    text = text_state.encode(observation, facts=bool(context.get('wiki_facts_enabled')))
    messages = build_messages(text, context, cfg['history'])
    totals = collections.Counter()
    began = time.monotonic()
    errors = []
    for attempt in range(1, cfg['max_retries'] + 2):
        t0 = time.monotonic()
        try:
            reply = complete(cfg, key, messages)
        except LLMPolicyError:
            _PENDING.update(totals)
            raise
        totals['api_latency_s'] += time.monotonic() - t0
        usage = reply['usage']
        for k, v in usage.items():
            totals[k] += v
        cost = cost_of(cfg, reply['served_model'] or cfg['model'], usage)
        if cost is not None:
            totals['cost_usd'] += cost
        try:
            nums, reason = parse_answer(reply['text'], len(refs), size)
        except ValueError as error:
            problem = f'{error}{reply["note"]}'
            errors.append(problem)
            turns = [reply['retry_turn']] if reply['retry_turn'] else []
            messages = messages + turns + [{'role': 'user', 'content': f'Invalid answer: {problem}. Reply again with '
                                                                       f'only the JSON object.'}]
            continue
        action = to_action(refs, nums, size)
        st = (observation.get('state') or {}).get('state_type')
        _HISTORY[context.get('seed')].append(
            f'[{st}] {"; ".join(text_state.action_label(refs[n]) for n in nums)} - {reason[:120]}')
        carried = dict(_PENDING)
        _PENDING.clear()
        llm = {'provider': cfg['provider'], 'model': cfg['model'], 'served_model': reply['served_model'],
               'attempts': attempt, 'validation_errors': errors, 'latency_s': round(time.monotonic() - began, 3),
               'api_latency_s': round(totals.pop('api_latency_s', 0.0), 3),
               'prompt_chars': len(messages[1]['content'])}
        for k in ('prompt_tokens', 'cached_tokens', 'completion_tokens', 'reasoning_tokens', 'output_tokens'):
            llm[k] = int(totals.get(k, 0))
        if 'cache_write_tokens' in totals:  # anthropic only: cache writes (part of prompt_tokens)
            llm['cache_write_tokens'] = int(totals['cache_write_tokens'])
        llm['cost_usd'] = round(totals['cost_usd'], 6) if 'cost_usd' in totals else None
        if cfg.get('reasoning_effort'):
            llm['reasoning_effort'] = cfg['reasoning_effort']
        if cfg['provider'] == 'anthropic' and cfg.get('thinking'):
            llm['thinking'] = cfg['thinking']
        if carried:
            llm['carried_from_failed'] = {k: (round(v, 6) if isinstance(v, float) else v) for k, v in carried.items()}
        return {'action': action, 'reason': reason or 'llm gave no rationale', 'llm': llm}
    _PENDING.update(totals)
    raise LLMPolicyError(f'{cfg["model"]}: no valid action after {len(errors)} attempts: {errors[-1]}')


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument('--list-models', action='store_true', help='GET /models with the configured key')
    ap.add_argument('--provider', choices=sorted(PROVIDERS), help='overrides LLM_PROVIDER')
    ap.add_argument('--model', help='overrides LLM_MODEL')
    ap.add_argument('--base-url', help='overrides LLM_BASE_URL')
    a = ap.parse_args(argv)
    try:
        cfg = config(provider=a.provider, model=a.model, base_url=a.base_url.rstrip('/') if a.base_url else None)
        if a.list_models:
            models = list_models(cfg)
            for m in models:
                price = cfg['prices'].get(m)
                note = f'  ${price[0]}/{price[1]}/{price[2]} per 1M in/cached/out' if price else ''
                if price and len(price) > 3:
                    note += f', cache write ${price[3]}'
                print(m + note)
            return 0
        request = json.load(sys.stdin)  # cmd: policy protocol (sts2-bench-policy-request/v1)
        reply = policy(request['observation'], request.get('context') or {}, cfg=cfg)
        print(json.dumps(reply, ensure_ascii=False))
        return 0
    except LLMPolicyError as error:
        print(f'llm_policy: {redact(error)}', file=sys.stderr)
        return 2


if __name__ == '__main__':
    sys.exit(main())
