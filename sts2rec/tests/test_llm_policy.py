"""Tests for scripts/bench/llm_policy.py and compare_models.py against a mock OpenAI-compatible /
Anthropic Messages API server (no real API calls)."""
import json
import subprocess
import sys
import threading
import types
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path

import pytest

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'scripts'))
sys.path.insert(0, str(ROOT / 'scripts' / 'bench'))
import compare_models  # noqa: E402
import llm_policy  # noqa: E402
import policies  # noqa: E402
import sts2_action_refs as refs  # noqa: E402

FIX = Path(__file__).parent / 'fixtures' / 'live_frictions'
KEY = 'xai-TESTKEY-not-a-real-secret-123456'
AKEY = 'sk-ant-api03-TESTKEY-not-a-real-secret-123456'
HEADERS = ('authorization', 'x-api-key', 'anthropic-version')


def observation(name):
    state = json.loads((FIX / f'{name}.json').read_text())
    return {'hash': 'h', 'state': state, 'legal_actions': refs.observe_actions(state)}


class Mock:
    def __init__(self):
        self.replies, self.requests, self.delay = [], [], 0.0
        self.headers, self.sent, self.models = [], [], ['grok-4.3', 'grok-4.7']

    def __enter__(self):
        mock = self

        class Handler(BaseHTTPRequestHandler):
            def log_message(self, *a):
                pass

            def _send(self, code, body):
                data = json.dumps(body).encode()
                self.send_response(code)
                self.send_header('Content-Type', 'application/json')
                self.send_header('Content-Length', str(len(data)))
                self.end_headers()
                self.wfile.write(data)

            def _record(self, method, body):
                mock.requests.append((method, self.path, self.headers.get('Authorization'), body))
                mock.headers.append({h: self.headers.get(h) for h in HEADERS})

            def do_GET(self):
                self._record('GET', None)
                self._send(200, {'data': [{'id': m} for m in mock.models]})

            def do_POST(self):
                self._record('POST', json.loads(self.rfile.read(int(self.headers['Content-Length']))))
                code, reply = mock.replies.pop(0)
                mock.sent.append(reply)
                if mock.delay:
                    threading.Event().wait(mock.delay)
                self._send(code, reply)

        self.server = ThreadingHTTPServer(('127.0.0.1', 0), Handler)
        threading.Thread(target=self.server.serve_forever, daemon=True).start()
        self.url = f'http://127.0.0.1:{self.server.server_address[1]}/v1'
        return self

    def __exit__(self, *exc):
        self.server.shutdown()

    def answer(self, content, prompt=1000, completion=20, reasoning=0, cached=0, ticks=None):
        usage = {'prompt_tokens': prompt, 'completion_tokens': completion,
                 'total_tokens': prompt + completion + reasoning,
                 'prompt_tokens_details': {'cached_tokens': cached},
                 'completion_tokens_details': {'reasoning_tokens': reasoning}}
        if ticks is not None:
            usage['cost_in_usd_ticks'] = ticks
        self.replies.append((200, {'model': 'grok-4.3', 'choices': [{'message': {'content': content}}],
                                   'usage': usage}))

    def claude(self, text, inp=1000, out=20, read=0, write=0, stop='end_turn', thinking=True, category=None,
               model='claude-opus-5-5'):
        """A Messages API reply: an (empty, signed) thinking block, then the text block unless text is None."""
        content = [{'type': 'thinking', 'thinking': '', 'signature': f'sig-{len(self.replies)}'}] if thinking else []
        if text is not None:
            content.append({'type': 'text', 'text': text, 'citations': None})
        reply = {'id': f'msg_{len(self.replies)}', 'type': 'message', 'role': 'assistant', 'model': model,
                 'content': content, 'stop_reason': stop, 'stop_sequence': None,
                 'usage': {'input_tokens': inp, 'cache_creation_input_tokens': write,
                           'cache_read_input_tokens': read, 'output_tokens': out}}
        if category:
            reply['stop_details'] = {'type': 'refusal', 'category': category, 'explanation': None}
        self.replies.append((200, reply))


def cfg(mock, **kw):
    env = {'LLM_MODEL': 'grok-4.3', 'LLM_BASE_URL': mock.url, 'XAI_API_KEY': KEY, 'LLM_HTTP_RETRIES': '0',
           'LLM_TIMEOUT_S': '5', **kw}
    return llm_policy.config(env), env


def acfg(mock, **kw):
    env = {'LLM_PROVIDER': 'anthropic', 'LLM_MODEL': 'claude-opus-5-5', 'LLM_BASE_URL': mock.url,
           'ANTHROPIC_API_KEY': AKEY, 'LLM_HTTP_RETRIES': '0', 'LLM_TIMEOUT_S': '5', **kw}
    return llm_policy.config(env), env


@pytest.fixture(autouse=True)
def fresh_state():
    llm_policy._HISTORY.clear()
    llm_policy._PENDING.clear()


def test_missing_key_fails_clearly():
    c = llm_policy.config({'LLM_MODEL': 'grok-4.3'})
    with pytest.raises(llm_policy.LLMPolicyError, match='XAI_API_KEY is not set'):
        llm_policy.policy(observation('combat'), {}, cfg=c, env={})
    assert issubclass(llm_policy.LLMPolicyError, policies.PolicyError)  # run_bench records a friction


def test_cli_missing_key_exit_code(tmp_path):
    proc = subprocess.run([sys.executable, str(ROOT / 'scripts/bench/llm_policy.py'), '--list-models'],
                          capture_output=True, text=True, env={'PATH': '/usr/bin:/bin'})
    assert proc.returncode == 2 and 'XAI_API_KEY is not set' in proc.stderr


def test_success_maps_number_to_ref_and_reports_telemetry():
    obs = observation('combat_three_inklets')
    with Mock() as mock:
        mock.answer('{"action": 7, "reason": "Twin Strike the 12 HP Inklet"}', prompt=1200, completion=30,
                    reasoning=100, cached=1000)
        c, env = cfg(mock, LLM_REASONING_EFFORT='low')
        reply = llm_policy.policy(obs, {'seed': 'S', 'wiki_facts_enabled': False}, cfg=c, env=env)
    assert reply['action'] == obs['legal_actions'][7]['ref']
    assert reply['action'] == {'action': 'play_card', 'card': 'Twin Strike+', 'target': 'Inklet@1'}
    llm = reply['llm']
    assert llm['model'] == 'grok-4.3' and llm['attempts'] == 1 and llm['reasoning_effort'] == 'low'
    assert llm['prompt_tokens'] == 1200 and llm['cached_tokens'] == 1000
    assert llm['output_tokens'] == 130  # xAI: reasoning billed on top of completion
    assert llm['cost_usd'] == pytest.approx((200 * 1.25 + 1000 * 0.20 + 130 * 2.50) / 1e6)
    method, path, auth, body = mock.requests[0]
    assert path == '/v1/chat/completions' and auth == f'Bearer {KEY}'
    assert body['model'] == 'grok-4.3' and body['reasoning_effort'] == 'low'
    prompt = body['messages'][1]['content']
    assert '7: play_card card=Twin Strike+ target=Inklet@1' in prompt and KEY not in prompt
    # decide() keeps the telemetry for the trajectory
    out, _ = policies.decide(lambda o, c: reply, obs, {})
    assert out['llm'] is llm


def test_reported_cost_wins_and_history_is_shown():
    obs = observation('combat')
    with Mock() as mock:
        mock.answer('```json\n{"action": "0", "reason": "bash first"}\n```', ticks=123_000_000)
        mock.answer('{"action": 1, "reason": "block"}')
        c, env = cfg(mock)
        first = llm_policy.policy(obs, {'seed': 'S'}, cfg=c, env=env)
        llm_policy.policy(obs, {'seed': 'S', 'last_error': {'error': 'x', 'message': 'boom'},
                                'avoid': [obs['legal_actions'][0]['ref']]}, cfg=c, env=env)
    assert first['llm']['cost_usd'] == pytest.approx(0.0123)
    second_prompt = mock.requests[1][3]['messages'][1]['content']
    assert '## Your recent decisions' in second_prompt and 'bash first' in second_prompt
    assert 'Last action failed: x: boom' in second_prompt and 'Do not repeat' in second_prompt


def test_illegal_answer_is_retried_with_the_error():
    obs = observation('map')
    with Mock() as mock:
        mock.answer('I would go left.')
        mock.answer('{"action": 9, "reason": "?"}')
        mock.answer('{"action": 1, "reason": "path with more events"}')
        c, env = cfg(mock)
        reply = llm_policy.policy(obs, {}, cfg=c, env=env)
    assert reply['action'] == obs['legal_actions'][1]['ref']
    assert reply['llm']['attempts'] == 3 and len(reply['llm']['validation_errors']) == 2
    assert reply['llm']['prompt_tokens'] == 3000  # all attempts are counted
    retry = mock.requests[2][3]['messages']
    assert 'legal action number 0..1' in retry[-1]['content'] and retry[-2]['role'] == 'assistant'


def test_gives_up_after_retries_and_carries_usage():
    obs = observation('map')
    with Mock() as mock:
        for _ in range(3):
            mock.answer('{"action": 5}')
        mock.answer('{"action": 0, "reason": "ok now"}')
        c, env = cfg(mock, LLM_MAX_RETRIES='2')
        with pytest.raises(llm_policy.LLMPolicyError, match='no valid action after 3 attempts'):
            llm_policy.policy(obs, {}, cfg=c, env=env)
        reply = llm_policy.policy(obs, {}, cfg=c, env=env)
    assert reply['llm']['carried_from_failed']['prompt_tokens'] == 3000


def test_multi_select_returns_cards_list():
    obs = observation('card_select_upgrade')
    with Mock() as mock:
        mock.answer('{"action": [0, 0, 2, 6], "reason": "upgrade strikes"}')
        c, env = cfg(mock)
        reply = llm_policy.policy(obs, {}, cfg=c, env=env)
        mock.answer('{"action": 3, "reason": "one only"}')
        mock.answer('{"action": [0,1,2,3], "reason": "four"}')
        again = llm_policy.policy(obs, {}, cfg=c, env=env)
    assert reply['action'] == {'action': 'select_card', 'cards': ['Strike', 'Strike', 'Setup Strike', 'Hemokinesis']}
    assert again['llm']['attempts'] == 2


def test_timeout_and_http_errors_do_not_leak_the_key():
    obs = observation('combat')
    with Mock() as mock:
        mock.delay = 1.5
        mock.answer('{"action": 0, "reason": "late"}')
        c, env = cfg(mock, LLM_TIMEOUT_S='0.3')
        with pytest.raises(llm_policy.LLMPolicyError) as err:
            llm_policy.policy(obs, {}, cfg=c, env=env)
        assert KEY not in str(err.value)
        mock.delay = 0
        mock.replies.append((401, {'error': f'bad key {KEY}'}))
        c, env = cfg(mock)
        with pytest.raises(llm_policy.LLMPolicyError, match='HTTP 401') as err:
            llm_policy.policy(obs, {}, cfg=c, env=env)
        assert KEY not in str(err.value) and 'xai-***' in str(err.value)


def test_list_models_and_any_base_url():
    with Mock() as mock:
        c, env = cfg(mock, LLM_API_KEY_ENV='OTHER_KEY', OTHER_KEY='sk-other')
        assert llm_policy.list_models(c, env) == ['grok-4.3', 'grok-4.7']
    assert mock.requests[0][:3] == ('GET', '/v1/models', 'Bearer sk-other')
    assert set(llm_policy.CANDIDATES) <= set(llm_policy.PRICES)


def test_cmd_mode_round_trip():
    obs = observation('rewards')
    with Mock() as mock:
        mock.answer('{"action": 0, "reason": "take gold"}')
        env = {'PATH': '/usr/bin:/bin', 'LLM_MODEL': 'grok-4.3', 'LLM_BASE_URL': mock.url, 'XAI_API_KEY': KEY}
        request = {'schema': 'sts2-bench-policy-request/v1', 'context': {}, 'observation': obs}
        proc = subprocess.run([sys.executable, str(ROOT / 'scripts/bench/llm_policy.py')], input=json.dumps(request),
                              capture_output=True, text=True, env=env, timeout=30)
    assert proc.returncode == 0, proc.stderr
    reply = json.loads(proc.stdout.strip().splitlines()[-1])
    assert reply['action'] == obs['legal_actions'][0]['ref'] and KEY not in proc.stdout + proc.stderr


# ------------------------------------------------------------ anthropic provider
def test_provider_defaults_and_config_guards():
    default = llm_policy.config({})
    assert (default['provider'], default['base_url'], default['key_env']) == \
        ('openai', 'https://api.x.ai/v1', 'XAI_API_KEY')  # unchanged when LLM_PROVIDER is unset
    c = llm_policy.config({'LLM_PROVIDER': 'Anthropic', 'LLM_MODEL': 'claude-opus-5-5'})
    assert (c['provider'], c['base_url'], c['key_env']) == \
        ('anthropic', 'https://api.anthropic.com/v1', 'ANTHROPIC_API_KEY')
    assert llm_policy.config({'LLM_PROVIDER': 'anthropic', 'LLM_API_KEY_ENV': 'MY_KEY'})['key_env'] == 'MY_KEY'
    assert llm_policy.config({'LLM_PROVIDER': 'openai-compatible'})['provider'] == 'openai'
    with pytest.raises(llm_policy.LLMPolicyError, match='ANTHROPIC_API_KEY is not set'):
        llm_policy.policy(observation('combat'), {}, cfg=c, env={'XAI_API_KEY': KEY})
    with pytest.raises(llm_policy.LLMPolicyError, match='unknown LLM_PROVIDER'):
        llm_policy.config({'LLM_PROVIDER': 'gemini'})
    # settings that would 400 on every decision fail before any request
    hot = llm_policy.config({'LLM_PROVIDER': 'anthropic', 'LLM_MODEL': 'claude-opus-5-5', 'LLM_TEMPERATURE': '0'})
    with pytest.raises(llm_policy.LLMPolicyError, match='LLM_TEMPERATURE is rejected'):
        llm_policy.policy(observation('combat'), {}, cfg=hot, env={'ANTHROPIC_API_KEY': AKEY})
    misrouted = llm_policy.config({'LLM_MODEL': 'claude-opus-5-5'})
    with pytest.raises(llm_policy.LLMPolicyError, match='set LLM_PROVIDER=anthropic'):
        llm_policy.policy(observation('combat'), {}, cfg=misrouted, env={'XAI_API_KEY': KEY})
    with pytest.raises(llm_policy.LLMPolicyError, match='LLM_MODEL is not set'):
        llm_policy.policy(observation('combat'), {}, cfg=llm_policy.config({'LLM_PROVIDER': 'anthropic'}), env={})


def test_anthropic_messages_request_and_telemetry():
    obs = observation('combat_three_inklets')
    with Mock() as mock:
        mock.claude('{"action": 7, "reason": "Twin Strike the 12 HP Inklet"}', inp=200, read=1000, out=130)
        c, env = acfg(mock, LLM_REASONING_EFFORT='low')
        reply = llm_policy.policy(obs, {'seed': 'S', 'wiki_facts_enabled': False}, cfg=c, env=env)
    assert reply['action'] == obs['legal_actions'][7]['ref']
    assert reply['reason'] == 'Twin Strike the 12 HP Inklet'
    llm = reply['llm']
    assert (llm['provider'], llm['model'], llm['served_model']) == ('anthropic', 'claude-opus-5-5', 'claude-opus-5-5')
    assert llm['attempts'] == 1 and llm['reasoning_effort'] == 'low'
    # input_tokens excludes cache reads/writes; thinking is inside output_tokens
    assert llm['prompt_tokens'] == 1200 and llm['cached_tokens'] == 1000 and llm['cache_write_tokens'] == 0
    assert llm['completion_tokens'] == llm['output_tokens'] == 130 and llm['reasoning_tokens'] == 0
    assert llm['cost_usd'] == pytest.approx((200 * 4.00 + 1000 * 0.20 + 130 * 20.00) / 1e6)
    method, path, auth, body = mock.requests[0]
    assert (method, path, auth) == ('POST', '/v1/messages', None)  # never a Bearer header
    assert mock.headers[0]['x-api-key'] == AKEY and mock.headers[0]['anthropic-version'] == '2026-10-21'
    assert body['model'] == 'claude-opus-5-5' and body['max_tokens'] == llm_policy.ANTHROPIC_MAX_TOKENS
    assert body['system'] == llm_policy.SYSTEM_PROMPT and [m['role'] for m in body['messages']] == ['user']
    assert body['output_config'] == {'effort': 'low'}
    assert not {'reasoning_effort', 'temperature', 'thinking'} & set(body)
    prompt = body['messages'][0]['content']
    assert '7: play_card card=Twin Strike+ target=Inklet@1' in prompt and AKEY not in prompt
    out, _ = policies.decide(lambda o, ctx: reply, obs, {})
    assert out['llm'] is llm


def test_anthropic_cache_write_pricing_thinking_and_max_tokens():
    obs = observation('rewards')
    with Mock() as mock:
        mock.claude('{"action": 0, "reason": "take gold"}', inp=100, write=2000, out=10, model='claude-sonnet-5-5')
        mock.claude('{"action": 0, "reason": "take gold"}', inp=100, write=2000, out=10, model='claude-sonnet-5-5')
        c, env = acfg(mock, LLM_MODEL='claude-sonnet-5-5', LLM_MAX_TOKENS='4096', LLM_THINKING='adaptive')
        priced = llm_policy.policy(obs, {}, cfg=c, env=env)['llm']
        c, env = acfg(mock, LLM_MODEL='claude-sonnet-5-5', LLM_PRICES_JSON='{"claude-sonnet-5-5": [1, 0.1, 5]}')
        override = llm_policy.policy(obs, {}, cfg=c, env=env)['llm']
    body = mock.requests[0][3]
    assert body['max_tokens'] == 4096 and body['thinking'] == {'type': 'adaptive'} and 'output_config' not in body
    assert priced['thinking'] == 'adaptive' and priced['cache_write_tokens'] == 2000 and priced['prompt_tokens'] == 2100
    assert priced['cost_usd'] == pytest.approx((100 * 2.00 + 2000 * 2.50 + 10 * 10.00) / 1e6)
    # a 3-element price bills cache writes at 1.25x input
    assert override['cost_usd'] == pytest.approx((100 * 1 + 2000 * 1.25 + 10 * 5) / 1e6)
    assert mock.requests[1][3]['max_tokens'] == llm_policy.ANTHROPIC_MAX_TOKENS


def test_anthropic_retry_replays_the_reply_unchanged():
    obs = observation('map')
    with Mock() as mock:
        mock.claude('{"action": 9, "reason": "?"}')
        mock.claude('{"action": 1, "reason": "path with more events"}')
        c, env = acfg(mock)
        reply = llm_policy.policy(obs, {}, cfg=c, env=env)
    assert reply['action'] == obs['legal_actions'][1]['ref']
    assert reply['llm']['attempts'] == 2 and reply['llm']['prompt_tokens'] == 2000
    retry = mock.requests[1][3]
    assert retry['system'] == llm_policy.SYSTEM_PROMPT
    assert [m['role'] for m in retry['messages']] == ['user', 'assistant', 'user']
    # append-only: the first reply's blocks (signed thinking block included) go back exactly as received
    assert retry['messages'][1]['content'] == mock.sent[0]['content']
    assert retry['messages'][0] == mock.requests[0][3]['messages'][0]
    assert 'legal action number 0..1' in retry['messages'][2]['content']


def test_anthropic_reply_without_text_is_re_asked_without_an_assistant_turn():
    obs = observation('map')
    with Mock() as mock:
        mock.claude(None, out=16000, stop='max_tokens')  # thinking used the whole budget
        mock.claude(None, thinking=False, out=0, stop='refusal', category='cyber')
        mock.claude('{"action": 0, "reason": "ok"}')
        c, env = acfg(mock)
        reply = llm_policy.policy(obs, {}, cfg=c, env=env)
    errors = reply['llm']['validation_errors']
    assert reply['llm']['attempts'] == 3 and reply['llm']['output_tokens'] == 16020
    assert 'stop_reason=max_tokens' in errors[0] and 'stop_reason=refusal, category=cyber' in errors[1]
    assert [m['role'] for m in mock.requests[1][3]['messages']] == ['user', 'user']
    third = mock.requests[2][3]['messages']
    assert [m['role'] for m in third] == ['user', 'user', 'user'] and all(m['content'] for m in third)


def test_anthropic_overload_is_retried_and_errors_never_leak_the_key(monkeypatch):
    monkeypatch.setattr(llm_policy.time, 'sleep', lambda s: None)  # skip the backoff wait
    obs = observation('combat')
    with Mock() as mock:
        mock.replies.append((529, {'type': 'error', 'error': {'type': 'overloaded_error', 'message': 'Overloaded'}}))
        mock.claude('{"action": 0, "reason": "after the overload"}')
        c, env = acfg(mock, LLM_HTTP_RETRIES='1')
        assert llm_policy.policy(obs, {}, cfg=c, env=env)['llm']['attempts'] == 1
        mock.replies.append((401, {'type': 'error', 'error': {'type': 'authentication_error',
                                                              'message': f'invalid x-api-key {AKEY}'}}))
        c, env = acfg(mock)
        with pytest.raises(llm_policy.LLMPolicyError, match='HTTP 401') as err:
            llm_policy.policy(obs, {}, cfg=c, env=env)
        assert AKEY not in str(err.value) and 'sk-ant-***' in str(err.value)
        # a key without a recognizable prefix is scrubbed by value
        mock.replies.append((400, {'error': 'echo plain-gateway-secret-0123456789'}))
        c, env = acfg(mock, LLM_API_KEY_ENV='GATEWAY_KEY', GATEWAY_KEY='plain-gateway-secret-0123456789')
        with pytest.raises(llm_policy.LLMPolicyError, match='HTTP 400') as err:
            llm_policy.policy(obs, {}, cfg=c, env=env)
        assert 'plain-gateway-secret' not in str(err.value) and 'echo ***' in str(err.value)
    assert [r[1] for r in mock.requests] == ['/v1/messages'] * 4
    assert mock.headers[3]['x-api-key'] == 'plain-gateway-secret-0123456789'


def test_telemetry_shape_is_identical_across_providers():
    obs = observation('rewards')
    with Mock() as mock:
        mock.answer('{"action": 0, "reason": "take gold"}')
        mock.claude('{"action": 0, "reason": "take gold"}')
        c, env = cfg(mock)
        openai_llm = llm_policy.policy(obs, {}, cfg=c, env=env)['llm']
        c, env = acfg(mock)
        claude_llm = llm_policy.policy(obs, {}, cfg=c, env=env)['llm']
    assert openai_llm['provider'] == 'openai' and 'cache_write_tokens' not in openai_llm
    assert set(claude_llm) - set(openai_llm) == {'cache_write_tokens'} and set(openai_llm) <= set(claude_llm)
    assert mock.requests[0][1] == '/v1/chat/completions' and mock.requests[0][2] == f'Bearer {KEY}'


def test_anthropic_list_models_and_cli():
    obs = observation('rewards')
    with Mock() as mock:
        mock.models = ['claude-opus-5-5', 'claude-haiku-5-5']
        c, env = acfg(mock)
        assert llm_policy.list_models(c, env) == ['claude-haiku-5-5', 'claude-opus-5-5']
        mock.claude('{"action": 0, "reason": "take gold"}')
        cli_env = {'PATH': '/usr/bin:/bin', 'LLM_MODEL': 'claude-opus-5-5', 'LLM_BASE_URL': mock.url,
                   'ANTHROPIC_API_KEY': AKEY}
        request = {'schema': 'sts2-bench-policy-request/v1', 'context': {}, 'observation': obs}
        proc = subprocess.run([sys.executable, str(ROOT / 'scripts/bench/llm_policy.py'), '--provider', 'anthropic'],
                              input=json.dumps(request), capture_output=True, text=True, env=cli_env, timeout=30)
    assert mock.requests[0][:2] == ('GET', '/v1/models?limit=1000') and mock.headers[0]['x-api-key'] == AKEY
    assert proc.returncode == 0, proc.stderr
    reply = json.loads(proc.stdout.strip().splitlines()[-1])
    assert reply['action'] == obs['legal_actions'][0]['ref'] and reply['llm']['provider'] == 'anthropic'
    assert AKEY not in proc.stdout + proc.stderr
    missing = subprocess.run([sys.executable, str(ROOT / 'scripts/bench/llm_policy.py'), '--provider', 'anthropic',
                              '--list-models'], capture_output=True, text=True, env={'PATH': '/usr/bin:/bin'})
    assert missing.returncode == 2 and 'ANTHROPIC_API_KEY is not set' in missing.stderr
    bad = subprocess.run([sys.executable, str(ROOT / 'scripts/bench/llm_policy.py'), '--list-models'],
                         capture_output=True, text=True, env={'PATH': '/usr/bin:/bin', 'LLM_PROVIDER': 'gemini'})
    assert bad.returncode == 2 and 'unknown LLM_PROVIDER' in bad.stderr
    assert set(llm_policy.ANTHROPIC_CANDIDATES) <= set(llm_policy.PRICES)


# ------------------------------------------------------------ compare_models
def _write_run(model_dir, seed, floor, win, llm_cost, provider=None):
    seed_dir = model_dir / seed
    seed_dir.mkdir(parents=True)
    (seed_dir / 'result.json').write_text(json.dumps({'seed': seed, 'policy': 'py:llm_policy:policy', 'floor': floor,
                                                      'act': 1 + floor // 17, 'win': win, 'boss_kills': floor // 17,
                                                      'decisions': 2, 'wall_time_s': 10}))
    lines = [{'type': 'decision', 'latency_s': {'policy': 2.0, 'act': 3.0},
              'llm': {'model': model_dir.name, 'attempts': a, 'latency_s': 2.0, 'prompt_tokens': 1000,
                      'cached_tokens': 500, 'output_tokens': 50, 'reasoning_tokens': 0, 'cost_usd': llm_cost,
                      **({'provider': provider, 'cache_write_tokens': 0} if provider else {})}}
             for a in (1, 2)] + [{'type': 'friction', 'kind': 'policy_error'}]
    (seed_dir / 'trajectory.jsonl').write_text(''.join(json.dumps(x) + '\n' for x in lines))


def test_compare_models_summary(tmp_path):
    _write_run(tmp_path / 'grok-4.3', 'S1', 20, False, 0.01)
    _write_run(tmp_path / 'grok-4.3', 'S2', 51, True, 0.01)
    _write_run(tmp_path / 'grok-4.7', 'S1', 10, False, 0.02)
    text = compare_models.write_summary(tmp_path)
    rows = {r['model']: r for r in json.loads((tmp_path / 'summary.json').read_text())}
    a = rows['grok-4.3']
    assert a['runs'] == 2 and a['wins'] == 1 and a['mean_floor'] == 35.5 and a['boss_kills'] == 4
    assert a['decisions'] == 4 and a['prompt_tokens'] == 4000 and a['retries'] == 2 and a['policy_errors'] == 2
    assert a['cost_usd'] == pytest.approx(0.04) and a['cost_per_run_usd'] == pytest.approx(0.02)
    assert a['tokens_per_decision'] == 1050 and a['mean_act_s'] == 3.0
    assert '| grok-4.7 | 1 | 0 | 10 |' in text


def test_compare_models_guards(tmp_path):
    pool = tmp_path / 'worker-pool-bench'
    (pool / 'inst1').mkdir(parents=True)
    (pool / 'inst1' / 'port').write_text('15611')
    with pytest.raises(SystemExit, match='worker-pool-llm only'):
        compare_models.check_pool(pool, [1])
    llm = tmp_path / 'worker-pool-llm'
    (llm / 'inst1').mkdir(parents=True)
    (llm / 'inst1' / 'port').write_text('15611')
    with pytest.raises(SystemExit, match='belongs to another pool'):
        compare_models.check_pool(llm, [1])
    (llm / 'inst1' / 'port').write_text('15641')
    compare_models.check_pool(llm, [1])


def test_compare_models_summary_keeps_the_provider(tmp_path):
    _write_run(tmp_path / 'grok-4.3', 'S1', 20, False, 0.01)  # a trajectory from before the provider field
    _write_run(tmp_path / 'claude-opus-5-5', 'S1', 30, False, 0.03, provider='anthropic')
    compare_models.write_summary(tmp_path)
    rows = {r['model']: r for r in json.loads((tmp_path / 'summary.json').read_text())}
    assert rows['claude-opus-5-5']['provider'] == 'anthropic' and rows['grok-4.3']['provider'] is None
    assert rows['claude-opus-5-5']['prompt_tokens'] == 2000 and rows['claude-opus-5-5']['cost_usd'] == pytest.approx(0.06)


def test_compare_models_per_model_provider(tmp_path, capsys, monkeypatch):
    for var in ('LLM_PROVIDER', 'LLM_API_KEY_ENV', 'LLM_BASE_URL', 'LLM_TEMPERATURE', 'LLM_MODEL'):
        monkeypatch.delenv(var, raising=False)
    assert compare_models.split_model('anthropic:claude-opus-5-5') == ('anthropic', 'claude-opus-5-5')
    assert compare_models.split_model('openai:grok-4.3') == ('openai', 'grok-4.3')
    assert compare_models.split_model('grok-4.3') == (None, 'grok-4.3')
    assert compare_models.split_model('vendor/model:free') == (None, 'vendor/model:free')
    assert compare_models.model_env('anthropic:claude-opus-5-5', {'X': '1'}) == \
        {'X': '1', 'LLM_MODEL': 'claude-opus-5-5', 'LLM_PROVIDER': 'anthropic'}
    mixed = ['grok-4.3', 'anthropic:claude-opus-5-5']
    with pytest.raises(SystemExit, match=r'ANTHROPIC_API_KEY is not set \(needed by anthropic:claude-opus-5-5\)') as err:
        compare_models.check_models(mixed, {'XAI_API_KEY': KEY})
    assert KEY not in str(err.value)
    assert compare_models.check_models(mixed, {'XAI_API_KEY': KEY, 'ANTHROPIC_API_KEY': AKEY}) == \
        {'grok-4.3': 'openai', 'anthropic:claude-opus-5-5': 'anthropic'}
    with pytest.raises(SystemExit, match='duplicate model ids'):
        compare_models.check_models(['claude-opus-5-5', 'anthropic:claude-opus-5-5'], {}, need_keys=False)
    with pytest.raises(SystemExit, match='anthropic:claude-opus-5-5'):  # a bare claude id would go to xAI
        compare_models.check_models(['claude-opus-5-5'], {}, need_keys=False)
    # a (sim-engine) launch: each model's process gets its own provider; keys stay out of argv and output
    monkeypatch.setenv('XAI_API_KEY', KEY)
    monkeypatch.setenv('ANTHROPIC_API_KEY', AKEY)
    launched = []
    monkeypatch.setattr(compare_models.subprocess, 'run',
                        lambda cmd, env, **kw: launched.append((cmd, env)) or types.SimpleNamespace(returncode=0))
    rc = compare_models.main(['--engine', 'sim', '--models', ','.join(mixed), '--seeds', 'BENCHA0S01',
                              '--out', str(tmp_path / 'cmp')])
    assert rc == 0
    assert {env['LLM_MODEL']: env.get('LLM_PROVIDER') for _, env in launched} == \
        {'grok-4.3': None, 'claude-opus-5-5': 'anthropic'}
    assert sorted(Path(cmd[cmd.index('--out') + 1]).name for cmd, _ in launched) == ['claude-opus-5-5', 'grok-4.3']
    assert all(KEY not in ' '.join(cmd) and AKEY not in ' '.join(cmd) for cmd, _ in launched)
    printed = capsys.readouterr().out
    assert '[anthropic:claude-opus-5-5] provider anthropic' in printed and '[grok-4.3] provider openai' in printed
    assert KEY not in printed and AKEY not in printed
