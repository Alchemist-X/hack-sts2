#!/usr/bin/env python3
"""Merge game truth + wiki.gg + Spire Codex into knowledge/facts/v<version>/*.json.

Game truth is authoritative. Wiki/Codex values are attached for reference and every
disagreement is recorded in the entry's ``conflicts`` list and in conflicts.json.

Usage: python3 scripts/facts/build_facts.py --version 0.107.1
"""

from __future__ import annotations

import argparse
import re
import sys
from collections import Counter
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

from common import cache_dir, facts_dir, norm_id, read_json, strip_bbcode, strip_wiki, today, wiki_variant, write_json  # noqa: E402

WIKI = "https://slaythespire.wiki.gg/wiki/"
CODEX = "https://spire-codex.com"
SCHEMA_VERSION = 1
NUM_RE = re.compile(r"(?<![A-Za-z0-9.])\d+(?![0-9])")


def wiki_url(link: str) -> str:
    return WIKI + (link or "").replace(" ", "_")


def canon_icons(text: str) -> str:
    """Normalize energy/star icon notation so '2[E]1[E]', '1[E] 1[E]' and '3[E]' compare equal."""
    text = re.sub(r"(\d) \[(E|Star)\]", r"\1[\2]", text or "")
    text = re.sub(r"\b(\d+) 1\[(E|Star)\]", r"\1[\2]", text)
    text = re.sub(r"(?<![\d\]])\[(E|Star)\]", r"1[\1]", text)

    def merge(m: re.Match[str]) -> str:
        icon = m.group(2)
        total = sum(int(n) for n in re.findall(rf"(\d+)\[{icon}\]", m.group(0)))
        return f"{total}[{icon}]"
    return re.sub(r"(\d+)\[(E|Star)\](?:\s*\d+\[\2\])+", merge, text)


def nums(text: str | None) -> list[int]:
    return sorted({int(x) for x in NUM_RE.findall(canon_icons(text or ""))})


def key(name: str | None) -> str:
    return re.sub(r"[^A-Z0-9]", "", norm_id(name or "").upper())


def index_by_name(rows, field="Name") -> dict:
    out = {}
    for r in rows:
        out.setdefault(key(r.get(field)), r)
    return out


class Conflicts:
    def __init__(self) -> None:
        self.items: list[dict] = []

    def add(self, entry: dict, category: str, field: str, game, other, source: str, note: str = "") -> None:
        c = {"field": field, "game": game, source: other, "authoritative": "game"}
        if note:
            c["note"] = note
        entry.setdefault("conflicts", []).append(c)
        self.items.append({"category": category, "id": entry["id"], **c, "source": source})


def norm_rarity(r: str | None) -> str | None:
    if not r:
        return None
    r = r.replace(" Relic", "").strip().lower()
    return {"basic": "basic", "starter": "basic"}.get(r, r)


def norm_type(t):
    return (t or "").strip().lower() or None


def src_game(version: str, ns: str, cls: str) -> dict:
    return {"name": "game", "ref": f"sts2.dll v{version} {ns}.{cls} + SlayTheSpire2.pck eng localization", "authoritative": True}


# ------------------------------------------------------------------------------------------------

def build_cards(gt, wiki, codex, conflicts, version, fetched):
    w_rows = wiki["Cards"]["rows"]
    w_idx = index_by_name(w_rows)
    c_idx = {r["id"]: r for r in codex["cards"]["rows"]}
    out = []
    matched_wiki: set[str] = set()
    for g in gt["cards"]:
        e = {"id": g["id"], "name": g["name"],
             "character": next((p for p in g["pools"] if p in ("Ironclad", "Silent", "Defect", "Regent", "Necrobinder")), None),
             "pools": g["pools"], "cost": g["cost"], "upgraded_cost": g["upgraded_cost"], "type": g["type"],
             "rarity": g["rarity"], "target": g["target"], "keywords": g["keywords"],
             "upgraded_keywords": g["upgraded_keywords"], "description": g["description"],
             "upgraded_description": g["upgraded_description"], "vars": g["vars"], "upgrade_deltas": g["upgrade_deltas"],
             "upgradable": g["upgradable"], "internal_or_undiscovered": g["internal_or_undiscovered"],
             "sources": [src_game(version, "Cards", g["class"])], "conflicts": []}
        if g["unresolved_vars"]:
            e["dynamic_vars"] = g["unresolved_vars"]
        w = w_idx.get(key(f"{g['name']} ({e['character']})")) or w_idx.get(key(g["name"]))
        if g["id"] == "MAD_SCIENCE":  # wiki splits the Tinker Time card into per-variant rows
            e["wiki_variants"] = sorted(r["Name"] for r in w_rows if r["Name"].startswith("Mad Science ("))
            matched_wiki.update(key(n) for n in e["wiki_variants"])
        if w:
            matched_wiki.add(key(w["Name"]))
            wd = strip_wiki(w.get("Description"))
            e["wiki"] = {"description": wiki_variant(wd, False), "upgraded_description": wiki_variant(wd, True),
                         "cost": w.get("Cost"), "upgraded_cost": w.get("CostPlus") or None, "rarity": w.get("Rarity"),
                         "type": w.get("Type"), "color": w.get("Color")}
            e["sources"].append({"name": "slaythespire.wiki.gg", "url": wiki_url(w.get("Link")), "fetched": fetched["wiki"]})
            wcost = {"-1": "X", "-2": "unplayable"}.get(str(w.get("Cost")), w.get("Cost"))
            if str(g["cost"]) != str(wcost) and w.get("Cost") not in ("", None):
                conflicts.add(e, "cards", "cost", g["cost"], wcost, "wiki")
            if w.get("CostPlus") not in ("", None) and str(g["upgraded_cost"]) != str(w["CostPlus"]):
                conflicts.add(e, "cards", "upgraded_cost", g["upgraded_cost"], w["CostPlus"], "wiki")
            if norm_type(g["type"]) != norm_type(w.get("Type")):
                conflicts.add(e, "cards", "type", g["type"], w.get("Type"), "wiki")
            if g["rarity"] and w.get("Rarity") and norm_rarity(g["rarity"]) != norm_rarity(w.get("Rarity")):
                conflicts.add(e, "cards", "rarity", g["rarity"], w.get("Rarity"), "wiki")
            if not g["unresolved_vars"]:
                for fld, gtxt in (("description", g["description"]), ("upgraded_description", g["upgraded_description"])):
                    wn, gn = nums(e["wiki"][fld]), nums(gtxt)
                    if wn != gn and e["wiki"][fld]:
                        conflicts.add(e, "cards", fld + ".numbers", gn, wn, "wiki", note=f"game: {gtxt} | wiki: {e['wiki'][fld]}")
        c = c_idx.get(g["id"])
        if c:
            e["sources"].append({"name": "spire-codex", "url": f"{CODEX}/cards/{g['id'].lower()}", "fetched": fetched["codex"]})
            if c.get("cost") is not None and c["cost"] >= 0 and isinstance(g["cost"], int) and c["cost"] != g["cost"]:
                conflicts.add(e, "cards", "cost", g["cost"], c["cost"], "codex")
            cd = strip_bbcode(c.get("description"))
            if not g["unresolved_vars"] and nums(cd) != nums(g["description"]) and cd:
                conflicts.add(e, "cards", "description.numbers", nums(g["description"]), nums(cd), "codex", note=f"codex: {cd}")
        if not e["conflicts"]:
            del e["conflicts"]
        out.append(e)
    wiki_only = sorted({w["Name"] for w in w_rows if key(w["Name"]) not in matched_wiki})
    return out, wiki_only


def build_simple(kind, gt, wiki, codex, conflicts, version, fetched):
    wtab = {"relics": "Relics", "potions": "Potions"}[kind]
    w_rows = wiki[wtab]["rows"]
    w_idx = index_by_name(w_rows)
    c_idx = {r["id"]: r for r in codex[kind]["rows"]}
    out = []
    for g in gt[kind]:
        e = {"id": g["id"], "name": g["name"], "pools": g["pools"], "rarity": g["rarity"],
             "description": g["description"], "vars": g["vars"],
             "internal_or_undiscovered": g["internal_or_undiscovered"],
             "sources": [src_game(version, kind.title(), g["class"])], "conflicts": []}
        for extra in ("usage", "target"):
            if extra in g:
                e[extra] = g[extra]
        if g["unresolved_vars"]:
            e["dynamic_vars"] = g["unresolved_vars"]
        w = w_idx.get(key(g["name"]))
        if w:
            wd = strip_wiki(w.get("Description"))
            e["wiki"] = {"description": wd, "rarity": w.get("Rarity"), "exclusive_to": w.get("ExclusiveTo")}
            e["sources"].append({"name": "slaythespire.wiki.gg", "url": wiki_url(w.get("Link")), "fetched": fetched["wiki"]})
            if norm_rarity(g["rarity"]) and norm_rarity(w.get("Rarity")) and norm_rarity(g["rarity"]) != norm_rarity(w.get("Rarity")):
                conflicts.add(e, kind, "rarity", g["rarity"], w.get("Rarity"), "wiki")
            if not g["unresolved_vars"] and nums(wd) != nums(g["description"]) and wd:
                conflicts.add(e, kind, "description.numbers", nums(g["description"]), nums(wd), "wiki",
                              note=f"game: {g['description']} | wiki: {wd}")
        c = c_idx.get(g["id"])
        if c:
            e["sources"].append({"name": "spire-codex", "url": f"{CODEX}/{kind}/{g['id'].lower()}", "fetched": fetched["codex"]})
            cd = strip_bbcode(c.get("description"))
            if not g["unresolved_vars"] and cd and nums(cd) != nums(g["description"]):
                conflicts.add(e, kind, "description.numbers", nums(g["description"]), nums(cd), "codex", note=f"codex: {cd}")
        if not e["conflicts"]:
            del e["conflicts"]
        out.append(e)
    names = {key(g["name"]) for g in gt[kind]}
    return out, sorted({w["Name"] for w in w_rows if key(w["Name"]) not in names})


# ------------------------------------------------------------------------------------------------

def fmt(v: dict | None) -> str:
    if not v or "base" not in v:
        return "X"
    s = str(v["base"])
    if v.get("asc") is not None and v["asc"] != v["base"]:
        s += f" (A{v['asc_level']}: {v['asc']})"
    return s


def pattern_text(m: dict) -> str:
    moves = {x["id"]: x for x in m["moves"]}
    br = {b["id"]: b for b in m["branches"]}
    label = lambda i: (moves[i]["title"] or i) if i in moves else (f"<{br[i]['kind']} branch {i}>" if i in br else i)
    parts = []
    if m.get("initial_move"):
        parts.append(f"starts with {label(m['initial_move'])}")
    for x in m["moves"]:
        if x["next"]:
            parts.append(f"{label(x['id'])} -> {', '.join(label(n) for n in x['next'])}")
    for b in m["branches"]:
        opts = []
        for o in b["options"]:
            s = label(o["to"])
            if o.get("condition"):
                s += f" if {o['condition'].replace('() => ', '')}"
            if o.get("repeat") and o["repeat"] != "None":
                s += f" [{o['repeat']}]"
            if o.get("weight"):
                s += f" w={o['weight']}"
            opts.append(s)
        parts.append(f"{b['kind']} branch {b['id']}: " + " | ".join(opts))
    for it in m.get("interrupts", []):
        parts.append(f"interrupt -> {label(it['to'])} (in {it['in']})")
    return "; ".join(parts)


ASC_RE = re.compile(r"\[A(\d+): (\d+)\]")


def parse_wiki_intent(text: str) -> dict:
    t = strip_wiki(text)
    out = {"text": t}
    m = re.search(r"Deals \(?(\d+)(?:;?\s*\(?\[A9: (\d+)\]\)?)?\)? damage(?: [x×](\d+))?", t)
    if m:
        out["damage"] = int(m.group(1))
        if m.group(2):
            out["asc_damage"] = int(m.group(2))
        if m.group(3):
            out["hits"] = int(m.group(3))
    return out


def parse_hp(s) -> tuple:
    if s in (None, ""):
        return None, None
    m = re.match(r"\s*(\d+)\s*(?:[-–]\s*(\d+))?", str(s))
    if not m:
        return None, None
    a = int(m.group(1))
    return a, int(m.group(2)) if m.group(2) else a


def wiki_enemy_modules(lua) -> dict:
    out = {}
    for mod, payload in lua["modules"].items():
        if not mod.startswith("Module:Enemies/StS2 data/"):
            continue
        for name, row in (payload.get("data") or {}).items():
            out[key(name)] = {**row, "_name": name, "_module": mod}
    return out


def extract_section_prose(content: str, heading: str) -> list[str]:
    m = re.search(rf"^==+\s*{re.escape(heading)}\s*==+\s*$(.*?)(?=^==|\Z)", content or "", re.M | re.S)
    if not m:
        return []
    body = re.sub(r"\{\{Intents Table/start.*?\{\{Intents Table/end\}\}", "", m.group(1), flags=re.S)
    lines = []
    for line in body.splitlines():
        line = line.strip()
        if not line or line.startswith(("{{Clear", "<blockquote", "</blockquote", "[[File", "{{Gallery")):
            continue
        txt = strip_wiki(re.sub(r"^[#*:]+", "", line)).strip()
        if txt and len(txt) > 3:
            lines.append(txt[:300])
    return lines[:20]


def build_enemies(gt, wiki, lua, pages, codex, conflicts, version, fetched):
    wmods = wiki_enemy_modules(lua)
    wcargo = index_by_name(wiki["Enemies"]["rows"])
    c_idx = {r["id"]: r for r in codex["monsters"]["rows"]}
    enc_by_monster: dict[str, list] = {}
    for enc in gt["encounters"]:
        for mid in enc["monsters"]:
            enc_by_monster.setdefault(mid, []).append(enc)
    out = []
    for m in gt["monsters"]:
        encs = enc_by_monster.get(m["id"], [])
        tiers = sorted({e["room_type"] for e in encs if e["room_type"]})
        e = {"id": m["id"], "name": m["name"], "hp": m["hp"], "tiers": tiers,
             "acts": sorted({a for x in encs for a in x["acts"]}),
             "encounters": sorted(x["id"] for x in encs),
             "starting_powers": [{"power": p["power"].removesuffix("Power"), "target": p["target"], "amount": p["amount"]} for p in m["starting_powers"]],
             "initial_move": m["initial_move"],
             "moves": [{"id": x["id"], "title": x["title"], "intents": [i["type"] for i in x["intents"]],
                        "effects": x["effects"], "summary": x["summary"], "next": x["next"],
                        **({"title_derived_from_id": True} if x.get("title_derived_from_id") else {})} for x in m["moves"]],
             "branches": m["branches"], "pattern": pattern_text(m),
             "sources": [src_game(version, "Monsters", m["class"])], "conflicts": []}
        w = wmods.get(key(m["name"])) or wmods.get(key(m["id"]))
        wc = wcargo.get(key(m["name"]))
        if w or wc:
            link = (w or {}).get("Link") or (wc or {}).get("Link") or f"Slay the Spire 2:{(w or wc).get('_name') or wc.get('Name')}"
            link = link.replace("_", " ")
            if not link.startswith("Slay the Spire 2:"):
                link = "Slay the Spire 2:" + link
            base = (w or {}).get("BaseHP") or (wc or {}).get("HP")
            asc = (w or {}).get("AscHP") or (wc or {}).get("AscHP")
            e["wiki"] = {"hp": base, "asc_hp": asc, "type": (w or wc or {}).get("Type"),
                         "intents": [{"name": i.get("Name"), **parse_wiki_intent(i.get("Text", ""))} for i in (w or {}).get("Intents", [])]}
            e["sources"].append({"name": "slaythespire.wiki.gg", "url": wiki_url(link), "fetched": fetched["wiki"],
                                 "module": (w or {}).get("_module")})
            b0, b1 = parse_hp(base)
            a0, a1 = parse_hp(asc)
            if b0 is not None and (b0, b1) != (m["hp"]["min"], m["hp"]["max"]):
                conflicts.add(e, "enemies", "hp", [m["hp"]["min"], m["hp"]["max"]], base, "wiki")
            if a0 is not None and m["hp"]["asc_min"] is not None and (a0, a1) != (m["hp"]["asc_min"], m["hp"]["asc_max"]):
                conflicts.add(e, "enemies", "asc_hp", [m["hp"]["asc_min"], m["hp"]["asc_max"]], asc, "wiki")
            titles: dict[str, list] = {}
            for x in m["moves"]:
                if x["title"]:
                    titles.setdefault(key(x["title"]), []).append(x)
            for wi in e["wiki"]["intents"]:
                cands = titles.get(key(wi["name"]), [])
                if not cands or "damage" not in wi:
                    continue
                atks = [(x, next((f for f in x["effects"] if f["op"] == "attack"), None)) for x in cands]
                atks = [(x, a) for x, a in atks if a and "base" in a["damage"]]
                if not atks:
                    continue
                gm, atk = next(((x, a) for x, a in atks if a["damage"]["base"] == wi["damage"]), atks[0])
                if atk["damage"]["base"] != wi["damage"]:
                    conflicts.add(e, "enemies", f"move.{gm['id']}.damage", atk["damage"]["base"], wi["damage"], "wiki", note=wi["text"])
                ga = atk["damage"].get("asc", atk["damage"]["base"])
                if "asc_damage" in wi and ga != wi["asc_damage"]:
                    conflicts.add(e, "enemies", f"move.{gm['id']}.asc_damage", ga, wi["asc_damage"], "wiki", note=wi["text"])
                gh = atk["hits"].get("base")
                if "hits" in wi and isinstance(gh, int) and gh != wi["hits"]:
                    conflicts.add(e, "enemies", f"move.{gm['id']}.hits", gh, wi["hits"], "wiki", note=wi["text"])
        wlink = e["sources"][-1].get("url", "").replace(WIKI, "").replace("_", " ") if w or wc else ""
        page = pages.get(wlink.split("#")[0]) if wlink else None
        if page and set(tiers) & {"Boss", "Elite"}:
            content = page["content"]
            if "#" in wlink:  # minion documented in a sub-section of the boss page
                sec = re.search(rf"^==\s*{re.escape(wlink.split('#', 1)[1])}\s*==\s*$", content, re.M)
                content = content[sec.start():] if sec else ""
            e["wiki_notes"] = {"pattern": extract_section_prose(content, "Pattern"),
                               "notes": extract_section_prose(page["content"], "Notes")}
        c = c_idx.get(m["id"])
        if c:
            e["sources"].append({"name": "spire-codex", "url": f"{CODEX}/monsters/{m['id'].lower()}", "fetched": fetched["codex"]})
            if c.get("min_hp") is not None and c["min_hp"] != m["hp"]["min"]:
                conflicts.add(e, "enemies", "hp", m["hp"]["min"], c["min_hp"], "codex")
            if c.get("min_hp_ascension") is not None and m["hp"]["asc_min"] is not None and c["min_hp_ascension"] != m["hp"]["asc_min"]:
                conflicts.add(e, "enemies", "asc_hp", m["hp"]["asc_min"], c["min_hp_ascension"], "codex")
            gmoves = {x["id"].removesuffix("_MOVE"): x for x in m["moves"]}
            for cm in c.get("moves") or []:
                gm = gmoves.get(cm["id"])
                dmg = (cm.get("damage") or {})
                if not gm or dmg.get("normal") is None:
                    continue
                atk = next((f for f in gm["effects"] if f["op"] == "attack"), None)
                if atk and "base" in atk["damage"] and atk["damage"]["base"] != dmg["normal"]:
                    conflicts.add(e, "enemies", f"move.{gm['id']}.damage", atk["damage"]["base"], dmg["normal"], "codex")
        if not e["conflicts"]:
            del e["conflicts"]
        out.append(e)
    names = {key(m["name"]) for m in gt["monsters"]}
    wiki_only = sorted({v["_name"] for k, v in wmods.items() if k not in names})
    encounters = [{**{k: v for k, v in enc.items() if k != "class"},
                   "sources": [src_game(version, "Encounters", enc["class"])]} for enc in gt["encounters"]]
    return out, encounters, wiki_only


def build_bosses(enemies, encounters, gt):
    by_id = {e["id"]: e for e in enemies}
    out = []
    for enc in encounters:
        if enc["room_type"] not in ("Boss", "Elite"):
            continue
        mons = [by_id[m] for m in enc["monsters"] if m in by_id]
        out.append({
            "id": enc["id"], "name": enc["name"], "room_type": enc["room_type"], "acts": enc["acts"],
            "boss_slot_order": [a for a, v in gt["acts"].items() if enc["id"] in v["boss_order"]],
            "monsters": [{"id": x["id"], "name": x["name"], "hp": x["hp"], "starting_powers": x["starting_powers"],
                          "moves": [{"id": mv["id"], "title": mv["title"], "summary": mv["summary"], "intents": mv["intents"]} for mv in x["moves"]],
                          "pattern": x["pattern"], "wiki_notes": x.get("wiki_notes"),
                          "conflicts": x.get("conflicts")} for x in mons],
            "sources": enc["sources"] + [s for x in mons for s in x["sources"] if s["name"] != "game"],
        })
    return out


# ------------------------------------------------------------------------------------------------

def normalize_condition(src: str | None) -> str | None:
    """Turn the event's IsAllowed() logic into a short normalized pseudo-expression (own wording, not code)."""
    if not src:
        return None
    t = src
    t = re.sub(r"\((?:Player|CardModel|PotionModel) (\w+)\) =>", r"\1 =>", t)
    t = re.sub(r"\((?:decimal|int)\)", "", t)
    t = re.sub(r"base\.DynamicVars\[\"(\w+)\"\]\.BaseValue|base\.DynamicVars\.(\w+)\.BaseValue", lambda m: "var:" + (m.group(1) or m.group(2)), t)
    t = t.replace("runState.", "").replace("CurrentActIndex", "act_index(0-based)").replace("Creature.", "")
    t = re.sub(r"ModelDb\.\w+<(\w+)>\(\)\.", r"\1.", t)
    t = re.sub(r"CardPile\.Get\(PileType\.Deck, (\w+)\)", r"\1.Deck", t)
    t = re.sub(r"if \((.*?)\) \{ return (.*?); \} return false;", r"(\1) and (\2)", t)
    t = re.sub(r"\breturn\s+|;", "", t)
    t = t.replace("&&", "and").replace("||", "or").replace("Players.All(", "all players(").replace("Players.Any(", "any player(")
    return re.sub(r"\s+", " ", t).strip()[:240]


def _title_re(t: str) -> str:
    parts = re.split(r"\bX\b|<[^>]*>|\[[^\]]*\]", t)
    return ".+".join(re.escape(p.strip().lower()) for p in parts)


def title_match(a: str, b: str) -> bool:
    a2, b2 = a.strip().lower(), b.strip().lower()
    return a2 == b2 or bool(re.fullmatch(_title_re(a), b2)) or bool(re.fullmatch(_title_re(b), a2))


def build_events(gt, lua, pages, codex, conflicts, version, fetched):
    wev = {key(k): v for k, v in (lua["modules"].get("Module:Events/StS2 data", {}).get("data") or {}).items()}
    wanc = {key(k): v for k, v in (lua["modules"].get("Module:Ancients/data", {}).get("data") or {}).items()}
    page_by_key = {key(t.replace("Slay the Spire 2:", "")): p for t, p in pages.items()}
    c_idx = {r["id"]: r for r in codex["events"]["rows"]}
    out = []
    for g in gt["events"]:
        if g["id"] in ("DEPRECATED_EVENT", "DEPRECATED_ANCIENT_EVENT") or not g["name"]:
            continue
        initial = next((p for p in g["pages"] if p["id"] == "INITIAL"), None)
        e = {"id": g["id"], "name": g["name"], "is_ancient": g["is_ancient"], "epithet": g.get("epithet"),
             "acts": g["acts"], "allowed_condition": normalize_condition(g["allowed_condition_src"]),
             "vars": g["vars"],
             "options": [{"id": o["id"], "title": o.get("title"), "description": o.get("description")}
                         for o in (initial or {}).get("options", [])],
             "pages": [{"id": p["id"], "options": [{"id": o["id"], "title": o.get("title"), "description": o.get("description")} for o in p["options"]]}
                       for p in g["pages"] if p["id"] != "INITIAL" and p["options"]],
             "relics_referenced": g["relics_referenced"], "cards_referenced": g["cards_referenced"],
             "sources": [src_game(version, "Events", g["class"])], "conflicts": []}
        wl = (wanc if g["is_ancient"] else wev).get(key(g["name"]))
        wp = page_by_key.get(key(g["name"]))
        if wl or wp:
            e["wiki"] = {}
            if wl:
                e["wiki"]["acts"] = wl.get("Act") or wl.get("Acts")
                if wl.get("Description"):
                    e["wiki"]["summary"] = strip_wiki(wl["Description"])[:400]
            if wp:
                parsed = wp.get("parsed") or {}
                e["wiki"]["options"] = [{"title": o["title"], "text": strip_wiki(o.get("text_wiki", ""))[:300]} for o in parsed.get("options", [])]
                e["wiki"]["mechanics"] = [strip_wiki(x)[:300] for x in parsed.get("mechanics", [])][:12]
                e["wiki"]["conditions"] = [strip_wiki(x)[:300] for x in parsed.get("conditions", [])][:8]
                e["sources"].append({"name": "slaythespire.wiki.gg", "url": wp["url"], "fetched": fetched["wiki"],
                                     "revid": wp.get("revid")})
                gtitles = [o.get("title") for p in g["pages"] for o in p["options"] if o.get("title")]
                game_titles = set(gtitles)
                missing = [o["title"] for o in e["wiki"]["options"] if o["title"] and not any(title_match(o["title"], t) for t in gtitles)]
                if missing and game_titles:
                    conflicts.add(e, "events", "options", sorted({o.get("title") for p in g["pages"] for o in p["options"] if o.get("title")}),
                                  missing, "wiki", note="wiki option titles not found in game localization")
        c = c_idx.get(g["id"])
        if c:
            e["codex_options"] = [{"id": o.get("id"), "title": o.get("title"), "description": strip_bbcode(o.get("description"))[:300]}
                                  for o in (c.get("options") or [])]
            e["sources"].append({"name": "spire-codex", "url": f"{CODEX}/events/{g['id'].lower()}", "fetched": fetched["codex"]})
        if not e["conflicts"]:
            del e["conflicts"]
        out.append(e)
    names = {key(g["name"]) for g in gt["events"] if g["name"]}
    wiki_only = sorted({k for k in list(wev) + list(wanc) if k not in names})
    return out, wiki_only


def build_keywords(gt, wiki, lua, codex, conflicts, version, fetched):
    wpow = index_by_name(wiki["Powers"]["rows"])
    wkw = index_by_name(wiki["Keywords"]["rows"])
    cpow = {r["id"]: r for r in codex["powers"]["rows"]}
    powers = []
    for p in gt["powers"]:
        if p["id"].startswith(("MOCK", "TEST", "DEPRECATED")):
            continue
        e = {"id": p["id"], "name": p["name"], "type": p["type"], "stack_type": p["stack_type"],
             "description": p["description"], "sources": [src_game(version, "Powers", p["class"])], "conflicts": []}
        w = wpow.get(key(p["name"]))
        if w:
            e["wiki"] = {"description": strip_wiki(w.get("Description"))[:300], "type": w.get("Type"),
                         "stacks": w.get("Stacks"), "between_turns": w.get("BetweenTurns")}
            e["sources"].append({"name": "slaythespire.wiki.gg", "url": wiki_url(w.get("Link")), "fetched": fetched["wiki"]})
            if p["type"] and w.get("Type") and p["type"].lower() != w["Type"].lower() and w["Type"] in ("Buff", "Debuff"):
                conflicts.add(e, "keywords", "power.type", p["type"], w["Type"], "wiki")
        c = cpow.get(p["id"])
        if c:
            e["sources"].append({"name": "spire-codex", "url": f"{CODEX}/powers/{p['id'].lower()}", "fetched": fetched["codex"]})
            if c.get("type") and p["type"] and c["type"] != p["type"]:
                conflicts.add(e, "keywords", "power.type", p["type"], c["type"], "codex")
        if not e["conflicts"]:
            del e["conflicts"]
        powers.append(e)
    card_kw = []
    seen = set()
    for k in gt["keywords_source"]:
        eng = (k.get("localized") or {}).get("eng") or {}
        card_kw.append({"id": k["id"], "name": eng.get("title"), "description": strip_bbcode(eng.get("description_template")),
                        "sources": [{"name": "game", "ref": f"knowledge/source/v{version}/keywords.jsonl", "authoritative": True}]})
        seen.add(k["id"])
    for k in gt["card_keywords"]:
        if k["id"] not in seen:
            card_kw.append({**k, "sources": [{"name": "game", "ref": "SlayTheSpire2.pck card_keywords", "authoritative": True}]})
            seen.add(k["id"])
    for e in card_kw:
        w = wkw.get(key(e["name"]))
        if w:
            e["wiki"] = {"description": strip_wiki(w.get("Description"))[:300]}
            e["sources"].append({"name": "slaythespire.wiki.gg", "url": wiki_url(w.get("Link")), "fetched": fetched["wiki"]})
    statuses = [c["id"] for c in gt["cards"] if "Status" in c["pools"]]
    curses = [c["id"] for c in gt["cards"] if "Curse" in c["pools"]]
    asc_mod = lua["modules"].get("Module:Ascension/StS2 data", {}).get("data") or {}
    ascension = [{"level": int(k), "wiki_description": strip_wiki(v)} for k, v in sorted(asc_mod.items(), key=lambda kv: int(kv[0]))]
    return {
        "card_keywords": card_kw,
        "powers": powers,
        "afflictions": [{**a, "sources": [src_game(version, "Afflictions", a["class"])]} for a in gt["afflictions"]],
        "enchantments": [{**a, "sources": [src_game(version, "Enchantments", a["class"])]} for a in gt["enchantments"] if not a["id"].startswith(("MOCK", "DEPRECATED"))],
        "status_cards": statuses, "curse_cards": curses,
        "ascension": ascension,
        "ascension_game_enum": {"1": "SwarmingElites", "2": "WearyTraveler", "3": "Poverty", "4": "TightBelt", "5": "AscendersBane",
                                "6": "Inflation", "7": "Scarcity", "8": "ToughEnemies (enemy HP)", "9": "DeadlyEnemies (enemy damage)", "10": "DoubleBoss"},
    }


# ------------------------------------------------------------------------------------------------

def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("--version", default="0.107.1")
    args = ap.parse_args()
    v = args.version
    cd = cache_dir(v)
    gt = read_json(cd / "game_truth.json")
    wiki = {t: read_json(cd / "wiki" / f"cargo_{t}.json") for t in ("Cards", "Relics", "Potions", "Enemies", "Encounters", "Powers", "Keywords")}
    lua = read_json(cd / "wiki" / "lua_modules.json")
    pages = read_json(cd / "wiki" / "pages.json")["pages"]
    codex = {t: read_json(cd / "codex" / f"{t}.json") for t in ("cards", "relics", "potions", "monsters", "events", "powers")}
    codex_meta = read_json(cd / "codex" / "meta.json")
    fetched = {"wiki": wiki["Cards"]["fetched"], "codex": codex["cards"]["fetched"]}
    conflicts = Conflicts()
    out = facts_dir(v)

    cards, cards_wiki_only = build_cards(gt, wiki, codex, conflicts, v, fetched)
    relics, relics_wiki_only = build_simple("relics", gt, wiki, codex, conflicts, v, fetched)
    potions, potions_wiki_only = build_simple("potions", gt, wiki, codex, conflicts, v, fetched)
    enemies, encounters, enemies_wiki_only = build_enemies(gt, wiki, lua, pages, codex, conflicts, v, fetched)
    bosses = build_bosses(enemies, encounters, gt)
    events, events_wiki_only = build_events(gt, lua, pages, codex, conflicts, v, fetched)
    keywords = build_keywords(gt, wiki, lua, codex, conflicts, v, fetched)

    def meta(category: str, extra: dict | None = None) -> dict:
        return {"schema": "schema.json", "schema_version": SCHEMA_VERSION, "category": category, "game_version": v,
                "generated": today(), "authority": "game (sts2.dll + pck localization) > wiki.gg > spire-codex",
                "sources": {"game": f"Slay the Spire 2 v{v} (read-only extraction)",
                            "wiki": {"url": "https://slaythespire.wiki.gg/", "fetched": fetched["wiki"]},
                            "codex": {"url": CODEX, "fetched": fetched["codex"], "note": "tracks latest stable build; may differ from pinned version",
                                      "api_meta": codex_meta.get("mod/latest")}},
                **(extra or {})}

    write_json(out / "cards.json", {"meta": meta("cards", {"wiki_only": cards_wiki_only}), "entries": cards})
    write_json(out / "relics.json", {"meta": meta("relics", {"wiki_only": relics_wiki_only}), "entries": relics})
    write_json(out / "potions.json", {"meta": meta("potions", {"wiki_only": potions_wiki_only}), "entries": potions})
    write_json(out / "enemies.json", {"meta": meta("enemies", {"wiki_only": enemies_wiki_only}), "entries": enemies,
                                      "encounters": encounters,
                                      "acts": {k: {kk: vv for kk, vv in a.items() if kk != "class"} for k, a in gt["acts"].items() if a["encounters"]}})
    write_json(out / "bosses.json", {"meta": meta("bosses_and_elites"), "entries": bosses})
    write_json(out / "events.json", {"meta": meta("events", {"wiki_only": events_wiki_only, "shared_events": gt["shared_events"]}), "entries": events})
    write_json(out / "keywords.json", {"meta": meta("keywords"), **keywords})
    by_cat = Counter((c["category"], c["source"]) for c in conflicts.items)
    write_json(out / "conflicts.json", {"meta": meta("conflicts"), "counts": {f"{a}/{b}": n for (a, b), n in sorted(by_cat.items())},
                                        "total": len(conflicts.items), "items": conflicts.items})
    stats = {
        "cards": {"total": len(cards), "with_wiki": sum("wiki" in c for c in cards),
                  "with_codex": sum(any(s["name"] == "spire-codex" for s in c["sources"]) for c in cards),
                  "ironclad": sum(c["character"] == "Ironclad" for c in cards), "wiki_only": len(cards_wiki_only)},
        "relics": {"total": len(relics), "with_wiki": sum("wiki" in c for c in relics), "wiki_only": len(relics_wiki_only)},
        "potions": {"total": len(potions), "with_wiki": sum("wiki" in c for c in potions), "wiki_only": len(potions_wiki_only)},
        "enemies": {"total": len(enemies), "with_wiki": sum("wiki" in c for c in enemies), "encounters": len(encounters),
                    "moves": sum(len(e["moves"]) for e in enemies), "wiki_only": len(enemies_wiki_only)},
        "bosses_elites": {"boss_encounters": sum(b["room_type"] == "Boss" for b in bosses), "elite_encounters": sum(b["room_type"] == "Elite" for b in bosses)},
        "events": {"total": len(events), "ancients": sum(e["is_ancient"] for e in events), "with_wiki": sum("wiki" in e for e in events), "wiki_only": len(events_wiki_only)},
        "keywords": {"card_keywords": len(keywords["card_keywords"]), "powers": len(keywords["powers"]),
                     "afflictions": len(keywords["afflictions"]), "enchantments": len(keywords["enchantments"]),
                     "status_cards": len(keywords["status_cards"])},
        "conflicts": {"total": len(conflicts.items), **{f"{a}/{b}": n for (a, b), n in sorted(by_cat.items())}},
    }
    write_json(out / "stats.json", stats)
    import json
    print(json.dumps(stats, indent=1))


if __name__ == "__main__":
    main()
