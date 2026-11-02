#!/usr/bin/env python3
"""E3 MCP coverage farm: N headless real-engine workers playing full runs with
the REC recorder on, to produce anchored traces for the sim diff gates.

Each worker thread loops: ensure the worker is up -> menu -> start a run
(character cycles IRONCLAD/SILENT/DEFECT/REGENT/NECROBINDER; the lobby
ascension comes from the worker's sandbox profile, see --ascensions) ->
observe/act through scripts/sts2_live.py with a coverage heuristic -> on game
over / victory return to the menu and start the next run.  A worker with no
progress for --hang-s seconds is stopped and relaunched.

Goal is COVERAGE (acts 2/3, elites, bosses, shops, rests, events, many cards
and relics), not win rate: the heuristic blocks when hit, attacks the weakest
enemy, plays powers early, uses potions when low, takes cards and buys items.

Outputs (all under --farm-root, never in the repo or the human corpus):
  farm.log          one line per room (worker, run, act/floor, room type, encounter)
  rooms.jsonl       the same, structured
  runs.jsonl        one record per run (character, ascension, floors, result, ...)
  events.jsonl      worker lifecycle (launch/hang/crash/restart)
  runs/<run_id>/    sts2_live run directory (observations/decisions)
  worker-<i>/       recorder root of worker i (sessions/<id>/...)

usage: run_farm.py --pool DIR --farm-root DIR [--workers 1,2,3,4]
                   [--duration-min 120] [--hard-stop-min 150]
"""
import argparse
import json
import random
import re
import signal
import subprocess
import sys
import threading
import time
from datetime import datetime, timezone
from pathlib import Path

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[1]
sys.path.insert(0, str(ROOT / 'scripts' / 'bench'))
import run_bench as rb  # noqa: E402

LAUNCHER = ROOT / 'scripts' / 'headless_launch.sh'
PLAN = ROOT / 'config' / 'bench' / 'stub-plan.json'
FLAGS = ['--allow-unverified-authority']
SEED_CHARS = '0123456789ABCDEFGHJKLMNPQRSTUVWXYZ'
CHARACTERS = ['IRONCLAD', 'SILENT', 'DEFECT', 'REGENT', 'NECROBINDER']
COMBAT = {'monster', 'elite', 'boss'}
SELECT = {'card_select', 'hand_select'}
LEAVE = ('proceed', 'leave_shop', 'leave', 'close_shop', 'skip')
STOP = threading.Event()
_lock = threading.Lock()


def now():
    return datetime.now(timezone.utc).isoformat(timespec='seconds')


class Out:
    def __init__(self, root):
        self.root = Path(root)
        self.root.mkdir(parents=True, exist_ok=True)

    def line(self, text):
        with _lock:
            with open(self.root / 'farm.log', 'a') as f:
                f.write(f'{datetime.now().strftime("%H:%M:%S")} {text}\n')

    def jsonl(self, name, record):
        with _lock:
            with open(self.root / name, 'a') as f:
                f.write(json.dumps({'t': now(), **record}, ensure_ascii=False) + '\n')


# ----------------------------------------------------------------- policy
def _num(text):
    m = re.findall(r'\d+', str(text or ''))
    return [int(x) for x in m]


def incoming_damage(state):
    total = 0
    for enemy in (state.get('battle') or {}).get('enemies') or []:
        if not enemy.get('is_alive', True):
            continue
        for intent in enemy.get('intents') or []:
            if 'attack' not in str(intent.get('type', '')).lower():
                continue
            nums = _num(intent.get('label'))
            if not nums:
                total += 6
            elif len(nums) >= 2:
                total += nums[0] * nums[1]
            else:
                total += nums[0]
    return total


def preview(card, name):
    for var in (card.get('rule_preview') or {}).get('variables') or []:
        if name == 'block' and var.get('block_preview') is not None:
            return var['block_preview']
        if name == 'damage' and var.get('damage_by_target'):
            return max(d.get('before_block') or 0 for d in var['damage_by_target'])
    return 0


def hand_card(state, ref):
    hand = (state.get('player') or {}).get('hand') or []
    key = ref.get('card')
    if isinstance(key, str):
        base = key.split('#')[0]
        for card in hand:
            if base in (card.get('name'), card.get('id'), card.get('model_id')):
                return card
    idx = ref.get('card_index', ref.get('index'))
    if isinstance(idx, int):
        for card in hand:
            if card.get('index') == idx:
                return card
    return {}


def enemy_hp(state, target):
    for enemy in (state.get('battle') or {}).get('enemies') or []:
        ids = {str(enemy.get('combat_id')), str(enemy.get('entity_id')), f'cid:{enemy.get("combat_id")}'}
        if str(target) in ids or (isinstance(target, str) and target.endswith(f'@{enemy.get("combat_id")}')):
            return (enemy.get('hp') or 0) + (enemy.get('block') or 0)
    return 999


class Policy:
    """Cheap coverage heuristic (no model, no search)."""

    def __init__(self, rng):
        self.rng = rng
        self.shop_buys = 0

    def combat(self, state, refs):
        player = state.get('player') or {}
        hp, max_hp = player.get('hp') or 1, player.get('max_hp') or 1
        block = player.get('block') or 0
        threat = max(0, incoming_damage(state) - block)
        battle = state.get('battle') or {}
        potions = [r for r in refs if r.get('action') == 'use_potion']
        if potions and (hp < 0.4 * max_hp or (state.get('state_type') in ('elite', 'boss') and battle.get('round') == 1)) \
                and self.rng.random() < 0.6:
            return self.rng.choice(potions), 'use a potion (low hp / elite or boss)'
        plays = [r for r in refs if r.get('action') == 'play_card']
        scored = []
        for ref in plays:
            card = hand_card(state, ref)
            ctype = str(card.get('type', '')).lower()
            dmg, blk = preview(card, 'damage'), preview(card, 'block')
            score = 1.0 + self.rng.random()
            if ctype == 'power':
                score += 6 if (battle.get('round') or 1) <= 3 else 2
            if blk and threat > 0:
                score += 4 + min(blk, threat) / 3
            if dmg:
                target = ref.get('target')
                score += 2 + dmg / 4
                ehp = enemy_hp(state, target) if target is not None else 999
                if dmg >= ehp:
                    score += 5
                score += max(0, 3 - ehp / 20)
            if ctype in ('status', 'curse'):
                score -= 1
            if str(card.get('cost')) == '0':
                score += 1
            scored.append((score, ref))
        if scored:
            scored.sort(key=lambda x: -x[0])
            if self.rng.random() < 0.9:
                return scored[0][1], 'heuristic: best scored card'
            return self.rng.choice(plays), 'heuristic: explore a random card'
        for ref in refs:
            if ref.get('action') == 'end_turn':
                return ref, 'nothing worth playing: end turn'
        return self.rng.choice(refs), 'combat fallback'

    def map(self, state, refs):
        player = state.get('player') or {}
        hp_frac = (player.get('hp') or 1) / max(1, player.get('max_hp') or 1)
        gold = player.get('gold') or 0
        weights = []
        for ref in refs:
            kind = str(ref.get('type', '')).lower()
            w = 1.0
            run = state.get('run') or {}
            early = run.get('act') == 1 and (run.get('floor') or 0) < 8
            if 'elite' in kind:
                # First farm minutes: starter decks died to every act-1 elite before
                # floor 8, which ended runs before act 2.  Elites stay reachable later.
                w = 0.1 if early else (1.0 if hp_frac > 0.8 else 0.15)
            elif 'rest' in kind:
                w = 3.0 if hp_frac < 0.5 else (1.5 if hp_frac < 0.7 else 0.8)
            elif 'shop' in kind:
                w = 2.5 if gold >= 120 else 0.8
            elif 'unknown' in kind:
                w = 1.6
            elif 'treasure' in kind:
                w = 2.0
            weights.append(w)
        return self.rng.choices(refs, weights)[0], 'coverage-weighted map choice'

    def rest(self, state, refs):
        player = state.get('player') or {}
        hp_frac = (player.get('hp') or 1) / max(1, player.get('max_hp') or 1)
        names = [(json.dumps(r, ensure_ascii=False).lower(), r) for r in refs]
        want = ('heal', 'rest') if hp_frac < 0.55 else ('smith', 'upgrade')
        for text, ref in names:
            if any(w in text for w in want):
                return ref, f'rest site: {want[0]} at hp {hp_frac:.0%}'
        others = [r for t, r in names if not any(x in t for x in LEAVE)]
        return self.rng.choice(others or refs), 'rest site: other option'

    def shop(self, state, refs):
        buys = [r for r in refs if r.get('action') not in LEAVE and 'purchase' in str(r.get('action'))
                or r.get('action') in ('buy', 'shop_purchase', 'remove_card_shop')]
        if buys and self.shop_buys < 4 and self.rng.random() < 0.8:
            self.shop_buys += 1
            return self.rng.choice(buys), 'shop: buy something (coverage)'
        leave = [r for r in refs if r.get('action') in LEAVE]
        if leave:
            return leave[0], 'shop: leave'
        return self.rng.choice(refs), 'shop fallback'

    def decide(self, obs, avoid):
        state = obs.get('state') or {}
        st = state.get('state_type')
        refs = [a['ref'] for a in obs.get('legal_actions') or [] if isinstance(a.get('ref'), dict)]
        refs = [r for r in refs if r.get('action') != 'discard_potion'
                and json.dumps(r, sort_keys=True) not in avoid]
        if not refs:
            return None
        if st != 'shop':
            self.shop_buys = 0
        block = state.get(st) if isinstance(state.get(st), dict) else {}
        if st in SELECT and isinstance(block.get('min_select'), int) and block['min_select'] > 1:
            picks = [r for r in refs if r.get('card')]
            names = list(dict.fromkeys(r['card'] for r in picks))
            while picks and len(names) < block['min_select']:
                names.append(picks[len(names) % len(picks)]['card'])
            if picks:
                return {'action': picks[0]['action'], 'cards': names[:block['min_select']]}, 'multi-select'
        if st in COMBAT:
            return self.combat(state, refs)
        if st == 'map':
            return self.map(state, refs)
        if st == 'rest_site':
            return self.rest(state, refs)
        if st == 'shop':
            return self.shop(state, refs)
        if st == 'card_reward':
            picks = [r for r in refs if r.get('action') not in LEAVE and 'skip' not in str(r.get('action'))]
            if picks and self.rng.random() < 0.85:
                return self.rng.choice(picks), 'card reward: take a card'
        if st == 'rewards':
            claims = [r for r in refs if r.get('action') not in LEAVE]
            if claims:
                return claims[0], 'rewards: claim'
        if st in ('treasure', 'chest'):
            claims = [r for r in refs if r.get('action') not in LEAVE]
            if claims:
                return claims[0], 'treasure: claim'
        non_leave = [r for r in refs if r.get('action') not in LEAVE]
        if st == 'event' and non_leave:
            return self.rng.choice(non_leave), 'event: random option'
        return self.rng.choice(refs), f'random legal action on {st}'


# ----------------------------------------------------------------- worker
class Worker:
    def __init__(self, args, number, out, deadline, char_offset):
        self.args, self.n, self.out, self.deadline = args, number, out, deadline
        self.pool = Path(args.pool)
        self.port = int((self.pool / f'inst{number}' / 'port').read_text().strip())
        self.rec_root = Path(args.farm_root) / f'worker-{number}'
        self.rng = random.Random(args.rng_seed * 1000 + number)
        self.policy = Policy(self.rng)
        self.char_i = char_offset
        self.runs = 0
        self.restarts = 0
        self.last_progress = time.monotonic()

    def log(self, msg):
        rb.log(f'[w{self.n}]', msg)

    def event(self, kind, **kw):
        self.out.jsonl('events.jsonl', {'worker': self.n, 'event': kind, **kw})

    def launcher(self, *extra, check=True):
        cmd = ['bash', str(LAUNCHER), str(self.n), '--base', str(self.pool), *extra]
        proc = subprocess.run(cmd, capture_output=True, text=True, cwd=ROOT)
        if check and proc.returncode != 0:
            raise rb.BenchError(f'launcher {extra} rc={proc.returncode}: {proc.stderr[-400:]}{proc.stdout[-400:]}')
        return proc

    def alive(self):
        try:
            rb.api(self.port, timeout=10)
            return True
        except Exception:  # noqa: BLE001
            return False

    def launch(self):
        self.launcher('--record', '--recorder-root', str(self.rec_root), '--wait-s', '180')
        self.event('launch', port=self.port)
        self.last_progress = time.monotonic()

    def stop(self):
        self.launcher('--stop', check=False)
        self.event('stop')

    def restart(self, why):
        self.restarts += 1
        self.log(f'restart #{self.restarts}: {why}')
        self.event('restart', reason=why)
        self.out.line(f'w{self.n} RESTART {why}')
        self.stop()
        time.sleep(3)
        self.launch()

    # -- menu
    def to_main_menu(self, timeout_s=120):
        deadline = time.monotonic() + timeout_s
        while time.monotonic() < deadline and not STOP.is_set():
            s = rb.api(self.port)
            st = s.get('state_type')
            if st == 'menu':
                scr = s.get('menu_screen')
                if scr == 'main':
                    return s
                opts = [o.get('name') if isinstance(o, dict) else o for o in s.get('options') or []]
                pick = 'back' if 'back' in opts else ('ok' if 'ok' in opts else (opts[0] if opts else None))
                if pick:
                    rb.api(self.port, {'action': 'menu_select', 'option': pick})
            else:
                return s
            time.sleep(1.5)
        raise rb.BenchError('could not reach the main menu')

    def new_seed(self):
        return ''.join(self.rng.choice(SEED_CHARS) for _ in range(10))

    def start_run(self, character, seed):
        """Menu -> standard run.  Returns (state, resumed).

        v0.111 answers a seeded embark with "Seed should not be changed in
        standard mode!" but the lobby keeps the seed (verified: the next plain
        embark starts that seed, and the recorder manifest carries it), so the
        farm sets a fresh random seed first, then embarks."""
        deadline = time.monotonic() + 180
        picked = seeded = False
        first = True
        while time.monotonic() < deadline and not STOP.is_set():
            s = rb.api(self.port)
            st = s.get('state_type')
            if st not in ('menu', None, 'unknown'):
                return s, first
            first = False
            scr = s.get('menu_screen')
            opts = [o.get('name') if isinstance(o, dict) else o for o in s.get('options') or []]
            if scr == 'main':
                # a leftover saved run (crash) is abandoned for a fresh start
                opt = 'singleplayer'
                if 'abandon_run' in opts:
                    opt = 'abandon_run'
                elif 'abandon' in opts:
                    opt = 'abandon'
                r = rb.api(self.port, {'action': 'menu_select', 'option': opt})
            elif scr == 'singleplayer':
                r = rb.api(self.port, {'action': 'menu_select', 'option': 'standard'})
            elif scr == 'character_select':
                if not picked:
                    r = rb.api(self.port, {'action': 'menu_select', 'option': character})
                    picked = r.get('status') == 'ok'
                elif not seeded:
                    r = rb.api(self.port, {'action': 'menu_select', 'option': 'embark', 'seed': seed})
                    seeded = True
                    if r.get('status') == 'ok':  # a build that accepts the seed embarks directly
                        time.sleep(1.5)
                        continue
                else:
                    r = rb.api(self.port, {'action': 'menu_select', 'option': 'embark'})
            else:
                pick = next((o for o in ('confirm', 'yes', 'ok', 'back') if o in opts), opts[0] if opts else None)
                r = rb.api(self.port, {'action': 'menu_select', 'option': pick}) if pick else None
                self.log(f'menu screen {scr} options={opts} -> {pick}')
            time.sleep(1.5)
        raise rb.BenchError('menu start timeout')

    # -- one run
    def play_run(self):
        character = CHARACTERS[self.char_i % len(CHARACTERS)]
        self.char_i += 1
        self.runs += 1
        run_id = f'w{self.n}-{self.args.segment}r{self.runs:03d}-{character.lower()}'
        run_dir = Path(self.args.farm_root) / 'runs' / run_id
        began = time.monotonic()
        self.to_main_menu()
        seed = self.new_seed()
        s, resumed = self.start_run(character, seed)
        if resumed:  # a run was already in progress (not started by this loop)
            character, seed = f'resumed({(s.get("player") or {}).get("character")})', None
        run = s.get('run') or {}
        asc = run.get('ascension')
        self.log(f'run {run_id} started asc={asc} seed={seed}')
        self.out.line(f'w{self.n} {run_id} START character={character} ascension={asc} seed={seed}')
        time.sleep(2)
        rb.live('start', run_dir, self.port, extra=['--plan', str(PLAN), '--memory', str(PLAN),
                                                    '--mode', 'practice', *FLAGS])
        rec = {'worker': self.n, 'run_id': run_id, 'character': character, 'ascension': asc, 'seed': seed,
               'decisions': 0, 'errors': 0, 'max_act': 1, 'max_floor': 0, 'rooms': 0, 'result': None,
               'restart': False}
        last_room = None
        avoid, errs, idle = [], 0, 0
        self.last_progress = time.monotonic()
        while not STOP.is_set():
            if time.monotonic() > self.deadline:
                rec['result'] = 'farm_deadline'
                break
            if time.monotonic() - self.last_progress > self.args.hang_s:
                rec['result'] = 'hung'
                rec['restart'] = True
                break
            try:
                code, obs = rb.live('observe', run_dir, self.port, extra=FLAGS)
            except rb.BenchError as error:
                if not self.alive():
                    rec['result'] = 'crash'
                    rec['restart'] = True
                    self.log(f'observe failed and worker dead: {str(error)[-200:]}')
                    break
                idle += 1
                time.sleep(2)
                continue
            if obs.get('warning_pending'):
                rb.live('reconcile', run_dir, self.port, 'farm auto-reconcile', extra=FLAGS)
                continue
            state = obs.get('state') or {}
            st = state.get('state_type')
            r = state.get('run') or {}
            act, floor = r.get('act'), r.get('floor')
            if isinstance(act, int):
                rec['max_act'] = max(rec['max_act'], act)
            if isinstance(floor, int):
                rec['max_floor'] = max(rec['max_floor'], floor)
            if st in ('game_over', 'victory') or (st == 'menu' and rec['decisions'] > 0):
                # The MCP game_over block carries no victory flag: a run that ended
                # after reaching the act-3 boss room is counted as a (probable) win.
                won = st == 'victory' or (rec['max_act'] >= 3 and rec.get('last_room') == 'boss'
                                          and (rec.get('last_hp') or 0) > 0)
                rec['result'] = 'victory?' if won else 'defeat'
                break
            key = (act, floor)
            if st not in (None, 'map', 'menu', 'unknown') and key != last_room and floor is not None:
                last_room = key
                rec['rooms'] += 1
                enc = None
                if st in COMBAT:
                    enc = '+'.join(sorted(str(e.get('id')) for e in (state.get('battle') or {}).get('enemies') or []))
                elif st == 'event':
                    enc = (state.get('event') or {}).get('event_id')
                p = state.get('player') or {}
                rec['last_room'], rec['last_hp'] = st, p.get('hp')
                self.out.line(f'w{self.n} {run_id} act={act} floor={floor} room={st} '
                              f'encounter={enc} hp={p.get("hp")}/{p.get("max_hp")} gold={p.get("gold")}')
                self.out.jsonl('rooms.jsonl', {'worker': self.n, 'run_id': run_id, 'character': character,
                                               'ascension': asc, 'act': act, 'floor': floor, 'room': st,
                                               'encounter': enc})
            if not obs.get('legal_actions'):
                idle += 1
                time.sleep(1)
                continue
            choice = self.policy.decide(obs, avoid)
            if choice is None:
                avoid = []
                time.sleep(0.5)
                continue
            action, reason = choice
            try:
                code, res = rb.live('act', run_dir, self.port, json.dumps(action, ensure_ascii=False), reason,
                                    obs['hash'], extra=FLAGS)
            except rb.BenchError as error:
                res = {'ok': False, 'error': 'harness', 'message': str(error)[-300:]}
            rec['decisions'] += 1
            if res.get('ok'):
                avoid, errs = [], 0
                self.last_progress = time.monotonic()
                continue
            rec['errors'] += 1
            errs += 1
            if res.get('error') in ('action_unresolved', 'pending_unresolved'):
                try:
                    rb.live('reconcile', run_dir, self.port, 'farm reconcile', extra=FLAGS)
                except rb.BenchError:
                    pass
            avoid.append(json.dumps(action, sort_keys=True))
            if errs > 25:
                avoid = []
                errs = 0
                time.sleep(2)
        if rec['result'] is None and STOP.is_set():
            rec['result'] = 'interrupted'
        rec['wall_s'] = round(time.monotonic() - began, 1)
        self.out.jsonl('runs.jsonl', rec)
        self.out.line(f'w{self.n} {run_id} END result={rec["result"]} act={rec["max_act"]} '
                      f'floor={rec["max_floor"]} decisions={rec["decisions"]} errors={rec["errors"]} '
                      f'wall={rec["wall_s"]}s')
        self.log(f'run {run_id} end {rec["result"]} floor {rec["max_floor"]}')
        return rec

    def finish_terminal_screen(self):
        """Click through game-over / victory screens back to the main menu."""
        deadline = time.monotonic() + 90
        while time.monotonic() < deadline and not STOP.is_set():
            s = rb.api(self.port)
            if s.get('state_type') == 'menu':
                return
            opts = [o.get('name') if isinstance(o, dict) else o for o in s.get('options') or []]
            for act in ({'action': 'menu_select', 'option': 'main_menu'}, {'action': 'proceed'},
                        {'action': 'menu_select', 'option': opts[0] if opts else 'back'}):
                try:
                    r = rb.api(self.port, act)
                except Exception:  # noqa: BLE001
                    continue
                if r.get('status') == 'ok':
                    break
            time.sleep(2)
        raise rb.BenchError('stuck on the terminal screen')

    def loop(self):
        while not STOP.is_set() and time.monotonic() < self.deadline:
            try:
                if not self.alive():
                    self.restart('not answering') if self.runs or self.restarts else self.launch()
                rec = self.play_run()
                if STOP.is_set():
                    break
                if rec['restart']:
                    self.restart(rec['result'])
                    continue
                if rec['result'] == 'farm_deadline':
                    break
                try:
                    self.finish_terminal_screen()
                except rb.BenchError as error:
                    self.restart(str(error))
            except Exception as error:  # noqa: BLE001
                self.log(f'error: {type(error).__name__}: {str(error)[-300:]}')
                self.event('error', error=f'{type(error).__name__}: {str(error)[-300:]}')
                if STOP.is_set() or time.monotonic() > self.deadline:
                    break
                try:
                    self.restart(f'error {type(error).__name__}')
                except Exception as error2:  # noqa: BLE001
                    self.log(f'restart failed: {error2}')
                    time.sleep(30)


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument('--pool', required=True, help='headless_provision base dir (inst<i>/ inside)')
    ap.add_argument('--farm-root', required=True)
    ap.add_argument('--workers', default='1,2,3,4')
    ap.add_argument('--duration-min', type=float, default=120)
    ap.add_argument('--hard-stop-min', type=float, default=150)
    ap.add_argument('--hang-s', type=float, default=300)
    ap.add_argument('--rng-seed', type=int, default=None, help='default: wall-clock seconds')
    ap.add_argument('--stagger-s', type=float, default=20)
    ap.add_argument('--char-shift', type=int, default=0, help='rotate the per-worker character start')
    args = ap.parse_args(argv)
    args.segment = datetime.now().strftime('%H%M-')  # unique run ids across driver restarts
    if args.rng_seed is None:
        args.rng_seed = int(time.time())
    workers_n = [int(x) for x in args.workers.split(',')]
    if len(workers_n) > 4:
        raise SystemExit('at most 4 game workers (keep the Mac responsive)')
    out = Out(args.farm_root)
    began = time.monotonic()
    deadline = began + args.duration_min * 60
    hard = began + args.hard_stop_min * 60
    out.line(f'FARM START workers={workers_n} duration_min={args.duration_min} rng_seed={args.rng_seed}')
    workers = [Worker(args, n, out, deadline, char_offset=k + args.char_shift) for k, n in enumerate(workers_n)]

    def on_signal(signum, _frame):
        STOP.set()
    signal.signal(signal.SIGINT, on_signal)
    signal.signal(signal.SIGTERM, on_signal)
    threads = []
    for w in workers:
        t = threading.Thread(target=w.loop, name=f'w{w.n}', daemon=True)
        t.start()
        threads.append(t)
        time.sleep(args.stagger_s)
    while any(t.is_alive() for t in threads):
        if time.monotonic() > hard:
            STOP.set()
            out.line('HARD STOP reached')
            break
        time.sleep(5)
    for t in threads:
        t.join(timeout=120)
    for w in workers:
        w.stop()
    out.line(f'FARM END wall_min={(time.monotonic() - began) / 60:.1f} '
             f'runs={sum(w.runs for w in workers)} restarts={sum(w.restarts for w in workers)}')


if __name__ == '__main__':
    main()
