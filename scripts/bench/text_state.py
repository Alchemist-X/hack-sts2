#!/usr/bin/env python3
"""Compact, complete, deterministic text encoding of one benchmark observation.

The benchmark policy input is the JSON printed by ``sts2_live.py observe``
(``state`` = limited information view with piles grouped by ``count``,
``legal_actions[*].ref``, ``unavailable_actions``, optional ``wiki_facts``).
:func:`encode` turns that into a plain-text block for an LLM:

* the player (HP, block, energy, gold, powers, relics, potions, piles),
* the hand, enemies with intents and powers,
* the screen-specific block (map, rewards, card reward, selection, shop,
  event, rest site, treasure, game over),
* a numbered legal-action list (``#n`` -> the exact ``ref`` it stands for),
* a glossary with each keyword / power text once,
* optionally the wiki facts the harness attached.

Nothing visible is dropped: keys the encoder does not know are appended as
compact JSON under "Other", so new game fields never silently disappear.
Same observation in, same bytes out (no clocks, no dict-order dependence on
anything but the observation itself).

CLI::

    # encode one observation (observe output or a bench observations.jsonl record)
    python3 scripts/bench/text_state.py encode obs.json [--facts]
    # token comparison over bench logs / fixtures
    python3 scripts/bench/text_state.py measure <observations.jsonl|state.json> [...] [--json]
"""
import argparse
import collections
import json
import re
import statistics
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import map_lookahead  # noqa: E402

SCRIPTS = Path(__file__).resolve().parents[1]
if str(SCRIPTS) not in sys.path:
    sys.path.insert(0, str(SCRIPTS))

COMBAT = ('monster', 'elite', 'boss')
SCREEN_KEYS = ('map', 'rewards', 'card_reward', 'card_select', 'hand_select', 'shop', 'event',
               'rest_site', 'treasure', 'game_over', 'battle')
# Fields that never carry decision information for a model (engine ids, positional
# indices that go stale, provenance strings). Everything else is rendered.
NOISE = {'model_id', 'index', 'entity_id', 'damage_scope', 'damage_source', 'keywords', 'id'}
ICON = re.compile(r'\[([a-z_]+?)_icon\.png\]')
NODE = {'Monster': 'M', 'Elite': 'E', 'RestSite': 'R', 'Shop': '$', 'Treasure': 'T', 'Unknown': '?',
        'Boss': 'B', 'Ancient': 'A', 'Event': '?'}


def _icon(match):
    name = match.group(1)
    if name.endswith('energy'):
        return '[E]'
    if 'star' in name:
        return '[Star]'
    return f'[{name}]'


def clean(text):
    if text is None:
        return ''
    text = ICON.sub(_icon, str(text))
    return re.sub(r'\s+', ' ', text).strip()


def compact(value):
    return json.dumps(value, ensure_ascii=False, sort_keys=True, separators=(',', ':'))


class Glossary:
    """Keyword / power texts, each printed once at the end."""

    def __init__(self):
        self.entries = {}

    def add_keywords(self, item):
        for kw in item.get('keywords') or []:
            if isinstance(kw, dict) and kw.get('name') and kw.get('description'):
                self.entries.setdefault(clean(kw['name']), clean(kw['description']))

    def render(self):
        if not self.entries:
            return []
        return ['## Glossary'] + [f'{k}: {v}' for k, v in sorted(self.entries.items())]


def _rest(item, used, gl):
    """Unknown non-empty keys of ``item`` as compact JSON (completeness guard)."""
    if not isinstance(item, dict):
        return ''
    gl.add_keywords(item)
    extra = {k: v for k, v in sorted(item.items()) if k not in used and k not in NOISE
             and v not in (None, '', [], {}, False)}
    return f' {{{compact(extra)[1:-1]}}}' if extra else ''


def powers(items, gl):
    out = []
    for p in items or []:
        if not isinstance(p, dict):
            out.append(clean(p))
            continue
        name = clean(p.get('name') or p.get('id'))
        amount = p.get('amount')
        out.append(f'{name} {amount}' if amount not in (None, '') else name)
        if p.get('description'):
            gl.entries.setdefault(f'{name} ({p.get("type") or "power"})' if p.get('type') else name,
                                  clean(p['description']))
        gl.add_keywords(p)
    return ', '.join(out)


def preview(rule_preview):
    """Engine per-target numbers when they differ from the card text (Vulnerable, Weak, ...)."""
    out = []
    for var in (rule_preview or {}).get('variables') or []:
        if not isinstance(var, dict):
            continue
        base = var.get('base_value')
        if var.get('damage_by_target'):
            vals = [(t.get('combat_id'), t.get('before_block')) for t in var['damage_by_target'] if isinstance(t, dict)]
            if any(v != base for _, v in vals):
                out.append('dmg ' + ' '.join(f'@{c}={v}' for c, v in vals))
        elif var.get('block_preview') is not None and var['block_preview'] != base:
            out.append(f'block {var["block_preview"]}')
    return f' (engine: {"; ".join(out)})' if out else ''


CARD_USED = {'name', 'rule_preview', 'cost', 'star_cost', 'type', 'description', 'rarity', 'is_upgraded', 'can_play',
             'unplayable_reason', 'target_type', 'enchantment', 'affliction', 'count', 'card_id', 'card_name',
             'card_cost', 'card_star_cost', 'is_selected', 'card_type', 'card_description', 'card_rarity'}


def card(c, gl, *, rarity=False, target=False, used=()):
    name = clean(c.get('name') or c.get('card_name') or c.get('id'))
    cost = c.get('cost', c.get('card_cost'))
    star = c.get('star_cost', c.get('card_star_cost'))
    ctype = c.get('type') or c.get('card_type')
    desc = clean(c.get('description') or c.get('card_description'))
    head = f'{name} [{cost}{"" if star in (None, "") else f"+{star}*"}]'
    bits = [ctype] if ctype else []
    if rarity and (c.get('rarity') or c.get('card_rarity')):
        bits.append(c.get('rarity') or c.get('card_rarity'))
    if target and c.get('target_type') not in (None, 'None', 'Self'):
        bits.append(f'->{c["target_type"]}')
    line = f'{head} {"/".join(bits)}: {desc}' if bits else f'{head}: {desc}'
    for k in ('enchantment', 'affliction'):
        v = c.get(k)
        if isinstance(v, dict):
            amt = f' {v["amount"]}' if v.get('amount') not in (None, '') else ''
            line += f' <{k} {clean(v.get("name") or v.get("id"))}{amt}: {clean(v.get("description"))}>'
        elif v:
            line += f' <{k} {compact(v) if isinstance(v, list) else clean(v)}>'
    if c.get('is_selected'):
        line += ' [selected]'
    line += preview(c.get('rule_preview'))
    if c.get('can_play') is False:
        line += f' (UNPLAYABLE: {clean(c.get("unplayable_reason")) or "?"})'
    return line + _rest(c, CARD_USED | set(used), gl)


def pile(cards, count, title, gl):
    cards = [c for c in cards or [] if isinstance(c, dict)]
    if not cards:
        return f'{title} ({count if count is not None else 0})'
    grouped = collections.Counter()
    for c in cards:
        grouped[clean(c.get('name'))] += int(c.get('count') or 1)
    for c in cards:
        gl.add_keywords(c)
    body = ', '.join(f'{n} x{k}' if k > 1 else n for n, k in sorted(grouped.items()))
    return f'{title} ({count if count is not None else sum(grouped.values())}): {body}'


PLAYER_USED = {'character', 'hp', 'max_hp', 'block', 'energy', 'max_energy', 'gold', 'status', 'relics',
               'potions', 'max_potion_slots', 'hand', 'draw_pile', 'discard_pile', 'exhaust_pile',
               'draw_pile_count', 'discard_pile_count', 'exhaust_pile_count', 'deck', 'stars', 'max_stars'}


def player_block(p, gl, in_combat):
    lines = ['## You']
    head = f'{clean(p.get("character"))} HP {p.get("hp")}/{p.get("max_hp")} Block {p.get("block") or 0}'
    if p.get('energy') is not None:
        head += f' Energy {p.get("energy")}/{p.get("max_energy")}'
    if p.get('stars') is not None:
        head += f' Stars {p.get("stars")}' + (f'/{p["max_stars"]}' if p.get('max_stars') is not None else '')
    head += f' Gold {p.get("gold")}'
    lines.append(head)
    if p.get('status'):
        lines.append(f'Powers: {powers(p["status"], gl)}')
    relics = []
    for r in p.get('relics') or []:
        gl.add_keywords(r)
        ctr = f' ({r["counter"]})' if r.get('counter') not in (None, '', -1) else ''
        desc = clean(r.get('description'))
        relics.append(f'{clean(r.get("name"))}{ctr}' + (f': {desc}' if desc else '')
                      + _rest(r, {'name', 'description', 'counter'}, gl))
    if relics:
        lines.append('Relics: ' + ' | '.join(relics))
    pots = []
    for pt in p.get('potions') or []:
        gl.add_keywords(pt)
        flag = '' if pt.get('can_use_in_combat', True) else ' (not in combat)'
        tgt = f' ->{pt["target_type"]}' if pt.get('target_type') not in (None, 'None', 'Self') else ''
        pots.append(f'slot{pt.get("slot")} {clean(pt.get("name"))}{tgt}{flag}: {clean(pt.get("description"))}'
                    + _rest(pt, {'slot', 'name', 'description', 'can_use_in_combat', 'target_type'}, gl))
    lines.append(f'Potions {len(pots)}/{p.get("max_potion_slots")}: ' + (' | '.join(pots) if pots else 'none'))
    if in_combat or any(k in p for k in ('draw_pile_count', 'draw_pile')):
        lines.append(pile(p.get('draw_pile'), p.get('draw_pile_count'), 'Draw', gl) + '; '
                     + pile(p.get('discard_pile'), p.get('discard_pile_count'), 'Discard', gl) + '; '
                     + pile(p.get('exhaust_pile'), p.get('exhaust_pile_count'), 'Exhaust', gl))
    if p.get('deck'):
        lines.append(pile(p['deck'], None, 'Deck', gl))
    if p.get('hand') is not None:
        lines.append(f'## Hand ({len(p["hand"])})')
        for i, c in enumerate(p['hand']):
            lines.append(f'H{i} ' + card(c, gl, target=True))
    rest = _rest(p, PLAYER_USED, gl)
    if rest:
        lines.append('Other:' + rest)
    return lines


def intent(i):
    if not isinstance(i, dict):
        return clean(i)
    kind = i.get('type') or i.get('title') or '?'
    if i.get('total_damage') is not None:
        hits = i.get('hits') or 1
        dmg = f'{i.get("damage_per_hit")}x{hits}={i["total_damage"]}' if hits > 1 else f'{i["total_damage"]}'
        return f'{kind} {dmg}'
    label = clean(i.get('label'))
    return f'{kind} {label}'.strip() if label and label != kind else kind


ENEMY_USED = {'name', 'combat_id', 'hp', 'max_hp', 'block', 'intents', 'status', 'is_alive', 'is_targetable',
              'current_move_id'}


def battle_block(b, gl):
    import sts2_action_refs as refs
    lines = [f'## Combat round {b.get("round")} turn={b.get("turn")} play_phase={b.get("is_play_phase")}'
             + (' ENDING' if b.get('is_over_or_ending') else '')]
    for e in b.get('enemies') or []:
        alive = '' if e.get('is_alive', True) else ' DEAD'
        tgt = '' if e.get('is_targetable', True) else ' untargetable'
        line = (f'{refs.enemy_ref(e)} HP {e.get("hp")}/{e.get("max_hp")} Block {e.get("block") or 0}{alive}{tgt}'
                f' | intent: {"; ".join(intent(i) for i in e.get("intents") or []) or "none"}')
        for i in e.get('intents') or []:
            if isinstance(i, dict) and i.get('title') and i.get('description'):
                gl.entries.setdefault(f'intent {clean(i["title"])}', 'see enemy line')  # title only
        if e.get('status'):
            line += f' | {powers(e["status"], gl)}'
        lines.append(line + _rest(e, ENEMY_USED, gl))
    if b.get('inactive_enemies'):
        lines.append('Inactive: ' + ', '.join(
            f'{refs.enemy_ref(e)} HP {e.get("hp")}/{e.get("max_hp")}{"" if e.get("is_alive") else " dead"}'
            if isinstance(e, dict) else clean(e) for e in b['inactive_enemies']))
    rest = _rest(b, {'round', 'turn', 'is_play_phase', 'is_over_or_ending', 'enemies', 'inactive_enemies',
                     'player_turn'}, gl)
    if rest:
        lines.append('Other:' + rest)
    # intent titles are redundant with the type; drop the placeholder entries
    for k in [k for k in gl.entries if k.startswith('intent ')]:
        del gl.entries[k]
    return lines


def map_block(m, gl, *, floor=None, hp_frac=None):
    cur = m.get('current_position') or {}
    lines = [f'## Map: at ({cur.get("col")},{cur.get("row")}) {cur.get("type", "")}; boss '
             + ', '.join(f'{clean(b.get("name"))} (row {b.get("row")})' for b in m.get('bosses') or [m.get('boss') or {}])]
    lines.append('Legend: M monster, E elite, R rest, $ shop, T treasure, ? unknown, A ancient, B boss; '
                 'node col,row -> children cols in next row')
    for o in m.get('next_options') or []:
        leads = ' '.join(f'{x.get("col")},{x.get("row")}{NODE.get(x.get("type"), x.get("type"))}'
                         for x in o.get('leads_to') or [])
        lines.append(f'Next: {o.get("col")},{o.get("row")} {o.get("type")} -> {leads}')
    lines += lookahead_lines(m, floor=floor, hp_frac=hp_frac)
    rows = collections.defaultdict(list)
    floor = cur.get('row') if isinstance(cur.get('row'), int) else -1
    for n in m.get('nodes') or []:
        if isinstance(n.get('row'), int) and n['row'] > floor:
            kids = ','.join(str(c[0]) for c in n.get('children') or [])
            rows[n['row']].append((n.get('col'), f'{n.get("col")}{NODE.get(n.get("type"), n.get("type"))}>{kids}'))
    for r in sorted(rows):
        lines.append(f'r{r}: ' + ' '.join(t for _, t in sorted(rows[r])))
    rest = _rest(m, {'boss', 'bosses', 'current_position', 'next_options', 'nodes', 'visited', 'path'}, gl)
    if m.get('visited') or m.get('path'):
        lines.append('Visited: ' + ' '.join(
            f'{v.get("col")},{v.get("row")}{NODE.get(v.get("type"), v.get("type"))}' if isinstance(v, dict) else clean(v)
            for v in m.get('visited') or m.get('path')))
    if rest:
        lines.append('Other:' + rest)
    return lines


def lookahead_lines(m, *, floor=None, hp_frac=None):
    """Compact per-option path summary (see map_lookahead.py); never breaks the encoder."""
    try:
        stats = map_lookahead.lookahead(m)
    except Exception as exc:  # noqa: BLE001 - lookahead is advisory
        return [f'Lookahead unavailable: {type(exc).__name__}']
    if not stats:
        return []
    cur = (m.get('current_position') or {}).get('row')
    off = floor - cur if isinstance(floor, int) and isinstance(cur, int) else None
    head = 'Lookahead to boss (E/R/$/?/T min-max over all paths; r=row' + (f', floor=row+{off}' if off is not None else '') + '):'
    out = [head]
    low = isinstance(hp_frac, (int, float)) and hp_frac < 0.6
    for st in stats:
        line = map_lookahead.fmt(st)
        if low and st.get('e_before_r'):
            line += ' | !HP<60% avoid'
        out.append(line)
    return out


def list_block(title, items, fmt, gl, extra_used=()):
    lines = [title]
    for it in items or []:
        lines.append('- ' + fmt(it))
    return lines


def screen_block(st, block, gl, **map_ctx):
    if not isinstance(block, dict):
        return [f'## {st}', compact(block)]
    used = set()
    lines = []
    if st == 'map':
        return map_block(block, gl, **map_ctx)
    if st == 'rewards':
        lines.append(f'## Rewards (can_proceed={block.get("can_proceed")})')
        for it in block.get('items') or []:
            gl.add_keywords(it)
            desc = clean(it.get('description'))
            if it.get('potion_name'):
                desc = f'{clean(it["potion_name"])}: {clean(it.get("potion_description"))}'
            lines.append(f'- {it.get("type")}: {desc}' + _rest(it, {'type', 'description', 'potion_name',
                                                                      'potion_description', 'potion_id',
                                                                      'gold_amount'}, gl))
        used = {'items', 'can_proceed'}
    elif st in ('card_reward', 'card_select', 'hand_select'):
        flags = {k: block[k] for k in ('can_skip', 'can_cancel', 'can_confirm', 'min_select', 'max_select',
                                       'mode', 'screen_type') if block.get(k) not in (None, '')}
        lines.append(f'## {st} {clean(block.get("prompt"))} ' + ' '.join(f'{k}={v}' for k, v in flags.items()))
        for c in block.get('cards') or []:
            lines.append('- ' + card(c, gl, rarity=True))
        if block.get('selected'):
            lines.append('Selected: ' + ', '.join(clean(c.get('name')) if isinstance(c, dict) else clean(c)
                                                  for c in block['selected']))
        for c in block.get('preview_cards') or []:
            if isinstance(c, dict):
                lines.append('Preview: ' + card(c, gl))
        for key in ('selected_cards',):
            if block.get(key):
                lines.append('Selected: ' + ', '.join(clean(c.get('name')) if isinstance(c, dict) else clean(c)
                                                      for c in block[key]))
        if block.get('selected_count') and not block.get('selected_cards'):
            lines.append(f'Selected: {block["selected_count"]} (indices {block.get("selected_indices")})')
        # preview_originals repeats `cards`; cycles_random_examples is UI-only.
        used = {'cards', 'prompt', 'selected', 'preview_showing', 'selection_tracking', 'preview_cards',
                'preview_originals', 'cycles_random_examples', 'selected_cards', 'selected_count',
                'selected_indices', *flags}
    elif st == 'shop':
        lines.append(f'## Shop (can_proceed={block.get("can_proceed")})')
        for it in block.get('items') or []:
            if not it.get('is_stocked', True):
                continue
            sale = ' SALE' if it.get('on_sale') else ''
            afford = '' if it.get('can_afford', True) else ' (cannot afford)'
            cat = it.get('category')
            used_i = {'category', 'price', 'on_sale', 'can_afford', 'is_stocked', 'name', 'description',
                      f'{cat}_name', f'{cat}_description', f'{cat}_id', *CARD_USED}
            if cat == 'card':
                what = card(it, gl, rarity=True, used=used_i)
            elif cat == 'card_removal':
                what = 'remove one card from your deck'
            else:
                gl.add_keywords(it)
                name = clean(it.get(f'{cat}_name') or it.get('name') or it.get('description'))
                desc = clean(it.get(f'{cat}_description') or (it.get('description') if it.get('name') else ''))
                what = f'{name}: {desc}' if desc else name
            lines.append(f'- {cat} {it.get("price")}g{sale}{afford} {what}'
                         + ('' if cat == 'card' else _rest(it, used_i, gl)))
        used = {'items', 'can_proceed'}
    elif st == 'event':
        lines.append(f'## Event: {clean(block.get("event_name"))}' + (' (ancient)' if block.get('is_ancient') else '')
                     + (' [in dialogue]' if block.get('in_dialogue') else ''))
        if block.get('body'):
            lines.append(clean(block['body']))
        for o in block.get('options') or []:
            gl.add_keywords(o)
            flags = ''.join(f' [{f}]' for f in ('is_locked', 'is_proceed', 'was_chosen') if o.get(f))
            desc = clean(o.get('description'))
            line = f'- {clean(o.get("title"))}{flags}: {desc}'
            if o.get('relic_description') and clean(o['relic_description']) != desc:
                line += f' (relic {clean(o.get("relic_name"))}: {clean(o["relic_description"])})'
            lines.append(line + _rest(o, {'title', 'description', 'is_locked', 'is_proceed', 'was_chosen',
                                          'relic_name', 'relic_description'}, gl))
        used = {'event_id', 'event_name', 'is_ancient', 'in_dialogue', 'body', 'options'}
    elif st == 'rest_site':
        lines.append(f'## Rest site (can_proceed={block.get("can_proceed")})')
        for o in block.get('options') or []:
            dis = '' if o.get('is_enabled', True) else ' (disabled)'
            lines.append(f'- {clean(o.get("name"))}{dis}: {clean(o.get("description"))}'
                         + _rest(o, {'name', 'description', 'is_enabled'}, gl))
        used = {'options', 'can_proceed'}
    elif st == 'treasure':
        lines.append(f'## Treasure (can_proceed={block.get("can_proceed")})')
        for r in block.get('relics') or []:
            gl.add_keywords(r)
            lines.append(f'- {clean(r.get("name"))} ({r.get("rarity")}): {clean(r.get("description"))}'
                         + _rest(r, {'name', 'description', 'rarity'}, gl))
        used = {'relics', 'can_proceed'}
    else:
        lines.append(f'## {st}')
    rest = _rest(block, used, gl)
    if rest:
        lines.append('Other:' + rest)
    return lines


def action_label(ref):
    if not isinstance(ref, dict):
        return clean(ref)
    parts = [str(ref.get('action'))]
    for k, v in ref.items():
        if k == 'action':
            continue
        parts.append(f'{k}={compact(v) if isinstance(v, (dict, list)) else v}')
    return ' '.join(parts)


def legal_refs(observation):
    return [a['ref'] for a in observation.get('legal_actions') or []
            if isinstance(a, dict) and isinstance(a.get('ref'), dict)]


def selection_size(observation):
    """(min, max) when the screen needs several cards picked in one answer, else None."""
    state = observation.get('state') if isinstance(observation.get('state'), dict) else observation
    block = state.get(state.get('state_type')) if isinstance(state.get(state.get('state_type')), dict) else {}
    lo = block.get('min_select')
    if isinstance(lo, int) and lo > 1:
        hi = block.get('max_select')
        return lo, hi if isinstance(hi, int) and hi >= lo else lo
    return None


def encode(observation, *, facts=False, header=True):
    """Text block for one observe output (``{'state': ..., 'legal_actions': ...}``)."""
    state = observation.get('state') if isinstance(observation.get('state'), dict) else observation
    st = state.get('state_type')
    gl = Glossary()
    run = state.get('run') or {}
    lines = []
    if header:
        lines.append(f'# STS2 Act {run.get("act")} Floor {run.get("floor")} Ascension {run.get("ascension")} '
                     f'| screen: {st}' + _rest(run, {'act', 'floor', 'ascension', 'seed'}, gl))
    if isinstance(state.get('player'), dict):
        lines += player_block(state['player'], gl, st in COMBAT)
    if isinstance(state.get('battle'), dict):
        lines += battle_block(state['battle'], gl)
    p = state.get('player') if isinstance(state.get('player'), dict) else {}
    hp_frac = p['hp'] / p['max_hp'] if isinstance(p.get('hp'), (int, float)) and p.get('max_hp') else None
    map_ctx = {'floor': run.get('floor'), 'hp_frac': hp_frac}
    if st in state and st != 'battle' and st not in COMBAT:
        lines += screen_block(st, state[st], gl, **(map_ctx if st == 'map' else {}))
    for key in SCREEN_KEYS:
        if key in state and key not in (st, 'battle'):
            lines += screen_block(key, state[key], gl)
    known = {'player', 'run', 'state_type', 'execution', 'rules_context', *SCREEN_KEYS}
    extra = {k: v for k, v in sorted(state.items()) if k not in known and v not in (None, '', [], {})}
    if extra:
        lines.append('Other state: ' + compact(extra))
    refs = legal_refs(observation)
    lines.append(f'## Legal actions ({len(refs)}) - answer with the number')
    for i, ref in enumerate(refs):
        lines.append(f'{i}: {action_label(ref)}')
    size = selection_size(observation)
    if size:
        lines.append(f'Multi-select: answer with a list of {size[0]}' + (f'-{size[1]}' if size[1] != size[0] else '')
                     + ' numbers; repeat a number to pick another copy of the same card.')
    blocked = observation.get('unavailable_actions') or []
    if blocked:
        shown = []
        for b in blocked:
            if isinstance(b, dict):
                what = action_label(b.get('ref') or {k: v for k, v in b.items() if k not in ('reason', 'reasons')})
                why = clean(b.get('reason') or ', '.join(map(str, b.get('reasons') or [])))
                shown.append(f'{what} ({why})' if why else what)
            else:
                shown.append(clean(b))
        lines.append('Unavailable: ' + '; '.join(shown))
    lines += gl.render()
    if facts and isinstance(observation.get('wiki_facts'), str) and observation['wiki_facts'].strip():
        lines.append('## Wiki facts (v0.107.1 reference, may lag the live game)')
        lines.append(observation['wiki_facts'].strip())
    return '\n'.join(lines) + '\n'


# ------------------------------------------------------------ measurement
def observation_from_record(record):
    """Rebuild the policy input from a bench ``observations.jsonl`` record."""
    import sts2_action_refs as refs
    state = record.get('observation') or record
    public = json.loads(json.dumps(state))
    player = public.get('player') or {}
    for key in ('draw_pile', 'discard_pile', 'exhaust_pile', 'deck'):
        cards = player.get(key)
        if isinstance(cards, list):
            counts = collections.Counter(compact({k: v for k, v in c.items() if k != 'index'}) for c in cards)
            player[key] = [{**json.loads(c), 'count': n} for c, n in sorted(counts.items())]
    return {'state': public, 'legal_actions': refs.observe_actions(state),
            'unavailable_actions': refs.blocked_actions(state)}


def load_observations(path):
    """Yield policy-input observations from a raw state .json, an observe .json or observations.jsonl."""
    import sts2_live as live
    path = Path(path)
    if path.suffix == '.jsonl':
        for line in path.read_text().splitlines():
            if line.strip():
                rec = json.loads(line)
                if isinstance(rec.get('observation'), dict) and rec['observation'].get('state_type'):
                    yield observation_from_record(rec)
        return
    data = json.loads(path.read_text())
    if 'legal_actions' in data and 'state' in data:
        yield data
    else:
        yield live.public_display(data)


def counter():
    try:
        import tiktoken
        enc = tiktoken.get_encoding('o200k_base')
        return 'o200k_base', lambda s: len(enc.encode(s))
    except Exception:  # noqa: BLE001 - optional dependency
        return 'chars/4', lambda s: (len(s) + 3) // 4


def measure(paths, facts=False):
    name, count = counter()
    rows = collections.defaultdict(list)
    for p in paths:
        for obs in load_observations(p):
            st = obs['state'].get('state_type')
            full = json.dumps(obs, ensure_ascii=False)  # what a cmd:/py: policy receives (no rules_context dup)
            text = encode(obs, facts=facts)
            rows[st].append((count(full), count(text), len(full), len(text)))
    total = [r for rs in rows.values() for r in rs]
    summary = {'tokenizer': name, 'decisions': len(total), 'by_state': {}}

    def agg(rs):
        jt, tt = sum(r[0] for r in rs), sum(r[1] for r in rs)
        return {'n': len(rs), 'json_tokens_mean': round(jt / len(rs)), 'text_tokens_mean': round(tt / len(rs)),
                'text_tokens_median': statistics.median(r[1] for r in rs),
                'text_tokens_max': max(r[1] for r in rs), 'ratio': round(tt / jt, 3)}
    for st, rs in sorted(rows.items()):
        summary['by_state'][st] = agg(rs)
    if total:
        summary['all'] = agg(total)
    return summary


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = ap.add_subparsers(dest='cmd', required=True)
    e = sub.add_parser('encode')
    e.add_argument('path')
    e.add_argument('--facts', action='store_true')
    e.add_argument('--index', type=int, default=0, help='record index inside a .jsonl file')
    m = sub.add_parser('measure')
    m.add_argument('paths', nargs='+')
    m.add_argument('--facts', action='store_true')
    m.add_argument('--json', action='store_true')
    a = ap.parse_args(argv)
    if a.cmd == 'encode':
        obs = list(load_observations(a.path))[a.index]
        sys.stdout.write(encode(obs, facts=a.facts))
        return 0
    s = measure(a.paths, facts=a.facts)
    if a.json:
        print(json.dumps(s, indent=2))
        return 0
    print(f'tokenizer {s["tokenizer"]}, {s["decisions"]} observations')
    print(f'{"state":<12} {"n":>5} {"json tok":>9} {"text tok":>9} {"median":>7} {"max":>6} {"ratio":>6}')
    for st, r in [*s['by_state'].items(), ('ALL', s.get('all'))]:
        if r:
            print(f'{st:<12} {r["n"]:>5} {r["json_tokens_mean"]:>9} {r["text_tokens_mean"]:>9} '
                  f'{r["text_tokens_median"]:>7} {r["text_tokens_max"]:>6} {r["ratio"]:>6}')
    return 0


if __name__ == '__main__':
    sys.exit(main())
