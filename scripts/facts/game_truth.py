#!/usr/bin/env python3
"""Extract game-truth facts for a pinned STS2 build (read-only).

Inputs (never modified, never committed):
  * knowledge/source/v<version>/*.jsonl   (exporter output already in repo)
  * artifacts/knowledge/decompiled-v<version>/  (ilspycmd output of sts2.dll, git-ignored)
  * <game install>/SlayTheSpire2.pck      (English localization JSON tables only)

Output: artifacts/facts-cache/v<version>/game_truth.json (git-ignored intermediate).
Only normalized, derived values are later copied into knowledge/facts.

Usage: python3 scripts/facts/game_truth.py --version 0.107.1 [--game-dir DIR]
"""

from __future__ import annotations

import argparse
import json
import mmap
import os
import re
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

from common import REPO_ROOT, cache_dir, camel_to_id, strip_bbcode, today, write_json  # noqa: E402

NS = "MegaCrit.Sts2.Core.Models"
ASC_LEVELS = {"SwarmingElites": 1, "WearyTraveler": 2, "Poverty": 3, "TightBelt": 4, "AscendersBane": 5,
              "Inflation": 6, "Scarcity": 7, "ToughEnemies": 8, "DeadlyEnemies": 9, "DoubleBoss": 10}

# --------------------------------------------------------------------------------------------
# Localization (English) from the PCK
# --------------------------------------------------------------------------------------------

LOC_SENTINELS = {
    "cards": "BASH.title", "relics": "BURNING_BLOOD.title", "potions": "FIRE_POTION.title",
    "events": "SLIPPERY_BRIDGE.pages.INITIAL.description", "ancients": "NEOW.epithet",
    "monsters": "TORCH_HEAD_AMALGAM.name", "encounters": "QUEEN_BOSS.title",
    "powers": "VULNERABLE_POWER.description", "afflictions": "BOUND.extraCardText",
    "card_keywords": "EXHAUST.title", "enchantments": "SHARP.title", "intents": None,
}
EN_WORDS = re.compile(r"\b(the|your|you|and|of|to|deal|gain|damage|card|cards|turn)\b", re.I)


def _objects_containing(m: mmap.mmap, key: str) -> list[dict]:
    out, pos, needle = [], 0, f'"{key}"'.encode()
    while True:
        p = m.find(needle, pos)
        if p < 0:
            return out
        pos = p + len(needle)
        start = m.rfind(b"{\n", max(0, p - 8 * 1024 * 1024), p)
        if start < 0:
            continue
        chunk = m[start:start + 8 * 1024 * 1024].decode("utf-8", "replace")
        try:
            value, _ = json.JSONDecoder().raw_decode(chunk)
        except ValueError:
            continue
        if isinstance(value, dict) and key in value and all(isinstance(v, str) for v in list(value.values())[:50]):
            out.append(value)


def _english_score(obj: dict) -> float:
    text = " ".join(list(obj.values())[:400])
    return len(EN_WORDS.findall(text)) / max(1, len(text) / 100)


def load_english_loc(pck: Path) -> dict[str, dict[str, str]]:
    tables: dict[str, dict[str, str]] = {}
    with pck.open("rb") as fh, mmap.mmap(fh.fileno(), 0, access=mmap.ACCESS_READ) as m:
        for table, key in LOC_SENTINELS.items():
            if not key:
                continue
            candidates = _objects_containing(m, key)
            if candidates:
                tables[table] = max(candidates, key=_english_score)
            else:
                tables[table] = {}
    return tables


# --------------------------------------------------------------------------------------------
# SmartFormat-ish template rendering
# --------------------------------------------------------------------------------------------

def _match_brace(s: str, i: int) -> int:
    depth = 0
    for j in range(i, len(s)):
        if s[j] == "{":
            depth += 1
        elif s[j] == "}":
            depth -= 1
            if depth == 0:
                return j
    return len(s) - 1


def _split_top(s: str, sep: str = "|") -> list[str]:
    parts, depth, cur = [], 0, []
    for ch in s:
        if ch == "{":
            depth += 1
        elif ch == "}":
            depth -= 1
        if ch == sep and depth == 0:
            parts.append("".join(cur))
            cur = []
        else:
            cur.append(ch)
    parts.append("".join(cur))
    return parts


def _fmt_num(v) -> str:
    if isinstance(v, float) and v.is_integer():
        v = int(v)
    return str(v)


def render(template: str, values: dict, upgraded: bool = False, missing: set | None = None) -> str:
    """Render a localization template with known DynamicVar values; unknown vars → 'X'."""
    missing = missing if missing is not None else set()
    out, i = [], 0
    while i < len(template):
        ch = template[i]
        if ch != "{":
            out.append(ch)
            i += 1
            continue
        j = _match_brace(template, i)
        body = template[i + 1:j]
        i = j + 1
        name, _, rest = body.partition(":")
        name = name.split(".")[0].strip()
        val = values.get(name)
        if name == "IfUpgraded":
            fm, _, opts = rest.partition(":")
            parts = _split_top(opts)
            out.append(render(parts[0] if upgraded else (parts[1] if len(parts) > 1 else ""), values, upgraded, missing))
            continue
        if name in ("singleStarIcon", "energyPrefix") and not rest:
            out.append("[Star]" if name == "singleStarIcon" else "")
            continue
        if not rest or rest.startswith(("diff", "inverseDiff", "percentMore")):
            if val is None:
                missing.add(name)
                out.append("X")
            else:
                if rest.startswith("percentMore") and isinstance(val, (int, float)):
                    val = round((val - 1) * 100 if val >= 1 else val * 100, 2)
                out.append(_fmt_num(abs(val) if rest.startswith("inverseDiff") and isinstance(val, (int, float)) else val))
            continue
        fm, _, opts = rest.partition(":")
        fname = re.match(r"[A-Za-z]+", fm)
        fname = fname.group(0) if fname else ""
        if fname in ("energyIcons", "starIcons"):
            arg = re.search(r"\((\d+)\)", fm)
            icon = "[E]" if fname == "energyIcons" else "[Star]"
            if arg:  # explicit icon count, e.g. "0{energyPrefix:energyIcons(1)}" -> "0[E]"
                out.append(icon)
                continue
            if val is None:
                missing.add(name)
            out.append(f"{_fmt_num(val) if val is not None else 'X'}{icon}")
            continue
        if fname == "plural":
            parts = _split_top(opts)
            chosen = parts[0] if val == 1 else parts[-1]
            if val is None and "{}" in chosen:
                missing.add(name)
            out.append(render(chosen.replace("{}", _fmt_num(val) if val is not None else "X"), values, upgraded, missing))
            continue
        if fname == "cond":
            m = re.match(r"([<>=!]+)(-?\d+)\?(.*)", opts, re.S)
            if m and isinstance(val, (int, float)):
                op, n = m.group(1), float(m.group(2))
                parts = _split_top(m.group(3))
                ok = {">": val > n, "<": val < n, ">=": val >= n, "<=": val <= n, "==": val == n, "!=": val != n}.get(op, False)
                out.append(render(parts[0] if ok else (parts[1] if len(parts) > 1 else ""), values, upgraded, missing))
            else:
                parts = _split_top(opts)
                out.append(render(parts[-1], values, upgraded, missing))
            continue
        if fname == "choose":
            missing.add(name)
            out.append("(varies)")
            continue
        # boolean-ish conditional such as {InCombat:...|...} or {HasRider:...|}: out-of-combat branch
        parts = _split_top(rest)
        out.append(render(parts[-1] if len(parts) > 1 else "", values, upgraded, missing))
    return "".join(out)


def clean_text(text: str) -> str:
    text = strip_bbcode(text)
    text = re.sub(r"[ \t]+", " ", text)
    return re.sub(r"\s*\n\s*", " ", text).strip()


# --------------------------------------------------------------------------------------------
# C# helpers
# --------------------------------------------------------------------------------------------

VAR_RE = re.compile(r"new\s+(\w+?)Var(?:<(\w+)>)?\((?:\"(\w+)\"\s*,\s*)?(-?\d+(?:\.\d+)?)m?")


def class_src(decomp: Path, ns: str, name: str) -> str:
    path = decomp / f"{NS}.{ns}" / f"{name}.cs"
    return path.read_text(encoding="utf-8", errors="replace") if path.exists() else ""


def block_after(src: str, marker: str) -> str:
    i = src.find(marker)
    if i < 0:
        return ""
    j = src.find(";", i)
    k = src.find("{", i)
    if k < 0 or (0 <= j < k):
        return src[i:j]
    return src[i:_match_brace(src, k) + 1]


def canonical_vars(src: str) -> dict:
    blk = block_after(src, "CanonicalVars =>")
    values: dict = {}
    for kind, generic, explicit, num in VAR_RE.findall(blk):
        key = explicit or generic or kind
        n = float(num)
        values[key] = int(n) if n.is_integer() else n
        if generic and generic.endswith("Power") and not explicit:
            values.setdefault(generic[:-5], values[key])
    return values


def upgrades(src: str) -> tuple[dict, dict]:
    blk = block_after(src, "void OnUpgrade()")
    deltas: dict = {}
    extra: dict = {"add_keywords": [], "remove_keywords": [], "cost_delta": 0}
    pat = r"DynamicVars(?:\.(\w+)|\[\"(\w+)\"\])\.UpgradeValueBy\((-?[\d.]+)m?\)"
    for a, b, n in re.findall(pat, blk):
        v = float(n)
        deltas[a or b] = int(v) if v.is_integer() else v
    extra["add_keywords"] = re.findall(r"AddKeyword\(CardKeyword\.(\w+)\)", blk)
    extra["remove_keywords"] = re.findall(r"RemoveKeyword\(CardKeyword\.(\w+)\)", blk)
    m = re.search(r"EnergyCost\.UpgradeBy\((-?\d+)\)", blk)
    if m:
        extra["cost_delta"] = int(m.group(1))
    return deltas, extra


def apply_upgrade(values: dict, deltas: dict) -> dict:
    up = dict(values)
    for name, d in deltas.items():
        targets = [k for k in up if k == name or k == name + "Power"] or [name]
        for k in targets:
            if k in up:
                up[k] = up[k] + d
    # keep alias pairs (VulnerablePower / Vulnerable) in sync
    for k in list(up):
        if k.endswith("Power") and k[:-5] in up and k[:-5] not in deltas and k in deltas:
            up[k[:-5]] = up[k]
        if k.endswith("Power") and k[:-5] in deltas:
            up[k] = values.get(k, 0) + deltas[k[:-5]]
            up[k[:-5]] = up[k]
    return up


def pool_members(decomp: Path, ns: str, kind: str) -> dict[str, list[str]]:
    out: dict[str, list[str]] = {}
    d = decomp / f"{NS}.{ns}"
    if not d.exists():
        return out
    for f in sorted(d.glob("*.cs")):
        pool = re.sub(r"(Card|Relic|Potion)Pool$", "", f.stem)
        for cls in re.findall(rf"ModelDb\.{kind}<(\w+)>\(\)", f.read_text(errors="replace")):
            out.setdefault(camel_to_id(cls), []).append(pool)
    return out


# --------------------------------------------------------------------------------------------
# Cards / relics / potions
# --------------------------------------------------------------------------------------------

def load_jsonl(path: Path) -> list[dict]:
    return [json.loads(line) for line in path.open(encoding="utf-8")] if path.exists() else []


def numbers_in(text: str) -> list[int]:
    return [int(x) for x in re.findall(r"(?<![\w.])\d+(?![\w.])", text or "", re.ASCII)]


def extract_cards(src_dir: Path, decomp: Path) -> list[dict]:
    pools = pool_members(decomp, "CardPools", "Card")
    out = []
    for rec in load_jsonl(src_dir / "cards.jsonl"):
        cls = rec.get("source", {}).get("type", "").split(".")[-1] or rec["id"].title().replace("_", "")
        src = class_src(decomp, "Cards", cls)
        values = canonical_vars(src)
        deltas, extra = upgrades(src)
        up_values = apply_upgrade(values, deltas)
        ctor = rec.get("source", {}).get("base_constructor") or ""
        m = re.match(r"\s*(-?\d+)\s*,\s*CardType\.(\w+)\s*,\s*CardRarity\.(\w+)\s*,\s*TargetType\.(\w+)", ctor)
        cost, ctype, rarity, target = (int(m.group(1)), m.group(2), m.group(3), m.group(4)) if m else (None, None, None, None)
        kw_blk = block_after(src, "CanonicalKeywords =>")
        keywords = re.findall(r"CardKeyword\.(\w+)", kw_blk)
        eng = rec.get("localized", {}).get("eng") or {}
        miss: set = set()
        base_txt = clean_text(render(eng.get("description_template", ""), values, False, miss))
        up_txt = clean_text(render(eng.get("description_template", ""), up_values, True, miss))
        rt = rec.get("runtime") or {}
        rb, ru = rt.get("base") or {}, rt.get("upgraded") or {}
        if rb.get("cost") not in (None, ""):
            try:
                cost = int(rb["cost"])
            except ValueError:
                cost = rb["cost"]  # "X"
        ctor_cost = (rec.get("source", {}).get("base_constructor") or "").split(",")[0].strip()
        if "Unplayable" in keywords or (ctor_cost == "-1" and cost != "X"):
            cost = "unplayable"  # ctor cost -1 without CostsX = no energy cost / not normally playable
        up_keywords = [k for k in keywords if k not in extra["remove_keywords"]] + extra["add_keywords"]
        out.append({
            "id": rec["id"], "class": cls, "name": eng.get("title"),
            "pools": pools.get(rec["id"], []),
            "cost": cost, "upgraded_cost": (cost + extra["cost_delta"]) if isinstance(cost, int) else cost,
            "type": rb.get("type") or ctype, "rarity": rb.get("rarity") or rarity, "target": target,
            "keywords": keywords, "upgraded_keywords": up_keywords,
            "vars": values, "upgrade_deltas": deltas,
            "description": base_txt, "upgraded_description": up_txt,
            "unresolved_vars": sorted(miss),
            "runtime_numbers": {"base": numbers_in(rb.get("description")), "upgraded": numbers_in(ru.get("description"))} if rb else None,
            "upgradable": rt.get("is_upgradable") if rt.get("is_upgradable") is not None else bool(deltas or extra or base_txt != up_txt),
            "internal_or_undiscovered": rec.get("internal_or_undiscovered", False),
        })
    return out


def _rarity_from_src(src: str) -> str | None:
    m = re.search(r"Rarity => \w+Rarity\.(\w+)", src)
    return m.group(1) if m else None


def extract_simple(src_dir: Path, decomp: Path, kind: str) -> list[dict]:
    ns = {"relics": "Relics", "potions": "Potions"}[kind]
    pools = pool_members(decomp, ns[:-1] + "Pools", ns[:-1])
    out = []
    for rec in load_jsonl(src_dir / f"{kind}.jsonl"):
        cls = rec.get("source", {}).get("type", "").split(".")[-1]
        src = class_src(decomp, ns, cls)
        values = canonical_vars(src)
        eng = rec.get("localized", {}).get("eng") or {}
        miss: set = set()
        rt = rec.get("runtime") or {}
        item = {
            "id": rec["id"], "class": cls, "name": eng.get("title"),
            "pools": pools.get(rec["id"], []),
            "rarity": rt.get("rarity") or _rarity_from_src(src),
            "vars": values,
            "description": clean_text(render(eng.get("description_template", ""), values, False, miss)),
            "unresolved_vars": sorted(miss),
            "runtime_numbers": numbers_in(rt.get("description")) if rt else None,
            "internal_or_undiscovered": rec.get("internal_or_undiscovered", False),
        }
        if kind == "potions":
            m = re.search(r"Usage => PotionUsage\.(\w+)", src)
            t = re.search(r"TargetType => TargetType\.(\w+)", src)
            item["usage"] = m.group(1) if m else None
            item["target"] = t.group(1) if t else None
        out.append(item)
    return out


# --------------------------------------------------------------------------------------------
# Monsters
# --------------------------------------------------------------------------------------------

PROP_RE = re.compile(r"(?:private|public|protected|internal)(?: override| static)? (?:int|decimal|float) (\w+) =>\s*([^;]+);")
CONST_RE = re.compile(r"(?:private|public|protected|internal) const (?:int|decimal|float) (\w+) = (-?[\d.]+)m?f?;")


def _num(s: str):
    v = float(s.rstrip("mf"))
    return int(v) if v.is_integer() else v


def monster_props(src: str) -> dict:
    props: dict = {}
    for name, val in CONST_RE.findall(src):
        props[name] = {"base": _num(val)}
    for name, expr in PROP_RE.findall(src):
        expr = expr.strip()
        m = re.match(r"AscensionHelper\.GetValueIfAscension\(AscensionLevel\.(\w+),\s*(-?[\d.]+)m?f?,\s*(-?[\d.]+)m?f?\)", expr)
        if m:
            props[name] = {"base": _num(m.group(3)), "asc": _num(m.group(2)), "asc_level": ASC_LEVELS.get(m.group(1), m.group(1))}
        elif re.fullmatch(r"-?[\d.]+m?f?", expr):
            props[name] = {"base": _num(expr)}
        elif expr in props:
            props[name] = dict(props[expr])
        else:
            props[name] = {"expr": expr[:120]}
    return props


def resolve(expr: str, props: dict) -> dict:
    expr = expr.strip()
    expr = re.sub(r"^\(\w+\)", "", expr)
    if re.fullmatch(r"-?[\d.]+[mf]?", expr):
        return {"base": _num(expr)}
    m = re.match(r"AscensionHelper\.GetValueIfAscension\(AscensionLevel\.(\w+),\s*(-?[\d.]+)m?f?,\s*(-?[\d.]+)m?f?\)", expr)
    if m:
        return {"base": _num(m.group(3)), "asc": _num(m.group(2)), "asc_level": ASC_LEVELS.get(m.group(1), m.group(1))}
    key = expr.replace("base.", "").replace("this.", "")
    if key in props and "expr" not in props[key]:
        return dict(props[key])
    return {"expr": expr[:120]}


def _fmt_val(v: dict) -> str:
    if "base" not in v:
        return "X"
    s = _fmt_num(v["base"])
    if "asc" in v and v["asc"] != v["base"]:
        s += f" (A{v['asc_level']}: {_fmt_num(v['asc'])})"
    return s


def _args(s: str, start: int) -> tuple[str, int]:
    depth, i = 0, start
    while i < len(s):
        if s[i] == "(":
            depth += 1
        elif s[i] == ")":
            depth -= 1
            if depth == 0:
                return s[start + 1:i], i
        i += 1
    return s[start + 1:], len(s)


def _split_args(s: str) -> list[str]:
    parts, depth, cur = [], 0, []
    for ch in s:
        if ch in "(<[{":
            depth += 1
        elif ch in ")>]}":
            depth -= 1
        if ch == "," and depth == 0:
            parts.append("".join(cur).strip())
            cur = []
        else:
            cur.append(ch)
    if cur:
        parts.append("".join(cur).strip())
    return parts


def method_body(src: str, name: str) -> str:
    m = re.search(rf"(?:private|public|protected) (?:override )?(?:async )?\w+(?:<[\w<>, ?]+>)? {name}\(", src)
    if not m:
        return ""
    k = src.find("{", m.end())
    return src[k:_match_brace(src, k) + 1] if k >= 0 else ""


def _target(expr: str) -> str:
    expr = expr.strip()
    if "base.Creature" in expr or expr == "Creature":
        return "self"
    if "targets" in expr or "Player" in expr:
        return "player"
    if "ally" in expr.lower() or "teammate" in expr.lower() or "item" in expr or "creature" in expr:
        return "ally/other"
    return expr[:40]


def method_effects(body: str, props: dict) -> list[dict]:
    effects = []
    for m in re.finditer(r"DamageCmd\.Attack\(", body):
        args, end = _args(body, m.end() - 1)
        tail = body[end:end + 600]
        hits = re.search(r"\.WithHitCount\(([^()]*(?:\([^()]*\))?[^()]*)\)", tail.split(";")[0])
        effects.append({"op": "attack", "damage": resolve(_split_args(args)[0] if args else "", props),
                        "hits": resolve(hits.group(1), props) if hits else {"base": 1}})
    for m in re.finditer(r"PowerCmd\.Apply<(\w+)>\(", body):
        a = _split_args(_args(body, m.end() - 1)[0])
        if len(a) >= 3:
            effects.append({"op": "apply_power", "power": m.group(1), "target": _target(a[1]), "amount": resolve(a[2], props)})
    for m in re.finditer(r"CreatureCmd\.GainBlock\(", body):
        a = _split_args(_args(body, m.end() - 1)[0])
        if len(a) >= 2:
            effects.append({"op": "block", "target": _target(a[0]), "amount": resolve(a[1], props)})
    for m in re.finditer(r"CreatureCmd\.Heal\(", body):
        a = _split_args(_args(body, m.end() - 1)[0])
        if len(a) >= 2:
            effects.append({"op": "heal", "target": _target(a[0]), "amount": resolve(a[1], props)})
    for m in re.finditer(r"CardPileCmd\.AddToCombatAndPreview<(\w+)>\(", body):
        a = _split_args(_args(body, m.end() - 1)[0])
        pile = next((re.sub(r"PileType\.", "", x) for x in a if "PileType." in x), None)
        count = resolve(a[-1], props) if a and not a[-1].startswith(("false", "true", "PileType")) else {"base": 1}
        effects.append({"op": "add_cards", "card": camel_to_id(m.group(1)), "pile": pile, "count": count})
    for m in re.finditer(r"(?:CreatureCmd\.Add|SummonMinion|Summon)\w*<(\w+)>", body):
        effects.append({"op": "summon", "monster": camel_to_id(m.group(1))})
    for m in re.finditer(r"(?:AfflictionCmd|CardCmd)\.Afflict\w*<(\w+)>", body):
        effects.append({"op": "afflict", "affliction": camel_to_id(m.group(1))})
    if "CreatureCmd.Escape" in body or "Escape(" in body:
        effects.append({"op": "escape"})
    return effects


def effect_text(e: dict) -> str:
    if e["op"] == "attack":
        h = e["hits"]
        return f"attack {_fmt_val(e['damage'])}" + ("" if h.get("base") == 1 and "asc" not in h else f" x{_fmt_val(h)}")
    if e["op"] == "apply_power":
        return f"apply {e['power'].removesuffix('Power')} {_fmt_val(e['amount'])} to {e['target']}"
    if e["op"] == "block":
        return f"block {_fmt_val(e['amount'])} ({e['target']})"
    if e["op"] == "heal":
        return f"heal {_fmt_val(e['amount'])} ({e['target']})"
    if e["op"] == "add_cards":
        return f"add {_fmt_val(e['count'])} {e['card']} to {e['pile'] or 'pile'}"
    if e["op"] == "summon":
        return f"summon {e['monster']}"
    if e["op"] == "afflict":
        return f"afflict {e['affliction']}"
    return e["op"]


INTENT_RE = re.compile(r"new (\w+)Intent\(")


def extract_monster(decomp: Path, cls: str, loc: dict) -> dict:
    src = class_src(decomp, "Monsters", cls)
    mid = camel_to_id(cls)
    props = monster_props(src)
    hp_min, hp_max = props.get("MinInitialHp", {}), props.get("MaxInitialHp", {})
    for _ in range(3):
        hp_min = props.get(hp_min.get("expr", ""), hp_min) if "expr" in hp_min else hp_min
        hp_max = props.get(hp_max.get("expr", ""), hp_max) if "expr" in hp_max else hp_max
    form_hp = {k: v for k, v in props.items() if k.endswith("FormHp") and "base" in v}
    sm = method_body(src, "GenerateMoveStateMachine")
    var_state: dict[str, str] = {}
    moves: dict[str, dict] = {}
    branches: dict[str, dict] = {}
    for m in re.finditer(r"(?:\w+ )?(\w+) = new MoveState\(", sm):
        a = _split_args(_args(sm, m.end() - 1)[0])
        if len(a) < 2:
            continue
        sid = a[0].strip('"')
        var_state[m.group(1)] = sid
        intents = []
        for arg in a[2:]:
            im = INTENT_RE.match(arg)
            if not im:
                continue
            ia = _split_args(_args(arg, im.end() - 1)[0])
            it = {"type": im.group(1)}
            if im.group(1) in ("SingleAttack", "MultiAttack", "DeathBlow") and ia:
                it["damage"] = resolve(ia[0], props)
                if len(ia) > 1:
                    it["hits"] = resolve(ia[1], props)
            elif im.group(1) == "Status" and ia:
                it["count"] = resolve(ia[0], props)
            intents.append(it)
        body = method_body(src, a[1])
        loc_key = sid.removesuffix("_MOVE")
        if f"{mid}.moves.{loc_key}.title" not in loc:
            loc_key = re.sub(r"_\d+$", "", loc_key)
        effects = method_effects(body, props)
        title = loc.get(f"{mid}.moves.{loc_key}.title")
        moves[sid] = {"id": sid, "title": title or loc_key.replace("_", " ").title(),
                      **({} if title else {"title_derived_from_id": True}), "method": a[1],
                      "intents": intents, "effects": effects,
                      "summary": "; ".join(effect_text(e) for e in effects), "next": []}
    for m in re.finditer(r"(?:\w+ )?(\w+) = new (ConditionalBranchState|RandomBranchState)\(\"(\w+)\"", sm):
        var_state[m.group(1)] = m.group(3)
        branches[m.group(3)] = {"id": m.group(3), "kind": "conditional" if m.group(2).startswith("Cond") else "random", "options": []}
    for m in re.finditer(r"(\w+)\.FollowUpState = (\w+);", sm):
        a, b = var_state.get(m.group(1)), var_state.get(m.group(2))
        if a in moves and b:
            moves[a]["next"].append(b)
    for m in re.finditer(r"(\w+)\.(AddState|AddBranch)\(", sm):
        args = _split_args(_args(sm, m.end() - 1)[0])
        br = var_state.get(m.group(1))
        if br in branches and args:
            opt = {"to": var_state.get(args[0], args[0])}
            if m.group(2) == "AddState" and len(args) > 1:
                opt["condition"] = re.sub(r"\s+", " ", args[1])[:160]
            elif len(args) > 1:
                opt["repeat"] = args[1].replace("MoveRepeatType.", "")
                if len(args) > 2:
                    opt["weight"] = args[2]
            branches[br]["options"].append(opt)
    interrupts = []
    for m in re.finditer(r"SetMoveImmediate\((\w+)\)", src):
        sid = var_state.get(m.group(1))
        owner = [n for n in re.findall(r"(?:override |private |public |protected )(?:async )?\w+ (\w+)\(", src[:m.start()])][-1:]
        if sid:
            interrupts.append({"to": sid, "in": owner[0] if owner else None})
    init = re.search(r"new MonsterMoveStateMachine\(\w+,\s*(\w+)\)", sm)
    start = method_body(src, "AfterAddedToRoom")
    starting = [e for e in method_effects(start, props) if e["op"] == "apply_power"]
    return {
        "id": mid, "class": cls, "name": loc.get(f"{mid}.name"),
        "hp": {"min": hp_min.get("base"), "max": hp_max.get("base"),
               "asc_min": hp_min.get("asc"), "asc_max": hp_max.get("asc"),
               "asc_level": hp_min.get("asc_level"),
               **({"forms": {k.removesuffix("Hp"): v for k, v in form_hp.items()}} if form_hp else {})},
        "starting_powers": [{"power": e["power"], "target": e["target"], "amount": e["amount"]} for e in starting],
        "initial_move": var_state.get(init.group(1)) if init else None,
        "moves": list(moves.values()), "branches": list(branches.values()),
        "interrupts": interrupts,
    }


# --------------------------------------------------------------------------------------------
# Encounters / acts / events / ancients / powers / misc
# --------------------------------------------------------------------------------------------

def extract_acts(decomp: Path) -> dict:
    acts = {}
    for f in sorted((decomp / f"{NS}.Acts").glob("*.cs")):
        s = f.read_text(errors="replace")
        enc_blk = block_after(s, "GenerateAllEncounters()") or s
        ev_blk = block_after(s, "AllEvents =>")
        anc_blk = block_after(s, "AllAncients =>")
        boss_blk = block_after(s, "BossDiscoveryOrder =>")
        num = re.search(r"ActNumber\w* => (\d+)", s)
        acts[camel_to_id(f.stem)] = {
            "class": f.stem,
            "encounters": [camel_to_id(x) for x in re.findall(r"ModelDb\.Encounter<(\w+)>", enc_blk)],
            "events": [camel_to_id(x) for x in re.findall(r"ModelDb\.Event<(\w+)>", ev_blk)],
            "ancients": [camel_to_id(x) for x in re.findall(r"ModelDb\.(?:Event|AncientEvent)<(\w+)>", anc_blk)],
            "boss_order": [camel_to_id(x) for x in re.findall(r"ModelDb\.Encounter<(\w+)>", boss_blk)],
            "act_number_hint": int(num.group(1)) if num else None,
        }
    return acts


def extract_encounters(decomp: Path, loc: dict, acts: dict) -> list[dict]:
    out = []
    for f in sorted((decomp / f"{NS}.Encounters").glob("*.cs")):
        s = f.read_text(errors="replace")
        eid = camel_to_id(f.stem)
        room = re.search(r"RoomType => RoomType\.(\w+)", s)
        weak = re.search(r"IsWeak => (true|false)", s)
        monsters = sorted({camel_to_id(x) for x in re.findall(r"ModelDb\.Monster<(\w+)>", s)})
        out.append({"id": eid, "class": f.stem, "name": loc.get(f"{eid}.title"),
                    "room_type": room.group(1) if room else None,
                    "is_weak": weak.group(1) == "true" if weak else None,
                    "monsters": monsters,
                    "acts": [a for a, v in acts.items() if eid in v["encounters"] or eid in v["boss_order"]]})
    return out


def _event_values(src: str) -> dict:
    values = canonical_vars(src)
    for name, val in CONST_RE.findall(src):
        values.setdefault(name.lstrip("_")[0].upper() + name.lstrip("_")[1:], _num(val))
    return values


def extract_events(decomp: Path, loc_events: dict, loc_ancients: dict, acts: dict, shared: list[str]) -> list[dict]:
    out = []
    for f in sorted((decomp / f"{NS}.Events").glob("*.cs")):
        s = f.read_text(errors="replace")
        if re.search(r"class \w+ : (?:Mock|Test)", s):
            continue
        eid = camel_to_id(f.stem)
        is_ancient = bool(re.search(r"class \w+ : AncientEventModel", s))
        loc = {**loc_events, **loc_ancients}
        values = _event_values(s)
        pages: dict[str, dict] = {}
        prefix = f"{eid}.pages."
        for key, text in loc.items():
            if not key.startswith(prefix):
                continue
            rest = key[len(prefix):].split(".")
            page = pages.setdefault(rest[0], {"id": rest[0], "description": None, "options": {}})
            miss: set = set()
            if len(rest) == 2 and rest[1] == "description":
                page["description"] = clean_text(render(text, values, False, miss))
            elif len(rest) >= 4 and rest[1] == "options":
                opt = page["options"].setdefault(rest[2], {"id": rest[2]})
                opt[rest[3]] = clean_text(render(text, values, False, miss))
        allowed = method_body(s, "IsAllowed")
        cond = " ".join(l.strip() for l in allowed.splitlines()[1:-1] if l.strip())[:400] if allowed else None
        relics = sorted({camel_to_id(x) for x in re.findall(r"(?:ModelDb\.Relic|RelicOption)<(\w+)>", s)})
        cards = sorted({camel_to_id(x) for x in re.findall(r"ModelDb\.Card<(\w+)>", s)})
        out.append({
            "id": eid, "class": f.stem, "is_ancient": is_ancient,
            "name": loc.get(f"{eid}.title"), "epithet": loc.get(f"{eid}.epithet") if is_ancient else None,
            "acts": [a for a, v in acts.items() if eid in v["events"] or eid in v["ancients"]] + (["SHARED"] if eid in shared else []),
            "vars": values,
            "allowed_condition_src": cond,
            "pages": [{**p, "options": list(p["options"].values())} for p in pages.values()],
            "relics_referenced": relics, "cards_referenced": cards,
        })
    return out


def extract_shared_events(decomp: Path) -> list[str]:
    s = (decomp / "MegaCrit.Sts2.Core.Models" / "ModelDb.cs")
    if not s.exists():
        cands = list(decomp.glob("*/ModelDb.cs"))
        s = cands[0] if cands else None
    if not s:
        return []
    blk = block_after(s.read_text(errors="replace"), "AllSharedEvents =>")
    return [camel_to_id(x) for x in re.findall(r"(?:ModelDb\.)?Event<(\w+)>", blk)]


def extract_powers(decomp: Path, loc: dict) -> list[dict]:
    out = []
    for f in sorted((decomp / f"{NS}.Powers").glob("*.cs")):
        s = f.read_text(errors="replace")
        pid = camel_to_id(f.stem)
        t = re.search(r"PowerType Type => PowerType\.(\w+)", s)
        st = re.search(r"PowerStackType StackType => PowerStackType\.(\w+)", s)
        desc = loc.get(f"{pid}.smartDescription") or loc.get(f"{pid}.description") or ""
        out.append({"id": pid, "class": f.stem, "name": loc.get(f"{pid}.title"),
                    "type": t.group(1) if t else None, "stack_type": st.group(1) if st else None,
                    "description": clean_text(render(desc, {"Amount": "X"}, False, set())) if desc else None})
    return out


def extract_named(decomp: Path, ns: str, loc: dict, desc_keys=("description",)) -> list[dict]:
    out = []
    d = decomp / f"{NS}.{ns}"
    for f in sorted(d.glob("*.cs")) if d.exists() else []:
        iid = camel_to_id(f.stem)
        desc = next((loc[f"{iid}.{k}"] for k in desc_keys if f"{iid}.{k}" in loc), "")
        out.append({"id": iid, "class": f.stem, "name": loc.get(f"{iid}.title"),
                    "description": clean_text(render(desc, {}, False, set())) if desc else None})
    return out


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("--version", default="0.107.1")
    ap.add_argument("--game-dir", default=os.environ.get("STS2_GAME_INSTALL_DIR", "/workspace/private/sts2-linux-0.107.1"))
    args = ap.parse_args()
    v = args.version
    src_dir = REPO_ROOT / "knowledge" / "source" / f"v{v}"
    decomp = REPO_ROOT / "artifacts" / "knowledge" / f"decompiled-v{v}"
    if not decomp.exists():
        sys.exit(f"missing decompiled tree {decomp}; run ilspycmd -p -o {decomp} <game>/data_*/sts2.dll")
    loc = load_english_loc(Path(args.game_dir) / "SlayTheSpire2.pck")
    print({k: len(t) for k, t in loc.items()})
    acts = extract_acts(decomp)
    shared = extract_shared_events(decomp)
    monsters = [extract_monster(decomp, f.stem, loc["monsters"])
                for f in sorted((decomp / f"{NS}.Monsters").glob("*.cs"))
                if "MonsterModel" in f.read_text(errors="replace") and not f.stem.endswith("MoveMonster")
                and f.stem not in ("OneHpMonster",)]
    data = {
        "version": v, "generated": today(),
        "cards": extract_cards(src_dir, decomp),
        "relics": extract_simple(src_dir, decomp, "relics"),
        "potions": extract_simple(src_dir, decomp, "potions"),
        "monsters": monsters,
        "encounters": extract_encounters(decomp, loc["encounters"], acts),
        "acts": acts, "shared_events": shared,
        "events": extract_events(decomp, loc["events"], loc["ancients"], acts, shared),
        "powers": extract_powers(decomp, loc["powers"]),
        "afflictions": extract_named(decomp, "Afflictions", loc["afflictions"], ("description",)),
        "enchantments": extract_named(decomp, "Enchantments", loc["enchantments"], ("description",)),
        "card_keywords": [{"id": k.split(".")[0], "name": t,
                           "description": clean_text(render(loc["card_keywords"].get(k.split(".")[0] + ".description", ""), {}, False, set()))}
                          for k, t in loc["card_keywords"].items() if k.endswith(".title")],
        "keywords_source": load_jsonl(src_dir / "keywords.jsonl"),
    }
    write_json(cache_dir(v) / "game_truth.json", data)
    print({k: len(x) for k, x in data.items() if isinstance(x, (list, dict))})


if __name__ == "__main__":
    main()
