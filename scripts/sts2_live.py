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
import sts2_action_refs as refs
from sts2_run_audit import run_report, usage_report

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / 'sts2rec'))
from sts2rec.information import build_information_view
from sts2rec.knowledge import visible_knowledge

WIKI_FACTS_MAX_CHARS = 4000


def wiki_facts(out, state):
    """Public wiki/codex priors for this state, if the session opted in (start --wiki-facts).

    Returns None when disabled so benchmark runs can A/B with and without facts.
    """
    session = out / 'session.json'
    meta = json.loads(session.read_text()) if session.exists() else {}
    if not meta.get('wiki_facts'):
        return None
    sys.path.insert(0, str(ROOT / 'scripts' / 'facts'))
    from lookup import FactDB
    try:
        return FactDB.load(str(meta.get('knowledge_version', 'v0.107.1')).lstrip('v')).facts_for_state(
            state, max_chars=WIKI_FACTS_MAX_CHARS)
    except (OSError, KeyError, ValueError) as error:
        return f'(wiki facts unavailable: {type(error).__name__}: {error})'


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
    # Every legal action carries a stable `ref` usable in `act`; positional
    # fields are shown for reference but go stale after any action.
    actions = refs.observe_actions(state)
    return {'hash': executor.decision_digest(state), 'state': public, 'legal_actions': actions,
            'unavailable_actions': refs.blocked_actions(state), 'audit': view['action_space_audit']}


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
    facts = wiki_facts(out, state)
    if facts is not None:
        result['wiki_facts'] = facts
    result['knowledge_references'] = knowledge['references']
    return result


def start_session(out, plan, memories, mode, port, version, codex_session, allow_unverified_authority=False,
                  use_wiki_facts=False):
    if (out / 'session.json').exists() or (out / 'decisions.jsonl').exists():
        raise SystemExit('Existing run directory; resume it without start, or use a new directory')
    # Read and preserve the exact working plan and memories before first gameplay.
    plan_data = json.loads(plan.read_text(encoding='utf-8'))
    for field in ('immediate_threat', 'deck_plan', 'draw_plan', 'potion_budget', 'stop_conditions'):
        if field not in plan_data:
            raise SystemExit(f'plan missing: {field}')
    out.mkdir(parents=True, exist_ok=True)
    (out / 'plan.json').write_text(json.dumps(plan_data, ensure_ascii=False, indent=2)+'\n')
    memory_refs = []
    for path in memories:
        content = path.read_bytes()
        memory_refs.append({'path': str(path.resolve()), 'sha256': hashlib.sha256(content).hexdigest()})
    meta = {'started_at': datetime.now(timezone.utc).isoformat(), 'mode': mode, 'port': port,
            'knowledge_version': version, 'knowledge_version_status': 'caller_supplied; verify against actual game build',
            'memory_inputs': memory_refs, 'allow_unverified_authority': bool(allow_unverified_authority), 'wiki_facts': bool(use_wiki_facts), 'codex_session': str(codex_session.resolve()) if codex_session else None}
    (out / 'session.json').write_text(json.dumps(meta, ensure_ascii=False, indent=2)+'\n')
    executor.append_live(out / 'live.log', f'开始新记录 | {mode} | 知识版本 {version}\n构筑计划：{json.dumps(plan_data, ensure_ascii=False)}')
    return meta


class ActError(SystemExit):
    """Explicit, machine-readable act failure (exit status 2)."""

    def __init__(self, payload):
        self.payload = payload
        super().__init__(2)


def fail(kind, message, *, state=None, out=None, knowledge_root=None, **extra):
    payload = {'ok': False, 'error': kind, 'message': message, **extra}
    if state is not None and out is not None:
        payload['observation'] = snapshot(out, state, knowledge_root)
    raise ActError(payload)


def state_diff(before, after):
    """Top-level description of what changed between two decision states."""
    keys = sorted(set(before) | set(after))
    changed = [k for k in keys if k != 'execution' and before.get(k) != after.get(k)]
    detail = {'state_type': [before.get('state_type'), after.get('state_type')], 'changed_keys': changed}
    for block, field in (('battle', 'round'), ('player', 'energy'), ('player', 'hp'), ('run', 'floor')):
        b, a = (before.get(block) or {}).get(field), (after.get(block) or {}).get(field)
        if b != a:
            detail[f'{block}.{field}'] = [b, a]
    return detail


def check_authority(meta, observed, allow_unverified):
    """Evaluation always fails closed; practice continues only on explicit opt-in."""
    auth = observed['decision_context']['authority']
    if auth.get('status') == 'verified':
        return True
    detail = auth.get('mismatches') or auth.get('source_errors') or auth.get('status')
    if meta.get('mode') == 'practice' and (allow_unverified or meta.get('allow_unverified_authority')):
        return False
    hint = (' Practice runs may pass --allow-unverified-authority (recorded as authority_verified=false).'
            if meta.get('mode') == 'practice' else ' Evaluation runs never bypass this check.')
    fail('rule_authority_unverified',
         f'Rule source mismatch or missing build fingerprint: {detail}.{hint}')


TREASURE_FIRST_CLICK_S = 4.0

SELECT_SCREENS = {'hand_select': ('combat_select_card', 'combat_confirm_selection'),
                  'card_select': ('select_card', 'confirm_selection')}


SELECT_ACTIONS = {'select', 'combat_select_card', 'select_card'}


def expand(state, request):
    """Turn a selection request into select steps plus an automatic confirm.

    On hand_select / card_select the cards named in one act call (``card`` or
    ``cards``) are the complete selection: the harness selects each by stable
    ref, then confirms once the game allows it.  ``"confirm": false`` keeps
    the screen open for incremental selection.
    """
    st = state.get('state_type')
    request = dict(request)
    if 'cards' not in request and not (request.get('action') in SELECT_ACTIONS and 'card' in request):
        return [request]
    if st not in SELECT_SCREENS:
        raise refs.ActionRefError(f"card selection given but screen is {st!r}")
    select, _confirm = SELECT_SCREENS[st]
    cards = request.pop('cards', None)
    if cards is None:
        cards = [request.pop('card')]
    elif not isinstance(cards, list):
        cards = [cards]
    confirm = request.pop('confirm', True)
    block = state.get(st) or {}
    already = len(block.get('selected_cards') or block.get('selected_indices') or [])
    minimum, maximum = block.get('min_select'), block.get('max_select')
    if confirm and isinstance(minimum, int) and already + len(cards) < minimum:
        raise refs.ActionRefError(f'{st} needs at least {minimum} card(s); request selects {already + len(cards)} '
                                  '(list them all in "cards", or pass "confirm": false)')
    if isinstance(maximum, int) and maximum > 0 and already + len(cards) > maximum:
        raise refs.ActionRefError(f'{st} allows at most {maximum} card(s); request selects {already + len(cards)}')
    extra = {k: v for k, v in request.items() if k not in {'action'}}
    steps = [{'action': select, 'card': card, **extra} for card in cards]
    steps[-1]['_confirm'] = bool(confirm)
    return steps


def selection_open(state):
    return state.get('state_type') in SELECT_SCREENS


def needs_confirm(state):
    st = state.get('state_type')
    return st in SELECT_SCREENS and bool((state.get(st) or {}).get('can_confirm'))


class Session:
    def __init__(self, out, meta, port, knowledge_root, reason, plan, controller='llm'):
        self.out, self.meta, self.port = out, meta, port
        self.knowledge_root, self.reason, self.plan = knowledge_root, reason, plan
        self.controller = controller
        self.steps = []
        self.authority_verified = None

    def execute(self, pre, wire, request, *, step, controller=None, note=None, legal=None):
        out = self.out
        decision_id = f"{datetime.now(timezone.utc).isoformat()}#s{step}"
        observed = snapshot(out, pre, self.knowledge_root, decision_id)
        run, battle = pre.get('run', {}), pre.get('battle', {})
        reason = self.reason if controller is None else f'[harness:{note}] {self.reason}'
        executor.append_live(out/'live.log', f"层 {run.get('floor', '-')} / 回合 {battle.get('round', '-')}\n{reason}\n执行：{json.dumps(wire, ensure_ascii=False)}")
        intent = {'type':'decision_intent', 'decision_id':decision_id, 'controller': controller or self.controller,
                  'state':pre, 'state_sha256':executor.digest(pre), 'decision_hash':executor.decision_digest(pre),
                  'action':wire, 'request':request, 'resolved_candidate': (legal or {}).get('candidate'),
                  'resolved_target': (legal or {}).get('target_candidate'),
                  'decision_reason':reason, 'plan_sha256':executor.digest(self.plan), 'plan':self.plan,
                  'authority_verified': self.authority_verified,
                  'knowledge_references': observed['knowledge_references'],
                  'rule_checks': observed['decision_context']['rules'],
                  'rule_authority': observed['decision_context']['authority'],
                  'legal_actions': build_information_view(pre, 'limited')['legal_actions']}
        if note:
            intent['harness_followup'] = note
        executor.append_jsonl(out/'decisions.jsonl', intent)
        executor.append_jsonl(out/'pending-action.json', {'decision_id':decision_id, 'action':wire})
        began = time.monotonic()
        try:
            response = executor.request(self.port, wire)
        except (Exception, SystemExit) as error:
            current = executor.request(self.port)
            unchanged = executor.decision_digest(current) == executor.decision_digest(pre)
            executor.append_jsonl(out/'decisions.jsonl', {'type':'decision_error', 'decision_id':decision_id,
                'error':str(error), 'stage':'request', 'state_unchanged':unchanged, 'retry_safe':False})
            if unchanged:
                # The game refused it and nothing moved: there is nothing to reconcile.
                (out/'pending-action.json').unlink()
                executor.append_live(out/'live.log', f'动作被拒绝（状态未变）：{error}')
                fail('action_rejected', str(error), state=current, out=out, knowledge_root=self.knowledge_root,
                     steps=self.steps, failed_step=step, wire=wire)
            executor.append_live(out/'live.log', f'动作待核对：{error}；不自动重试。')
            fail('action_unresolved', f'{error}; state changed, run `reconcile` after inspecting it',
                 state=current, out=out, knowledge_root=self.knowledge_root, steps=self.steps,
                 failed_step=step, wire=wire, pending_decision_id=decision_id)
        acknowledged = time.monotonic()
        try:
            post = self.settle(pre, wire)
        except (Exception, SystemExit) as error:
            current = executor.request(self.port)
            executor.append_jsonl(out/'decisions.jsonl', {'type':'decision_error', 'decision_id':decision_id,
                'error':str(error), 'stage':'settle', 'retry_safe':False})
            executor.append_live(out/'live.log', f'动作待核对：{error}；不自动重试。')
            fail('action_unresolved', f'{error}; inspect the state and run `reconcile` (never blind-retry)',
                 state=current, out=out, knowledge_root=self.knowledge_root, steps=self.steps,
                 failed_step=step, wire=wire, pending_decision_id=decision_id)
        executor.append_jsonl(out/'decisions.jsonl', {'type':'decision_outcome', 'decision_id':decision_id,
            'controller': controller or self.controller, 'action_response':response, 'post_state':post,
            'post_state_sha256':executor.digest(post),
            'timing_s':{'request':acknowledged-began, 'settlement':time.monotonic()-acknowledged}})
        (out/'pending-action.json').unlink()
        player = post.get('player', {})
        executor.append_live(out/'live.log', f"结算：{post.get('state_type')} | HP {player.get('hp','-')}/{player.get('max_hp','-')} | 能量 {player.get('energy','-')} | 格挡 {player.get('block','-')}")
        self.steps.append({'step': step, 'action': wire, **({'harness_followup': note} if note else {}),
                           'state_type': post.get('state_type'), 'hash': executor.decision_digest(post)})
        return post

    def settle(self, pre, wire):
        if wire.get('action') != 'claim_treasure_relic':
            return executor.settled_state(self.port, pre, wire)
        # The first treasure click is sometimes swallowed by the opening chest
        # animation. Re-click only while the very same relic is still offered,
        # which cannot double-claim.
        try:
            return executor.settled_state(self.port, pre, wire, timeout=TREASURE_FIRST_CLICK_S)
        except SystemExit:
            current = executor.request(self.port)
            if executor._action_committed(pre, current, wire):
                raise
            executor.append_live(self.out/'live.log', 'treasure: first claim not registered; re-clicking the same relic')
            executor.request(self.port, wire)
            self.steps.append({'harness_followup': 'treasure_reclick', 'action': wire})
            return executor.settled_state(self.port, pre, wire)


def chain_error(error, pre, state, step):
    """Error text for a step that could not be resolved.

    When an earlier step of the same chain moved the game to another screen
    (combat won -> rewards, event -> card_select, ...) the bare "no card
    matches" is misleading: say the screen changed and nothing more was sent.
    """
    message = str(error)
    before, after = pre.get('state_type'), state.get('state_type')
    if step > 1 and before != after:
        return (f'{message} (screen changed from {before!r} to {after!r} after step {step - 1}; '
                'remaining steps were not sent; decide again from this observation)',
                {'screen_changed': {'from': before, 'to': after, 'after_step': step - 1}})
    return message, {}


def run_act(a, out, meta, knowledge_root):
    if (out/'pending-action.json').exists():
        pending = (out/'pending-action.json').read_text().strip()
        fail('pending_unresolved', 'Previous action unresolved. Inspect the state, then run '
             '`reconcile "<what happened>"`; never blindly retry.', pending=pending)
    if not a.reason or len(a.reason.strip()) < 4:
        fail('reason_required', 'A public decision summary (>=4 chars) is required')
    if not a.expected_hash:
        fail('hash_required', 'Pass the hash printed by observe (or the previous act)')
    try:
        requests = refs.parse_request(a.payload)
    except (refs.ActionRefError, json.JSONDecodeError) as error:
        fail('bad_request', str(error))
    pre, settled = executor.wait_settled(a.port, a.settle_timeout)
    if not settled:
        fail('not_settled', f'game did not reach a stable decision frame within {a.settle_timeout}s',
             state=pre, out=out, knowledge_root=knowledge_root)
    observed_hash = executor.decision_digest(pre)
    if a.expected_hash != observed_hash:
        try:
            recorded = json.loads(next(l for l in reversed((out/'observations.jsonl').read_text().splitlines())
                                       if json.loads(l).get('decision_hash') == a.expected_hash))
        except (StopIteration, FileNotFoundError):
            recorded = None
        diff = state_diff(recorded['observation'], pre) if recorded else 'expected hash never observed in this run'
        fail('stale_hash', f'Observed state changed since hash {a.expected_hash}; nothing was sent. '
             'Decide again from the fresh observation below.', expected=a.expected_hash,
             observed=observed_hash, diff=diff, state=pre, out=out, knowledge_root=knowledge_root)
    session = Session(out, meta, a.port, knowledge_root, a.reason, json.loads((out/'plan.json').read_text()))
    session.authority_verified = check_authority(meta, snapshot(out, pre, knowledge_root), a.allow_unverified_authority)
    observed_entities = refs.entity_map(pre)
    state, step, notes, warnings = pre, 0, [], []
    for position, original in enumerate(requests):
        try:
            items = expand(state, original)
        except refs.ActionRefError as error:
            message, changed = chain_error(error, pre, state, step + 1)
            fail('unresolvable_action', message, failed_step=step + 1, request=original, steps=session.steps,
                 state=state, out=out, knowledge_root=knowledge_root, **changed)
        for request in items:
            step += 1
            confirm = request.pop('_confirm', None)
            try:
                resolved = refs.resolve(state, request, allow_index=(step == 1), observed_entities=observed_entities)
            except refs.ActionRefError as error:
                message, changed = chain_error(error, pre, state, step)
                executor.append_live(out/'live.log', f'请求未发送（第 {step} 步）：{message}')
                fail('unresolvable_action', message, available=error.available, failed_step=step,
                     request=request, steps=session.steps, state=state, out=out, knowledge_root=knowledge_root,
                     **changed)
            notes += resolved['notes']
            before_type = state.get('state_type')
            state = session.execute(state, resolved['wire'], request, step=step, legal=resolved['legal'])
            session.steps[-1]['resolved'] = refs.describe(resolved)
            if resolved['wire']['action'] == 'proceed' and state.get('state_type') == 'card_reward':
                warnings.append('proceed landed on card_reward: an unclaimed card reward reopened; use skip')
            if confirm and selection_open(state):
                if needs_confirm(state):
                    step += 1
                    confirm_wire = {'action': SELECT_SCREENS[state['state_type']][1]}
                    state = session.execute(state, confirm_wire, {'action': 'confirm'}, step=step,
                                            controller='harness', note='auto_confirm_selection')
                else:
                    fail('selection_incomplete', f"selection made but {state.get('state_type')} cannot confirm yet "
                         '(more cards required?); add cards, or send {"action":"confirm"} when ready',
                         steps=session.steps, state=state, out=out, knowledge_root=knowledge_root)
            elif confirm and before_type in SELECT_SCREENS and not selection_open(state):
                session.steps[-1]['note'] = 'screen closed on select (single-choice screen); no confirm needed'
    result = {'ok': True, 'steps': session.steps, 'notes': notes, 'warnings': warnings,
              'authority_verified': session.authority_verified,
              'observation': snapshot(out, state, knowledge_root, session.steps[-1]['step'] if session.steps else None)}
    print(json.dumps(result, ensure_ascii=False))
    return state


def run_reconcile(a, out, knowledge_root):
    path = out/'pending-action.json'
    if not path.exists():
        fail('nothing_pending', 'No pending action to reconcile')
    if not a.payload or len(a.payload.strip()) < 4:
        fail('reason_required', 'Describe what actually happened (from the observation/visual evidence)')
    pending = [json.loads(l) for l in path.read_text().splitlines() if l.strip()]
    current, settled = executor.wait_settled(a.port, a.settle_timeout)
    for item in pending:
        executor.append_jsonl(out/'decisions.jsonl', {'type':'decision_outcome_recovered',
            'decision_id':item['decision_id'], 'controller':'llm', 'reconcile_note':a.payload,
            'post_state':current, 'post_state_sha256':executor.digest(current), 'settled':settled})
    path.unlink()
    executor.append_live(out/'live.log', f'核对完成：{a.payload}')
    print(json.dumps({'ok': True, 'reconciled': [p['decision_id'] for p in pending],
                      'observation': snapshot(out, current, knowledge_root)}, ensure_ascii=False))


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('command', choices=['start', 'observe', 'act', 'reconcile', 'note', 'report', 'annotate'])
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
    p.add_argument('--allow-unverified-authority', action='store_true',
                   help='practice only: act even if the rule-source fingerprint does not match this build')
    p.add_argument('--settle-timeout', type=float, default=20.0)
    p.add_argument('--wiki-facts', action='store_true',
                   help='start only: include public wiki/codex facts (knowledge/facts) in every observation')
    a = p.parse_args()
    out = a.run_dir.resolve()
    try:
        _dispatch(a, out)
    except ActError as error:
        print(json.dumps(error.payload, ensure_ascii=False))
        sys.exit(2)


def _dispatch(a, out):
    if a.command == 'start':
        if not a.plan or not a.memory:
            raise SystemExit('start requires --plan JSON and at least one --memory file; read these before playing')
        if a.allow_unverified_authority and a.mode != 'practice':
            raise SystemExit('--allow-unverified-authority is only valid in practice mode')
        meta = start_session(out, a.plan, a.memory, a.mode, a.port, a.game_version, a.codex_session,
                             a.allow_unverified_authority, a.wiki_facts)
        print(json.dumps(meta, ensure_ascii=False))
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
    if a.allow_unverified_authority and meta.get('mode') != 'practice':
        fail('rule_authority_unverified', '--allow-unverified-authority is refused in evaluation mode')
    if a.command in {'note', 'annotate'}:
        if not a.payload: raise SystemExit('payload required')
        if a.command == 'annotate':
            annotation = json.loads(a.payload)
            if not annotation.get('kind') or not annotation.get('reason'): raise SystemExit('annotation needs kind and reason')
            executor.append_jsonl(out/'annotations.jsonl', {'timestamp': datetime.now(timezone.utc).isoformat(), **annotation})
        executor.append_live(out/'live.log', a.payload)
        return
    knowledge_root = ROOT/'knowledge/source'/meta['knowledge_version']
    if a.command == 'observe':
        state, settled = executor.wait_settled(a.port, a.settle_timeout)
        view = snapshot(out, state, knowledge_root)
        view['settled'] = settled
        if not settled:
            view['warning'] = 'state still changing / not a decision point; observe again before acting'
        if (out/'pending-action.json').exists():
            view['warning_pending'] = 'a previous action is unresolved; run reconcile before acting'
        print(json.dumps(view, ensure_ascii=False))
        return
    if a.command == 'reconcile':
        run_reconcile(a, out, knowledge_root)
        return
    post = run_act(a, out, meta, knowledge_root)
    if post.get('state_type') == 'game_over':
        report = run_report(out/'decisions.jsonl')
        source = meta.get('codex_session')
        report['cost'] = usage_report(source, datetime.now(timezone.utc).isoformat(), meta.get('started_at')) if source else {'available':False, 'reason':'unknown; no session path supplied'}
        report['cost']['snapshot_note'] = 'Current model response may not yet be in the rollout. Refresh report after completion.'
        (out/'audit.json').write_text(json.dumps(report, ensure_ascii=False, indent=2)+'\n')
        executor.append_live(out/'live.log', f"游戏结束，报告已保存：{out/'audit.json'}")


if __name__ == '__main__':
    main()
