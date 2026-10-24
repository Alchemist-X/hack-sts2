"""Tests for scripts/bench/llm_policy.py and compare_models.py against a mock OpenAI-compatible server."""
import json
import subprocess
import sys
import threading
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


def observation(name):
    state = json.loads((FIX / f'{name}.json').read_text())
    return {'hash': 'h', 'state': state, 'legal_actions': refs.observe_actions(state)}


class Mock:
    def __init__(self):
        self.replies, self.requests, self.delay = [], [], 0.0

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

            def do_GET(self):
                mock.requests.append(('GET', self.path, self.headers.get('Authorization'), None))
                self._send(200, {'data': [{'id': 'grok-4.3'}, {'id': 'grok-4.7'}]})

            def do_POST(self):
                body = json.loads(self.rfile.read(int(self.headers['Content-Length'])))
                mock.requests.append(('POST', self.path, self.headers.get('Authorization'), body))
                code, reply = mock.replies.pop(0)
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


def cfg(mock, **kw):
    env = {'LLM_MODEL': 'grok-4.3', 'LLM_BASE_URL': mock.url, 'XAI_API_KEY': KEY, 'LLM_HTTP_RETRIES': '0',
           'LLM_TIMEOUT_S': '5', **kw}
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


# ------------------------------------------------------------ compare_models
def _write_run(model_dir, seed, floor, win, llm_cost):
    seed_dir = model_dir / seed
    seed_dir.mkdir(parents=True)
    (seed_dir / 'result.json').write_text(json.dumps({'seed': seed, 'policy': 'py:llm_policy:policy', 'floor': floor,
                                                      'act': 1 + floor // 17, 'win': win, 'boss_kills': floor // 17,
                                                      'decisions': 2, 'wall_time_s': 10}))
    lines = [{'type': 'decision', 'latency_s': {'policy': 2.0, 'act': 3.0},
              'llm': {'model': model_dir.name, 'attempts': a, 'latency_s': 2.0, 'prompt_tokens': 1000,
                      'cached_tokens': 500, 'output_tokens': 50, 'reasoning_tokens': 0, 'cost_usd': llm_cost}}
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
