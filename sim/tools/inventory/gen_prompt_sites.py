#!/usr/bin/env python3
"""Generate sim/Sts2Sim.Core/Choice/PromptSites.g.cs: the static prompt-site table behind RunSim.MayPrompt.

Usage (from the repo root):
    python3 sim/tools/inventory/gen_prompt_sites.py           # regenerate PromptSites.g.cs
    python3 sim/tools/inventory/gen_prompt_sites.py --check   # exit 1 when the checked-in file is stale
    python3 sim/tools/inventory/gen_prompt_sites.py --list    # print the table (one site per line)

What it scans (stdlib only; the structural scanner is cs_source.py):

1. Every model def under sim/Sts2Sim.Core (cards, enchantments, afflictions, relics, powers, potions, monsters, orbs,
   events, ancients). A model's id is its own `const string <Kind>Id = "..."`.
2. In each model, three kinds of hazard call site, with the member (method) that contains it:
   - prompt:   CardSelectCmd.From*, RelicDeckPromptsR91.From*, RoomPrompts.From*, EventKit3456.From*/Select*,
               <x>.Choice.Ask(, new ChoicePrompt(  -- the call reaches ChoiceRuntime.Ask, i.e. may need an answer;
   - autoplay: CardCmd.AutoPlay(, CardPileCmd.AutoPlayFromDrawPile(, CardCmdForwarders.AutoPlay(  -- plays a card the
               player did not choose, which may be a prompting card;
   - sly:      ApplySingleTurnSly( / GiveSingleTurnSly( / ApplyKeyword(..Sly..) or a (Try)ModifyKeywordsInCombat override
               naming Sly  -- a discarded Sly card is auto-played by the kernel (CardCmd.Play.cs, Discard).
3. A closure: a model that references a hazardous power, monster, relic, enchantment, affliction or orb (by class
   name, or a static call on a hazardous card/potion class) is hazardous too, at the member holding the reference
   (e.g. the ENTROPY card applies ENTROPY_POWER in OnPlay). Iterated to a fixed point.
4. Trigger class per model (what makes the hazard live):
   - card:   "play" when every hazard member is OnPlay, else "presence";
   - potion: "use" when every hazard member is OnUse, else "presence";
   - relic:  "obtain" when every hazard member is AfterObtained, else "presence";
   - power, monster, enchantment, affliction, orb: "presence"; event: "event".
5. Non-model files that contain hazard calls must be in KERNEL_FILES (reviewed: each is either a prompt helper whose
   entry points are in the patterns above, or a room/reward prompt that RunSim.MayPrompt gates by action kind).
   A new hit fails generation, so a new prompt path cannot slip past the table silently.
6. Cross-check with sim/specs/inventory/*.classified.json: every decompile prompt record whose opener (or `via`
   model) exists in the sim must be in the table, else generation fails.

The runtime soak (sim/Sts2Sim.Tests/FastPathTests.cs) is the other half of the fail-closed contract: every prompt seen
in a random soak must come from a source id in PromptSourceIds, and MayPrompt == false must never be followed by a
pending prompt.
"""

import argparse
import glob
import json
import os
import re
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

from cs_source import CsFile, iter_types, split_args, balanced_call_args, STRING_PLACEHOLDER_RE  # noqa: E402

REPO_ROOT = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", ".."))
CORE = os.path.join(REPO_ROOT, "sim", "Sts2Sim.Core")
OUT = os.path.join(CORE, "Choice", "PromptSites.g.cs")
INVENTORY = os.path.join(REPO_ROOT, "sim", "specs", "inventory")

# kind -> (directories relative to Sts2Sim.Core, own-id const name)
KINDS = {
    "card": (["Cards/Defs"], "CardId"),
    "enchantment": (["Cards/Enchantments/Defs"], "EnchantmentId"),
    "affliction": (["Cards/Afflictions/Defs"], "AfflictionId"),
    "relic": (["Relics/Defs"], "RelicId"),
    "power": (["Powers/Defs"], "PowerId"),
    "potion": (["Potions/Defs"], "PotionId"),
    "monster": (["Monsters/Defs"], "MonsterIdEntry"),
    "orb": (["Orbs/Defs"], "OrbIdEntry"),
    "event": (["Run/Events/Defs", "Run/Events/Ancients"], "EventId"),
    "modifier": (["Modifiers/Defs"], "ModifierId"),
}
PRESENCE_KINDS = {"power", "monster", "enchantment", "affliction", "orb", "modifier"}
EDGE_KINDS = {"power", "monster", "relic", "enchantment", "affliction", "orb", "modifier"}
GATED = {"card": ("OnPlay", "play"), "potion": ("OnUse", "use"), "relic": ("AfterObtained", "obtain")}

PROMPT_RE = re.compile(
    r"\bCardSelectCmd\.From\w+\s*\(|\bRelicDeckPromptsR91\.From\w+\s*\(|\bRoomPrompts\.From\w+\s*\("
    r"|\bEventKit3456\.(?:From\w+|Select\w+)\s*\(|\bChoice\.Ask\s*\(|\bnew\s+ChoicePrompt\s*\(")
AUTOPLAY_RE = re.compile(r"\bCardCmd\.AutoPlay\s*\(|\bCardPileCmd\.AutoPlayFromDrawPile\s*\(|\bCardCmdForwarders\.AutoPlay\s*\(|(?<![\w.])AutoPlay\s*\(cs\b")
# Kernel functions that can reach a prompt when a model calls them (relic obtain -> AfterObtained prompts, merchant
# purchase -> card removal / relic obtain, reward claim, rest option, event option, room/act transitions).
ENTRY_RE = re.compile(r"\bRelicCmd\.Obtain\s*\(|\.OnTryPurchaseWrapper\s*\(|\.OnSelectWrapper\s*\(|\bRoomStack\.EnterRoom\w*\s*\("
                      r"|\.ChooseOption\s*\(|\bMoveToNextAct\s*\(|\bEnterMapCoord\w*\s*\(|\bEnterAct\s*\(")
SLY_CALL_RE = re.compile(r"\b(?:ApplySingleTurnSly|GiveSingleTurnSly)\s*\(")
KEYWORD_CALL_RE = re.compile(r"\b(?:ApplyKeyword|AddKeyword)\s*\(")
KEYWORD_HOOKS = {"ModifyKeywordsInCombat", "TryModifyKeywordsInCombat"}

# Non-model files allowed to contain hazard calls, with the reason each is covered.
KERNEL_FILES = {
    "Choice/CardSelectCmd.cs": "prompt helper; its entry points CardSelectCmd.From* are in PROMPT_RE",
    "Choice/CardSelectCmd.Deck.cs": "prompt helper; CardSelectCmd.From* (deck) entry points are in PROMPT_RE",
    "Choice/ChoiceRuntime.cs": "the runtime itself",
    "Relics/Defs/RelicDeckPromptsR91.cs": "prompt helper; RelicDeckPromptsR91.From* entry points are in PROMPT_RE",
    "Run/Events/Defs/EventKit3456.cs": "prompt helper; EventKit3456.From*/Select* entry points are in PROMPT_RE",
    "Run/Rooms/RoomPrompts.cs": "prompt helper; RoomPrompts.From* entry points are in PROMPT_RE",
    "Run/Rooms/RestSiteOption.cs": "rest-site options (SMITH, COOK): MayPrompt treats ChooseRestOption as prompting",
    "Run/Rooms/MerchantEntry.cs": "merchant card removal: MayPrompt treats BuyEntry as prompting",
    "Run/Rewards/BasicRewards.cs": "card-removal / relic rewards: MayPrompt treats ClaimReward of those as prompting",
    "Combat/Loop/CardCmd.Play.cs": "kernel AutoPlay + Sly discard auto-play: MayPrompt's Sly rule",
    "Combat/Loop/CardPileCmd.cs": "AutoPlayFromDrawPile helper; its callers match AUTOPLAY_RE",
    "Combat/Pipeline/CardCmd.cs": "ApplyKeyword/ApplySingleTurnSly helpers; their callers match the sly patterns",
    "Powers/CardCmdForwarders.cs": "AutoPlay forwarder; its callers match AUTOPLAY_RE",
    "Cards/CardInstance.cs": "GiveSingleTurnSly/AddKeyword primitives; their callers match the sly patterns",
}


class Model:
    __slots__ = ("kind", "id", "cls", "file", "line", "sites", "refs", "calls")

    def __init__(self, kind, mid, cls, rel, line):
        self.kind = kind
        self.id = mid
        self.cls = cls
        self.file = rel
        self.line = line
        self.sites = []  # (hazard, member, detail, line)
        self.refs = []   # (identifier, member, line, is_static_call)
        self.calls = []

    @property
    def key(self):
        return self.kind + ":" + self.id


def rel_of(path):
    return os.path.relpath(path, CORE).replace(os.sep, "/")


def member_at(td_list, index):
    """Innermost (type, member) whose span contains index."""
    best = (None, None)
    for top in td_list:
        for td in iter_types(top):
            if not (td.body_start <= index <= td.body_end):
                continue
            for m in td.members:
                if m.kind != "type" and m.decl_start <= index <= m.end:
                    best = (td, m)
    return best


def own_ids(cs, const_name):
    """[(TypeDecl, id)] for types declaring `const string <const_name> = "..."`."""
    out = []
    for top in cs.types:
        for td in iter_types(top):
            for m in td.members:
                if m.kind == "const" and m.name == const_name:
                    vals = cs.string_values(cs.masked[m.decl_start:m.end])
                    if vals:
                        out.append((td, vals[0]))
    return out


def scan_hazards(cs):
    """[(hazard, index, detail)] in a file (masked text: literals cannot match)."""
    s = cs.masked
    hits = []
    for m in PROMPT_RE.finditer(s):
        hits.append(("prompt", m.start(), m.group(0).rstrip("( ").strip()))
    for m in ENTRY_RE.finditer(s):
        hits.append(("entry", m.start(), m.group(0).rstrip("( ").strip()))
    for m in AUTOPLAY_RE.finditer(s):
        hits.append(("autoplay", m.start(), m.group(0).rstrip("( ").strip()))
    for m in SLY_CALL_RE.finditer(s):
        hits.append(("sly", m.start(), m.group(0).rstrip("( ").strip()))
    for m in KEYWORD_CALL_RE.finditer(s):
        args, _ = balanced_call_args(s, s.index("(", m.start()))
        if args is not None and re.search(r"\bSly\b", args):
            hits.append(("sly", m.start(), m.group(0).rstrip("( ").strip() + "(Sly)"))
    for top in cs.types:
        for td in iter_types(top):
            for mem in td.members:
                if mem.kind == "method" and mem.name in KEYWORD_HOOKS and re.search(r"\bSly\b", cs.masked[mem.decl_start:mem.end]):
                    hits.append(("sly", mem.decl_start, mem.name))
    return hits


def load_models():
    models = []
    by_class = {}
    kernel_hits = {}
    files = sorted(glob.glob(os.path.join(CORE, "**", "*.cs"), recursive=True))
    files = [f for f in files if "/bin/" not in f and "/obj/" not in f and not f.endswith(".g.cs")]
    kind_of_dir = []
    for kind, (dirs, const) in KINDS.items():
        for d in dirs:
            kind_of_dir.append((d.rstrip("/") + "/", kind, const))
    for path in files:
        rel = rel_of(path)
        cs = CsFile(path, rel)
        hits = scan_hazards(cs)
        kind = const = None
        for prefix, k, c in kind_of_dir:
            if rel.startswith(prefix) and "/" not in rel[len(prefix):]:
                kind, const = k, c
        ids = own_ids(cs, const) if kind else []
        if not ids:
            if hits:
                kernel_hits[rel] = hits
            continue
        file_models = []
        for td, mid in ids:
            mdl = Model(kind, mid, td.name, rel, td.line)
            file_models.append((td, mdl))
            models.append(mdl)
            by_class[td.name] = mdl
        primary = file_models[0][1]

        def owner_of(index):
            td, mem = member_at(cs.types, index)
            t = td
            while t is not None:
                for ftd, fm in file_models:
                    if ftd is t:
                        return fm, mem
                t = t.parent if hasattr(t, "parent") else None
            return primary, mem

        for hazard, index, detail in hits:
            mdl, mem = owner_of(index)
            mdl.sites.append((hazard, mem.name if mem is not None else "?", detail, cs.line_of(index)))
        # string consts of this file (a literal id in a const counts where the const is used, not where it is declared)
        file_consts = {}
        for top in cs.types:
            for td in iter_types(top):
                for mem in td.members:
                    if mem.kind == "const":
                        vals = cs.string_values(cs.masked[mem.decl_start:mem.end])
                        if vals:
                            file_consts[mem.name] = vals[0]
        # references (identifiers) for the closure, with the member holding them
        for m in re.finditer(r"\b([A-Z][A-Za-z0-9_]*)\b(\s*\.\s*([A-Za-z_]\w*)\s*(\()?)?", cs.masked):
            ident = m.group(1)
            if re.fullmatch(r"STR\d+", ident):
                continue
            mdl, mem = owner_of(m.start())
            is_static_call = bool(m.group(2)) and bool(m.group(4))
            if mem is not None and mem.kind == "const":
                continue
            mdl.refs.append((ident, mem.name if mem is not None else "?", cs.line_of(m.start()), is_static_call))
            if ident in file_consts:
                mdl.refs.append(("\"" + file_consts[ident], mem.name if mem is not None else "?", cs.line_of(m.start()), False))
        # string-literal ids (PowerRegistry.Create("STRATAGEM_POWER"), GetPower("..."), ...)
        for m in STRING_PLACEHOLDER_RE.finditer(cs.masked):
            value = cs.literal(m.group(1))
            if re.fullmatch(r"[A-Z][A-Z0-9_]*", value or ""):
                mdl, mem = owner_of(m.start())
                if mem is not None and mem.kind == "const":
                    continue
                mdl.refs.append(("\"" + value, mem.name if mem is not None else "?", cs.line_of(m.start()), False))
    return models, by_class, kernel_hits


def classify(models, by_class):
    by_id = {}
    for mdl in models:
        by_id.setdefault(mdl.id, []).append(mdl)
    hazard = {}  # key -> list of (hazard, member, detail, line)
    for mdl in models:
        if mdl.sites:
            hazard[mdl.key] = list(mdl.sites)
    changed = True
    while changed:
        changed = False
        for mdl in models:
            seen = {(h, mem, det) for h, mem, det, _ in hazard.get(mdl.key, [])}
            for ident, mem, line, is_static_call in mdl.refs:
                if ident.startswith('"'):
                    targets = [t for t in by_id.get(ident[1:], []) if t.kind in EDGE_KINDS]
                else:
                    t = by_class.get(ident)
                    targets = [t] if t is not None and (t.kind in EDGE_KINDS or (t.kind in ("card", "potion") and is_static_call)) else []
                for tgt in targets:
                    if tgt is mdl or tgt.key not in hazard:
                        continue
                    entry = ("via", mem, tgt.key, line)
                    if ("via", mem, tgt.key) not in seen:
                        hazard.setdefault(mdl.key, []).append(entry)
                        seen.add(("via", mem, tgt.key))
                        changed = True
    return hazard


def trigger_of(kind, sites):
    if kind == "event":
        return "event"
    if kind in PRESENCE_KINDS:
        return "presence"
    gate_member, gate = GATED[kind]
    return gate if all(mem == gate_member for _, mem, _, _ in sites) else "presence"


def room_sources(kernel_hits):
    """Source ids of RoomPrompts calls in room/reward kernel files (literal or a type's OptionId)."""
    out = {}
    for rel in ("Run/Rooms/RestSiteOption.cs", "Run/Rooms/MerchantEntry.cs", "Run/Rewards/BasicRewards.cs"):
        cs = CsFile(os.path.join(CORE, rel), rel)
        for m in re.finditer(r"\bRoomPrompts\.From\w+\s*\(", cs.masked):
            args, _ = balanced_call_args(cs.masked, m.end() - 1)
            parts = split_args(args)
            src = parts[4].strip() if len(parts) > 4 else ""
            vals = cs.string_values(src)
            if vals:
                out[vals[0]] = rel
                continue
            if src == "OptionId":
                td, _ = member_at(cs.types, m.start())
                for mem in td.members:
                    if mem.name == "OptionId":
                        v = cs.string_values(cs.masked[mem.decl_start:mem.end])
                        if v:
                            out[v[0]] = rel
                            break
                else:
                    raise SystemExit(f"{rel}:{cs.line_of(m.start())}: OptionId not resolvable")
                continue
            raise SystemExit(f"{rel}:{cs.line_of(m.start())}: RoomPrompts source id '{src}' is not a literal or OptionId")
    return out


# Inventory prompt records that do not make the entry a hazard in the sim (entry -> reason).
CROSS_CHECK_EXEMPT = {
    "card:DISINTEGRATION": "offered as an option by KNOWLEDGE_DEMON:ChooseCurse; opens nothing",
    "card:MIND_ROT": "offered as an option by KNOWLEDGE_DEMON:ChooseCurse; opens nothing",
    "card:SLOTH": "offered as an option by KNOWLEDGE_DEMON:ChooseCurse; opens nothing",
    "card:WASTE_AWAY": "offered as an option by KNOWLEDGE_DEMON:ChooseCurse; opens nothing",
}

INV_KIND = {"cards": "card", "relics": "relic", "potions": "potion", "powers": "power", "monsters": "monster",
            "events": "event", "modifiers": "modifier", "encounters": "encounter"}


def cross_check(models, hazard):
    """Every decompile prompt record whose opener exists in the sim must be in the table."""
    present = {m.key for m in models}
    problems = []
    checked = 0
    for path in sorted(glob.glob(os.path.join(INVENTORY, "*.classified.json"))):
        with open(path) as fh:
            doc = json.load(fh)
        for e in doc.get("entries", []):
            for p in e.get("prompts") or []:
                for ref in filter(None, [p.get("opened_by"), p.get("via")]):
                    kind, _, mid = ref.partition(":")
                    key = INV_KIND.get(kind, kind) + ":" + mid
                    if key in present:
                        checked += 1
                        if key not in hazard:
                            problems.append(f"{os.path.basename(path)} {e['id']}: {p['site']} opener {key} is ported but not in the table")
                # the entry itself, when it reaches the prompt through `via` (e.g. card ENTROPY via ENTROPY_POWER)
                ekind = INV_KIND.get(doc.get("content_kind", ""), None)
                if ekind and p.get("via") and ekind in ("card", "relic", "potion", "power", "monster"):
                    key = ekind + ":" + e["id"]
                    if key in present and key not in CROSS_CHECK_EXEMPT:
                        checked += 1
                        if key not in hazard:
                            problems.append(f"{os.path.basename(path)} {e['id']}: reaches {p['site']} via {p['via']} but is not in the table")
    return checked, problems


def cs_set(name, ids, doc):
    body = ",\n".join(f'        "{i}"' for i in sorted(ids))
    inner = f"\n{body},\n    " if ids else ""
    return (f"    /// <summary>{doc}</summary>\n"
            f"    public static readonly FrozenSet<string> {name} = FrozenSet.ToFrozenSet(new string[] {{{inner}}}, StringComparer.Ordinal);\n")


def render(models, hazard, rooms):
    by_key = {m.key: m for m in models}
    groups = {}
    for key, sites in hazard.items():
        mdl = by_key[key]
        trig = trigger_of(mdl.kind, sites)
        groups.setdefault((mdl.kind, trig), set()).add(mdl.id)
    prompt_sources = {by_key[k].id for k, s in hazard.items() if any(h == "prompt" for h, _, _, _ in s)} | set(rooms)
    out = []
    out.append("// <auto-generated>\n// Generated by sim/tools/inventory/gen_prompt_sites.py from the sim's own model defs; do not edit.\n"
               "// Regenerate after porting content: python3 sim/tools/inventory/gen_prompt_sites.py (--check in gates).\n// </auto-generated>\n")
    out.append("using System.Collections.Frozen;\n\nnamespace Sts2Sim.Core.Choice;\n\n")
    out.append("/// <summary>\n/// Static prompt-site table behind <see cref=\"Run.RunSim.MayPrompt\"/>: model ids whose code can reach\n"
               "/// <see cref=\"ChoiceRuntime.Ask\"/> (directly, through a hazardous model they reference, or by auto-playing a card),\n"
               "/// grouped by kind and by what makes the hazard live. Ids are model entries (e.g. \"SURVIVOR\").\n/// </summary>\n")
    out.append("public static partial class PromptSites\n{\n")
    out.append(f"    public const int ModelCount = {len(hazard)};\n\n")
    specs = [
        ("CardsOnPlay", ("card", "play"), "Cards whose hazard sites are all in OnPlay: live only when the card itself is played (or auto-played)."),
        ("CardsPresence", ("card", "presence"), "Cards with a hazard site outside OnPlay: live while the card is anywhere (deck or a combat pile)."),
        ("PotionsOnUse", ("potion", "use"), "Potions whose hazard sites are all in OnUse: live only when used."),
        ("PotionsPresence", ("potion", "presence"), "Potions with a hazard site outside OnUse: live while owned."),
        ("RelicsOnObtain", ("relic", "obtain"), "Relics whose hazard sites are all in AfterObtained: live only when obtained."),
        ("RelicsPresence", ("relic", "presence"), "Relics with a hazard site outside AfterObtained: live while owned."),
        ("Powers", ("power", "presence"), "Powers with a hazard site: live while on any creature."),
        ("Monsters", ("monster", "presence"), "Monsters with a hazard site: live while in the combat."),
        ("Enchantments", ("enchantment", "presence"), "Enchantments with a hazard site: live while on any card."),
        ("Afflictions", ("affliction", "presence"), "Afflictions with a hazard site: live while on any card."),
        ("Orbs", ("orb", "presence"), "Orbs with a hazard site: live while channeled."),
        ("Modifiers", ("modifier", "presence"), "Run modifiers with a hazard site: live while active (hook listeners)."),
        ("Events", ("event", "event"), "Events with a prompt site (MayPrompt treats every event step as prompting anyway)."),
    ]
    known = {k for _, k, _ in specs}
    extra = set(groups) - known
    if extra:
        raise SystemExit(f"unexpected (kind, trigger) groups: {sorted(extra)}")
    for name, k, doc in specs:
        out.append(cs_set(name, groups.get(k, set()), doc))
        out.append("\n")
    out.append(cs_set("RoomSources", set(rooms), "Prompt source ids of room/reward prompts (rest SMITH/COOK, merchant and reward card removal)."))
    out.append("\n")
    out.append(cs_set("PromptSourceIds", prompt_sources,
                      "Every source id a ChoicePrompt may carry: models with a direct prompt site plus RoomSources (the soak's fail-closed check)."))
    out.append("\n    /// <summary>One row per hazard site: kind, model id, hazard (prompt|autoplay|sly|via), member, detail, file:line.</summary>\n")
    out.append("    public static readonly (string Kind, string ModelId, string Hazard, string Member, string Detail, string Where)[] Sites =\n    {\n")
    rows = []
    for key in sorted(hazard):
        mdl = by_key[key]
        for h, mem, det, line in sorted(hazard[key], key=lambda r: (r[3], r[0], r[1], r[2])):
            rows.append(f'        ("{mdl.kind}", "{mdl.id}", "{h}", "{mem}", "{det}", "{mdl.file}:{line}"),')
    out.append("\n".join(rows) + "\n    };\n}\n")
    return "".join(out)


def main():
    ap = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    ap.add_argument("--check", action="store_true", help="exit 1 when PromptSites.g.cs is stale")
    ap.add_argument("--list", action="store_true", help="print the table")
    args = ap.parse_args()

    models, by_class, kernel_hits = load_models()
    # Run-layer kernel files may call the run-transition entry points (that is the run loop; MayPrompt gates those
    # paths by action kind); any other hazard call outside a model def must be a reviewed KERNEL_FILES entry.
    bad = [(rel, h, det) for rel, hits in sorted(kernel_hits.items()) for h, _, det in hits
           if rel not in KERNEL_FILES and not (h == "entry" and rel.startswith("Run/"))]
    for rel, h, det in bad:
        print(f"error: {rel}: {h} call {det} outside a model def and not in KERNEL_FILES", file=sys.stderr)
    if bad:
        return 1
    hazard = classify(models, by_class)
    rooms = room_sources(kernel_hits)
    checked, problems = cross_check(models, hazard)
    for p in problems:
        print("error: " + p, file=sys.stderr)
    if problems:
        return 1
    text = render(models, hazard, rooms)
    if args.list:
        by_key = {m.key: m for m in models}
        for key in sorted(hazard):
            mdl = by_key[key]
            trig = trigger_of(mdl.kind, hazard[key])
            for h, mem, det, line in hazard[key]:
                print(f"{mdl.kind:11} {mdl.id:32} {trig:8} {h:8} {mem:28} {det}  {mdl.file}:{line}")
        print(f"rooms: {sorted(rooms)}")
    counts = {}
    for key, sites in hazard.items():
        k = key.split(":")[0]
        counts[k] = counts.get(k, 0) + 1
    summary = f"prompt-site table: {len(hazard)} hazardous models of {len(models)} ({', '.join(f'{k} {v}' for k, v in sorted(counts.items()))}); " \
              f"{len(rooms)} room sources; inventory cross-check {checked} records ok"
    if args.check:
        try:
            with open(OUT, encoding="utf-8") as fh:
                current = fh.read()
        except FileNotFoundError:
            current = ""
        if current != text:
            print(f"error: {os.path.relpath(OUT, REPO_ROOT)} is stale; run python3 sim/tools/inventory/gen_prompt_sites.py", file=sys.stderr)
            return 1
        print(summary + "; PromptSites.g.cs up to date")
        return 0
    with open(OUT, "w", encoding="utf-8", newline="\n") as fh:
        fh.write(text)
    print(summary + f"; wrote {os.path.relpath(OUT, REPO_ROOT)}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
