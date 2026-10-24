#!/usr/bin/env python3
"""Look up structured STS2 facts for a live harness state (prompt-injection helper).

Usage (library):
    from scripts.facts.lookup import FactDB
    db = FactDB.load("0.107.1")
    text = db.facts_for_state(state)          # compact text block for the LLM prompt
    data = db.facts_for_state(state, as_text=False)  # structured dict

CLI:
    python3 scripts/facts/lookup.py --state path/to/state.json [--version 0.107.1] [--json]
    python3 scripts/facts/lookup.py --id QUEEN --id BASH

The state shape is the STS2MCP / hack-sts2 live state (state_type, battle.enemies[].id,
player.hand[].id, player.relics[].id, player.potions[].id, event.event_id, card_reward.cards[].id,
shop.items[].card_id/relic_id/potion_id, rewards.items[]...). Unknown ids are skipped silently.

Integration point (intentionally NOT wired, sts2_live.py is being edited elsewhere):
scripts/sts2_live.py `snapshot(out, state, knowledge_root, ...)` builds the dict returned to the
controller (`result = public_display(state)` ... `result['decision_context'] = ...`). Add, next to
`decision_context`:
    from scripts.facts.lookup import FactDB   # or sys.path-insert scripts/facts
    result['wiki_facts'] = FactDB.load(version).facts_for_state(state, max_chars=4000)
(`version` = the session's game version, e.g. meta['version']). The facts are public priors
(card/relic/potion text, enemy HP/move tables, event options), so they don't leak hidden state.
"""
from __future__ import annotations

import argparse
import json
import sys
from functools import lru_cache
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
FACTS_ROOT = ROOT / "knowledge" / "facts"


def _val(v) -> str:
    if isinstance(v, dict):
        if "base" not in v:
            return "?"
        s = str(v["base"])
        if "asc" in v and v["asc"] != v["base"]:
            s += f" (A{v.get('asc_level', '?')}+: {v['asc']})"
        return s
    return str(v)


def _hp(hp: dict) -> str:
    if not hp or hp.get("min") is None:
        return "HP ?"
    s = f"HP {hp['min']}" + (f"-{hp['max']}" if hp.get("max") not in (None, hp["min"]) else "")
    if hp.get("asc_min") is not None:
        s += f" (A{hp.get('asc_level', 8)}+: {hp['asc_min']}" + (f"-{hp['asc_max']}" if hp.get("asc_max") not in (None, hp["asc_min"]) else "") + ")"
    if hp.get("forms"):
        s += "; forms " + ", ".join(f"{k} {_val(v)}" for k, v in hp["forms"].items())
    return s


class FactDB:
    def __init__(self, data: dict[str, dict], version: str):
        self.version = version
        self.data = data
        self.index: dict[str, dict[str, dict]] = {}
        for cat in ("cards", "relics", "potions", "events"):
            self.index[cat] = {e["id"]: e for e in data[cat]["entries"]}
        self.index["enemies"] = {e["id"]: e for e in data["enemies"]["entries"]}
        self.index["encounters"] = {e["id"]: e for e in data["enemies"].get("encounters", [])}
        self.index["bosses"] = {e["id"]: e for e in data["bosses"]["entries"]}
        kw = data["keywords"]
        self.index["powers"] = {e["id"]: e for e in kw.get("powers", [])}
        self.index["card_keywords"] = {e["id"]: e for e in kw.get("card_keywords", [])}

    @classmethod
    @lru_cache(maxsize=4)
    def load(cls, version: str = "0.107.1") -> "FactDB":
        d = FACTS_ROOT / f"v{version.lstrip('v')}"
        names = ("cards", "relics", "potions", "enemies", "bosses", "events", "keywords")
        return cls({n: json.loads((d / f"{n}.json").read_text()) for n in names}, version)

    # ---- per-entity compact lines -------------------------------------------------
    def card_line(self, cid: str, upgraded: bool = False) -> str | None:
        c = self.index["cards"].get(_strip_model(cid))
        if not c:
            return None
        cost = c.get("upgraded_cost") if upgraded else c.get("cost")
        desc = c.get("upgraded_description") if upgraded else c.get("description")
        kws = c.get("upgraded_keywords") if upgraded else c.get("keywords")
        s = f"[card] {c['name']}{'+' if upgraded else ''} ({c['id']}): {cost} cost {c.get('rarity')} {c.get('type')}"
        if kws:
            s += f" [{', '.join(kws)}]"
        s += f" - {desc}"
        if not upgraded and c.get("upgradable"):
            ups = []
            if c.get("upgraded_cost") != cost:
                ups.append(f"cost {c.get('upgraded_cost')}")
            if c.get("upgraded_description") != desc:
                ups.append(str(c.get("upgraded_description")))
            if ups:
                s += " | upgraded: " + "; ".join(ups)
        return s

    def relic_line(self, rid: str) -> str | None:
        r = self.index["relics"].get(_strip_model(rid))
        return f"[relic] {r['name']} ({r['id']}, {r.get('rarity')}): {r.get('description')}" if r else None

    def potion_line(self, pid: str) -> str | None:
        p = self.index["potions"].get(_strip_model(pid))
        return f"[potion] {p['name']} ({p['id']}, {p.get('usage')}, target {p.get('target')}): {p.get('description')}" if p else None

    def power_line(self, pid: str) -> str | None:
        p = self.index["powers"].get(_strip_model(pid))
        return f"[power] {p['name']} ({p.get('type')}, {p.get('stack_type')}): {p.get('description')}" if p else None

    def enemy_lines(self, mid: str) -> list[str]:
        e = self.index["enemies"].get(_strip_model(mid))
        if not e:
            return []
        out = [f"[enemy] {e['name']} ({e['id']}): {_hp(e.get('hp', {}))}"
               + (f"; starts with {', '.join(p['power'] + ' ' + _val(p['amount']) for p in e['starting_powers'])}" if e.get("starting_powers") else "")]
        for mv in e.get("moves", []):
            nxt = f" -> {'/'.join(mv['next'])}" if mv.get("next") else ""
            out.append(f"  move {mv['id']} '{mv.get('title')}' [{'/'.join(mv.get('intents', []))}]: {mv.get('summary') or '-'}{nxt}")
        if e.get("pattern"):
            out.append(f"  pattern: {e['pattern']}")
        notes = (e.get("wiki_notes") or {}).get("pattern") or []
        if notes:
            out.append("  wiki pattern notes: " + " / ".join(notes[:8]))
        return out

    def event_lines(self, eid: str) -> list[str]:
        e = self.index["events"].get(_strip_model(eid))
        if not e:
            return []
        out = [f"[event] {e['name']} ({e['id']})" + (" [Ancient]" if e.get("is_ancient") else "")
               + (f" vars {json.dumps(e['vars'])}" if e.get("vars") else "")
               + (f"; condition: {e['allowed_condition']}" if e.get("allowed_condition") else "")]
        for o in e.get("options", []):
            out.append(f"  option '{o.get('title')}': {o.get('description') or '-'}")
        for o in (e.get("wiki") or {}).get("options", [])[:8]:
            out.append(f"  wiki option '{o.get('title')}': {o.get('text', '')[:200]}")
        return out

    def lookup_id(self, any_id: str) -> list[str]:
        i = _strip_model(any_id)
        for fn in (self.card_line, self.relic_line, self.potion_line, self.power_line):
            line = fn(i)
            if line:
                return [line]
        return self.enemy_lines(i) or self.event_lines(i)

    # ---- state ----------------------------------------------------------------------
    def facts_for_state(self, state: dict, as_text: bool = True, max_chars: int | None = 6000):
        player = state.get("player") or {}
        battle = state.get("battle") or {}
        sections: dict[str, list[str]] = {"enemies": [], "cards": [], "relics": [], "potions": [], "powers": [], "event": [], "offers": []}
        seen: set[str] = set()

        def add(sec: str, lines):
            if isinstance(lines, str):
                lines = [lines]
            for line in lines or []:
                if line and line not in seen:
                    seen.add(line)
                    sections[sec].append(line)

        for en in battle.get("enemies", []) or []:
            if en.get("is_alive", True):
                add("enemies", self.enemy_lines(en.get("id") or en.get("model_id", "")))
            for st in en.get("status", []) or []:
                add("powers", self.power_line(st.get("id", "")))
        for c in player.get("hand", []) or []:
            add("cards", self.card_line(c.get("id", ""), bool(c.get("is_upgraded"))))
        for r in player.get("relics", []) or []:
            add("relics", self.relic_line(r.get("id", "")))
        for p in player.get("potions", []) or []:
            add("potions", self.potion_line(p.get("id", "")))
        for st in player.get("status", []) or []:
            add("powers", self.power_line(st.get("id", "")))
        ev = state.get("event") or {}
        if ev.get("event_id"):
            add("event", self.event_lines(ev["event_id"]))
        for c in (state.get("card_reward") or {}).get("cards", []) or []:
            add("offers", self.card_line(c.get("id", ""), bool(c.get("is_upgraded"))))
        for container in ("shop", "rewards", "card_select", "relic_select", "treasure"):
            for it in (state.get(container) or {}).get("items", []) or []:
                for k in ("card_id", "relic_id", "potion_id", "id"):
                    if it.get(k):
                        add("offers", self.lookup_id(str(it[k]))[:1])
        if not as_text:
            return {"version": self.version, **{k: v for k, v in sections.items() if v}}
        parts = [f"Known STS2 facts (v{self.version}, game data authoritative):"]
        for k in ("enemies", "event", "cards", "offers", "relics", "potions", "powers"):
            if sections[k]:
                parts.extend(sections[k])
        text = "\n".join(parts)
        if max_chars and len(text) > max_chars:
            text = text[: max_chars - 15].rsplit("\n", 1)[0] + "\n...[truncated]"
        return text


def _strip_model(i: str) -> str:
    """'CARD.STRIKE_IRONCLAD' / 'MONSTER.INKLET' -> bare id; drops '+' upgrade marker."""
    i = (i or "").strip().rstrip("+")
    return i.split(".", 1)[1] if "." in i else i


def main(argv: list[str] | None = None) -> int:
    ap = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    ap.add_argument("--version", default="0.107.1")
    ap.add_argument("--state", help="path to a live state JSON")
    ap.add_argument("--id", action="append", default=[], help="look up an entity id (repeatable)")
    ap.add_argument("--json", action="store_true")
    ap.add_argument("--max-chars", type=int, default=6000)
    a = ap.parse_args(argv)
    db = FactDB.load(a.version)
    if a.state:
        state = json.loads(Path(a.state).read_text())
        out = db.facts_for_state(state, as_text=not a.json, max_chars=a.max_chars)
        print(json.dumps(out, ensure_ascii=False, indent=1) if a.json else out)
    for i in a.id:
        print("\n".join(db.lookup_id(i)) or f"(no facts for {i})")
    return 0


if __name__ == "__main__":
    sys.exit(main())
