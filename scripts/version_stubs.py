#!/usr/bin/env python3
"""Per-model work packets from a version-delta manifest (game version bump, step 3 of the migration playbook).

    python3 scripts/version_stubs.py <manifest.json> [--out tmp/version-update/<name>] [--kinds cards,relics]
                                     [--max-diff-lines 400] [--no-bulk-only]

Input: the `sts2sim.version-delta/v1` manifest written by scripts/version_delta.py (--out DIR -> DIR/manifest.json).
Output, under <out>/packets/:

  <kind>/<ModelId>.md   one packet per changed / added / removed content model:
                        - a one-line "what changed" summary derived from the diff (constants and enum values,
                          added / removed hooks and members, bulk patterns, members edited);
                        - the sim file(s) to edit, or for a NEW model a skeleton that follows the file-per-model
                          convention of its kind (header, id constant, registration note, canonical vars, overrides
                          with the sim's own signatures, a test stub);
                        - the sim/specs and docs/sim-port lines that cite the changed members (to re-read / update);
                        - the tests that mention the id, and the filter command to run them;
                        - the old -> new decompiled source diff, trimmed to the model's class.
  INDEX.md              every packet ordered by kind, then by size (new / removed / unique edits / bulk only).
  index.json            the same, machine-readable (schema sts2sim.version-stubs/v1).

Stdlib only; reuses scripts/version_delta.py (tree keys, member spans) and sim/tools/inventory/cs_source.py.
"""

import argparse
import collections
import difflib
import json
import os
import re
import sys
import time

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import version_delta as vd  # noqa: E402

REPO = vd.REPO
SCHEMA = "sts2sim.version-stubs/v1"
KIND_ORDER = ["cards", "relics", "potions", "powers", "monsters", "encounters", "events", "enchantments",
              "afflictions", "orbs", "characters", "acts", "modifiers", "card_pools", "relic_pools", "potion_pools"]

# Sim conventions per content kind: Defs directory, namespace, base class, id constant, id style, registry.
#   id_style "override": `public const string <IdConst> = "<ENTRY>"; public override string Id => <IdConst>;`
#   id_style "ctor":     `: base(<IdConst>)` (monsters, encounters); "card": `: base(<IdConst>, cost, type, rarity, target)`
# Kinds without an entry are table data (pools, modifiers): the packet names the table file instead of a skeleton.
SIM_KIND = {
    "cards": ("sim/Sts2Sim.Core/Cards/Defs", "Sts2Sim.Core.Cards", "CardDef", "CardId", "card", "CardRegistry"),
    "powers": ("sim/Sts2Sim.Core/Powers/Defs", "Sts2Sim.Core.Powers", "PowerModel", "PowerId", "override",
               "PowerRegistry"),
    "relics": ("sim/Sts2Sim.Core/Relics/Defs", "Sts2Sim.Core.Relics", "RelicModel", "RelicId", "override",
               "RelicRegistry"),
    "potions": ("sim/Sts2Sim.Core/Potions/Defs", "Sts2Sim.Core.Potions", "PotionModel", "PotionId", "override",
                "PotionRegistry"),
    "monsters": ("sim/Sts2Sim.Core/Monsters/Defs", "Sts2Sim.Core.Monsters", "MonsterDef", "MonsterIdEntry", "ctor",
                 "MonsterRegistry"),
    "encounters": ("sim/Sts2Sim.Core/Monsters/Encounters", "Sts2Sim.Core.Monsters", "EncounterDef",
                   "EncounterIdEntry", "ctor", "EncounterRegistry"),
    "events": ("sim/Sts2Sim.Core/Run/Events/Defs", "Sts2Sim.Core.Run", "EventModel", "EventId", "override",
               "EventRegistry"),
    "enchantments": ("sim/Sts2Sim.Core/Cards/Enchantments/Defs", "Sts2Sim.Core.Cards.Enchantments",
                     "EnchantmentModel", "EnchantmentId", "override", "EnchantmentRegistry"),
    "afflictions": ("sim/Sts2Sim.Core/Cards/Afflictions/Defs", "Sts2Sim.Core.Cards.Afflictions", "AfflictionModel",
                    "AfflictionId", "override", "AfflictionRegistry"),
    "orbs": ("sim/Sts2Sim.Core/Orbs/Defs", "Sts2Sim.Core.Orbs", "OrbModel", "OrbIdEntry", "override", "OrbRegistry"),
    "characters": ("sim/Sts2Sim.Core/Run/Characters", "Sts2Sim.Core.Run", "CharacterDef", "CharacterId", "override",
                   "CharacterRegistry"),
    "acts": ("sim/Sts2Sim.Core/Run/Acts", "Sts2Sim.Core.Run", "ActModel", "ActId", "override", "ActRegistry"),
}
# presentation-only overrides the sim never ports (hover tips, art, animation, VFX)
UI_MEMBERS = {"ExtraHoverTips", "HoverTips", "PortraitPath", "PortraitPngPath", "GenerateAnimator", "AnimationStates",
              "Vfx", "CustomVfxPath", "IconPath", "GetCameraScaling", "ShouldGlowGoldInternal", "ShouldGlowRedInternal"}
TABLE_KIND = {
    "card_pools": "add / remove the card in the pool's list in sim/Sts2Sim.Core/Cards/Pools/CardPools.Data.cs "
                  "(order = GenerateAllCards order) and the catalog row in CardCatalog.Data.cs",
    "relic_pools": "edit the pool list in sim/Sts2Sim.Core/Relics/RelicPools.cs and the catalog row in "
                   "sim/Sts2Sim.Core/Run/Relics/RelicCatalog.Data.cs",
    "potion_pools": "edit the pool list in sim/Sts2Sim.Core/Potions/PotionPools.cs",
    "modifiers": "modifiers are table data in sim/Sts2Sim.Core/Modifiers/ModifierModel.cs",
}


# ---------------------------------------------------------------------------------------------------------------
# Source helpers

def read_lines(path):
    if not path or not os.path.exists(path):
        return []
    return vd.read_text(path).split("\n")


def class_span(path, cls):
    """(first, last) 1-based lines of type `cls` in path (attributes included), or None."""
    cs = vd._cs_source()
    if cs is None or not path or not os.path.exists(path):
        return None
    try:
        cf = cs.CsFile(path, os.path.basename(path))
    except Exception:
        return None
    for td in cf.types:
        if td.name == cls:
            return (td.line, td.end_line)
    return None


def class_lines(path, cls):
    """[(line number, text)] of the class body (whole file when the class is not found)."""
    lines = read_lines(path)
    span = class_span(path, cls)
    lo, hi = span if span else (1, len(lines))
    return [(i, lines[i - 1]) for i in range(max(1, lo), min(len(lines), hi) + 1)]


def class_diff(old_path, new_path, cls, context=3, max_lines=400):
    """Unified diff of the class text (tabs -> 4 spaces), headers carry the original line numbers.
    -> (diff lines, truncated?)"""
    old = class_lines(old_path, cls) if old_path else []
    new = class_lines(new_path, cls) if new_path else []
    a = [t.replace("\t", "    ").rstrip() for _, t in old]
    b = [t.replace("\t", "    ").rstrip() for _, t in new]
    out = []
    sm = difflib.SequenceMatcher(None, a, b, autojunk=False)
    for group in sm.get_grouped_opcodes(context):
        i1, i2, j1, j2 = group[0][1], group[-1][2], group[0][3], group[-1][4]
        ol = old[i1][0] if i1 < len(old) else (old[-1][0] + 1 if old else 0)
        nl = new[j1][0] if j1 < len(new) else (new[-1][0] + 1 if new else 0)
        out.append("@@ -%d,%d +%d,%d @@" % (ol, i2 - i1, nl, j2 - j1))
        for tag, x1, x2, y1, y2 in group:
            if tag == "equal":
                out.extend(" " + s for s in a[x1:x2])
                continue
            out.extend("-" + s for s in a[x1:x2])
            out.extend("+" + s for s in b[y1:y2])
    truncated = len(out) > max_lines
    return (out[:max_lines], truncated)


# ---------------------------------------------------------------------------------------------------------------
# "What changed" summary

_TOK = re.compile(r'"(?:\\.|[^"\\])*"|\d+(?:\.\d+)?[mMfFdDuUlL]?|\w+|[^\w\s]')
_NUM = re.compile(r"^\d+(?:\.\d+)?[mMfFdDuUlL]?$")
_KEYWORDS = {"new", "public", "private", "protected", "internal", "override", "static", "readonly", "const",
             "return", "base", "this", "await", "async", "var", "int", "decimal", "bool", "string", "void", "if",
             "else", "get", "set", "global"}


def _num(t):
    return t.rstrip("mMfFdDuUlL")


def _label(tokens, idx):
    """Nearest identifier before tokens[idx], skipping punctuation and keywords."""
    for k in range(idx - 1, -1, -1):
        t = tokens[k]
        if re.match(r"^[A-Za-z_]\w*$", t) and t not in _KEYWORDS:
            return t
    return ""


def token_edits(old_line, new_line, max_tokens=4):
    """Replaced token runs between two similar lines: [(label, old, new, numeric?)]."""
    a, b = _TOK.findall(old_line), _TOK.findall(new_line)
    out = []
    sm = difflib.SequenceMatcher(None, a, b, autojunk=False)
    for tag, i1, i2, j1, j2 in sm.get_opcodes():
        if tag != "replace" or i2 - i1 > max_tokens or j2 - j1 > max_tokens:
            continue
        old, new = a[i1:i2], b[j1:j2]
        # skip punctuation-only edits and compiler-generated names (collection literal types, closures)
        if not any(re.search(r"\w", t) for t in old) or not any(re.search(r"\w", t) for t in new):
            continue
        if any("_003" in t for t in old + new):
            continue
        numeric = all(_NUM.match(t) for t in old + new)
        o = " ".join(_num(t) if numeric else t for t in old)
        n = " ".join(_num(t) if numeric else t for t in new)
        if o == n:
            continue
        out.append((_label(a, i1), o, n, numeric))
    return out


def diff_value_edits(diff_lines):
    """Pair the -/+ lines of each change block and collect token-level value edits."""
    edits = []
    minus, plus = [], []

    def flush():
        if minus and plus:
            sm = difflib.SequenceMatcher(None, [m.strip() for m in minus], [p.strip() for p in plus],
                                         autojunk=False)
            used = set()
            for m in minus:
                best, bi = 0.0, None
                for i, p in enumerate(plus):
                    if i in used:
                        continue
                    r = difflib.SequenceMatcher(None, m.strip(), p.strip(), autojunk=False).ratio()
                    if r > best:
                        best, bi = r, i
                if bi is not None and best >= 0.6:
                    used.add(bi)
                    edits.extend(token_edits(m, plus[bi]))
            del sm
        minus.clear()
        plus.clear()

    for line in diff_lines:
        if line.startswith("-"):
            if plus:
                flush()
            minus.append(line[1:])
        elif line.startswith("+"):
            plus.append(line[1:])
        else:
            flush()
    flush()
    return edits


def member_ids(path, cls):
    """{member id 'Name(sig)' -> modifiers} for the class's own members (generated types folded into owners)."""
    out = {}
    for lo, hi, mid in vd.member_spans(path):
        typ, _, mem = mid.partition(".")
        if typ != cls or mem == vd.HEADER:
            continue
        out.setdefault(mem, None)
    return out


def summarize(f, diff_lines, hooks, old_members, new_members):
    """One line: value edits, added / removed hooks and members, bulk patterns, edited members."""
    parts = []
    if f["delta"] == "added":
        return None  # the caller builds the NEW line from the skeleton facts
    if f["delta"] == "removed":
        return "REMOVED in the new version"
    seen = set()
    edits = diff_value_edits(diff_lines)
    # numbers first, then enum-like values (Type.Member labels such as CardRarity), then other renames
    edits.sort(key=lambda e: (0 if e[3] else 1 if e[0][:1].isupper() and re.match(r"^[A-Z]\w*$", e[1]) else 2))
    for label, o, n, numeric in edits:
        key = (label, o, n)
        if key in seen:
            continue
        seen.add(key)
        parts.append("%s %s -> %s" % (label, o, n) if label else "%s -> %s" % (o, n))
        if len(seen) >= 5:
            break
    old_b = {vd._base(m) for m in old_members}
    new_b = {vd._base(m) for m in new_members}
    for name in sorted(new_b - old_b):
        parts.append(("+hook %s" if name in hooks else "+member %s") % name)
    for name in sorted(old_b - new_b):
        parts.append(("-hook %s" if name in hooks else "-member %s") % name)
    sig_changed = sorted({vd._base(m) for m in (set(new_members) ^ set(old_members))} & old_b & new_b)
    for name in sig_changed[:3]:
        parts.append("signature %s" % name)
    if f.get("bulk_patterns"):
        parts.append("bulk %s%s" % (",".join(f["bulk_patterns"]), " only" if f.get("bulk_only") else ""))
    if not parts or (len(parts) == 1 and f.get("bulk_patterns") and not f.get("bulk_only")):
        members = [m.split(".", 1)[-1] for m in f.get("changed_members", [])][:4]
        if members:
            parts.append("edits in " + ", ".join(members))
    if not parts:
        parts.append("%d edit(s), see diff" % (f.get("unique_edits") or 0))
    return "; ".join(parts)


# ---------------------------------------------------------------------------------------------------------------
# Sim index (base-class virtual members, dynamic var classes)

class SimIndex:
    def __init__(self, repo):
        self.repo = repo
        self.type_file = {}
        core = os.path.join(repo, "sim", "Sts2Sim.Core")
        decl = re.compile(r"\b(?:class|record|struct|interface)\s+([A-Z]\w*)")
        for dp, dns, fns in os.walk(core):
            dns[:] = sorted(d for d in dns if d not in ("bin", "obj"))
            for fn in sorted(fns):
                if fn.endswith(".cs"):
                    p = os.path.join(dp, fn)
                    for m in decl.finditer(vd.read_text(p)):
                        self.type_file.setdefault(m.group(1), p)
        self._virtuals = {}

    def virtuals(self, base):
        """name -> [(kind, abstract?, head)] over base and its ancestors in sim Core (nearest first)."""
        if base in self._virtuals:
            return self._virtuals[base]
        out = collections.OrderedDict()
        cs = vd._cs_source()
        seen = set()
        name = base
        while name and name not in seen and name in self.type_file and cs is not None:
            seen.add(name)
            try:
                cf = cs.CsFile(self.type_file[name], os.path.basename(self.type_file[name]))
            except Exception:
                break
            td = next((t for t in cf.types if t.name == name), None)
            if td is None:
                break
            for m in td.members:
                if m.kind in ("method", "property") and ({"virtual", "abstract"} & m.modifiers):
                    head = " ".join(m.head.split())
                    out.setdefault(m.name, []).append((m.kind, "abstract" in m.modifiers, head))
            # the base class: the first base that is not an interface (I + capital letter)
            name = next((b.split("<")[0].strip() for b in td.bases if not re.match(r"^\s*I[A-Z]", b)), None)
        self._virtuals[base] = out
        return out

    def has_type(self, name):
        return name in self.type_file


# ---------------------------------------------------------------------------------------------------------------
# Skeletons for NEW models

def _balanced(s, i):
    """s[i] == '(' -> index of the matching ')'."""
    depth = 0
    for k in range(i, len(s)):
        if s[k] == "(":
            depth += 1
        elif s[k] == ")":
            depth -= 1
            if depth == 0:
                return k
    return -1


def _split_top(s):
    out, depth, cur = [], 0, []
    for ch in s:
        if ch in "([{<":
            depth += 1
        elif ch in ")]}>":
            depth -= 1
        if ch == "," and depth == 0:
            out.append("".join(cur).strip())
            cur = []
        else:
            cur.append(ch)
    if "".join(cur).strip():
        out.append("".join(cur).strip())
    return out


def decompiled_facts(path, cls):
    """What a skeleton needs from the decompiled class: base ctor args, canonical vars, keywords, overrides."""
    facts = {"base": None, "ctor_args": [], "vars": [], "keywords": [], "overrides": [], "simple_props": {}}
    cs = vd._cs_source()
    if cs is None or not path or not os.path.exists(path):
        return facts
    cf = cs.CsFile(path, os.path.basename(path))
    td = next((t for t in cf.types if t.name == cls), None)
    if td is None:
        return facts
    facts["base"] = td.bases[0].strip() if td.bases else None
    # member offsets index the MASKED text (string literals replaced by placeholders); resolve them back on output
    text = cf.masked
    for m in td.members:
        body = text[m.decl_start:m.end]
        if m.kind == "ctor":
            k = body.find(": base(")
            if k != -1:
                close = _balanced(body, k + 6)
                facts["ctor_args"] = [cf.resolve_strings(x) for x in _split_top(body[k + 7:close])]
        if m.name == "CanonicalVars":
            for v in re.finditer(r"\bnew\s+(\w+Var(?:<[\w.]+>)?)\s*\(", body):
                close = _balanced(body, v.end() - 1)
                facts["vars"].append(cf.resolve_strings(re.sub(r"\s+", " ", body[v.start():close + 1])))
        if m.name == "CanonicalKeywords":
            facts["keywords"] = sorted(set(re.findall(r"CardKeyword\.(\w+)", body)))
        if "override" in m.modifiers and m.kind in ("method", "property"):
            facts["overrides"].append((m.kind, m.name, m.line))
            sm = re.match(r"^[^=]*=>\s*([\w.]+)\s*;\s*$", " ".join(body.split()))
            if m.kind == "property" and sm:
                facts["simple_props"][m.name] = sm.group(1)
    return facts


def _override_head(head):
    h = re.sub(r"\b(virtual|abstract)\b", "override", head, count=1)
    return re.sub(r"\s*=>.*$", "", h).strip()


def skeleton(kind, cls, model_id, facts, new_rel, sim, hooks_seen):
    """C# skeleton text for a NEW model of `kind`, or None for table kinds."""
    if kind not in SIM_KIND:
        return None
    sdir, ns, base, id_const, id_style, registry = SIM_KIND[kind]
    entry = model_id.split(".", 1)[-1] if model_id else cls.upper()
    virt = sim.virtuals(base)
    lines = ["// %s: NEW in this game version. Ported from %s" % (cls, new_rel),
             "// Skeleton from scripts/version_stubs.py: replace every TODO, then delete [SimExclude] so",
             "// DefDiscovery registers the class (%s picks up every concrete %s subclass with a" % (registry, base),
             "// parameterless constructor; sim/Sts2Sim.Core/Registry/Registries.cs).",
             "using Sts2Sim.Core.Registry;", "", "namespace %s;" % ns, "", "[SimExclude]",
             "public sealed class %s : %s" % (cls, base), "{",
             '    public const string %s = "%s";' % (id_const, entry)]
    done = {"Id"}
    if kind == "cards":
        args = facts["ctor_args"] + [""] * 4
        lines += ["    public const int Cost = %s;" % (args[0] or "0 /* TODO */"), "",
                  "    public %s()" % cls,
                  "        : base(%s, Cost, %s, %s, %s)" % (id_const, args[1] or "CardType.Skill /* TODO */",
                                                          args[2] or "CardRarity.Common /* TODO */",
                                                          args[3] or "TargetType.None /* TODO */"),
                  "    {", "    }"]
        if facts["keywords"]:
            lines += ["", "    public override IReadOnlyCollection<CardKeyword> CanonicalKeywords =>",
                      "        Array.AsReadOnly(new[] { %s });" % ", ".join("CardKeyword." + k
                                                                         for k in facts["keywords"])]
            done.add("CanonicalKeywords")
    elif id_style == "ctor":
        lines += ["", "    public %s()" % cls, "        : base(%s)" % id_const, "    {", "    }"]
    else:
        lines += ["", "    public override string Id => %s;" % id_const]
    if facts["vars"] and "CanonicalVars" not in virt:
        lines += ["", "    // decompiled CanonicalVars (%s has no CanonicalVars: keep them as constants):" % base]
        lines += ["    //   %s" % v for v in facts["vars"]]
        done.add("CanonicalVars")
    elif facts["vars"]:
        lines += ["", "    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]", "    {"]
        for i, v in enumerate(facts["vars"]):
            cls_name = re.match(r"new\s+(\w+)", v).group(1)
            note = "" if sim.has_type(cls_name) else " // TODO: no sim class %s" % cls_name
            lines.append("        %s%s%s" % (v, "," if i + 1 < len(facts["vars"]) else "", note))
        lines += ["    };"]
        done.add("CanonicalVars")
    # decompiled overrides that exist as sim virtuals, then the sim's remaining abstract members. Card hooks the
    # sim's CardDef does not declare live on a nested CardListener (an AbstractModel), as in Cards/Defs/Bolas.cs.
    todo = []
    listener = []
    hook_virt = sim.virtuals("CardListener") if kind == "cards" else {}
    for mk, name, line in facts["overrides"]:
        if name in done:
            continue
        done.add(name)
        if name not in virt and name in hook_virt:
            vk, _, head = hook_virt[name][0]
            listener += ["", "        %s" % _override_head(head), "        {",
                         "            throw new NotImplementedException(\"TODO port %s.%s, %s:%d\");"
                         % (cls, name, new_rel, line), "        }"]
            continue
        if name in virt:
            vk, _, head = virt[name][0]
            oh = _override_head(head)
            if name in facts["simple_props"] and vk == "property":
                lines += ["", "    %s => %s;" % (oh, facts["simple_props"][name])]
            elif vk == "property":
                lines += ["", "    %s => throw new NotImplementedException(\"TODO %s:%d\");" % (oh, new_rel, line)]
            else:
                lines += ["", "    %s" % oh, "    {",
                          "        throw new NotImplementedException(\"TODO port %s.%s, %s:%d\");"
                          % (cls, name, new_rel, line), "    }"]
            if name in hooks_seen:
                hooks_seen[name] += 1
        elif name in UI_MEMBERS:
            todo.append("    // (UI only, not ported: %s, %s:%d)" % (name, new_rel, line))
        else:
            todo.append("    // TODO: decompiled override %s (%s:%d) has no sim virtual on %s or its bases"
                        % (name, new_rel, line, base))
    for name, defs in virt.items():
        vk, is_abstract, head = defs[0]
        if not is_abstract or name in done:
            continue
        done.add(name)
        oh = _override_head(head)
        if vk == "property":
            lines += ["", "    %s => throw new NotImplementedException(\"TODO abstract %s\");" % (oh, name)]
        else:
            lines += ["", "    %s" % oh, "    {",
                      "        throw new NotImplementedException(\"TODO abstract %s\");" % name, "    }"]
    if listener:
        lines += ["", "    public override AbstractModel? CreateListener(CardInstance card) => new Listener(card);", "",
                  "    private sealed class Listener : CardListener", "    {",
                  "        public Listener(CardInstance card)", "            : base(card)", "        {", "        }", "",
                  "        public override AbstractModel CloneFor(CardInstance newOwner) => new Listener(newOwner);"]
        lines += listener + ["    }"]
    if todo:
        lines += [""] + todo
    lines += ["}"]
    return "\n".join(lines) + "\n"


def test_stub(kind, cls, model_id, test_class_hint):
    if kind not in SIM_KIND:
        return None
    _, ns, _, id_const, _, registry = SIM_KIND[kind]
    entry = model_id.split(".", 1)[-1] if model_id else cls.upper()
    return "\n".join([
        "// in %s (or a new Content_<kind>_<wave>Tests.cs; the runner picks up every public static Test_* method)"
        % (test_class_hint or "sim/Sts2Sim.Tests/<kind>Tests.cs"),
        "public static void Test_%s_RegisteredUnderItsGameId()" % cls,
        "{",
        '    Assert.True(%s.TryGet(%s.%s, out var def), "%s registered");' % (registry, cls, id_const, entry),
        "    Assert.True(def.GetType() == typeof(%s));" % cls,
        '    Assert.Equal("%s", DeterministicHash.Slugify(nameof(%s)));' % (entry, cls),
        "}",
        "",
        "// TODO: one behaviour test per decompiled override (values from the decompile, not from memory)",
    ]) + "\n"


# ---------------------------------------------------------------------------------------------------------------
# Packets

def rel_tree(path, manifest):
    for key, label in (("new_tree", "new"), ("old_tree", "old")):
        root = manifest.get(key)
        if root and path and os.path.realpath(path).startswith(os.path.realpath(root) + os.sep):
            return "<%s>/%s" % (label, os.path.relpath(path, root))
    return path or "-"


def packet_name(model):
    return re.sub(r"[^A-Za-z0-9_.-]", "_", model)


def spec_lines(f):
    hits, other = [], []
    for c in f.get("spec_cites", []):
        row = "`%s:%d` (%s) cites `%s`, hit %s" % (c["doc"], c["md_line"], c.get("section") or "", c["cite"],
                                                   c["hit"])
        (hits if vd.is_hit(c) else other).append(row)
    return hits, other


def test_classes(tests):
    return sorted({t.split(".", 1)[0] for t in tests})


def build_packets(manifest, out, kinds=None, max_diff_lines=400, include_bulk_only=True, repo=REPO):
    t0 = time.time()
    sim = SimIndex(repo)
    hooks = set(sim.virtuals("AbstractModel"))
    for k in SIM_KIND.values():
        hooks.update(n for n in sim.virtuals(k[2]) if re.match(r"^(After|Before|On|Modify|Should|Try)[A-Z]", n))
    hooks_seen = collections.Counter()
    pdir = os.path.join(out, "packets")
    os.makedirs(pdir, exist_ok=True)
    rows = []
    files = [f for f in manifest["files"] if f.get("scope") == "content" and f.get("delta") != "cosmetic"]
    for f in files:
        kind = f.get("kind")
        if kinds and kind not in kinds:
            continue
        if f.get("bulk_only") and not include_bulk_only:
            continue
        cls = f.get("class") or vd.class_of(f["key"])
        old_path = f.get("old_path") if f["delta"] != "added" else None
        new_path = f.get("new_path") if f["delta"] != "removed" else None
        diff, truncated = class_diff(old_path, new_path, cls, max_lines=max_diff_lines)
        old_m = member_ids(old_path, cls) if old_path else {}
        new_m = member_ids(new_path, cls) if new_path else {}
        models = [m for m in f.get("models") or [] if not m.get("is_mock")] or \
            [{"kind": kind, "model_id": None, "class": cls}]
        if all(m.get("is_mock") for m in f.get("models") or [{}]) and f.get("models"):
            models = [dict(f["models"][0])]
        facts = decompiled_facts(new_path, cls) if f["delta"] == "added" else None
        for model in models:
            mid = model.get("model_id") or cls
            summary = summarize(f, diff, hooks, old_m, new_m)
            skel = tstub = None
            if f["delta"] == "added":
                skel = skeleton(kind, cls, model.get("model_id"), facts, rel_tree(new_path, manifest), sim,
                                hooks_seen) if not f.get("sim_files") else None
                ov = [n for _, n, _ in facts["overrides"] if n not in ("CanonicalVars", "CanonicalKeywords")]
                bits = ["NEW %s" % (facts.get("base") or kind)]
                if facts["ctor_args"]:
                    bits.append("(%s)" % ", ".join(a.split(".")[-1] for a in facts["ctor_args"]))
                if facts["vars"]:
                    bits.append("vars " + ", ".join(re.sub(r"^new\s+", "", v) for v in facts["vars"][:3]))
                if ov:
                    bits.append("overrides " + ", ".join(ov[:6]) + (" +%d" % (len(ov) - 6) if len(ov) > 6 else ""))
                if f.get("sim_files"):
                    bits.append("sim file already exists")
                summary = "; ".join(bits)
                if skel:
                    hint = None
                    for t in f.get("tests", []):
                        hint = "sim/Sts2Sim.Tests/%s.cs" % t.split(".", 1)[0]
                        break
                    tstub = test_stub(kind, cls, model.get("model_id"), hint)
            size = (f.get("plus", 0) + f.get("minus", 0)) if f["delta"] == "modified" else \
                (f.get("lines_new") or f.get("lines_old") or len(diff))
            if f["delta"] == "added":
                section = "new"
            elif f["delta"] == "removed":
                section = "removed"
            elif f.get("bulk_only"):
                section = "bulk_only"
            else:
                section = "unique"
            sdir = SIM_KIND.get(kind, (None,))[0]
            sim_target = f.get("sim_files") or ([] if not sdir else ["%s/%s.cs (new)" % (sdir, cls)]
                                                if f["delta"] == "added" else [])
            hits, other = spec_lines(f)
            name = packet_name(mid)
            path = os.path.join(pdir, kind, name + ".md")
            os.makedirs(os.path.dirname(path), exist_ok=True)
            write_packet(path, f, kind, cls, mid, summary, sim_target, hits, other, diff, truncated, skel, tstub,
                         manifest, section)
            rows.append({"kind": kind, "model_id": mid, "class": cls, "key": f["key"], "delta": f["delta"],
                         "section": section, "size": size, "summary": summary, "sim_files": sim_target,
                         "tests": len(f.get("tests", [])), "test_classes": test_classes(f.get("tests", [])),
                         "spec_hits": len(hits), "spec_file_cites": len(other),
                         "bulk_patterns": f.get("bulk_patterns", []), "skeleton": bool(skel),
                         "packet": os.path.relpath(path, out)})
    rows.sort(key=lambda r: (KIND_ORDER.index(r["kind"]) if r["kind"] in KIND_ORDER else 99,
                             ["new", "removed", "unique", "bulk_only"].index(r["section"]), -r["size"],
                             r["model_id"]))
    index = {"schema": SCHEMA, "manifest": manifest.get("_path"), "old_version": manifest.get("old_version"),
             "new_version": manifest.get("new_version"), "packets": rows,
             "counts": dict(collections.Counter(r["section"] for r in rows)),
             "by_kind": {k: dict(collections.Counter(r["section"] for r in rows if r["kind"] == k))
                         for k in sorted({r["kind"] for r in rows}, key=lambda k: KIND_ORDER.index(k)
                                         if k in KIND_ORDER else 99)},
             "elapsed_s": round(time.time() - t0, 2)}
    with open(os.path.join(pdir, "index.json"), "w", encoding="utf-8") as fh:
        json.dump(index, fh, indent=1)
    with open(os.path.join(pdir, "INDEX.md"), "w", encoding="utf-8") as fh:
        fh.write(render_index(index, manifest))
    return index


def write_packet(path, f, kind, cls, mid, summary, sim_target, hits, other, diff, truncated, skel, tstub,
                 manifest, section):
    L = ["# %s / %s (`%s`, %s)" % (kind, mid, cls, f["delta"]), "",
         "**What changed:** %s" % summary, "",
         "| field | value |", "| --- | --- |",
         "| decompiled file | `%s` |" % f["key"],
         "| old | `%s` |" % rel_tree(f.get("old_path"), manifest),
         "| new | `%s` |" % rel_tree(f.get("new_path"), manifest),
         "| size | +%d / -%d lines (normalised) |" % (f.get("plus", 0), f.get("minus", 0))
         if f["delta"] == "modified" else "| size | %s lines |" % (f.get("lines_new") or f.get("lines_old") or "?"),
         "| changed members | %s |" % (", ".join("`%s`" % m for m in f.get("changed_members", [])[:12]) or "-"),
         "| bulk patterns | %s |" % (", ".join(f.get("bulk_patterns", [])) or "-"),
         "| packet section | %s |" % section, ""]
    L += ["## Sim file", ""]
    if f.get("sim_files"):
        L += ["- `%s`" % s for s in f["sim_files"]]
    elif kind in TABLE_KIND:
        L += ["- %s" % TABLE_KIND[kind]]
    elif f["delta"] == "added":
        L += ["- none yet: create `%s` from the skeleton below" % (sim_target[0].replace(" (new)", "")
                                                                   if sim_target else "?")]
    else:
        L += ["- none (the model was never ported, or it lives in a table file: grep the id)"]
    if f["delta"] == "removed":
        L += ["", "Removed model: delete the sim file, or tag it `[VersionExclusive(\"%s\")]` while both versions "
                  "stay served (sim/Sts2Sim.Core/Registry/Registries.cs)." % (manifest.get("old_version") or "?")]
    elif f["delta"] == "added" and f.get("sim_files"):
        L += ["", "Added model with an existing sim file: compare it with the new decompile below; tag it "
                  "`[VersionExclusive(\"%s\")]` if the old version must stay served without it."
              % (manifest.get("new_version") or "?")]
    L += ["", "## Spec lines to update", ""]
    if hits:
        L += ["- %s" % h for h in hits]
    else:
        L += ["- none cite a changed member"]
    if other:
        L += ["", "Also cite this file (other members; re-read only if the change touches them): %d" % len(other)]
        L += ["- %s" % o for o in other[:8]]
        if len(other) > 8:
            L += ["- ... %d more" % (len(other) - 8)]
    L += ["", "## Tests", ""]
    tests = f.get("tests", [])
    if tests:
        L += ["```bash", "dotnet run -c Release --project sim/Sts2Sim.Tests -- %s" % " ".join(test_classes(tests)),
              "```", ""]
        L += ["- `%s`" % t for t in tests[:25]]
        if len(tests) > 25:
            L += ["- ... %d more" % (len(tests) - 25)]
    else:
        L += ["- no test mentions this model"]
    if skel:
        L += ["", "## Skeleton (`%s`)" % (sim_target[0].replace(" (new)", "") if sim_target else "?"), "",
              "```csharp", skel.rstrip(), "```"]
    if tstub:
        L += ["", "## Test stub", "", "```csharp", tstub.rstrip(), "```"]
    L += ["", "## Decompiled source diff (class `%s`, old -> new)" % cls, ""]
    if diff:
        L += ["```diff"] + diff + ["```"]
        if truncated:
            L += ["", "(diff truncated; full files: old / new above)"]
    else:
        L += ["(no textual difference inside the class: the change is outside it, e.g. `using` or attributes)"]
    with open(path, "w", encoding="utf-8") as fh:
        fh.write("\n".join(L) + "\n")


def _md(s):
    return (s or "").replace("|", "\\|")


def render_index(index, manifest, top=None):
    """INDEX.md text; with `top`, each kind lists its new and removed packets and only the `top` largest
    unique-edit packets (the trimmed form committed as a demo)."""
    rows = index["packets"]
    L = ["# Version work packets: %s -> %s" % (index.get("old_version") or "?", index.get("new_version") or "?"),
         "",
         "Generated by `scripts/version_stubs.py` from `%s`. One packet per content model: what changed, sim file "
         "(or a skeleton for a new model), spec lines, tests, the class diff. Order: kind, then section (new, "
         "removed, unique edits, bulk only), then size." % (index.get("manifest") or "manifest.json"), "",
         "| kind | new | removed | unique edits | bulk only | total |", "| --- | ---: | ---: | ---: | ---: | ---: |"]
    tot = collections.Counter()
    for k, c in index["by_kind"].items():
        tot.update(c)
        L.append("| %s | %d | %d | %d | %d | %d |" % (k, c.get("new", 0), c.get("removed", 0), c.get("unique", 0),
                                                     c.get("bulk_only", 0), sum(c.values())))
    L.append("| **total** | %d | %d | %d | %d | %d |" % (tot["new"], tot["removed"], tot["unique"], tot["bulk_only"],
                                                         sum(tot.values())))
    for k in index["by_kind"]:
        kr = [r for r in rows if r["kind"] == k]
        L += ["", "## %s" % k, ""]
        L += ["| packet | delta | size | what changed | sim file | tests | spec hits |",
              "| --- | --- | ---: | --- | --- | ---: | ---: |"]
        bulk = [r for r in kr if r["section"] == "bulk_only"]
        shown_unique = 0
        hidden = 0
        for r in kr:
            if r["section"] == "bulk_only":
                continue
            if r["section"] == "unique" and top is not None:
                shown_unique += 1
                if shown_unique > top:
                    hidden += 1
                    continue
            sim = ", ".join("`%s`" % os.path.basename(s) for s in r["sim_files"][:2]) or "-"
            if r["skeleton"]:
                sim += " (skeleton)"
            # the trimmed form lives outside the output tree: plain ids instead of relative links
            label = "`%s`" % r["model_id"] if top is not None else "[%s](%s)" % (
                r["model_id"], os.path.relpath(r["packet"], "packets"))
            L.append("| %s | %s | %d | %s | %s | %d | %d |" % (
                label, r["delta"], r["size"], _md(r["summary"]), sim, r["tests"], r["spec_hits"]))
        if hidden:
            L.append("| ... %d smaller unique-edit packets | modified | - | (full INDEX.md) | - | - | - |" % hidden)
        if bulk:
            by_pat = collections.defaultdict(list)
            for r in bulk:
                by_pat[",".join(r["bulk_patterns"])].append(r["class"])
            for pat, names in sorted(by_pat.items(), key=lambda x: -len(x[1])):
                L.append("| %d bulk-only packets (%s) | modified | - | mechanical: %s | - | - | - |" % (
                    len(names), pat, _md(", ".join(names[:6]) + (", ..." if len(names) > 6 else ""))))
    pats = (manifest.get("bulk_edits") or {}).get("patterns") or []
    if pats:
        L += ["", "## Bulk patterns", "", "| id | files | edit |", "| --- | ---: | --- |"]
        for p in pats:
            L.append("| %s | %d | %s |" % (p["id"], p["files"], _md(p["pattern"])))
    return "\n".join(L) + "\n"


_SKEL = re.compile(r"## Skeleton[^\n]*\n\n```csharp\n(.*?)\n```", re.S)
CHECK_DIR = os.path.join("sim", "Sts2Sim.Core", "ZzVersionStubsCheck")


def check_skeletons(out, repo=REPO):
    """Compile every skeleton inside sim Core (a temporary directory, removed afterwards). -> (rc, n, errors)."""
    import shutil
    import subprocess
    with open(os.path.join(out, "packets", "index.json"), encoding="utf-8") as fh:
        idx = json.load(fh)
    target = os.path.join(repo, CHECK_DIR)
    if os.path.exists(target):
        shutil.rmtree(target)
    os.makedirs(target)
    n = 0
    try:
        for r in idx["packets"]:
            if not r["skeleton"]:
                continue
            with open(os.path.join(out, r["packet"]), encoding="utf-8") as fh:
                m = _SKEL.search(fh.read())
            if m:
                with open(os.path.join(target, "%s_%s.cs" % (r["kind"], r["class"])), "w", encoding="utf-8") as fh:
                    fh.write(m.group(1) + "\n")
                n += 1
        p = subprocess.run(["dotnet", "build", os.path.join(repo, "sim", "Sts2Sim.Core", "Sts2Sim.Core.csproj"),
                            "-c", "Release", "-nologo", "-v", "q"], capture_output=True, text=True)
        errors = sorted({ln.strip() for ln in p.stdout.splitlines() if re.search(r"\b(error|warning) CS\d+", ln)})
        return (p.returncode, n, errors)
    finally:
        shutil.rmtree(target, ignore_errors=True)
        # rebuild without the stubs so bin/ holds the real Core again
        subprocess.run(["dotnet", "build", os.path.join(repo, "sim", "Sts2Sim.Core", "Sts2Sim.Core.csproj"), "-c",
                        "Release", "-nologo", "-v", "q"], capture_output=True, text=True)


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    ap.add_argument("manifest", help="version_delta manifest.json")
    ap.add_argument("--out", help="output root (default tmp/version-update/<old>-to-<new>)")
    ap.add_argument("--kinds", help="comma-separated content kinds (default all)")
    ap.add_argument("--max-diff-lines", type=int, default=400)
    ap.add_argument("--no-bulk-only", action="store_true", help="skip files whose only edits are bulk patterns")
    ap.add_argument("--trimmed-index", metavar="PATH",
                    help="also write a trimmed INDEX (new / removed + the --top largest unique packets per kind)")
    ap.add_argument("--top", type=int, default=8, help="unique-edit packets per kind in --trimmed-index (default 8)")
    ap.add_argument("--check-skeletons", action="store_true",
                    help="compile every skeleton inside sim Core (temporary %s/, removed afterwards)" % CHECK_DIR)
    a = ap.parse_args(argv)
    with open(a.manifest, encoding="utf-8") as fh:
        manifest = json.load(fh)
    if manifest.get("schema") != vd.SCHEMA:
        print("version_stubs: %s is not a %s manifest" % (a.manifest, vd.SCHEMA), file=sys.stderr)
        return 2
    mpath = os.path.abspath(a.manifest)
    manifest["_path"] = os.path.relpath(mpath, REPO) if mpath.startswith(REPO + os.sep) else mpath
    out = a.out or os.path.join(REPO, "tmp", "version-update", "%s-to-%s" % (
        manifest.get("old_version") or "old", manifest.get("new_version") or "new"))
    kinds = set(a.kinds.split(",")) if a.kinds else None
    idx = build_packets(manifest, out, kinds, a.max_diff_lines, not a.no_bulk_only)
    c = idx["counts"]
    print("version-stubs %s -> %s: %d packets (new %d, removed %d, unique %d, bulk only %d), %d skeletons; %.1f s"
          % (idx["old_version"], idx["new_version"], len(idx["packets"]), c.get("new", 0), c.get("removed", 0),
             c.get("unique", 0), c.get("bulk_only", 0), sum(1 for r in idx["packets"] if r["skeleton"]),
             idx["elapsed_s"]))
    print("index: %s" % os.path.join(out, "packets", "INDEX.md"))
    if a.trimmed_index:
        with open(a.trimmed_index, "w", encoding="utf-8") as fh:
            fh.write(render_index(idx, manifest, top=a.top))
        print("trimmed index: %s" % a.trimmed_index)
    if a.check_skeletons:
        rc, n, errors = check_skeletons(out)
        print("skeleton compile check: %d skeletons, %s (%d diagnostics)" % (n, "OK" if rc == 0 and not errors
                                                                             else "FAILED", len(errors)))
        for e in errors[:20]:
            print("  " + e)
        if rc != 0 or errors:
            return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
