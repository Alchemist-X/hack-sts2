#!/usr/bin/env python3
"""One external decision at a time, with durable per-game narration and evidence."""
import argparse
import collections
import hashlib
import json
import sys
import time
from datetime import datetime, timezone
from pathlib import Path

import sts2_action_executor as executor
from sts2_run_audit import run_report, usage_report

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / 'sts2rec'))
from sts2rec.information import build_information_view
from sts2rec.knowledge import visible_knowledge


def public_display(state):
    view = build_information_view(state, 'limited')
    public = view['observation']
    for key in ('draw_pile', 'discard_pile', 'exhaust_pile', 'deck'):
        cards = public.get('player', {}).get(key)
        if isinstance(cards, list):
            # Different enchantments/costs/text are distinct public card states.
            counts = collections.Counter(json.dumps({k:v for k,v in c.items() if k != 'index'},
                ensure_ascii=False, sort_keys=True) for c in cards)
            public['player'][key] = [{**json.loads(card), 'count':count} for card,count in sorted(counts.items())]
    actions = [{k:v for k,v in a.items() if k not in {'candidate', 'target_candidate'}} for a in view['legal_actions']]
    return {'hash': executor.decision_digest(state), 'state': public, 'legal_actions': actions, 'audit': view['action_space_audit']}


def snapshot(out, state, knowledge_root, decision_id=None):
    view = build_information_view(state, 'limited')
    knowledge = visible_knowledge(view['observation'], knowledge_root)
    executor.append_jsonl(out / 'observations.jsonl', {
        'type': 'observation', 'timestamp': datetime.now(timezone.utc).isoformat(),
        'decision_id': decision_id, 'decision_hash': executor.decision_digest(state),
        'raw_state_sha256': executor.digest(state), **view,
        'knowledge_references': knowledge['references'], 'knowledge_missing': knowledge['missing'],
        'rule_authority': knowledge['authority'], 'rule_checks': knowledge['decision_context']['rules']})
    # Persist the corpus, and also return the relevant facts to the controller.
    known_path = out / 'knowledge.jsonl'
    known = {json.loads(l)['model_id'] for l in known_path.read_text().splitlines()} if known_path.exists() else set()
    new = [f for f in knowledge['facts'] if f['model_id'] not in known]
    for fact in new:
        executor.append_jsonl(known_path, fact)
    result = public_display(state)
    result['knowledge_file'] = str(known_path.resolve())
    result['new_knowledge_ids'] = [f['model_id'] for f in new]
    result['knowledge_missing'] = knowledge['missing']
    result['decision_context'] = knowledge['decision_context']
    result['knowledge_references'] = knowledge['references']
    return result


def start_session(out, plan, memories, mode, port, version, codex_session):
    if (out / 'session.json').exists() or (out / 'decisions.jsonl').exists():
        raise SystemExit('Existing run directory; resume it without start, or use a new directory')
    # Read and preserve the exact working plan and memories before first gameplay.
    plan_data = json.loads(plan.read_text(encoding='utf-8'))
    for field in ('immediate_threat', 'deck_plan', 'draw_plan', 'potion_budget', 'stop_conditions'):
        if field not in plan_data:
            raise SystemExit(f'plan missing: {field}')
    out.mkdir(parents=True, exist_ok=True)
    (out / 'plan.json').write_text(json.dumps(plan_data, ensure_ascii=False, indent=2)+'\n')
    refs = []
    for path in memories:
        content = path.read_bytes()
        refs.append({'path': str(path.resolve()), 'sha256': hashlib.sha256(content).hexdigest()})
    meta = {'started_at': datetime.now(timezone.utc).isoformat(), 'mode': mode, 'port': port,
            'knowledge_version': version, 'knowledge_version_status': 'caller_supplied; verify against actual game build',
            'memory_inputs': refs, 'codex_session': str(codex_session.resolve()) if codex_session else None}
    (out / 'session.json').write_text(json.dumps(meta, ensure_ascii=False, indent=2)+'\n')
    executor.append_live(out / 'live.log', f'开始新记录 | {mode} | 知识版本 {version}\n构筑计划：{json.dumps(plan_data, ensure_ascii=False)}')
    return meta


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('command', choices=['start', 'observe', 'act', 'note', 'report', 'annotate'])
    p.add_argument('payload', nargs='?')
    p.add_argument('reason', nargs='?')
    p.add_argument('expected_hash', nargs='?')
    p.add_argument('--run-dir', type=Path, required=True)
    p.add_argument('--port', type=int, default=15701)
    p.add_argument('--plan', type=Path)
    p.add_argument('--memory', type=Path, action='append', default=[])
    p.add_argument('--mode', choices=['practice', 'evaluation'], default='practice')
    p.add_argument('--game-version', default='v0.107.1')
    p.add_argument('--codex-session', type=Path)
    a = p.parse_args()
    out = a.run_dir.resolve()
    if a.command == 'start':
        if not a.plan or not a.memory:
            raise SystemExit('start requires --plan JSON and at least one --memory file; read these before playing')
        print(json.dumps(start_session(out, a.plan, a.memory, a.mode, a.port, a.game_version, a.codex_session), ensure_ascii=False))
        return
    if a.command == 'report':
        report = run_report(out / 'decisions.jsonl')
        meta = json.loads((out/'session.json').read_text()) if (out/'session.json').exists() else {}
        source = a.codex_session or meta.get('codex_session')
        report['cost'] = usage_report(source, datetime.now(timezone.utc).isoformat(), meta.get('started_at')) if source else {'available': False, 'reason': 'unknown; no session path supplied'}
        (out/'audit.json').write_text(json.dumps(report, ensure_ascii=False, indent=2)+'\n')
        executor.append_live(out/'live.log', f"报告：{out/'audit.json'}；token={report['cost'].get('usage', {}).get('total_tokens', '未知')}；压缩={report['cost'].get('compaction_count', '未知')}")
        print(str(out/'audit.json'))
        return
    if not (out/'session.json').exists():
        raise SystemExit('Run start first. Archived pre-migration runs remain read-only; use sts2_run_audit.py to audit them.')
    meta = json.loads((out/'session.json').read_text())
    if a.port != meta['port']:
        raise SystemExit('Port differs from run session; select the correct run/port')
    if a.command in {'note', 'annotate'}:
        if not a.payload: raise SystemExit('payload required')
        if a.command == 'annotate':
            annotation = json.loads(a.payload)
            if not annotation.get('kind') or not annotation.get('reason'): raise SystemExit('annotation needs kind and reason')
            executor.append_jsonl(out/'annotations.jsonl', {'timestamp': datetime.now(timezone.utc).isoformat(), **annotation})
        executor.append_live(out/'live.log', a.payload)
        return
    pre = executor.request(a.port)
    knowledge_root = ROOT/'knowledge/source'/meta['knowledge_version']
    if a.command == 'observe':
        print(json.dumps(snapshot(out, pre, knowledge_root), ensure_ascii=False))
        return
    if not a.expected_hash or a.expected_hash != executor.decision_digest(pre):
        raise SystemExit('Stale or missing hash; re-observe before acting')
    action = json.loads(a.payload)
    if not isinstance(action, dict) or not isinstance(action.get('action'), str) or not a.reason:
        raise SystemExit('One explicit action and public decision summary required')
    if (out/'pending-action.json').exists():
        raise SystemExit('Previous action unresolved. Inspect observation/visual evidence and reconcile its outcome before removing pending-action.json; never blindly retry.')
    plan = json.loads((out/'plan.json').read_text())
    decision_id = datetime.now(timezone.utc).isoformat()
    observed = snapshot(out, pre, knowledge_root, decision_id)
    if observed['decision_context']['authority']['status'] != 'verified':
        raise SystemExit('Rule source mismatch or missing build fingerprint; inspect decision_context.authority before acting')
    run, battle = pre.get('run', {}), pre.get('battle', {})
    executor.append_live(out/'live.log', f"层 {run.get('floor', '-')} / 回合 {battle.get('round', '-')}\n{a.reason}\n执行：{json.dumps(action, ensure_ascii=False)}")
    intent = {'type':'decision_intent', 'decision_id':decision_id, 'controller':'llm', 'state':pre,
              'state_sha256':executor.digest(pre), 'decision_hash':a.expected_hash, 'action':action,
              'decision_reason':a.reason, 'plan_sha256':executor.digest(plan), 'plan':plan,
              'knowledge_references': observed['knowledge_references'],
              'rule_checks': observed['decision_context']['rules'],
              'rule_authority': observed['decision_context']['authority'],
              'legal_actions': build_information_view(pre, 'limited')['legal_actions']}
    executor.append_jsonl(out/'decisions.jsonl', intent)
    executor.append_jsonl(out/'pending-action.json', {'decision_id':decision_id, 'action':action})
    began = time.monotonic()
    try:
        response = executor.request(a.port, action)
        acknowledged = time.monotonic()
        post = executor.settled_state(a.port, pre, action)
    except (Exception, SystemExit) as error:
        executor.append_jsonl(out/'decisions.jsonl', {'type':'decision_error', 'decision_id':decision_id, 'error':str(error), 'retry_safe':False})
        executor.append_live(out/'live.log', f'动作待核对：{error}；不自动重试。')
        raise
    executor.append_jsonl(out/'decisions.jsonl', {'type':'decision_outcome', 'decision_id':decision_id, 'controller':'llm',
        'action_response':response, 'post_state':post, 'post_state_sha256':executor.digest(post),
        'timing_s':{'request':acknowledged-began, 'settlement':time.monotonic()-acknowledged}})
    (out/'pending-action.json').unlink()
    player = post.get('player', {})
    executor.append_live(out/'live.log', f"结算：{post.get('state_type')} | HP {player.get('hp','-')}/{player.get('max_hp','-')} | 能量 {player.get('energy','-')} | 格挡 {player.get('block','-')}")
    print(json.dumps(snapshot(out, post, knowledge_root, decision_id), ensure_ascii=False))
    if post.get('state_type') == 'game_over':
        report = run_report(out/'decisions.jsonl')
        source = meta.get('codex_session')
        report['cost'] = usage_report(source, datetime.now(timezone.utc).isoformat(), meta.get('started_at')) if source else {'available':False, 'reason':'unknown; no session path supplied'}
        report['cost']['snapshot_note'] = 'Current model response may not yet be in the rollout. Refresh report after completion.'
        (out/'audit.json').write_text(json.dumps(report, ensure_ascii=False, indent=2)+'\n')
        executor.append_live(out/'live.log', f"游戏结束，报告已保存：{out/'audit.json'}")


if __name__ == '__main__':
    main()
