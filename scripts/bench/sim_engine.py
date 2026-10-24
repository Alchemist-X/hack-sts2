#!/usr/bin/env python3
"""Offline 'sim' engine: drive the spire-mentor Rust simulator with our policies.

spire-mentor (https://github.com/tfriedel/spire-mentor, MIT, (c) Thomas Friedel)
is an external dependency, never vendored here.  Build its PyO3 module
``sts2_rs`` at the pinned commit (see docs/sim-engine.md) and either install it
into the Python running this script or point ``STS2_SIM_PATH`` at a directory
containing it.

The adapter turns the simulator's state into the same observation shape the
live harness produces (``{'state': {...}, 'legal_actions': [{'ref': ...}]}``),
so ``text_state.encode`` and every ``policies`` spec (stub, ``py:llm_policy``,
commands) work unchanged.  Each seed writes ``<out>/<seed>/trajectory.jsonl``
and ``result.json`` in the format ``score.py``/``compare_models.py`` read.

No game process, port or worker pool is touched.
"""
import argparse
import json
import os
import re
import sys
import time
import traceback
from pathlib import Path

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[1]
sys.path.insert(0, str(HERE))
sys.path.insert(0, str(ROOT / 'scripts' / 'facts'))
import metrics as metrics_mod  # noqa: E402
import policies  # noqa: E402

SPIRE_MENTOR_REPO = 'https://github.com/tfriedel/spire-mentor'
SPIRE_MENTOR_COMMIT = '386d760b7bffd5d29ba50578d8b1286c9359285b'
SCHEMA = 'sts2-bench-trajectory/1'
SEED_CHARS = set('0123456789ABCDEFGHJKLMNPQRSTUVWXYZ')
DEFAULT_LIMITS = {'max_decisions': 3000, 'max_wall_s': 7200, 'max_consecutive_errors': 5}
ROOM_STATE = {'monster': 'monster', 'elite': 'elite', 'boss': 'boss'}
REST_HELP = {'HEAL': 'Heal 30% of your Max HP.', 'SMITH': 'Upgrade a card in your deck.'}
DECK_OPS = {'remove_deck_card': 'remove', 'upgrade_deck_card': 'upgrade', 'transform_deck_card': 'transform',
            'enchant_deck_card': 'enchant', 'grant_deck_card': 'add'}


class SimError(RuntimeError):
    pass


def canonical_seed(seed):
    seed = seed.strip().upper().replace('O', '0').replace('I', '1')
    if not seed or set(seed) - SEED_CHARS:
        raise ValueError(f'invalid seed {seed!r}')
    return seed


def import_sim():
    """Import ``sts2_rs`` (optionally from ``STS2_SIM_PATH``) or raise SimError with build steps."""
    extra = os.environ.get('STS2_SIM_PATH')
    if extra and extra not in sys.path:
        sys.path.insert(0, extra)
    try:
        import sts2_rs
    except ImportError as error:
        raise SimError(f'sts2_rs is not importable ({error}); build spire-mentor@{SPIRE_MENTOR_COMMIT[:7]} '
                       f'per docs/sim-engine.md or set STS2_SIM_PATH') from None
    return sts2_rs


# ------------------------------------------------------------------ names
def screaming(sim_id):
    """'FlameBarrierCard' / 'RELIC.BURNING_BLOOD' -> 'FLAME_BARRIER_CARD' / 'BURNING_BLOOD'."""
    s = str(sim_id or '')
    s = s.split('.', 1)[1] if '.' in s else s
    if s.isupper() or '_' in s:
        return s.upper()
    return re.sub(r'(?<=[a-z0-9])(?=[A-Z])|(?<=[A-Z])(?=[A-Z][a-z])', '_', s).upper()


def humanize(sim_id):
    return screaming(sim_id).replace('_', ' ').title()


class Names:
    """id -> name/description from our own knowledge/facts (v0.107.1)."""

    def __init__(self, db=None):
        if db is None:
            try:
                from lookup import FactDB
                db = FactDB.load()
            except Exception:  # facts missing: fall back to humanized ids
                db = None
        self.index = db.index if db is not None else {}

    def entry(self, cat, sim_id, suffixes=('',)):
        table = self.index.get(cat) or {}
        base = screaming(sim_id)
        for cand in (base, *(base + s for s in suffixes), re.sub(r'_(CARD|POWER|POTION|RELIC)$', '', base)):
            if cand in table:
                return table[cand]
        return None

    def card(self, sim_id, upgraded=False, cost=None):
        e = self.entry('cards', sim_id) or {}
        desc = (e.get('upgraded_description') if upgraded else None) or e.get('description') or ''
        base_cost = (e.get('upgraded_cost') if upgraded else None)
        base_cost = e.get('cost') if base_cost is None else base_cost
        return {'id': screaming(sim_id), 'name': (e.get('name') or humanize(sim_id)) + ('+' if upgraded else ''),
                'cost': base_cost if cost is None else cost, 'type': e.get('type'), 'rarity': e.get('rarity'),
                'description': desc, 'upgraded': bool(upgraded)}

    def simple(self, cat, sim_id, suffixes=('',)):
        e = self.entry(cat, sim_id, suffixes) or {}
        return {'id': screaming(sim_id), 'name': e.get('name') or humanize(sim_id),
                'description': e.get('description') or ''}

    def relic(self, sim_id):
        return self.simple('relics', sim_id)

    def potion(self, sim_id):
        return self.simple('potions', sim_id, ('_POTION',))

    def power(self, sim_id):
        return self.simple('powers', sim_id, ('_POWER',))

    def enemy(self, sim_id):
        return self.simple('enemies', sim_id)


# ------------------------------------------------------------------ game
class SimGame:
    """One seeded run inside the simulator, exposed as live-harness observations."""

    def __init__(self, seed, *, character='Ironclad', ascension=0, sim=None, names=None):
        self.sim = sim or import_sim()
        self.seed = canonical_seed(seed)
        self.character = character
        self.ascension = int(ascension)
        self.names = names or Names()
        self.rm = self.sim.RunManager(string_seed=self.seed, character=character, ascension_level=self.ascension,
                                      neow_start=True, act1='roll', interactive_card_removal=True,
                                      interactive_card_upgrade=True, interactive_card_transform=True,
                                      interactive_potion_swap=True)
        self._refs = []
        self.boss_entry_hp = []
        self.boss_kills = 0
        self._in_boss = False
        self.last_combat = None

    # ---------------------------------------------------------------- state
    def room_type(self):
        room = self.rm.current_room()
        return (room or {}).get('room_type') if isinstance(room, dict) else None

    def combat_dict(self):
        cs = self.rm.combat_state()
        return (cs, self.sim.state_to_dict(cs)) if cs is not None else (None, None)

    def state_type(self, actions):
        phase = self.rm.phase()
        kinds = {a.get('action') for a in actions}
        if self.rm.is_over():
            return 'game_over'
        if phase == 'combat':
            if kinds & {'combat_resolve_choice', 'combat_resolve_choice_selection'}:
                return 'hand_select'
            return ROOM_STATE.get(self.room_type(), 'monster')
        if kinds & set(DECK_OPS) or 'potion_swap' in kinds:
            return 'card_select'
        if phase == 'map_choice':
            return 'map'
        if phase == 'card_reward':
            return 'card_reward' if 'pick_card_reward' in kinds else 'rewards'
        if phase == 'boss_relic':
            return 'rewards'
        return {'event': 'event', 'shop': 'shop', 'rest_site': 'rest_site'}.get(phase, phase or 'unknown')

    def potions(self):
        out = []
        for slot, pid in enumerate(self.rm.potions()):
            if pid:
                out.append({'slot': slot, **self.names.potion(pid)})
        return out

    def deck_cards(self):
        return [self.names.card(cid, up) for cid, up in self.rm.master_deck_detailed()]

    def player(self, combat):
        rm = self.rm
        p = {'character': self.character, 'hp': rm.current_hp, 'max_hp': rm.max_hp, 'gold': rm.gold,
             'relics': [self.names.relic(r) for r in rm.relics()], 'potions': self.potions(),
             'max_potion_slots': rm.max_potion_slots()}
        if combat is None:
            p['deck'] = self.deck_cards()
            return p
        cp, deck = combat.get('player') or {}, combat.get('deck') or {}
        p.update({'hp': cp.get('hp', p['hp']), 'max_hp': cp.get('max_hp', p['max_hp']), 'block': cp.get('block', 0),
                  'energy': cp.get('energy'), 'max_energy': cp.get('max_energy', 3),
                  'status': [self.power_info(pw) for pw in cp.get('powers') or []]})
        for pile in ('hand', 'draw_pile', 'discard_pile', 'exhaust_pile'):
            cards = [self.combat_card(c) for c in deck.get(pile) or []]
            p[pile] = sorted(cards, key=lambda c: c['name']) if pile == 'draw_pile' else cards
            if pile != 'hand':
                p[f'{pile}_count'] = len(cards)
        return p

    def card_from(self, o):
        """A card from the simulator's ``(id, upgraded)`` tuples or ``'Id'`` / ``'Id+'`` strings."""
        if isinstance(o, (list, tuple)):
            return self.names.card(o[0], bool(o[1]) if len(o) > 1 else False)
        o = str(o)
        return self.names.card(o.rstrip('+'), o.endswith('+'))

    def combat_card(self, c):
        cost = c.get('cost_override') if c.get('cost_override') is not None else c.get('base_cost')
        card = self.names.card(c.get('id'), c.get('upgraded'), cost)
        if c.get('enchantment'):
            card['enchantment'] = f"{c['enchantment']} {c.get('enchantment_amount') or ''}".strip()
        if c.get('affliction'):
            card['affliction'] = f"{c['affliction']} {c.get('affliction_stacks') or ''}".strip()
        return card

    def power_info(self, pw):
        info = self.names.power(pw.get('name') or pw.get('id'))
        return {'name': info['name'], 'amount': pw.get('display_amount', pw.get('amount')),
                'description': info['description']}

    def enemies(self, combat):
        out = []
        for i, e in enumerate(combat.get('encounter') or []):
            info = self.names.enemy(e.get('kind'))
            intents = []
            for it in e.get('intents') or []:
                kind = it.get('kind')
                dmg = it.get('effective_amount', it.get('amount'))
                hits = it.get('times') or 1
                intent = {'type': kind}
                if kind in ('Attack', 'DeathBlow') and dmg is not None:
                    intent.update({'damage_per_hit': dmg, 'hits': hits, 'total_damage': dmg * hits})
                elif it.get('amount') is not None:
                    intent['amount'] = it['amount']
                intents.append(intent)
            out.append({'id': info['id'], 'name': info['name'], 'combat_id': i, 'hp': e.get('hp'),
                        'max_hp': e.get('max_hp'), 'block': e.get('block', 0), 'intents': intents,
                        'move': e.get('move_state'), 'is_alive': not e.get('is_dead') and (e.get('hp') or 0) > 0,
                        'status': [self.power_info(pw) for pw in e.get('powers') or []]})
        return out

    def map_block(self):
        nodes, seen = [], set()
        for n in self.rm.act_map():
            if (n['col'], n['row']) in seen:
                continue
            seen.add((n['col'], n['row']))
            nodes.append({'col': n['col'], 'row': n['row'], 'type': n['point_type'],
                          'children': [[c['col'], c['row']] if isinstance(c, dict) else list(c)
                                       for c in n.get('children') or []]})
        coord = self.rm.map_coord()
        full = self.rm.full_map() if hasattr(self.rm, 'full_map') else {}
        boss = (full or {}).get('boss') or {}
        return {'current_position': {'col': coord[0], 'row': coord[1]} if coord else None, 'nodes': nodes,
                'boss': {'name': self.names.enemy(boss.get('kind') or boss.get('id') or '')['name'],
                         'col': boss.get('col'), 'row': boss.get('row')} if boss else None}

    # ---------------------------------------------------------------- refs
    def label_ref(self, a, actions, combat, cs):
        """Readable, policy-facing ref for one simulator action."""
        kind = a.get('action')
        rm, names = self.rm, self.names
        if kind == 'play_card':
            hand = (combat or {}).get('deck', {}).get('hand') or []
            idx = a.get('hand_idx')
            ref = {'action': 'play_card', 'card': self.combat_card(hand[idx])['name'] if idx is not None
                   and idx < len(hand) else idx, 'hand_index': idx}
            if a.get('target_index') is not None:
                ref['target'] = self._enemy_ref(combat, a['target_index'])
            return ref
        if kind == 'end_turn':
            return {'action': 'end_turn'}
        if kind == 'drink_potion':
            pots = rm.potions()
            slot = a.get('potion_slot')
            ref = {'action': 'use_potion', 'slot': slot,
                   'potion': names.potion(pots[slot])['name'] if slot is not None and slot < len(pots) and pots[slot] else slot}
            if a.get('target_index') is not None:
                ref['target'] = self._enemy_ref(combat, a['target_index'])
            return ref
        if kind == 'combat_resolve_choice':
            opt = a.get('option')
            if opt is None:
                return {'action': 'skip_choice'}
            cards = self._choice_cards(cs)
            return {'action': 'select_card', 'card': cards[opt]['name'] if opt < len(cards) else opt, 'index': opt}
        if kind == 'move':
            node = next((n for n in rm.act_map() if n['col'] == a.get('col') and n['row'] == self._next_row()), None)
            return {'action': 'choose_map_node', 'col': a.get('col'), 'row': self._next_row(),
                    'type': node['point_type'] if node else None}
        if kind == 'event_choice':
            opts = self._event_options()
            opt = opts[a['idx']] if a['idx'] < len(opts) else {'title': f"option {a['idx']}"}
            return {'action': 'choose_event_option', 'index': a['idx'], 'option': opt['title']}
        if kind == 'pick_card_reward':
            if a.get('idx') is None:
                return {'action': 'skip_card_reward'}
            ids = rm.card_reward_option_ids()
            return {'action': 'select_card_reward', 'card': names.card(ids[a['idx']])['name'], 'index': a['idx']}
        if kind == 'pick_relic_reward':
            ids = rm.relic_reward_ids()
            return {'action': 'claim_reward', 'type': 'relic', 'relic': names.relic(ids[0])['name'] if ids else None}
        if kind == 'skip_relic':
            return {'action': 'skip_reward', 'type': 'relic'}
        if kind == 'pick_boss_relic':
            ids = rm.boss_relic_options()
            return {'action': 'choose_boss_relic', 'relic': names.relic(ids[a['idx']])['name'], 'index': a['idx']}
        if kind in ('shop_buy_card', 'shop_buy_relic', 'shop_buy_potion'):
            item = self._shop_items()[(kind[9:], a['idx'])]
            return {'action': kind, 'item': item['name'], 'price': item['price'], 'index': a['idx']}
        if kind == 'shop_remove_card':
            deck = self.deck_cards()
            return {'action': 'shop_remove_card', 'card': deck[a['deck_idx']]['name'], 'deck_index': a['deck_idx']}
        if kind == 'leave_shop':
            return {'action': 'leave_shop'}
        if kind == 'rest_option':
            name = rm.rest_site_options()[a['idx']][0]
            return {'action': 'choose_rest_option', 'option': name, 'index': a['idx']}
        if kind == 'smith_card':
            opts = rm.smith_options()
            card = self.card_from(opts[a['idx']]) if a['idx'] < len(opts) else None
            return {'action': 'smith_card', 'card': card['name'] if card else a['idx'], 'index': a['idx']}
        if kind in DECK_OPS:
            card = self._deck_op_card(kind, a['idx'])
            return {'action': kind, 'card': card['name'] if card else a['idx'], 'index': a['idx']}
        if kind == 'potion_swap':
            pots = rm.potions()
            slot = a.get('discard_slot')
            held = pots[slot] if slot is not None and slot < len(pots) else None
            new = getattr(rm, 'pending_potion_swap', None)
            return {'action': 'potion_swap', 'discard': names.potion(held)['name'] if held else
                    f'the new {names.potion(new)["name"] if new else "potion"}', 'slot': slot}
        return dict(a)

    def _enemy_ref(self, combat, idx):
        enemies = self.enemies(combat or {})
        return f"{enemies[idx]['name']}@{idx}" if idx < len(enemies) else idx

    def _choice_cards(self, cs):
        choice = cs.pending_choice() if cs is not None and hasattr(cs, 'pending_choice') else None
        cards = (choice or {}).get('cards') or []
        return [self.names.card(c.get('id'), c.get('upgraded')) for c in cards]

    def _next_row(self):
        coord = self.rm.map_coord()
        if coord:
            return coord[1] + 1
        nodes = self.rm.act_map()
        first = min((n['row'] for n in nodes), default=0)
        starts_with_ancient = any(n['row'] == first and str(n['point_type']).lower() == 'ancient' for n in nodes)
        return first + 1 if starts_with_ancient else first

    def _event_options(self):
        opts = []
        infos = (self.rm.event_option_infos() if hasattr(self.rm, 'event_option_infos') else None) or []
        for i, opt in enumerate(self.rm.event_options() or [(f'option_{j}', '') for j in range(len(infos or []))]):
            option_id, label = (opt[0], opt[1]) if isinstance(opt, (list, tuple)) else (opt, opt)
            info = infos[i] if i < len(infos) else {}
            relic = self.names.entry('relics', label) if label else None
            title = relic['name'] if relic else humanize(info.get('label') or label or option_id)
            desc = info.get('description') or ''
            if relic:
                desc = f"{desc} {relic['name']}: {relic.get('description')}".strip()
            opts.append({'index': i, 'title': title, 'description': desc,
                         'is_locked': not info.get('enabled', True)})
        return opts

    def _shop_items(self):
        inv = self.rm.shop_inventory() or {}
        items = {}
        for cat, key in (('card', 'cards'), ('relic', 'relics'), ('potion', 'potions')):
            for i, it in enumerate(inv.get(key) or []):
                it = it if isinstance(it, dict) else {'id': it[0], 'price': it[1]}
                sim_id = it.get('id') or it.get('card') or it.get('relic') or it.get('potion')
                info = (self.names.card(sim_id, it.get('upgraded')) if cat == 'card' else
                        getattr(self.names, cat)(sim_id))
                items[(cat, i)] = {**info, 'category': cat, 'price': it.get('price') or it.get('cost'),
                                   'on_sale': bool(it.get('on_sale')),
                                   'can_afford': (it.get('price') or 0) <= self.rm.gold}
        return items

    def _deck_op_card(self, kind, idx):
        rm = self.rm
        deck = self.deck_cards()
        if kind == 'remove_deck_card':
            opts = rm.card_removal_options()
            return self.card_from(opts[idx]) if idx < len(opts) else None
        if kind == 'grant_deck_card':
            opts = rm.card_grant_options()
            return self.card_from(opts[idx]) if idx < len(opts) else None
        return deck[idx] if idx < len(deck) else None  # upgrade/transform/enchant: raw master-deck indices

    # ---------------------------------------------------------------- observe/act
    def observe(self):
        rm = self.rm
        if not rm.is_over() and rm.phase() == 'event' and rm.current_event_name() is None \
                and hasattr(rm, 'materialize_event'):
            rm.materialize_event()  # RNG-free; the port otherwise binds the event lazily on the first choice
        actions = [] if rm.is_over() else list(rm.available_actions())
        st = self.state_type(actions)
        cs, combat = self.combat_dict() if rm.phase() == 'combat' and not rm.is_over() else (None, None)
        state = {'state_type': st, 'run': {'act': rm.act_index, 'floor': rm.total_floor(),
                                           'ascension': self.ascension, 'seed': self.seed},
                 'player': self.player(combat)}
        if combat is not None:
            state['battle'] = {'round': combat.get('round'), 'turn': str(combat.get('side') or 'player').lower(),
                               'is_play_phase': True, 'enemies': self.enemies(combat)}
        refs = [self.label_ref(a, actions, combat, cs) for a in actions]
        if st == 'map':
            state['map'] = self.map_block()
            state['map']['next_options'] = [{'col': r['col'], 'row': r['row'], 'type': r['type']}
                                            for r in refs if r.get('action') == 'choose_map_node']
        elif st == 'event':
            name = rm.current_event_name()
            state['event'] = {'event_name': self.names.simple('events', name)['name'] if name else None,
                              'options': self._event_options()}
        elif st == 'rest_site':
            state['rest_site'] = {'options': [{'name': n, 'is_enabled': bool(ok), 'description': REST_HELP.get(n, '')}
                                              for n, ok in rm.rest_site_options()]}
            if any(a.get('action') == 'smith_card' for a in actions):
                state['state_type'] = st = 'card_select'
                state['card_select'] = {'prompt': 'Choose a card to upgrade', 'min_select': 1, 'max_select': 1,
                                        'cards': [self.card_from(c) for c in rm.smith_options()]}
        elif st == 'shop':
            state['shop'] = {'can_proceed': True, 'items': list(self._shop_items().values())
                             + ([{'category': 'card_removal', 'price': None}]
                                if any(a.get('action') == 'shop_remove_card' for a in actions) else [])}
        elif st == 'card_reward':
            state['card_reward'] = {'prompt': 'Choose a card reward', 'can_skip': any(
                a.get('idx') is None for a in actions if a.get('action') == 'pick_card_reward'),
                'cards': [self.names.card(c) for c in rm.card_reward_option_ids()]}
        elif st == 'rewards':
            ids = rm.boss_relic_options() if rm.phase() == 'boss_relic' else rm.relic_reward_ids()
            relics = [self.names.relic(r) for r in ids]
            state['rewards'] = {'can_proceed': False, 'items': [
                {'type': 'relic', 'description': f"{r['name']}: {r['description']}"} for r in relics]}
        elif st == 'card_select':
            kind = next((DECK_OPS.get(a.get('action'), a.get('action')) for a in actions), '')
            state['card_select'] = {'prompt': f'Choose a card to {kind}', 'min_select': 1, 'max_select': 1,
                                    'cards': self.deck_cards()}
        elif st == 'hand_select':
            choice = cs.pending_choice() if cs is not None and hasattr(cs, 'pending_choice') else None
            state['hand_select'] = {'prompt': (choice or {}).get('kind') or 'Choose a card',
                                    'can_skip': any(a.get('option') is None for a in actions),
                                    'cards': self._choice_cards(cs)}
        elif st == 'game_over':
            state['game_over'] = {'victory': bool(rm.player_won)}
        self._refs = list(zip(refs, actions))
        return {'state': state, 'legal_actions': [{'ref': r} for r in refs], 'unavailable_actions': [],
                'engine': 'sim'}

    def resolve(self, chosen):
        """Map a policy's chosen ref (dict, or an action-name string) back to a simulator action."""
        for ref, act in self._refs:
            if chosen == ref:
                return act
        key = json.dumps(chosen, sort_keys=True, default=str)
        for ref, act in self._refs:
            if json.dumps(ref, sort_keys=True, default=str) == key:
                return act
        name = chosen if isinstance(chosen, str) else chosen.get('action') if isinstance(chosen, dict) else None
        if isinstance(chosen, dict) and len(chosen) > 1:
            subset = [act for ref, act in self._refs if all(ref.get(k) == v for k, v in chosen.items())]
            if len(subset) == 1:
                return subset[0]
        if name is not None and not (isinstance(chosen, dict) and len(chosen) > 1):
            same = [act for ref, act in self._refs if ref.get('action') == name]
            if len(same) == 1:
                return same[0]
        raise SimError(f'not a legal action here: {json.dumps(chosen, default=str)[:300]}')

    def act(self, chosen):
        sim_action = self.resolve(chosen)
        was_boss = self.rm.phase() == 'combat' and self.room_type() == 'boss'
        if was_boss and not self._in_boss:
            self._in_boss = True
            self.boss_entry_hp.append(self.rm.current_hp)
        if self.rm.phase() == 'combat':
            _, combat = self.combat_dict()
            self.last_combat = sorted(e.get('kind') for e in (combat or {}).get('encounter') or [])
        self.rm.take_action(sim_action)
        if self._in_boss and (self.rm.phase() != 'combat' or self.room_type() != 'boss'):
            self._in_boss = False
            if self.rm.current_hp > 0 and not (self.rm.is_over() and not self.rm.player_won):
                self.boss_kills += 1
        return sim_action

    def outcome(self):
        rm = self.rm
        over, won = rm.is_over(), bool(rm.player_won)
        cause = None
        if over and not won:
            cause = ('combat:' + '+'.join(self.last_combat)) if self.last_combat else 'unknown'
        return {'source': 'sim', 'win': won if over else None, 'act': rm.act_index, 'floor': rm.total_floor(),
                'boss_kills': self.boss_kills, 'boss_entry_hp': self.boss_entry_hp,
                'death_cause': cause, 'final_hp': rm.current_hp, 'max_hp': rm.max_hp, 'gold': rm.gold}


# ------------------------------------------------------------------ runner
class Recorder:
    def __init__(self, path):
        self.path = Path(path)
        self.path.parent.mkdir(parents=True, exist_ok=True)
        self.records = []

    def write(self, record):
        record = {'schema': SCHEMA, **record}
        self.records.append(record)
        with open(self.path, 'a', encoding='utf-8') as f:
            f.write(json.dumps(record, ensure_ascii=False, default=str) + '\n')


def summarize(obs):
    state = obs.get('state') or {}
    run, player, battle = state.get('run') or {}, state.get('player') or {}, state.get('battle') or {}
    enemies = [e.get('id') for e in battle.get('enemies') or [] if e.get('is_alive', True)]
    return {'state_type': state.get('state_type'), 'act': run.get('act'), 'floor': run.get('floor'),
            'hp': player.get('hp'), 'max_hp': player.get('max_hp'), 'gold': player.get('gold'),
            'round': battle.get('round'), 'enemies': enemies or None}


def play(game, rec, policy_name, policy, *, limits, wiki_facts=False, began=None, log_observations=None):
    began = began or time.monotonic()
    index, errors, avoid, last_error = 0, 0, [], None
    while True:
        if index >= limits['max_decisions']:
            return 'max_decisions'
        if time.monotonic() - began > limits['max_wall_s']:
            return 'max_wall_s'
        obs = game.observe()
        if obs['state']['state_type'] == 'game_over':
            rec.write({'type': 'terminal', 'index': index, **summarize(obs)})
            return 'game_over'
        if not obs['legal_actions']:
            return 'no_legal_actions'
        if log_observations is not None:
            with open(log_observations, 'a', encoding='utf-8') as f:
                f.write(json.dumps({'index': index, **obs}, ensure_ascii=False, default=str) + '\n')
        context = {'seed': game.seed, 'index': index, 'policy': policy_name, 'avoid': avoid,
                   'last_error': last_error, 'wiki_facts_enabled': bool(wiki_facts)}
        try:
            reply, policy_latency = policies.decide(policy, obs, context)
        except policies.PolicyError as error:
            rec.write({'type': 'friction', 'kind': 'policy_error', 'index': index, 'message': str(error)[:1000]})
            errors += 1
            avoid = []
            if errors >= limits['max_consecutive_errors']:
                return 'policy_error'
            continue
        t0 = time.monotonic()
        before = summarize(obs)
        try:
            sim_action = game.act(reply['action'])
            outcome = {'ok': True, 'error': None, 'message': None}
        except Exception as error:  # rejected by the adapter or the simulator
            sim_action = None
            outcome = {'ok': False, 'error': 'action_rejected', 'message': str(error)[:500]}
        act_latency = time.monotonic() - t0
        post = game.observe() if outcome['ok'] else obs
        outcome.update({'post_state_type': post['state']['state_type'], 'post_hp': post['state']['player']['hp']})
        rec.write({'type': 'decision', 'seed': game.seed, 'instance': 'sim', 'index': index,
                   't_wall_s': round(time.monotonic() - began, 3), **before,
                   'legal_actions': [a['ref'] for a in obs['legal_actions']], 'chosen': reply['action'],
                   'sim_action': sim_action, 'rationale': reply.get('reason'), 'policy': policy_name,
                   'outcome': outcome, 'latency_s': {'policy': round(policy_latency, 3), 'act': round(act_latency, 6)},
                   **({'llm': reply['llm']} if reply.get('llm') else {})})
        index += 1
        if outcome['ok']:
            errors, avoid, last_error = 0, [], None
            continue
        errors += 1
        last_error = {'error': outcome['error'], 'message': outcome['message']}
        if isinstance(reply['action'], dict):
            avoid.append(reply['action'])
        if errors >= limits['max_consecutive_errors']:
            return 'harness_stuck'


def run_seed(seed, out_root, *, policy_name, policy, character='Ironclad', ascension=0, limits=None,
             wiki_facts=False, benchmark=None, sim=None, names=None, log_observations=False):
    seed = canonical_seed(seed)
    seed_dir = Path(out_root) / seed
    if seed_dir.exists():
        raise SimError(f'{seed_dir} exists; use a new --out')
    limits = {**DEFAULT_LIMITS, **(limits or {})}
    rec = Recorder(seed_dir / 'trajectory.jsonl')
    began = time.monotonic()
    game, end_reason, error = None, None, None
    try:
        game = SimGame(seed, character=character, ascension=ascension, sim=sim, names=names)
        rec.write({'type': 'run_start', 'seed': seed, 'instance': 'sim', 'policy': policy_name,
                   'wiki_facts': bool(wiki_facts), 'character': character, 'ascension': ascension,
                   'benchmark': benchmark, 'engine': engine_info(game.sim)})
        end_reason = play(game, rec, policy_name, policy, limits=limits, wiki_facts=wiki_facts, began=began,
                          log_observations=seed_dir / 'observations.jsonl' if log_observations else None)
    except Exception as exc:  # recorded, never silently dropped
        error = f'{type(exc).__name__}: {exc}'
        end_reason = end_reason or 'runner_error'
        rec.write({'type': 'friction', 'kind': 'harness_crash', 'message': error[:1000],
                   'traceback': traceback.format_exc()[-2000:]})
    wall = round(time.monotonic() - began, 3)
    result = {'seed': seed, 'instance': 'sim', 'engine': 'sim', 'policy': policy_name, 'wiki_facts': bool(wiki_facts),
              'benchmark': benchmark, 'character': character, 'ascension': ascension, 'error': error,
              **metrics_mod.run_metrics(rec.records, None, wall_time_s=wall, end_reason=end_reason)}
    if game is not None:
        result.update(game.outcome())
        result['hp_at_boss_entry'] = result['boss_entry_hp'][0] if result['boss_entry_hp'] else None
        if result['win'] is None and end_reason != 'game_over':
            result['death_cause'] = f'not_finished:{end_reason}'
    rec.write({'type': 'run_end', 'metrics': result})
    (seed_dir / 'result.json').write_text(json.dumps(result, ensure_ascii=False, indent=2, default=str) + '\n')
    return result


def engine_info(sim):
    info = {'name': 'spire-mentor', 'repo': SPIRE_MENTOR_REPO, 'pinned_commit': SPIRE_MENTOR_COMMIT}
    for attr in ('__version__', 'build_git', 'game_version'):
        v = getattr(sim, attr, None)
        if v is not None:
            info[attr] = v() if callable(v) else v
    return info


def main(argv=None):
    p = argparse.ArgumentParser(description=__doc__.split('\n\n')[0])
    p.add_argument('--policy', default='stub', help='policies.load_policy spec (stub, py:llm_policy:policy, ...)')
    p.add_argument('--seeds', help='comma-separated seeds (default: the config seeds)')
    p.add_argument('--config', type=Path, help='bench config json (name, character, ascension, seeds, limits)')
    p.add_argument('--character', default=None)
    p.add_argument('--ascension', type=int, default=None)
    p.add_argument('--out', type=Path, required=True)
    p.add_argument('--wiki-facts', action='store_true')
    p.add_argument('--max-decisions', type=int)
    p.add_argument('--max-wall-s', type=float)
    p.add_argument('--log-observations', action='store_true', help='also write observations.jsonl per seed')
    a = p.parse_args(argv)
    config = json.loads(a.config.read_text()) if a.config else {}
    character = a.character or config.get('character') or 'Ironclad'
    ascension = a.ascension if a.ascension is not None else int(config.get('ascension') or 0)
    seeds = a.seeds.split(',') if a.seeds else config.get('seeds')
    if not seeds:
        raise SystemExit('give --seeds or a --config with seeds')
    limits = dict(config.get('limits') or {})
    if a.max_decisions:
        limits['max_decisions'] = a.max_decisions
    if a.max_wall_s:
        limits['max_wall_s'] = a.max_wall_s
    sim = import_sim()
    policy_name, policy = policies.load_policy(a.policy)
    a.out.mkdir(parents=True, exist_ok=True)
    (a.out / 'manifest.json').write_text(json.dumps({
        'engine': engine_info(sim), 'policy': policy_name, 'character': character, 'ascension': ascension,
        'seeds': [canonical_seed(s) for s in seeds], 'benchmark': config.get('name'), 'limits': limits,
        'wiki_facts': bool(a.wiki_facts)}, indent=2, default=str) + '\n')
    for seed in seeds:
        r = run_seed(seed, a.out, policy_name=policy_name, policy=policy, character=character, ascension=ascension,
                     limits=limits, wiki_facts=a.wiki_facts, benchmark=config.get('name'), sim=sim,
                     log_observations=a.log_observations)
        print(f"[sim] {r['seed']}: {r['end_reason']} act={r['act']} floor={r['floor']} win={r['win']} "
              f"bosses={r['boss_kills']} decisions={r['decisions']} frictions={r['friction_total']} "
              f"wall={r['wall_time_s']}s", flush=True)
    return 0


if __name__ == '__main__':
    sys.exit(main())
