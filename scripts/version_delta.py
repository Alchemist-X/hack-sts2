#!/usr/bin/env python3
"""Game-version delta: diff two decompile trees and locate the affected sim files, specs, tests and gates.

Usage:
  python3 scripts/version_delta.py <old_tree> <new_tree> [--out tmp/version-delta/<name>]
        [--old-version 0.107.1] [--new-version 0.111.0] [--max-diff-lines 160] [--no-diffs]

Both trees are ILSpy decompiles of sts2.dll. Two layouts are accepted and normalised to one key:
  flattened  MegaCrit.Sts2.Core.Models.Cards/Bash.cs   (output/server-bootstrap/linux-decompiled)
  nested     MegaCrit/sts2/Core/Models/Cards/Bash.cs   (artifacts/knowledge/decompiled-v0.107.1)
  key        Models.Cards/Bash.cs                      (the form sim/specs/*.md cite and sim/specs/inventory uses)

A file counts as changed only when its normalised text differs: comments removed, whitespace collapsed, spaces
around punctuation dropped. Files whose raw text differs but whose normalised text is equal are counted as
`cosmetic` and listed only in the manifest. A file present in one tree only under a different namespace but with
the same file name is paired as `moved`.

Each changed file is classified as content (cards, relics, potions, powers, monsters, encounters, events and the
other model kinds), kernel (Commands, Hooks, Combat/Entities state, Random, Map, Rewards, Rooms, Runs, Saves, ...)
or out_of_scope (Nodes, UI, VFX, audio, multiplayer transport, platform, dev tooling), then mapped to:
  * sim model ids (sim/specs/inventory/<kind>.json `file` -> `model_id`; fallback: class name -> slug),
  * sim/Sts2Sim.Core files (Defs/<Class>.cs, a sim file whose stem equals the decompiled class, sim files that cite
    the decompiled path in a comment),
  * spec sections: every `file:line[-line]` citation in sim/specs/**/*.md and docs/sim-port/*.md that resolves to the
    changed file, with the nearest markdown heading and a hit kind: `line` (the cited range overlaps a changed
    line, when the cite tree is one of the compared trees), `member` (the cited lines lie in a member that changed;
    members are resolved in --cite-tree, so citations of the Linux rule-truth tree still resolve when the compared
    trees were decompiled differently), `added` / `removed`, else `file`,
  * tests: Sts2Sim.Tests methods mentioning the model id, the class name or a mapped sim file's class,
then a rerun plan is derived (test filters, content soak kinds, G0 / G3, registry version filter, knowledge export).

Outputs <out>/report.md and <out>/manifest.json (schema sts2sim.version-delta/v1). Stdlib only; no uv.
"""

import argparse
import collections
import difflib
import json
import os
import re
import sys
import time

SCHEMA = "sts2sim.version-delta/v1"
REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
DEFAULT_CITE_TREE = os.environ.get(
    "STS2_DECOMPILE_ROOT", "/Users/Aincrad/dev-proj/hack-sts2/output/server-bootstrap/linux-decompiled")

# ---------------------------------------------------------------------------------------------------------------
# Tree layout -> canonical key


def canonical_key(relpath):
    """Map a path relative to a decompile root to the namespace key used by specs and the inventory."""
    parts = relpath.replace(os.sep, "/").split("/")
    fname = parts[-1]
    ns = ".".join(p for p in parts[:-1] if p)
    low = ns.lower()
    for prefix in ("megacrit.sts2.core.", "megacrit.sts2."):
        if low.startswith(prefix):
            ns = ns[len(prefix):]
            break
    else:
        if low in ("megacrit.sts2.core", "megacrit.sts2"):
            ns = ""
    return (ns + "/" + fname) if ns else fname


def namespace_of(key):
    return key.rsplit("/", 1)[0] if "/" in key else ""


def class_of(key):
    return key.rsplit("/", 1)[-1][:-3] if key.endswith(".cs") else key.rsplit("/", 1)[-1]


def index_tree(root):
    """key -> absolute path, for every .cs file under root."""
    out = {}
    for dp, dns, fns in os.walk(root):
        dns.sort()
        for fn in fns:
            if fn.endswith(".cs"):
                ab = os.path.join(dp, fn)
                key = canonical_key(os.path.relpath(ab, root))
                out.setdefault(key, ab)
    return out


# ---------------------------------------------------------------------------------------------------------------
# Normaliser


def strip_comments(text):
    """Remove // and /* */ comments; keep string and char literals verbatim and keep every newline."""
    out = []
    i, n = 0, len(text)
    while i < n:
        c = text[i]
        nxt = text[i + 1] if i + 1 < n else ""
        if c == "/" and nxt == "/":
            j = text.find("\n", i)
            i = n if j < 0 else j
            continue
        if c == "/" and nxt == "*":
            j = text.find("*/", i + 2)
            end = n if j < 0 else j + 2
            out.append("\n" * text.count("\n", i, end))
            i = end
            continue
        if c == '"' or (c in "$@" and nxt in '"$@'):
            i = _copy_string(text, i, out)
            continue
        if c == "'":
            j = i + 1
            while j < n and text[j] != "'" and text[j] != "\n":
                j += 2 if text[j] == "\\" else 1
            out.append(text[i:j + 1])
            i = j + 1
            continue
        out.append(c)
        i += 1
    return "".join(out)


def _copy_string(text, i, out):
    """Copy one C# string literal starting at i (with optional $ / @ prefixes); return the index after it."""
    n = len(text)
    j = i
    interp = verbatim = False
    while j < n and text[j] in "$@":
        interp |= text[j] == "$"
        verbatim |= text[j] == "@"
        j += 1
    if j >= n or text[j] != '"':  # a lone $ or @ (identifier escape), not a string
        out.append(text[i:j])
        return j
    if text.startswith('"""', j):  # raw string literal
        k = text.find('"""', j + 3)
        end = n if k < 0 else k + 3
        out.append(text[i:end])
        return end
    j += 1
    depth = 0
    while j < n:
        ch = text[j]
        if interp and ch == "{":
            if text.startswith("{{", j) and depth == 0:
                j += 2
                continue
            depth += 1
        elif interp and ch == "}" and depth > 0:
            depth -= 1
        elif depth > 0 and ch == '"':
            sub = []
            j = _copy_string(text, j, sub)
            continue
        elif depth == 0 and ch == "\\" and not verbatim:
            j += 2
            continue
        elif depth == 0 and ch == '"':
            if verbatim and text.startswith('""', j):
                j += 2
                continue
            j += 1
            break
        elif depth == 0 and ch == "\n" and not verbatim:
            break
        j += 1
    out.append(text[i:j])
    return j


_WS = re.compile(r"\s+")
_PUNCT_SP = re.compile(r" ?([^\w\s]) ?")
_USING = re.compile(r"^(?:global )?using (?:static )?[\w.]+(?: ?= ?[\w.<>, ]+)?;$")


def normalise_lines(text):
    """[(original line number, normalised line)] for every line that is non-empty after comment stripping.

    `using` directives are dropped too: ILSpy emits them by need, and they never change a rule by themselves."""
    res = []
    for no, line in enumerate(strip_comments(text).split("\n"), 1):
        s = _WS.sub(" ", line).strip()
        if s and not _USING.match(s):
            res.append((no, s))
    return res


def signature(lines):
    """Whitespace-insensitive signature of normalised lines (line breaks and spaces around punctuation ignored)."""
    return _PUNCT_SP.sub(r"\1", " ".join(s for _, s in lines))


def read_text(path):
    with open(path, encoding="utf-8-sig", errors="replace") as fh:
        return fh.read()


_PTOK = re.compile(r'"(?:\\.|[^"\\])*"|\w+|[^\w\s]')


def _mask_class(tokens, cls):
    if not cls:
        return tokens
    low = cls[:1].lower() + cls[1:]
    return ["<C>" if t == cls else "<c>" if t == low else t for t in tokens]


def edit_patterns(a, b, opcodes, cls=None):
    """Edit fingerprints of one file, independent of the class name, so one API refactor applied to N files is
    recognisable: a 1:1 replaced line yields (left context token, old token span, new token span) per token-level
    change; other inserted / deleted lines yield the masked line."""
    pats = set()
    for tag, i1, i2, j1, j2 in opcodes:
        if tag == "equal":
            continue
        if tag == "replace" and i2 - i1 == j2 - j1:
            for k in range(i2 - i1):
                ta = _mask_class(_PTOK.findall(a[i1 + k]), cls)
                tb = _mask_class(_PTOK.findall(b[j1 + k]), cls)
                for t2, x1, x2, y1, y2 in difflib.SequenceMatcher(None, ta, tb, autojunk=False).get_opcodes():
                    if t2 != "equal":
                        ctx = " ".join(ta[max(0, x1 - 2):x1])
                        pats.add(("~", ctx, " ".join(ta[x1:x2]), " ".join(tb[y1:y2])))
            continue
        for k in range(i1, i2):
            pats.add(("-", " ".join(_mask_class(_PTOK.findall(a[k]), cls))))
        for k in range(j1, j2):
            pats.add(("+", " ".join(_mask_class(_PTOK.findall(b[k]), cls))))
    return pats


def render_pattern(p):
    if p[0] == "~":
        return "`%s %s` -> `%s %s`" % (p[1], p[2] or "", p[1], p[3] or "")
    return "%s `%s`" % (p[0], p[1][:160])


def line_diff(old_lines, new_lines, context=3, cls=None):
    """Unified diff over normalised lines, headers carrying ORIGINAL line numbers.

    Returns (hunks_text_lines, changed_old_lines, changed_new_lines, edit_patterns)."""
    a = [s for _, s in old_lines]
    b = [s for _, s in new_lines]
    sm = difflib.SequenceMatcher(None, a, b, autojunk=len(a) > 2000 or len(b) > 2000)
    text = []
    ch_old, ch_new = set(), set()
    opcodes = sm.get_opcodes()
    pats = edit_patterns(a, b, opcodes, cls)
    for tag, i1, i2, j1, j2 in opcodes:
        if tag == "equal":
            continue
        ch_old.update(old_lines[k][0] for k in range(i1, i2))
        ch_new.update(new_lines[k][0] for k in range(j1, j2))
        if tag == "insert" and i1 > 0:  # mark the neighbour so a pure insertion still hits a citation range
            ch_old.add(old_lines[i1 - 1][0])
        if tag == "delete" and j1 > 0:
            ch_new.add(new_lines[j1 - 1][0])
    for group in sm.get_grouped_opcodes(context):
        i1, i2, j1, j2 = group[0][1], group[-1][2], group[0][3], group[-1][4]
        ol = old_lines[i1][0] if i1 < len(old_lines) else (old_lines[-1][0] if old_lines else 0)
        nl = new_lines[j1][0] if j1 < len(new_lines) else (new_lines[-1][0] if new_lines else 0)
        text.append("@@ -%d,%d +%d,%d @@" % (ol, i2 - i1, nl, j2 - j1))
        for tag, a1, a2, b1, b2 in group:
            if tag == "equal":
                text.extend("  %5d  %s" % (new_lines[b1 + k][0], b[b1 + k]) for k in range(b2 - b1))
                continue
            text.extend("- %5d  %s" % (old_lines[k][0], a[k]) for k in range(a1, a2))
            text.extend("+ %5d  %s" % (new_lines[k][0], b[k]) for k in range(b1, b2))
    return text, ch_old, ch_new, pats


# ---------------------------------------------------------------------------------------------------------------
# Classification

# kind, namespace prefix (first match wins; longer prefixes first)
CONTENT_NS = [
    ("cards", "Models.Cards"), ("relics", "Models.Relics"), ("potions", "Models.Potions"),
    ("powers", "Models.Powers"), ("monsters", "Models.Monsters"), ("encounters", "Models.Encounters"),
    ("events", "Models.Events"), ("enchantments", "Models.Enchantments"), ("afflictions", "Models.Afflictions"),
    ("orbs", "Models.Orbs"), ("modifiers", "Models.Modifiers"), ("acts", "Models.Acts"),
    ("characters", "Models.Characters"), ("card_pools", "Models.CardPools"), ("relic_pools", "Models.RelicPools"),
    ("potion_pools", "Models.PotionPools"),
]
# area, namespace prefix, sim directory hint
KERNEL_NS = [
    ("commands", "Commands", "sim/Sts2Sim.Core/Combat/Pipeline"),
    ("hooks", "Hooks", "sim/Sts2Sim.Core/Combat/Pipeline"),
    ("combat", "Combat", "sim/Sts2Sim.Core/Combat"),
    ("state", "Entities", "sim/Sts2Sim.Core/State"),
    ("models_base", "Models", "sim/Sts2Sim.Core"),  # CardModel.cs, ModelDb.cs, AbstractModel.cs ... (exact ns only)
    ("monster_moves", "MonsterMoves", "sim/Sts2Sim.Core/Monsters"),
    ("dynamic_vars", "Localization.DynamicVars", "sim/Sts2Sim.Core/Cards/DynamicVars"),
    ("value_props", "ValueProps", "sim/Sts2Sim.Core/Enums"),
    ("rng", "Random", "sim/Sts2Sim.Core/Random"),
    ("rng", "Entities.Rngs", "sim/Sts2Sim.Core/Random"),
    ("map", "Map", "sim/Sts2Sim.Core/Run/Map"),
    ("rewards", "Rewards", "sim/Sts2Sim.Core/Run/Rewards"),
    ("rooms", "Rooms", "sim/Sts2Sim.Core/Run/Rooms"),
    ("runs", "Runs", "sim/Sts2Sim.Core/Run"),
    ("odds", "Odds", "sim/Sts2Sim.Core/Run/Odds"),
    ("factories", "Factories", "sim/Sts2Sim.Core/Run/Factories"),
    ("save", "Saves", "sim/Sts2Sim.Core/Run/Save"),
    ("game_actions", "GameActions", "sim/Sts2Sim.Core/Combat/Loop"),  # incl. .Multiplayer (PlayerChoiceContext)
    ("card_selection", "CardSelection", "sim/Sts2Sim.Core/Choice"),
    ("events_kernel", "Events", "sim/Sts2Sim.Core/Run/Events"),
    ("unlocks", "Unlocks", "sim/Sts2Sim.Core/Unlocks"),
    ("unlocks", "Timeline", "sim/Sts2Sim.Core/Unlocks"),
    ("context", "Context", "sim/Sts2Sim.Core"),
    ("helpers", "Helpers", "sim/Sts2Sim.Core"),
    ("helpers", "Extensions", "sim/Sts2Sim.Core"),
]
# out-of-scope prefixes (UI / VFX / Nodes / Net / platform / tooling); matched before the kernel table for these
OUT_NS = [
    "Nodes", "Multiplayer", "Entities.Multiplayer", "Platform", "Audio", "Animation",
    "Assets", "Bindings", "ControllerInput", "DevConsole", "Debug", "Achievements", "Models.Achievements",
    "Models.Badges", "Leaderboard", "Settings", "Logging", "Modding", "TestSupport", "AutoSlay", "HoverTips",
    "RichTextTags", "TextEffects", "Localization", "Daily", "Exceptions", "Models.Exceptions", "Entities.UI",
    "Entities.Text", "Combat.History", "Runs.Metrics", "Runs.History", "Saves.Managers", "Saves.MapDrawing",
    "Saves.Test", "GameInfo", "SourceGeneration", "addons", "GodotPlugins", "System", "Properties",
    "RiderTestRunner", "Nodes.TopBar", "Models.Singleton",
]
# kernel areas whose change alters seeding / save layout: G3 + save import must rerun, sim may need dual impl
SEED_SAVE_AREAS = {"rng", "save"}
COMBAT_AREAS = {"commands", "hooks", "combat", "state", "models_base", "monster_moves", "dynamic_vars",
                "value_props", "rng", "game_actions", "card_selection", "helpers", "context"}
RUN_AREAS = {"map", "rewards", "rooms", "runs", "odds", "factories", "save", "events_kernel", "unlocks", "rng",
             "state", "models_base"}
COMBAT_CONTENT = {"cards", "relics", "potions", "powers", "monsters", "encounters", "enchantments",
                  "afflictions", "orbs", "characters"}
RUN_CONTENT = {"events", "acts", "card_pools", "relic_pools", "potion_pools", "modifiers", "characters", "relics",
               "potions", "encounters"}
# content soak test per content kind (sim/Sts2Sim.Tests/ContentSoakTests.cs)
SOAK = {
    "cards": ["Test_ContentSoak_EveryCard"], "relics": ["Test_ContentSoak_EveryRelic"],
    "potions": ["Test_ContentSoak_EveryPotion"], "monsters": ["Test_ContentSoak_EveryEncounter"],
    "encounters": ["Test_ContentSoak_EveryEncounter"],
    "powers": ["Test_ContentSoak_EveryCard", "Test_ContentSoak_EveryEncounter"],
    "enchantments": ["Test_ContentSoak_EveryCard"], "afflictions": ["Test_ContentSoak_EveryCard"],
    "orbs": ["Test_ContentSoak_EveryCard"], "card_pools": ["Test_ContentSoak_EveryCard"],
    "relic_pools": ["Test_ContentSoak_EveryRelic"], "potion_pools": ["Test_ContentSoak_EveryPotion"],
}
# content kinds whose sim port is table data, not one Defs/<Class>.cs per model
CONTENT_SIM_HINT = {
    "card_pools": ["sim/Sts2Sim.Core/Cards/Pools/CardPools.Data.cs", "sim/Sts2Sim.Core/Cards/Pools/CardCatalog.Data.cs"],
    "relic_pools": ["sim/Sts2Sim.Core/Relics/RelicPools.cs", "sim/Sts2Sim.Core/Run/Relics/RelicCatalog.Data.cs"],
    "potion_pools": ["sim/Sts2Sim.Core/Potions/PotionPools.cs"],
    "modifiers": ["sim/Sts2Sim.Core/Modifiers/ModifierModel.cs"],
}
RUN_SOAK_TEST = "Test_RunSoak_RandomRuns"  # RunLoopTests: 10^4 random runs x 5 characters
INVENTORY_KINDS = ["cards", "relics", "potions", "powers", "monsters", "encounters", "events", "enchantments",
                   "afflictions", "orbs", "modifiers", "acts", "characters"]
CATEGORY = {"cards": "CardModel", "relics": "RelicModel", "potions": "PotionModel", "powers": "PowerModel",
            "monsters": "MonsterModel", "encounters": "EncounterModel", "events": "EventModel",
            "enchantments": "EnchantmentModel", "afflictions": "AfflictionModel", "orbs": "OrbModel",
            "modifiers": "ModifierModel", "acts": "ActModel", "characters": "CharacterModel",
            "card_pools": "CardPoolModel", "relic_pools": "RelicPoolModel", "potion_pools": "PotionPoolModel"}


def _ns_match(ns, prefix):
    return ns == prefix or ns.startswith(prefix + ".")


def classify(key):
    """-> (scope, kind_or_area, sim_dir_hint). scope in content | kernel | out_of_scope | other."""
    ns = namespace_of(key)
    for kind, prefix in CONTENT_NS:
        if _ns_match(ns, prefix):
            if ".Mock" in ns[len(prefix):] or ns[len(prefix):].startswith(".Mock"):
                return ("out_of_scope", "test_mocks", None)
            return ("content", kind, None)
    for prefix in OUT_NS:
        if _ns_match(ns, prefix) and not (prefix == "Localization" and _ns_match(ns, "Localization.DynamicVars")):
            return ("out_of_scope", prefix.lower(), None)
    for area, prefix, hint in sorted(KERNEL_NS, key=lambda r: -len(r[1])):  # most specific prefix first
        if prefix == "Models":
            if ns == "Models":
                return ("kernel", area, hint)
            continue
        if _ns_match(ns, prefix):
            return ("kernel", area, hint)
    if not ns:
        return ("out_of_scope", "compiler_generated", None)
    return ("other", ns.split(".")[0].lower(), None)


# ---------------------------------------------------------------------------------------------------------------
# Repo indexes (inventory, sim files, specs, tests)

_CITE = re.compile(r"(?<![\w./-])((?:[A-Za-z_][\w.\-]*/)*[A-Za-z_][\w.\-]*\.cs):(\d+)(?:\s*[-–]\s*(\d+))?")
_HEADING = re.compile(r"^(#{1,6})\s+(.*\S)\s*$")


def load_inventory(repo):
    """file key -> list of {kind, model_id, id, class}; class -> same (for added files and renamed paths)."""
    by_file = collections.defaultdict(list)
    by_class = collections.defaultdict(list)
    inv = os.path.join(repo, "sim", "specs", "inventory")
    for kind in INVENTORY_KINDS:
        path = os.path.join(inv, kind + ".json")
        if not os.path.exists(path):
            continue
        with open(path, encoding="utf-8") as fh:
            data = json.load(fh)
        for e in data.get("entries", []):
            if not isinstance(e, dict) or not e.get("file"):
                continue
            rec = {"kind": kind, "model_id": e.get("model_id"), "id": e.get("id"), "class": e.get("class"),
                   "is_mock": bool(e.get("is_mock")), "registered": e.get("registered")}
            by_file[e["file"]].append(rec)
            if e.get("class"):
                by_class[(kind, e["class"])].append(rec)
    return by_file, by_class


def slug_model_id(kind, cls):
    """CATEGORY.SLUG via sim/tools/inventory/model_ids.py (the .NET Slugify port); a regex fallback otherwise."""
    cat = CATEGORY.get(kind)
    if not cat:
        return None
    try:
        sys.path.insert(0, os.path.join(REPO, "sim", "tools", "inventory"))
        import model_ids  # noqa: E402
        return model_ids.model_id(cat, cls)
    except Exception:  # pragma: no cover - fallback only
        def slug(t):
            return re.sub(r"(?<=[a-z0-9])(?=[A-Z])|(?<=[A-Z])(?=[A-Z][a-z])", "_", t).upper()
        return "%s.%s" % (slug(cat[:-5]) if cat.endswith("Model") else slug(cat), slug(cls))


def index_sim(repo):
    """Sim Core file index: stem -> [paths]; decompile-citation key -> [paths]; Defs class -> path."""
    core = os.path.join(repo, "sim", "Sts2Sim.Core")
    by_stem = collections.defaultdict(list)
    defs = collections.defaultdict(list)
    cited = collections.defaultdict(set)
    for dp, dns, fns in os.walk(core):
        dns[:] = sorted(d for d in dns if d not in ("bin", "obj"))
        for fn in sorted(fns):
            if not fn.endswith(".cs"):
                continue
            ab = os.path.join(dp, fn)
            rel = os.path.relpath(ab, repo).replace(os.sep, "/")
            stem = fn.split(".")[0]
            by_stem[stem].append(rel)
            if "/Defs/" in rel or "/Encounters/" in rel or "/Ancients/" in rel or "/Acts/" in rel \
                    or "/Characters/" in rel:
                defs[stem].append(rel)
            for m in _CITE.finditer(read_text(ab)):
                cited[normalise_citation(m.group(1))].add(rel)
    return by_stem, defs, cited


def normalise_citation(path):
    """`Models/Potions/FairyInABottle.cs`, `Models.Cards/Bash.cs`, `.../decompiled-x/MegaCrit.Sts2.Core.Map/X.cs`,
    `.../MegaCrit/sts2/Core/Map/X.cs` -> key form."""
    parts = path.replace("\\", "/").split("/")
    for i, p in enumerate(parts[:-1]):
        if p.lower().startswith("megacrit"):
            parts = parts[i:]
            break
    return canonical_key("/".join(parts))


def index_specs(repo):
    """[{doc, md_line, section, cite, key, lo, hi}] for every file:line citation in specs and sim-port docs."""
    roots = [os.path.join(repo, "sim", "specs"), os.path.join(repo, "docs", "sim-port")]
    out = []
    for root in roots:
        for dp, dns, fns in os.walk(root):
            dns.sort()
            for fn in sorted(fns):
                if not fn.endswith(".md"):
                    continue
                ab = os.path.join(dp, fn)
                rel = os.path.relpath(ab, repo).replace(os.sep, "/")
                section = ""
                for no, line in enumerate(read_text(ab).split("\n"), 1):
                    h = _HEADING.match(line)
                    if h:
                        section = h.group(2)
                        continue
                    for m in _CITE.finditer(line):
                        lo = int(m.group(2))
                        hi = int(m.group(3)) if m.group(3) else lo
                        if hi < lo:
                            hi = lo
                        out.append({"doc": rel, "md_line": no, "section": section, "cite": m.group(0),
                                    "key": normalise_citation(m.group(1)), "lo": lo, "hi": hi})
    return out


_TEST_METHOD = re.compile(r"\bstatic\s+(?:async\s+)?(?:void|Task)\s+(Test_\w+)\s*\(")
_TEST_CLASS = re.compile(r"\bpublic\s+(?:static\s+)?(?:sealed\s+)?(?:partial\s+)?class\s+(\w+)")
_TOKEN = re.compile(r"[A-Za-z_][A-Za-z0-9_]*")


def index_tests(repo):
    """token -> set of 'Class.Method' (or 'Class.*' for class-level code before the first test method)."""
    tdir = os.path.join(repo, "sim", "Sts2Sim.Tests")
    idx = collections.defaultdict(set)
    all_tests = set()
    if not os.path.isdir(tdir):
        return idx, all_tests
    for dp, dns, fns in os.walk(tdir):
        dns[:] = sorted(d for d in dns if d not in ("bin", "obj"))
        for fn in sorted(fns):
            if not fn.endswith(".cs"):
                continue
            text = read_text(os.path.join(dp, fn))
            marks = [(m.start(), "class", m.group(1)) for m in _TEST_CLASS.finditer(text)]
            marks += [(m.start(), "method", m.group(1)) for m in _TEST_METHOD.finditer(text)]
            marks.sort()
            bounds = [p for p, _, _ in marks] + [len(text)]
            cls, owner = fn.split(".")[0], None
            spans = [(0, bounds[0] if marks else len(text), cls + ".*")]
            for k, (pos, what, name) in enumerate(marks):
                if what == "class":
                    cls = name
                    owner = cls + ".*"
                else:
                    owner = cls + "." + name
                    all_tests.add(owner)
                spans.append((pos, bounds[k + 1], owner))
            for lo, hi, owner in spans:
                if owner is None:
                    continue
                for tok in set(_TOKEN.findall(text, lo, hi)):
                    idx[tok].add(owner)
    return idx, all_tests


# ---------------------------------------------------------------------------------------------------------------
# Core pipeline


def compare_trees(old_root, new_root, max_diff_lines=160, want_diffs=True):
    old_idx = index_tree(old_root)
    new_idx = index_tree(new_root)
    keys_old, keys_new = set(old_idx), set(new_idx)
    only_old = sorted(keys_old - keys_new)
    only_new = sorted(keys_new - keys_old)
    # pair moves: same file name, one candidate on each side
    by_name_old = collections.defaultdict(list)
    by_name_new = collections.defaultdict(list)
    for k in only_old:
        by_name_old[k.rsplit("/", 1)[-1]].append(k)
    for k in only_new:
        by_name_new[k.rsplit("/", 1)[-1]].append(k)
    pairs = [(k, k) for k in sorted(keys_old & keys_new)]
    moved = set()
    for name, olds in by_name_old.items():
        news = by_name_new.get(name, [])
        if len(olds) == 1 and len(news) == 1:
            pairs.append((olds[0], news[0]))
            moved.add(olds[0])
            moved.add(news[0])
    files = []
    stats = collections.Counter()
    style = {"old": collections.Counter(), "new": collections.Counter()}

    def note_style(side, text):
        style[side]["files"] += 1
        if "IAsyncStateMachine" in text:
            style[side]["async_state_machines"] += 1
        if re.search(r"\basync\s+Task\b", text):
            style[side]["async_methods"] += 1

    for ko, kn in pairs:
        to, tn = read_text(old_idx[ko]), read_text(new_idx[kn])
        note_style("old", to)
        note_style("new", tn)
        if to == tn:
            stats["identical"] += 1
            continue
        lo, ln = normalise_lines(to), normalise_lines(tn)
        if signature(lo) == signature(ln):
            stats["cosmetic"] += 1
            files.append({"key": kn, "old_key": ko, "delta": "cosmetic"})
            continue
        diff, ch_old, ch_new, pats = line_diff(lo, ln, cls=class_of(kn))
        rec = {"key": kn, "old_key": ko, "delta": "moved" if ko != kn else "modified",
               "old_path": old_idx[ko], "new_path": new_idx[kn], "_patterns": pats,
               "changed_lines_old": sorted(ch_old), "changed_lines_new": sorted(ch_new),
               "plus": sum(1 for d in diff if d.startswith("+")),
               "minus": sum(1 for d in diff if d.startswith("-"))}
        if want_diffs:
            rec["diff"] = diff[:max_diff_lines]
            rec["diff_truncated"] = len(diff) > max_diff_lines
        stats[rec["delta"]] += 1
        files.append(rec)
    for k in only_old:
        if k not in moved:
            stats["removed"] += 1
            text = read_text(old_idx[k])
            note_style("old", text)
            files.append({"key": k, "old_key": k, "delta": "removed", "old_path": old_idx[k],
                          "lines_old": len(normalise_lines(text))})
    for k in only_new:
        if k not in moved:
            stats["added"] += 1
            rec = {"key": k, "old_key": None, "delta": "added", "new_path": new_idx[k]}
            text = read_text(new_idx[k])
            note_style("new", text)
            body = normalise_lines(text)
            rec["lines_new"] = len(body)
            if want_diffs:
                d = ["+ %5d  %s" % (no, s) for no, s in body]
                rec["diff"] = d[:max_diff_lines]
                rec["diff_truncated"] = len(d) > max_diff_lines
            files.append(rec)
    return files, stats, len(old_idx), len(new_idx), style_verdict(style)


BULK_MIN_FILES = 8
_STRUCTURAL = re.compile(r"^(?:[{}()\[\];,] ?)*$|^(?:else|get|set|try|finally|do)$")


def is_structural(p):
    """Brace / keyword-only lines that come and go with control-flow restructuring; never a bulk pattern."""
    return p[0] in "+-" and bool(_STRUCTURAL.match(p[1]))


def mark_bulk(files, min_files=BULK_MIN_FILES):
    """Edit patterns shared by >= min_files in-scope files are bulk refactors (an API signature change applied
    everywhere). A file whose every pattern is bulk gets `bulk_only` true: porting it is the same mechanical
    edit as its peers, not a separate rule change. Returns the bulk pattern table."""
    freq = collections.Counter()
    owners = collections.defaultdict(list)
    for f in files:
        pats = f.get("_patterns")
        if pats is None or classify(f["key"])[0] == "out_of_scope":
            continue
        for p in pats:
            freq[p] += 1
            owners[p].append(f["key"])
    table = []
    ids = {}
    for p, n in sorted(freq.items(), key=lambda kv: (-kv[1], kv[0])):
        if n < min_files:
            break
        if is_structural(p):
            continue
        ids[p] = "B%d" % (len(table) + 1)
        table.append({"id": ids[p], "files": n, "pattern": render_pattern(p), "examples": owners[p][:4]})
    for f in files:
        pats = f.pop("_patterns", None)
        if pats is None:
            continue
        real = [p for p in pats if not is_structural(p)] or list(pats)
        f["bulk_patterns"] = sorted({ids[p] for p in real if p in ids}, key=lambda i: int(i[1:]))
        f["unique_edits"] = sum(1 for p in real if p not in ids)
        f["bulk_only"] = bool(real) and f["unique_edits"] == 0
    return {"min_files": min_files, "patterns": table}


def style_verdict(style):
    """Flag a decompiler-settings mismatch: one tree shows async methods as raw IAsyncStateMachine structs (ILSpy
    without async/await decompilation), the other as `async Task` bodies. Such a pair diffs on nearly every file."""
    o, n = style["old"], style["new"]

    def ratio(c):
        return c["async_state_machines"] / max(1, c["async_methods"] + c["async_state_machines"])

    ro, rn = ratio(o), ratio(n)
    mismatch = abs(ro - rn) > 0.5 and max(o["async_state_machines"], n["async_state_machines"]) >= 50
    return {"old": dict(o), "new": dict(n), "old_state_machine_share": round(ro, 3),
            "new_state_machine_share": round(rn, 3), "mismatch": mismatch}


def enrich(files, repo, cite_tree=None, old_tree=None, new_tree=None):
    by_file, by_class = load_inventory(repo)
    cite_ctx = {"tree": cite_tree, "index": index_tree(cite_tree) if cite_tree else {}, "spans": {},
                "same": ("new" if _same(cite_tree, new_tree) else "old" if _same(cite_tree, old_tree) else None)}
    sim_stem, sim_defs, sim_cited = index_sim(repo)
    cites = index_specs(repo)
    test_idx, all_tests = index_tests(repo)
    cites_by_key = collections.defaultdict(list)
    cites_by_name = collections.defaultdict(list)
    for c in cites:
        cites_by_key[c["key"]].append(c)
        if "/" not in c["key"]:
            cites_by_name[c["key"]].append(c)
    for f in files:
        scope, kind, hint = classify(f["key"])
        f["scope"], f["kind"] = scope, kind
        if f["delta"] == "cosmetic":
            continue
        cls = class_of(f["key"])
        f["class"] = cls
        models = list(by_file.get(f["key"], [])) or list(by_file.get(f.get("old_key") or "", []))
        if not models and scope == "content":
            models = list(by_class.get((kind, cls), []))
        if not models and scope == "content" and kind in CATEGORY:
            models = [{"kind": kind, "model_id": slug_model_id(kind, cls), "id": None, "class": cls,
                       "is_mock": False, "registered": None, "source": "slug"}]
        f["models"] = [{k: m.get(k) for k in ("kind", "model_id", "class", "is_mock")} for m in models]
        sims = set()
        if scope == "content":
            sims.update(sim_defs.get(cls, []))
        elif scope in ("kernel", "other"):
            sims.update(sim_stem.get(cls, []))
        for k in (f["key"], f.get("old_key")):
            if k:
                sims.update(sim_cited.get(k, ()))
        if scope == "content" and not sims and kind in CONTENT_SIM_HINT:
            hint = CONTENT_SIM_HINT[kind]
            sims.update(p for p in hint if os.path.exists(os.path.join(repo, p)))
        f["sim_files"] = sorted(sims)
        f["sim_dir_hint"] = hint if isinstance(hint, str) or hint is None else None
        # changed members (decompiler-style independent: state machines map back to their source method)
        spans_old = member_spans(f.get("old_path")) if f["delta"] in ("modified", "moved", "removed") else []
        spans_new = member_spans(f.get("new_path")) if f["delta"] in ("modified", "moved", "added") else []
        if f["delta"] in ("modified", "moved"):
            members = owners_of_lines(spans_old, f.get("changed_lines_old", ())) | owners_of_lines(
                spans_new, f.get("changed_lines_new", ()))
        else:
            members = {o for _, _, o in (spans_new or spans_old)}
        f["changed_members"] = sorted(members)
        f["spec_cites"] = match_citations(f, cites_by_key, cites_by_name, cite_ctx, members, spans_old, spans_new)
        # tests
        tokens = {cls}
        for m in models:
            if m.get("model_id"):
                tokens.add(m["model_id"].split(".", 1)[-1])
        for s in f["sim_files"]:
            tokens.add(os.path.basename(s).split(".")[0])
        tests = set()
        for t in tokens:
            if len(t) >= 4:
                tests.update(test_idx.get(t, ()))
        f["tests"] = sorted(tests)
    return {"spec_citations": len(cites), "test_methods": len(all_tests), "cite_tree": cite_tree,
            "cite_tree_is": cite_ctx["same"] or ("separate" if cite_tree else None)}


def _same(a, b):
    return bool(a and b) and os.path.realpath(a) == os.path.realpath(b)


_CS = []


def _cs_source():
    if not _CS:
        try:
            sys.path.insert(0, os.path.join(REPO, "sim", "tools", "inventory"))
            import cs_source  # noqa: E402
            _CS.append(cs_source)
        except Exception:  # pragma: no cover
            _CS.append(None)
    return _CS[0]


def member_spans(path):
    """[(first line, last line, 'Type.Member')] of every top-level member; compiler-generated state machines,
    lambdas and local functions are named after the source method that owns them (cs_source.generated_owner)."""
    cs = _cs_source()
    if cs is None or not path or not os.path.exists(path):
        return []
    try:
        cf = cs.CsFile(path, os.path.basename(path))
    except Exception:
        return []
    out = []
    for td in cf.types:
        machine_of = {}  # state-machine type -> the method whose [AsyncStateMachine(typeof(X))] names it
        for m in td.members:
            if m.kind in ("method", "ctor"):
                for x in _SM_ATTR.findall(cf.masked[m.start:m.decl_start]):
                    machine_of[x] = _member_id(cs, m)
        for m in td.members:
            if m.kind in ("method", "ctor"):
                owner = _member_id(cs, m)
            else:
                owner = machine_of.get(m.name) or cs.generated_owner(m.name) or m.name or "?"
            out.append((m.attr_line or m.line, m.end_line, "%s.%s" % (td.name, owner)))
    return out


_SM_ATTR = re.compile(r"(?:Async|Iterator)StateMachine\s*\(\s*typeof\s*\(\s*(\w+)")
_QUAL = re.compile(r"\b(?:global::)?(?:[A-Za-z_]\w*\.)+(?=[A-Za-z_])")


def param_signature(params, split=None):
    """`PlayerChoiceContext choiceContext, System.Decimal amount, bool silent = false` -> 'PlayerChoiceContext,
    Decimal,bool' (names, defaults, attributes and namespace qualifiers dropped: decompile styles differ there)."""
    parts = split(params) if split else [p for p in params.split(",")]
    sig = []
    for p in parts:
        p = re.sub(r"\[[^\]]*\]", "", p).split("=", 1)[0].strip()
        if not p:
            continue
        words = p.split()
        words = [w for w in words if w not in ("this", "ref", "out", "in", "params", "scoped")]
        typ = " ".join(words[:-1]) if len(words) > 1 else words[0] if words else ""
        sig.append(_QUAL.sub("", typ).replace(" ", ""))
    return ",".join(sig)


def _member_id(cs, m):
    return "%s(%s)" % (m.name, param_signature(m.params or "", getattr(cs, "split_args", None)))


def _base(member):
    return member.split("(", 1)[0]


def members_overlap(cited, changed):
    """True when a cited member is a changed member. Overloads are told apart by parameter types when both sides
    carry them; a member known only by name (a state machine without its attribute) matches by name."""
    for a in cited:
        for b in changed:
            if a == b or (_base(a) == _base(b) and ("(" not in a or "(" not in b)):
                return True
    return False


HEADER = "(header)"


def owners_of_range(spans, lo, hi):
    hit = {o for a, b, o in spans if a <= hi and lo <= b}
    return hit or {HEADER}


def owners_of_lines(spans, lines):
    res = set()
    for ln in lines:
        res |= owners_of_range(spans, ln, ln)
    return res


def match_citations(f, cites_by_key, cites_by_name, ctx, members, spans_old, spans_new):
    """Spec citations of a changed file. hit: line (cited range overlaps a changed line in the cited tree),
    member (the cited lines lie in a changed member), added / removed (the file exists in one tree only),
    file (the file changed elsewhere)."""
    hits = collections.OrderedDict()
    keys = [k for k in (f["key"], f.get("old_key")) if k]
    for key in dict.fromkeys(keys):
        for c in cites_by_key.get(key, []) + cites_by_name.get(key.rsplit("/", 1)[-1], []):
            ident = (c["doc"], c["md_line"], c["cite"])
            if ident in hits:
                continue
            if f["delta"] in ("added", "removed"):
                hit, cited = f["delta"], []
            else:
                if ctx["same"] == "new":
                    spans, changed = spans_new, f.get("changed_lines_new", ())
                elif ctx["same"] == "old":
                    spans, changed = spans_old, f.get("changed_lines_old", ())
                else:
                    spans, changed = _cite_spans(ctx, keys), ()
                cited = sorted(owners_of_range(spans, c["lo"], c["hi"])) if spans else []
                if any(c["lo"] <= ln <= c["hi"] for ln in changed):
                    hit = "line"
                elif cited and members_overlap(cited, members):
                    hit = "member"
                else:
                    hit = "file"
            hits[ident] = {"doc": c["doc"], "md_line": c["md_line"], "section": c["section"], "cite": c["cite"],
                           "hit": hit, "cited_members": cited}
    return list(hits.values())


def _cite_spans(ctx, keys):
    for k in keys:
        if k in ctx["spans"]:
            return ctx["spans"][k]
        path = ctx["index"].get(k)
        if path:
            ctx["spans"][k] = member_spans(path)
            return ctx["spans"][k]
    return []


HIT_KINDS = ("line", "member", "added", "removed")


def is_hit(h):
    return h["hit"] in HIT_KINDS


def rerun_plan(files, old_version, new_version):
    live = [f for f in files if f["delta"] != "cosmetic"]
    content = [f for f in live if f["scope"] == "content"]
    kernel = [f for f in live if f["scope"] == "kernel"]
    other = [f for f in live if f["scope"] == "other"]
    content_kinds = collections.Counter(f["kind"] for f in content)
    kernel_areas = collections.Counter(f["kind"] for f in kernel)
    test_classes = collections.Counter()
    for f in content + kernel + other:
        for t in f.get("tests", []):
            test_classes[t.split(".", 1)[0]] += 1
    soak = sorted({s for k in content_kinds for s in SOAK.get(k, [])})
    combat = bool(kernel_areas.keys() & COMBAT_AREAS) or bool(content_kinds.keys() & COMBAT_CONTENT)
    run = bool(kernel_areas.keys() & RUN_AREAS) or bool(content_kinds.keys() & RUN_CONTENT)
    in_scope = bool(content or kernel or other)
    added = [f for f in content if f["delta"] == "added"]
    removed = [f for f in content if f["delta"] == "removed"]
    version_moves = bool(old_version and new_version and old_version != new_version)
    reasons = []
    if kernel:
        reasons.append("kernel files changed (%s): run the FULL test suite (`dotnet run -c Release --project "
                       "sim/Sts2Sim.Tests`), not only the filters" % ", ".join(sorted(kernel_areas)))
    seeds = sorted(kernel_areas.keys() & SEED_SAVE_AREAS)
    if seeds:
        reasons.append("seed / save areas changed (%s): check `Saves.Migrations` for a seeding or schema bump "
                       "first; G3 and SaveImportTests must rerun; keep the old implementation selectable by "
                       "version if old corpora are still replayed" % ", ".join(seeds))
    plan = collections.OrderedDict([
        ("full_suite", bool(kernel) or bool(other)),
        ("test_filters", [c for c, _ in sorted(test_classes.items(), key=lambda kv: (-kv[1], kv[0]))]),
        ("content_soak", soak),
        ("run_soak", run or bool(kernel)),
        ("run_soak_filter", RUN_SOAK_TEST),
        ("selftest", in_scope),
        ("g0_combat_diff", combat),
        ("g3_seed_replay", in_scope),
        ("registry_version_filter", {
            "must_move": version_moves and in_scope,
            "default": new_version,
            "where": "sim/Sts2Sim.Core/Registry/Registries.cs GameVersions.Default",
            "version_exclusive_candidates": sorted(
                {m["model_id"] for f in added + removed for m in f.get("models", [])
                 if m.get("model_id") and not m.get("is_mock")}),
            "note": "models present in one version only need [VersionExclusive(\"<ver>\")] if the sim keeps "
                    "serving both versions; otherwise port added models and delete removed ones",
        }),
        ("knowledge_export", {
            "must_move": version_moves,
            "command": "python3 scripts/export_sts2_localization.py --output knowledge/source/v%s" % (
                new_version or "<new>"),
            "reason": "localisation templates are pinned per game version (knowledge/source/<ver>/manifest.json)",
        }),
        ("inventory_regen", in_scope),
        ("notes", reasons),
    ])
    return plan


def guess_version(path):
    m = re.search(r"v?(\d+\.\d+\.\d+)", path)
    return m.group(1) if m else None


# ---------------------------------------------------------------------------------------------------------------
# Report


def _md_escape(s):
    return str(s).replace("|", "\\|")


def render_report(man, max_rows=400, with_diffs=True):
    L = []
    w = L.append
    w("# Version delta: %s -> %s" % (man["old_version"] or man["old_tree"], man["new_version"] or man["new_tree"]))
    w("")
    w("Generated by `scripts/version_delta.py` (%s). Old tree `%s` (%d .cs), new tree `%s` (%d .cs); %.1f s."
      % (man["schema"], man["old_tree"], man["old_files"], man["new_tree"], man["new_files"], man["elapsed_s"]))
    w("Changed = normalised text differs (comments, `using` directives, whitespace and line breaks ignored). "
      "Spec citations resolve against the cite tree `%s` (%s); a hit is `line` (cited range overlaps a changed "
      "line), `member` (cited lines lie in a changed member: method, property, its state machine or lambdas) or "
      "`added` / `removed`; other citations of a changed file are `file`." % (
          man["indexes"].get("cite_tree"), man["indexes"].get("cite_tree_is")))
    w("")
    st = man["decompiler_style"]
    if st["mismatch"]:
        w("> **Decompiler style mismatch.** %.0f %% of async models are raw `IAsyncStateMachine` structs in the old "
          "tree vs %.0f %% in the new tree: the trees were decompiled with different ILSpy settings and almost "
          "every file differs for that reason alone. Re-decompile both builds with the same settings (the "
          "playbook's step 1) before reading this report." % (100 * st["old_state_machine_share"],
                                                               100 * st["new_state_machine_share"]))
        w("")
    w("## Summary")
    w("")
    w("| scope | modified | moved | added | removed | total |")
    w("| --- | ---: | ---: | ---: | ---: | ---: |")
    for scope in ("content", "kernel", "other", "out_of_scope"):
        c = man["summary"]["by_scope"].get(scope, {})
        w("| %s | %d | %d | %d | %d | %d |" % (scope, c.get("modified", 0), c.get("moved", 0), c.get("added", 0),
                                               c.get("removed", 0), sum(c.values())))
    s = man["summary"]["stats"]
    w("")
    w("Unchanged: %d identical, %d cosmetic only (whitespace / comments / layout)." % (
        s.get("identical", 0), s.get("cosmetic", 0)))
    w("")
    w("`bulk only` = every edit in the file is a recurring pattern shared by >= %d files (see Bulk edits): one "
      "mechanical change covers them all. The rest carry at least one unique edit and are the per-file port list."
      % man["bulk_edits"]["min_files"])
    w("")
    w("| content kind | changed | bulk only | unique | with sim file | with tests | spec hits |")
    w("| --- | ---: | ---: | ---: | ---: | ---: | ---: |")
    tot = collections.Counter()
    for kind, row in sorted(man["summary"]["content_kinds"].items()):
        w("| %s | %d | %d | %d | %d | %d | %d |" % (kind, row["changed"], row["bulk_only"],
                                                   row["changed"] - row["bulk_only"], row["sim"], row["tests"],
                                                   row["spec_hits"]))
        tot.update(row)
    w("| **total** | %d | %d | %d | %d | %d | %d |" % (tot["changed"], tot["bulk_only"],
                                                      tot["changed"] - tot["bulk_only"], tot["sim"], tot["tests"],
                                                      tot["spec_hits"]))
    w("")
    w("| kernel area | changed | bulk only | unique | sim files | spec hits |")
    w("| --- | ---: | ---: | ---: | ---: | ---: |")
    tot = collections.Counter()
    for area, row in sorted(man["summary"]["kernel_areas"].items()):
        w("| %s | %d | %d | %d | %d | %d |" % (area, row["changed"], row["bulk_only"], row["changed"] - row["bulk_only"],
                                               row["sim"], row["spec_hits"]))
        tot.update(row)
    w("| **total** | %d | %d | %d | %d | %d |" % (tot["changed"], tot["bulk_only"], tot["changed"] - tot["bulk_only"],
                                                 tot["sim"], tot["spec_hits"]))
    w("")
    oos = man["summary"]["out_of_scope_areas"]
    w("Out of scope (not diffed further): " + (", ".join("%s %d" % kv for kv in sorted(oos.items())) or "none") + ".")
    w("")
    plan = man["rerun_plan"]
    w("## Rerun plan")
    w("")
    w("| step | needed | detail |")
    w("| --- | --- | --- |")
    w("| full test suite | %s | `dotnet run -c Release --project sim/Sts2Sim.Tests` |" % _yn(plan["full_suite"]))
    filt = plan["test_filters"]
    w("| filtered tests | %s | %s |" % (_yn(bool(filt)), "`-- %s`" % " ".join(filt[:40]) + (
        " (+%d more)" % (len(filt) - 40) if len(filt) > 40 else "") if filt else "-"))
    w("| content soak | %s | %s |" % (_yn(bool(plan["content_soak"])), " ".join(
        "`%s`" % t for t in plan["content_soak"]) or "-"))
    w("| run soak | %s | `-- %s` (also in the full suite) |" % (_yn(plan["run_soak"]), plan["run_soak_filter"]))
    w("| server selftest | %s | `dotnet run -c Release --project sim/Sts2Sim.Server -- --selftest 300 --content "
      "ported_only` |" % _yn(plan["selftest"]))
    w("| G0 combat diff gate | %s | `dotnet run -c Release --project sim/Sts2Sim.Diff -- <bundles>` |"
      % _yn(plan["g0_combat_diff"]))
    w("| G3 seed replay | %s | `dotnet run -c Release --project sim/Sts2Sim.Diff -- g3 <bundles-v2>` |"
      % _yn(plan["g3_seed_replay"]))
    rv = plan["registry_version_filter"]
    w("| registry version filter | %s | %s -> `%s`; VersionExclusive candidates: %d |" % (
        _yn(rv["must_move"]), rv["where"], rv["default"], len(rv["version_exclusive_candidates"])))
    ke = plan["knowledge_export"]
    w("| knowledge export | %s | `%s` |" % (_yn(ke["must_move"]), ke["command"]))
    w("| inventory regen | %s | `python3 sim/tools/inventory/extract_models.py --root <new>` then "
      "`classify/run_all.py` |" % _yn(plan["inventory_regen"]))
    for n in plan["notes"]:
        w("")
        w("- " + n)
    w("")
    in_scope = [f for f in man["files"] if f["delta"] != "cosmetic" and f["scope"] in ("content", "kernel", "other")]
    w("## Bulk edits (patterns shared by >= %d in-scope files)" % man["bulk_edits"]["min_files"])
    w("")
    w("| id | files | edit (class name masked as `<C>`) | examples |")
    w("| --- | ---: | --- | --- |")
    for b in man["bulk_edits"]["patterns"][:60]:
        w("| %s | %d | %s | %s |" % (b["id"], b["files"], _md_escape(b["pattern"]),
                                     ", ".join("`%s`" % class_of(e) for e in b["examples"][:3])))
    if len(man["bulk_edits"]["patterns"]) > 60:
        w("")
        w("(%d more in manifest.json)" % (len(man["bulk_edits"]["patterns"]) - 60))
    w("")
    w("## Changed content files")
    w("")
    w("| file | kind | delta | +/- | bulk | model ids | sim files | tests | spec cites (hit/other) |")
    w("| --- | --- | --- | --- | --- | --- | --- | ---: | --- |")
    rows = [f for f in in_scope if f["scope"] == "content"]
    for f in rows[:max_rows]:
        w("| `%s` | %s | %s | %s | %s | %s | %s | %d | %s |" % (
            f["key"], f["kind"], f["delta"], _pm(f), _bulk(f),
            _md_escape(", ".join(m["model_id"] or "?" for m in f["models"]))
            or "-", "<br>".join("`%s`" % s for s in f["sim_files"]) or "**none**", len(f["tests"]), _cites(f)))
    if len(rows) > max_rows:
        w("")
        w("(%d more rows in manifest.json)" % (len(rows) - max_rows))
    w("")
    w("## Changed kernel and unclassified files")
    w("")
    w("| file | scope / area | delta | +/- | bulk | sim files | tests | spec cites (hit/other) |")
    w("| --- | --- | --- | --- | --- | --- | ---: | --- |")
    for f in [f for f in in_scope if f["scope"] != "content"][:max_rows]:
        sims = "<br>".join("`%s`" % s for s in f["sim_files"]) or ("hint `%s`" % f["sim_dir_hint"] if f[
            "sim_dir_hint"] else "**none**")
        w("| `%s` | %s / %s | %s | %s | %s | %s | %d | %s |" % (f["key"], f["scope"], f["kind"], f["delta"], _pm(f),
                                                               _bulk(f), sims, len(f["tests"]), _cites(f)))
    w("")
    hits = [(f, h) for f in in_scope for h in f["spec_cites"] if is_hit(h)]
    hits.sort(key=lambda fh: (bool(fh[0].get("bulk_only")), fh[0]["scope"] != "kernel", fh[0]["key"]))
    w("## Spec rules to re-read (%d citations in a changed member / line / added or removed file)" % len(hits))
    w("")
    w("Kernel files first, then content; rows of bulk-only files (one mechanical edit, see Bulk edits) last.")
    w("")
    w("| spec | section | citation | hit | changed file | bulk |")
    w("| --- | --- | --- | --- | --- | --- |")
    for f, h in hits[:max_rows]:
        w("| `%s:%d` | %s | `%s` | %s%s | `%s` | %s |" % (
            h["doc"], h["md_line"], _md_escape(h["section"][:80]), h["cite"], h["hit"],
            (" " + ", ".join(_md_escape(m.split(".", 1)[-1]) if m != HEADER else m for m in h["cited_members"][:3]))
            if h["hit"] == "member" else "", f["key"], _bulk(f)))
    if len(hits) > max_rows:
        w("")
        w("(%d more in manifest.json)" % (len(hits) - max_rows))
    w("")
    if with_diffs:
        w("## Diffs (normalised; line numbers are original; `-` old tree, `+` new tree)")
        w("")
        for f in in_scope:
            if not f.get("diff"):
                continue
            w("### `%s` (%s, %s)" % (f["key"], f["kind"], f["delta"] if f["delta"] != "moved" else
                                     "moved from `%s`" % f["old_key"]))
            w("")
            w("```diff")
            L.extend(f["diff"])
            if f.get("diff_truncated"):
                w("... (truncated)")
            w("```")
            w("")
    return "\n".join(L) + "\n"


def _bulk(f):
    if f.get("bulk_only"):
        return "only " + ",".join(f["bulk_patterns"])
    if f.get("bulk_patterns"):
        return ",".join(f["bulk_patterns"]) + " +%d" % f["unique_edits"]
    return "-"


def _yn(b):
    return "**yes**" if b else "no"


def _pm(f):
    if f["delta"] == "added":
        return "+%d" % f.get("lines_new", 0)
    if f["delta"] == "removed":
        return "-%d" % f.get("lines_old", 0)
    return "+%d/-%d" % (f.get("plus", 0), f.get("minus", 0))


def _cites(f):
    line = sum(1 for h in f["spec_cites"] if is_hit(h))
    return "%d/%d" % (line, len(f["spec_cites"]) - line) if f["spec_cites"] else "-"


def summarise(files, stats):
    by_scope = collections.defaultdict(collections.Counter)
    content_kinds = collections.defaultdict(lambda: {"changed": 0, "bulk_only": 0, "sim": 0, "tests": 0,
                                                     "spec_hits": 0})
    kernel_areas = collections.defaultdict(lambda: {"changed": 0, "bulk_only": 0, "sim": 0, "spec_hits": 0})
    oos = collections.Counter()
    for f in files:
        if f["delta"] == "cosmetic":
            continue
        by_scope[f["scope"]][f["delta"]] += 1
        if f["scope"] == "out_of_scope":
            oos[f["kind"]] += 1
            continue
        lh = sum(1 for h in f.get("spec_cites", []) if is_hit(h))
        if f["scope"] == "content":
            r = content_kinds[f["kind"]]
            r["changed"] += 1
            r["bulk_only"] += bool(f.get("bulk_only"))
            r["sim"] += bool(f["sim_files"])
            r["tests"] += bool(f["tests"])
            r["spec_hits"] += lh
        else:
            r = kernel_areas["%s/%s" % (f["scope"], f["kind"]) if f["scope"] == "other" else f["kind"]]
            r["changed"] += 1
            r["bulk_only"] += bool(f.get("bulk_only"))
            r["sim"] += bool(f["sim_files"])
            r["spec_hits"] += lh
    return {"stats": dict(stats), "by_scope": {k: dict(v) for k, v in by_scope.items()},
            "content_kinds": dict(content_kinds), "kernel_areas": dict(kernel_areas),
            "out_of_scope_areas": dict(oos)}


def run(old_tree, new_tree, out=None, old_version=None, new_version=None, max_diff_lines=160, want_diffs=True,
        repo=REPO, write=True, cite_tree="auto"):
    t0 = time.time()
    files, stats, n_old, n_new, style = compare_trees(old_tree, new_tree, max_diff_lines, want_diffs)
    for f in files:  # out-of-scope diffs are not kept (size); the counts stay
        if classify(f["key"])[0] == "out_of_scope":
            f.pop("diff", None)
    bulk = mark_bulk(files)
    if cite_tree == "auto":
        cite_tree = DEFAULT_CITE_TREE if os.path.isdir(DEFAULT_CITE_TREE) else new_tree
    idx_counts = enrich(files, repo, cite_tree or None, old_tree, new_tree)
    old_version = old_version or guess_version(old_tree)
    new_version = new_version or guess_version(new_tree)
    order = {"content": 0, "kernel": 1, "other": 2, "out_of_scope": 3}
    files.sort(key=lambda f: (order.get(f["scope"], 9), f["kind"] or "", f["key"]))
    man = collections.OrderedDict([
        ("schema", SCHEMA),
        ("old_tree", old_tree), ("new_tree", new_tree),
        ("old_version", old_version), ("new_version", new_version),
        ("old_files", n_old), ("new_files", n_new),
        ("indexes", idx_counts),
        ("decompiler_style", style),
        ("bulk_edits", bulk),
        ("summary", summarise(files, stats)),
        ("rerun_plan", rerun_plan(files, old_version, new_version)),
        ("files", files),
    ])
    man["elapsed_s"] = round(time.time() - t0, 2)
    if write:
        if out is None:
            name = "%s-to-%s" % (old_version or os.path.basename(os.path.normpath(old_tree)),
                                 new_version or os.path.basename(os.path.normpath(new_tree)))
            out = os.path.join(repo, "tmp", "version-delta", name)
        os.makedirs(out, exist_ok=True)
        with open(os.path.join(out, "manifest.json"), "w", encoding="utf-8") as fh:
            json.dump(man, fh, indent=1)
            fh.write("\n")
        with open(os.path.join(out, "report.md"), "w", encoding="utf-8") as fh:
            fh.write(render_report(man, with_diffs=want_diffs))
        man["out"] = out
    return man


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("old_tree")
    ap.add_argument("new_tree")
    ap.add_argument("--out", help="output directory (default tmp/version-delta/<old>-to-<new>)")
    ap.add_argument("--old-version")
    ap.add_argument("--new-version")
    ap.add_argument("--max-diff-lines", type=int, default=160, help="per-file diff cap in the report (default 160)")
    ap.add_argument("--no-diffs", action="store_true", help="omit per-file diffs from report and manifest")
    ap.add_argument("--cite-tree", default="auto",
                    help="decompile tree whose line numbers sim/specs cite (default: $STS2_DECOMPILE_ROOT or the "
                         "rule-truth tree output/server-bootstrap/linux-decompiled when present, else <new_tree>); "
                         "'none' disables member mapping of citations")
    ap.add_argument("--repo", default=REPO, help=argparse.SUPPRESS)
    a = ap.parse_args(argv)
    for t in (a.old_tree, a.new_tree):
        if not os.path.isdir(t):
            ap.error("not a directory: %s" % t)
    man = run(os.path.abspath(a.old_tree), os.path.abspath(a.new_tree), a.out, a.old_version, a.new_version,
              a.max_diff_lines, not a.no_diffs, a.repo, True,
              None if a.cite_tree == "none" else a.cite_tree if a.cite_tree == "auto" else os.path.abspath(a.cite_tree))
    s = man["summary"]["by_scope"]
    print("version-delta %s -> %s: %s; %.1f s" % (
        man["old_version"], man["new_version"],
        ", ".join("%s %d" % (k, sum(v.values())) for k, v in sorted(s.items())) or "no changes", man["elapsed_s"]))
    p = man["rerun_plan"]
    print("plan: full_suite=%s g0=%s g3=%s soak=%s filters=%d registry=%s knowledge=%s" % (
        p["full_suite"], p["g0_combat_diff"], p["g3_seed_replay"], ",".join(p["content_soak"]) or "-",
        len(p["test_filters"]), p["registry_version_filter"]["must_move"], p["knowledge_export"]["must_move"]))
    print("report: %s" % os.path.join(man["out"], "report.md"))
    print("manifest: %s" % os.path.join(man["out"], "manifest.json"))
    return 0


if __name__ == "__main__":
    sys.exit(main())
