"""Compact observation mode (--obs-mode compact, protocol v0.3 draft).

The runner renders its own text from the observation JSON (sim/PLAY.md) instead of sending the server's
`text`. What rarely changes - character, ascension, act, relics, potions and the deck with card rules
text (GET /games/{id}/piles) - is the "run context", sent after the static system prompt as cached
system blocks; each is re-sent byte-identical while unchanged (a cache read) and rebuilt only when its
content changes. The run context is split into three blocks ordered by volatility - (character,
ascension, relics), (deck), (act, potions) - so a used potion re-writes only the small last block and
a new card only the deck and potion blocks (static + 3 = the API's 4 cache breakpoints). Everything else
is the per-step block of the user message.

Information parity: every id, number and text of the server `text` appears in context + step text
(`parity_problems` checks it; tests/test_compact.py and `sts2eval parity` run it). Dropped only:
the game id in the header; legal-action descs that repeat what the step text already shows (card,
target, potion, room, entry); card names and rules text in the hand and in prompt options when the
same card's deck line in the run context carries them; repeated power texts within one step.
"""

from __future__ import annotations

import hashlib
import re

from .prompt import SYSTEM_PROMPT

RENDER_VERSION = "compact-v2"  # v2: UI-parity additions (docs/parity-audit-2026-11-02.md)

_READING_FULL = SYSTEM_PROMPT[SYSTEM_PROMPT.index("## Reading the observation"):SYSTEM_PROMPT.index("## Basic strategy")]

_READING_COMPACT = """\
## Reading the observation
The system blocks after this one are the run context, always current: character, ascension,
relics (counters are in the message), the deck (one line per distinct card: count, id, name, cost
"e", type, keywords, enchantment, rules text), the act's boss, act and potion slots. The user
message is the step: header (phase, act, floor, turn, HP / max HP, block, energy, gold), incoming
damage, powers, relic counters, orbs (passive/evoke numbers), enemies with HP, block, intents and
powers, allies, the draw pile (sorted: its order is hidden), discard and exhaust piles (oldest
first), the hand (index, id, cost, type, target, numbers, keywords; name and rules text only when
they differ from the card's deck line), the screen, the legal actions (tokens of one kind share a
line, separated by " | ") and recent history. A power's rules text is printed once, under its
first owner.
Intent "Attack 12" = 12 damage this enemy turn; "Attack 5x3" = three hits of 5. The shown damage
already includes the enemy's modifiers; your block absorbs it first.

"""

COMPACT_SYSTEM_PROMPT = SYSTEM_PROMPT.replace(_READING_FULL, _READING_COMPACT)
assert COMPACT_SYSTEM_PROMPT != SYSTEM_PROMPT

# Legal-action kinds whose desc only repeats the step text (hand card / enemy, potion slot, map option,
# reward, card reward, shop entry, event option, prompt option): listed as bare tokens, grouped.
GROUPED_KINDS = ("play", "potion", "discard", "pick", "travel", "reward", "card", "buy", "event")
# Kinds grouped one line per first argument (one line per hand card / potion slot).
PER_ARG_KINDS = ("play", "potion")


def compact_prompt_hash() -> str:
    """prompt_hash of a compact run: static system prompt plus the renderer version."""
    blob = COMPACT_SYSTEM_PROMPT + "\x00obs_mode=compact\x00" + RENDER_VERSION
    return hashlib.sha256(blob.encode("utf-8")).hexdigest()[:16]


def sha16(text: str) -> str:
    return hashlib.sha256(text.encode("utf-8")).hexdigest()[:16]


# ---------------------------------------------------------------------------------------------------
# small helpers mirroring sim/Sts2Sim.Server/ObservationBuilder*.cs

def _collapse(s: str) -> str:
    return " ".join(s.split())


def _s(node, key: str = "name") -> str | None:
    v = node.get(key) if isinstance(node, dict) else None
    return v if isinstance(v, str) and v else None


def _name(node, key: str = "name") -> str:
    n = _s(node, key)
    return f" ({_collapse(n)})" if n else ""


def _text(node, key: str = "text") -> str:
    t = _s(node, key)
    return f" — {_collapse(t)}" if t else ""


def _num(v) -> str:
    if isinstance(v, bool):
        return "true" if v else "false"
    return str(v)


def card_display_name(node: dict) -> str:
    return node.get("id", "?") + ("+" if node.get("upgraded") else "")


def var_summary(vars_: dict) -> str:
    parts = []
    for k, v in (vars_ or {}).items():
        if k == "damage":
            parts.append(f"{v} dmg")
        elif k == "block":
            parts.append(f"{v} block")
        else:
            parts.append(f"{k}={v}")
    return ", ".join(parts)


def _cost_text(cost) -> str:
    return "Xe" if isinstance(cost, str) else f"{cost}e"


def _effective_text(node: dict) -> str:
    eff = node.get("effective")
    if not isinstance(eff, dict):
        return ""
    vars_ = node.get("vars") or {}
    parts = []
    dmg = eff.get("damage")
    if isinstance(dmg, dict) and dmg:
        base = vars_.get("damage", vars_.get("osty_damage"))
        values = list(dmg.items())
        times = eff.get("times") if eff.get("times") is not None else 1
        differs = base is None or times > 1 or any(v != base for _, v in values)
        if differs:
            hits = f" x{times}" if times > 1 else ""
            if len({v for _, v in values}) == 1:
                parts.append(f"{values[0][1]} dmg{hits}")
            else:
                parts.append(", ".join(f"{v} vs {k}" for k, v in values) + hits)
    if eff.get("block") is not None:
        if vars_.get("block") != eff["block"]:
            parts.append(f"{eff['block']} block")
    return " (eff " + "; ".join(parts) + ")" if parts else ""


def _card_tags(node: dict) -> str:
    out = ""
    aff = node.get("affliction")
    if isinstance(aff, dict):
        out += " [" + aff["id"] + (f" {aff['amount']}" if aff.get("amount", 0) > 1 else "") + "]"
    for e in node.get("enchantments") or []:
        out += f" [ench {e['id']} {_num(e['amount'])}]"
    if node.get("dampened"):
        out += " [dampened]"
    return out


def _powers_summary(powers: list) -> str:
    out = []
    for pw in powers:
        s = f"{pw['id']} {_num(pw['amount'])}"
        d = _s(pw, "detail")
        out.append(s + (f" [{d}]" if d else ""))
    return ", ".join(out)


def _deck_summary(names: list[str]) -> str:
    counts: dict[str, int] = {}
    for n in names:
        counts[n] = counts.get(n, 0) + 1
    return ", ".join(f"{n} x{c}" if c > 1 else n for n, c in counts.items())


_MAP_LETTER = {"Monster": "M", "Elite": "E", "RestSite": "R", "Shop": "$", "Treasure": "T", "Unknown": "?",
               "Boss": "B", "Ancient": "A"}
MAP_LEGEND = ("-- act map (r<row>: <col><type>[! quest][*current|x visited]><next-row cols>; M monster E elite R rest "
              "$ shop T treasure ? unknown A ancient B boss)")


def _map_grid(nodes: list) -> list[str]:
    if not nodes:
        return []
    rows: dict[int, list] = {}
    current_row = -1
    for n in nodes:
        if n is None:
            continue
        rows.setdefault(n["row"], []).append(n)
        if n.get("current"):
            current_row = n["row"]
    keys = sorted(rows)
    max_rows = 19
    if len(keys) > max_rows:
        at = max(0, keys.index(current_row) if current_row in keys else -1)
        first = min(max(at - 1, 0), len(keys) - max_rows)
        keys = keys[first:first + max_rows]
    lines = [MAP_LEGEND]
    for row in keys:
        s = f" r{row}:"
        for n in rows[row]:
            s += f" {n['col']}{_MAP_LETTER.get(n.get('type'), '_')}"
            if n.get("quest"):
                s += "!"
            if n.get("current"):
                s += "*"
            elif n.get("visited"):
                s += "x"
            if n.get("edges"):
                s += ">" + ",".join(str(c) for c in n["edges"])
        lines.append(s)
    return lines


def _relic_label(r: dict) -> str:
    return r["id"] + (f"({r['counter']})" if r.get("counter") is not None else "")


def boss_line(obs: dict) -> str | None:
    """The act's boss(es) and Ancient, as the map's boss / ancient points and the top-bar icon show them
    (server BossLine)."""
    bosses = obs.get("bosses") or []
    if not bosses:
        return None
    anc = obs.get("ancient")
    return ("boss: " + ", then ".join(b["id"] + _name(b) for b in bosses)
            + (f" | ancient: {anc['id']}{_name(anc)}" if isinstance(anc, dict) else ""))


def orbs_line(orbs: list | None, slots) -> str:
    """Server OrbsLine: "orbs (3 slots; passive/evoke): LIGHTNING_ORB 3/8" in combat, ids only outside."""
    orbs = orbs or []
    values = bool(orbs) and all(o.get("passive") is not None and o.get("evoke") is not None for o in orbs)
    s = f"orbs ({slots} slots" + ("; passive/evoke): " if values else "): ")
    if not orbs:
        return s + "-"
    return s + ", ".join(f"{o['id']} {o['passive']}/{o['evoke']}" if values else o["id"] for o in orbs)


_PILE_TITLES = (("draw", "draw pile (sorted): "), ("discard", "discard pile (oldest first): "),
                ("exhaust", "exhaust pile (oldest first): "))


def pile_lines(view: dict | None) -> list[str]:
    """Server AppendPiles: one line per non-empty combat pile from combat.pile_view."""
    out = []
    for key, title in _PILE_TITLES:
        groups = (view or {}).get(key) or []
        if groups:
            out.append(title + ", ".join(g["card"] + (f" x{g['n']}" if g["n"] > 1 else "") for g in groups))
    return out


# ---------------------------------------------------------------------------------------------------
# run context

def deck_lines(names: list[str], nodes: list[dict] | None) -> tuple[list[str], dict[str, set]]:
    """One line per distinct card of the master deck (first-appearance order) and the rules texts per
    display name (for the hand / prompt dedupe). `names` = player.deck (with "+"/"+N"), `nodes` = the
    /piles deck card nodes in the same order (None: ids only)."""
    texts: dict[str, set] = {}
    if not nodes or len(nodes) != len(names):
        return [f" {n}" for n in _deck_summary(names).split(", ")] if names else [], texts
    groups: dict[str, list] = {}
    order: list[str] = []
    for name, node in zip(names, nodes):
        detail = _card_detail(node)
        line_key = name + "\x00" + detail
        if line_key not in groups:
            groups[line_key] = [name, detail, 0]
            order.append(line_key)
        groups[line_key][2] += 1
        if _s(node, "text"):
            texts.setdefault(name, set()).add(_collapse(node["text"]))
    lines = []
    for k in order:
        name, detail, count = groups[k]
        lines.append(f" {name}" + (f" x{count}" if count > 1 else "") + detail)
    return lines, texts


def _card_detail(node: dict) -> str:
    """Deck line after the name: title, cost, type, tags, keywords, rules text (the numbers are in the
    text; the hand line repeats target and vars when the card is drawn)."""
    s = _name(node)
    s += f" {_cost_text(node.get('cost'))}"
    if node.get("star_cost") is not None:
        s += f" {node['star_cost']}*"
    s += f" {node.get('type')}"
    s += _card_tags(node)
    kws = node.get("keywords") or []
    if kws:
        s += " {" + ",".join(kws) + "}"
    return s + _text(node)


def render_context(obs: dict, ascension, deck_nodes: list[dict] | None) -> tuple[tuple[str, str, str], dict[str, set]]:
    """The run context as three blocks ordered by how often they change (each its own cached system
    block): (character, ascension, relics), (deck), (act, potions)."""
    p = obs.get("player") or {}
    head = [f"== Run context: {p.get('character')} | ascension {ascension}"]
    relics = p.get("relics") or []
    head.append("relics:" if relics else "relics: -")
    for r in relics:
        head.append(f" {r['id']}{_name(r)}{_text(r)}")
    names = list(p.get("deck") or [])
    dl, texts = deck_lines(names, deck_nodes)
    deck = [f"deck({p.get('deck_size', len(names))}):"] + dl
    pots = p.get("potions") or []
    bl = boss_line(obs)
    tail = ([bl] if bl else []) + [f"act {obs.get('act')} | potions ({len(pots)} slots):"]
    for i, pot in enumerate(pots):
        tail.append(f" [{i}] " + (pot["id"] + _name(pot) + _text(pot) if pot else "-"))
    return tuple("\n".join(b) + "\n" for b in (head, deck, tail)), texts


# ---------------------------------------------------------------------------------------------------
# per-step block

def _header(obs: dict) -> str:
    p = obs.get("player") or {}
    s = f"== {obs.get('phase')}"
    if obs.get("act") is not None:
        s += f" | act {obs['act']} floor {obs.get('floor')}"
    combat = obs.get("combat")
    if combat is not None and obs.get("turn") is not None:
        s += f" | turn {obs['turn']}"
    s += f" | HP {p.get('hp')}/{p.get('max_hp')}"
    if combat is not None:
        s += f" block {p.get('block')} energy {p.get('energy')}/{_num(p.get('max_energy'))}"
    if "stars" in p:
        s += f" stars {p['stars']}"
    s += f" gold {p.get('gold')}"
    if obs.get("terminal"):
        s += f" | RESULT {obs.get('result')}"
    return s


def _danger(danger: dict | None, max_hp) -> list[str]:
    if not danger:
        return []
    inc, block = danger["incoming"], danger["block"]
    if inc == 0:
        s = f"incoming: no attack, block {block}"
    else:
        hits = danger["hits"]
        parts = ", ".join(f"{e['uid']} {e['damage']}x{e['times']}" for e in danger["by_enemy"])
        s = (f"incoming: {inc} dmg in {hits} {'hit' if hits == 1 else 'hits'} ({parts}), block {block}, "
             f"net -{danger['net_hp_loss']} HP -> {danger['hp_after']}/{max_hp}")
    return [s] + [f"! {t['text']}" for t in danger.get("timers") or []]


class _PowerTexts:
    """Power rules text lines, each (id, text) once per step."""

    def __init__(self):
        self.seen: set = set()

    def lines(self, powers, indent: str) -> list[str]:
        out = []
        for pw in powers or []:
            t = _s(pw, "text")
            if t:
                key = (pw["id"], _collapse(t))
                if key in self.seen:
                    continue
                self.seen.add(key)
                out.append(f"{indent}{pw['id']} — {_collapse(t)}")
        return out


def _intent_text(it: dict) -> str:
    if it.get("damage") is not None:
        times = it.get("times") or 1
        return f"{it['type']} {it['damage']}x{times}" if times > 1 else f"{it['type']} {it['damage']}"
    if it.get("card_count") is not None:
        return f"{it['type']}({_num(it['card_count'])} cards)"
    return it["type"]


def _hand_line(c: dict, deck_texts: dict[str, set]) -> str:
    name = card_display_name(c)
    t = _s(c, "text")
    dedupe = t is not None and _collapse(t) in deck_texts.get(name, ())
    s = f" [{c['index']}] {name}" + ("" if dedupe else _name(c))
    s += f" {_cost_text(c.get('cost'))}"
    if c.get("base_cost") is not None:
        s += f" (base {_num(c['base_cost'])}e)"
    if c.get("star_cost") is not None:
        s += f" {c['star_cost']}*"
    s += f" {c.get('type')}->{c.get('target')}"
    vs = var_summary(c.get("vars"))
    if vs:
        s += f" ({vs})"
    s += _effective_text(c) + _card_tags(c)
    kws = c.get("keywords") or []
    if kws:
        s += " {" + ",".join(kws) + "}"
    if c.get("playable") is False:
        s += f" UNPLAYABLE:{c.get('reason')}"
    return s + ("" if dedupe else _text(c))


def _screen(obs: dict, deck_texts: dict[str, set]) -> list[str]:
    out: list[str] = []
    pr = obs.get("prompt")
    if isinstance(pr, dict):
        s = f"-- prompt {pr.get('kind')} at {pr.get('site')}"
        src = _s(pr, "source")
        if src and src != _s(pr, "site"):
            s += f" from {src}"
        s += _name(pr, "source_name")
        s += f": pick {_num(pr.get('min'))}..{_num(pr.get('max'))}, picked [" + ",".join(
            _num(x) for x in pr.get("picked") or []) + "]"
        out.append(s)
        if _s(pr, "desc"):
            out.append(f"   {pr['desc']}")
        if _s(pr, "source_text"):
            out.append(f"   source: {_collapse(pr['source_text'])}")
        for o in pr.get("options") or []:
            name = o.get("id", "?") + ("+" if o.get("upgraded") else "")
            t = _s(o, "text")
            dedupe = t is not None and _collapse(t) in deck_texts.get(name, ())
            out.append(f" ({o['index']}) {name}" + ("" if dedupe else _name(o) + _text(o)))
            if _s(o, "upgrade_text"):
                out.append(f"     upgraded: {_collapse(o['upgrade_text'])}")
    mp = obs.get("map")
    if isinstance(mp, dict):
        out.append("-- map: next rooms")
        for o in mp.get("options") or []:
            out.append(f" ({o['index']}) {o['type']} col {o['col']} row {o['row']} -> " + "/".join(o.get("leads_to") or []))
        out += _map_grid(mp.get("nodes") or [])
    rw = obs.get("rewards")
    if isinstance(rw, list):
        out.append("-- rewards")
        for r in rw:
            out.append(f" ({r['index']}) {r['type']}: {r.get('detail')}")
    cr = obs.get("card_reward")
    if isinstance(cr, dict):
        note = ""
        if cr.get("can_skip"):
            note = (" or skip (skipping completes the reward)" if cr.get("skip_consumes")
                    else " or skip (skip keeps this reward on the rewards screen until you proceed)")
        out.append(f"-- card reward (pick one{note})")
        for c in cr.get("cards") or []:
            s = f" ({c['index']}) {card_display_name(c)}{_name(c)} {c.get('type')} {c.get('rarity')} cost {_num(c.get('cost'))}"
            vars_ = c.get("vars") or {}
            if vars_:
                s += " (" + ", ".join(f"{k}={_num(v)}" for k, v in vars_.items()) + ")"
            s += _card_tags(c)
            kws = c.get("keywords") or []
            if kws:
                s += " {" + ",".join(kws) + "}"
            out.append(s + _text(c))
    sh = obs.get("shop")
    if isinstance(sh, dict):
        rp = sh.get("removal_price")
        out.append(f"-- shop (card removal: {str(rp) + 'g' if rp is not None else 'n/a'})")
        for e in sh.get("entries") or []:
            s = f" ({e['index']}) {e.get('category')} {e.get('id') or '-'}{_name(e)} {e.get('price')}g"
            if e.get("on_sale"):
                s += " (sale" + (f", base ~{e['base_price']}g" if e.get("base_price") is not None else "") + ")"
            if e.get("sold"):
                s += " SOLD"
            out.append(s + _text(e))
    rest = obs.get("rest")
    if isinstance(rest, dict):
        out.append("-- rest site")
        for o in rest.get("options") or []:
            out.append(f" ({o['index']}) {o['id']}" + ("" if o.get("enabled") else " (disabled)") + _text(o))
    t = obs.get("treasure")
    if isinstance(t, dict):
        s = "-- treasure: chest " + ("open" if t.get("chest_opened") else "closed")
        if t.get("relic") is not None:
            s += f", relic {t['relic']}" + _name(t, "relic_name") + _text(t, "relic_text")
        out.append(s)
    ev = obs.get("event")
    if isinstance(ev, dict):
        out.append(f"-- event {ev.get('id')}{_name(ev)} page {ev.get('page') or '-'}"
                   + (" (finished)" if ev.get("finished") else ""))
        if _s(ev, "page_text"):
            out.append(f"   {_collapse(ev['page_text'])}")
        for o in ev.get("options") or []:
            s = f" ({o['index']}) {o.get('key')}" + ("" if o.get("enabled") else " (disabled)")
            if o.get("relic") is not None:
                s += f" relic {o['relic']}"
            out.append(s + _text(o))
    return out


def legal_lines(legal_actions: list[dict]) -> list[str]:
    """Tokens of the grouped kinds share a line (one line per hand card / potion slot for play / potion);
    every other token keeps its desc on its own line, as the server prints it."""
    groups: dict[str, list[str]] = {}
    lines: list = []
    for a in legal_actions or []:
        tok = a.get("token")
        if not tok:
            continue
        parts = tok.split()
        kind = parts[0]
        if kind in GROUPED_KINDS:
            key = kind + (" " + parts[1] if kind in PER_ARG_KINDS and len(parts) > 1 else "")
            if key not in groups:
                groups[key] = []
                lines.append(groups[key])
            groups[key].append(tok)
        else:
            lines.append(f" {tok}  : {a.get('desc')}")
    return [" " + " | ".join(x) if isinstance(x, list) else x for x in lines]


def render_step(obs: dict, deck_texts: dict[str, set]) -> str:
    p = obs.get("player") or {}
    combat = obs.get("combat")
    pt = _PowerTexts()
    lines = [_header(obs)]
    if combat is not None:
        lines += _danger(combat.get("danger"), p.get("max_hp"))
        if p.get("powers"):
            lines.append("powers: " + _powers_summary(p["powers"]))
            lines += pt.lines(p["powers"], "  ")
    counters = [_relic_label(r) for r in p.get("relics") or [] if r.get("counter") is not None]
    if counters:
        lines.append("relic counters: " + ", ".join(counters))
    if p.get("orb_slots"):
        lines.append(orbs_line(p.get("orbs"), p["orb_slots"]))
    if combat is not None:
        facing = p.get("facing")
        lines.append("-- enemies" + (f" (you face {facing})" if facing else ""))
        for e in combat.get("enemies") or []:
            s = f" [{e.get('index')}] {e.get('uid')} {e.get('id')}{_name(e)} {e.get('hp')}/{e.get('max_hp')}"
            if e.get("block"):
                s += f" block {e['block']}"
            if _s(e, "flank"):
                s += f" | {e['flank']} side" + (" (BEHIND you: x1.5)" if e.get("behind") else "")
            s += " | intent: "
            intents = e.get("intents") or []
            if intents and _s(intents[0], "move"):
                s += "[" + intents[0]["move"] + (f" ({intents[0]['move_name']})" if _s(intents[0], "move_name") else "") + "] "
            s += " + ".join(_intent_text(i) for i in intents) if intents else "-"
            if e.get("powers"):
                s += " | " + _powers_summary(e["powers"])
            lines.append(s)
            lines += pt.lines(e.get("powers"), "    ")
        for a in combat.get("allies") or []:
            s = f" ally [{a.get('index')}] {a.get('uid')} {a.get('id')}{_name(a)} {a.get('hp')}/{a.get('max_hp')}"
            if a.get("block"):
                s += f" block {a['block']}"
            if a.get("powers"):
                s += " | " + _powers_summary(a["powers"])
            lines.append(s)
            lines += pt.lines(a.get("powers"), "    ")
        lines += pile_lines(combat.get("pile_view"))
        lines.append(f"-- hand (draw {combat.get('draw_count')}, discard {combat.get('discard_count')}, "
                     f"exhaust {combat.get('exhaust_count')})")
        lines += [_hand_line(c, deck_texts) for c in combat.get("hand") or []]
    lines += _screen(obs, deck_texts)
    lines.append("-- legal actions")
    lines += legal_lines(obs.get("legal_actions"))
    return "\n".join(lines) + "\n"


# ---------------------------------------------------------------------------------------------------
# per-game state

def deck_key(obs: dict) -> tuple:
    return tuple((obs.get("player") or {}).get("deck") or ())


class CompactView:
    """Per-game renderer state: the last /piles deck and the current run-context blocks."""

    def __init__(self, ascension):
        self.ascension = ascension
        self.deck_nodes: list[dict] | None = None
        self.deck_key: tuple | None = None
        self.context: tuple[str, ...] | None = None
        self.deck_texts: dict[str, set] = {}
        self.rebuilds = 0                      # steps on which some block changed
        self.block_rebuilds = [0, 0, 0]        # per block: changed (the block itself, not only a later one)
        self.piles_fetches = 0

    def needs_piles(self, obs: dict) -> bool:
        """Fetch /piles when the deck list changed, and on every out-of-combat step (enchantments and
        var changes keep the id list)."""
        return self.deck_nodes is None or deck_key(obs) != self.deck_key or obs.get("combat") is None

    def set_piles(self, obs: dict, piles: dict | None) -> None:
        self.piles_fetches += 1
        self.deck_nodes = (piles or {}).get("deck")
        self.deck_key = deck_key(obs)

    def render(self, obs: dict) -> tuple[tuple[str, ...], str, int | None]:
        """(context blocks, step_text, index of the first changed block or None). Unchanged blocks are
        returned as the very same strings as on the previous step."""
        ctx, texts = render_context(obs, self.ascension, self.deck_nodes if deck_key(obs) == self.deck_key else None)
        first = None
        if self.context is None:
            first = 0
            self.block_rebuilds = [b + 1 for b in self.block_rebuilds]
        else:
            for i, (old, new) in enumerate(zip(self.context, ctx)):
                if old != new:
                    self.block_rebuilds[i] += 1
                    first = i if first is None else first
            ctx = tuple(old if old == new else new for old, new in zip(self.context, ctx))
        if first is not None:
            self.rebuilds += 1
        self.context = ctx
        self.deck_texts = texts
        return self.context, render_step(obs, texts), first


# ---------------------------------------------------------------------------------------------------
# information parity

_ID = re.compile(r"\b[A-Z][A-Z0-9_]*[A-Z0-9]\b")
_UID = re.compile(r"\bu\d+\b")
_NUM = re.compile(r"\d+")
_WORD = re.compile(r"[A-Za-z]+")


def _split_server(text: str) -> tuple[list[str], list[str]]:
    lines = text.rstrip("\n").split("\n")
    if "-- legal actions" in lines:
        i = lines.index("-- legal actions")
        return lines[:i], lines[i + 1:]
    return lines, []


def parity_problems(server_text: str, context: str, step: str, obs: dict) -> list[str]:
    """What of the server text is missing from context + step (empty list = parity holds).

    Checks: (1) every id, uid, number and word of the server text (legal-action descs: ids, uids and
    numbers only) occurs in the compact text; (2) every rules text (" — ..."), parenthesised group and
    card / relic / potion name of the server text occurs verbatim; (3) every server line outside the
    header, relic / potion / deck block, hand, prompt options and legal actions occurs verbatim as a line;
    hand and prompt option lines match except for a name and text found on the card's deck line;
    (4) every legal token is listed."""
    problems: list[str] = []
    if not isinstance(context, str):
        context = "".join(context)
    body, legal = _split_server(server_text)
    body = [re.sub(r"^== \S+ ", "== ", body[0])] + body[1:] if body else body
    compact = context + step
    clines = set(compact.split("\n"))
    ctoks_id = set(_ID.findall(compact)) | set(_UID.findall(compact))
    ctoks_num = set(_NUM.findall(compact))
    cwords = set(_WORD.findall(compact))
    body_text = "\n".join(body)
    legal_text = "\n".join(legal)
    for tok in (set(_ID.findall(body_text + legal_text)) | set(_UID.findall(body_text + legal_text))) - ctoks_id:
        problems.append(f"id {tok}")
    for n in set(_NUM.findall(body_text + legal_text)) - ctoks_num:
        problems.append(f"number {n}")
    for w in set(_WORD.findall(body_text)) - cwords:
        problems.append(f"word {w}")
    for line in body:
        if " — " in line:
            t = line.split(" — ", 1)[1]
            if t not in compact:
                problems.append(f"text {t[:60]}")
        for g in re.findall(r"\(([^()]*)\)", line):
            if f"({g})" not in compact:
                problems.append(f"group ({g[:60]})")
    # (3) line structure
    section = "head"
    hand = {c["index"]: c for c in ((obs.get("combat") or {}).get("hand") or [])}
    prompt_opts = {o["index"]: o for o in ((obs.get("prompt") or {}).get("options") or [])}
    for i, line in enumerate(body):
        if i == 0:
            continue
        if line.startswith("relics") or line.startswith("potions") or line.startswith("deck("):
            section = "player"
            continue
        if line.startswith("-- ") or line.startswith("incoming") or line.startswith("powers") or line.startswith("orbs") \
                or line.startswith("! "):
            section = "hand" if line.startswith("-- hand") else "prompt" if line.startswith("-- prompt") else "other"
            if line not in clines:
                problems.append(f"line {line[:80]}")
            continue
        if section == "player":
            continue
        if re.match(r"^\s+[A-Z][A-Z0-9_]* — ", line):
            # a power text line: printed once per step, under the first owner
            if not any(cl.strip() == line.strip() for cl in clines):
                problems.append(f"power text {line.strip()[:80]}")
            continue
        m = re.match(r"^ \[(\d+)\] ", line) if section == "hand" else re.match(r"^ \((\d+)\) ", line) if section == "prompt" else None
        if m and ((section == "hand" and int(m.group(1)) in hand) or (section == "prompt" and int(m.group(1)) in prompt_opts)):
            node = hand[int(m.group(1))] if section == "hand" else prompt_opts[int(m.group(1))]
            if line in clines:
                continue
            stripped = line.replace(f" ({_collapse(node['name'])})", "", 1) if _s(node) else line
            stripped = stripped.split(" — ", 1)[0]
            if stripped not in clines:
                problems.append(f"card line {line[:80]}")
            elif _s(node, "text") and f"— {_collapse(node['text'])}" not in context:
                problems.append(f"card text not in context {line[:80]}")
            continue
        if line not in clines:
            problems.append(f"line {line[:80]}")
    # player block: relic / potion / deck names in context
    p = obs.get("player") or {}
    for r in p.get("relics") or []:
        if r["id"] not in context:
            problems.append(f"relic {r['id']}")
        if r.get("counter") is not None and _relic_label(r) not in step:
            problems.append(f"relic counter {_relic_label(r)}")
    for pot in p.get("potions") or []:
        if pot and pot["id"] not in context:
            problems.append(f"potion {pot['id']}")
    for name, count in _count(p.get("deck") or []).items():
        if not re.search(rf"^ {re.escape(name)}{' x' + str(count) if count > 1 else ''}( |$)", context, re.M) \
                and not _deck_count_split(context, name, count):
            problems.append(f"deck {name} x{count}")
    # (4) every legal token listed
    legal_block = step.split("-- legal actions\n", 1)[-1]
    listed = set()
    for ln in legal_block.split("\n"):
        ln = ln.strip()
        if not ln:
            continue
        if "  : " in ln:
            listed.add(ln.split("  : ", 1)[0])
        else:
            listed.update(x.strip() for x in ln.split(" | "))
    for a in obs.get("legal_actions") or []:
        if a.get("token") and a["token"] not in listed:
            problems.append(f"legal {a['token']}")
    return problems


def _count(names):
    out: dict[str, int] = {}
    for n in names:
        out[n] = out.get(n, 0) + 1
    return out


def _deck_count_split(context: str, name: str, count: int) -> bool:
    """A name split over several deck lines (same card, different enchantment / text): counts add up."""
    total = 0
    for m in re.finditer(rf"^ {re.escape(name)}(?: x(\d+))?(?: |$)", context, re.M):
        total += int(m.group(1) or 1)
    return total == count
