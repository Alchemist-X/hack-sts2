#!/usr/bin/env python3
"""Mechanical inventory of every content model kind in the decompiled game tree.

Usage (from the repo root):
    python3 sim/tools/inventory/extract_models.py                 # all kinds
    python3 sim/tools/inventory/extract_models.py --kind cards    # one kind (context is still loaded)
    python3 sim/tools/inventory/extract_models.py --check         # validate only, write nothing

Writes JSON manifests to sim/specs/inventory/. Stdlib only. See README.md here
and sim/specs/inventory/README.md for the field contract.
"""

import argparse
import hashlib
import json
import os
import re
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

from cs_source import (  # noqa: E402
    CsFile, STRING_PLACEHOLDER_RE, generated_owner, iter_types, split_args, balanced_call_args,
)
from model_ids import model_id, slugify, slugify_category, self_test as slug_self_test  # noqa: E402

SCHEMA = "sts2sim.inventory/v1"
GAME_VERSION = "0.111.0"
NS_PREFIX = "MegaCrit.Sts2.Core."
DEFAULT_ROOT = "/Users/Aincrad/dev-proj/hack-sts2/output/server-bootstrap/linux-decompiled"
REPO_ROOT = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", ".."))

KINDS = [
    ("cards", ["Models.Cards", "Models.Cards.Mocks"], "CardModel"),
    ("powers", ["Models.Powers", "Models.Powers.Mocks"], "PowerModel"),
    ("relics", ["Models.Relics"], "RelicModel"),
    ("potions", ["Models.Potions", "Models.Potions.Mocks"], "PotionModel"),
    ("monsters", ["Models.Monsters", "Models.Monsters.Mocks"], "MonsterModel"),
    ("encounters", ["Models.Encounters", "Models.Encounters.Mocks"], "EncounterModel"),
    ("events", ["Models.Events", "Models.Events.Mocks"], "EventModel"),
    ("acts", ["Models.Acts"], "ActModel"),
    ("characters", ["Models.Characters"], "CharacterModel"),
    ("enchantments", ["Models.Enchantments", "Models.Enchantments.Mocks"], "EnchantmentModel"),
    ("afflictions", ["Models.Afflictions", "Models.Afflictions.Mocks"], "AfflictionModel"),
    ("modifiers", ["Models.Modifiers"], "ModifierModel"),
    ("orbs", ["Models.Orbs", "Models.Orbs.Mock"], "OrbModel"),
]
KIND_NAMES = [k[0] for k in KINDS]
KIND_DIRS = dict((k[0], k[1]) for k in KINDS)
KIND_ROOT = dict((k[0], k[2]) for k in KINDS)
POOL_DIRS = {"card": "Models.CardPools", "relic": "Models.RelicPools", "potion": "Models.PotionPools"}
OTHER_MODEL_DIRS = ["Models.Achievements", "Models.Badges", "Models.Singleton", "Models.Exceptions"]
REF_KINDS = ["cards", "powers", "relics", "potions", "monsters", "encounters", "events", "acts", "characters",
             "enchantments", "afflictions", "modifiers", "orbs"]

ACCESSOR_KIND = {
    "Card": "cards", "Power": "powers", "Relic": "relics", "Potion": "potions", "Monster": "monsters",
    "Encounter": "encounters", "Event": "events", "AncientEvent": "events", "Act": "acts",
    "Character": "characters", "Enchantment": "enchantments", "Affliction": "afflictions",
    "Modifier": "modifiers", "Orb": "orbs",
}
HINT_WORDS = [("Card", "cards"), ("Power", "powers"), ("Relic", "relics"), ("Potion", "potions"),
              ("Monster", "monsters"), ("Encounter", "encounters"), ("Event", "events"), ("Enchant", "enchantments"),
              ("Afflict", "afflictions"), ("Modifier", "modifiers"), ("Orb", "orbs"), ("Character", "characters"),
              ("Act", "acts"), ("Pet", "monsters")]

API_RE = re.compile(r"\b(\w*Cmd)\.(\w+)")
HOOK_CALL_RE = re.compile(r"(?<![\w.])Hook\.(\w+)")
CHOICE_RES = [
    re.compile(r"\bCardSelectCmd\.\w+"),
    re.compile(r"\bRelicSelectCmd\.\w+"),
    re.compile(r"\bPlayerChoice\w*"),
    re.compile(r"\bSignalPlayerChoiceBegun\b"),
    re.compile(r"\bSelectCardsToAddToDeckFromGrid\b"),
]
CHOICE_EXCLUDE_RE = re.compile(r"PlayerChoiceContext$")
GODOT_TYPES = {
    "Godot", "GodotObject", "Node", "Node2D", "Node3D", "Control", "CanvasItem", "Vector2", "Vector2I", "Vector3",
    "Color", "Colors", "Tween", "Variant", "PackedScene", "Texture2D", "Resource", "ResourceLoader", "SceneTree",
    "Callable", "StringName", "Material", "ShaderMaterial", "Label", "RichTextLabel", "Sprite2D", "AnimationPlayer",
    "Viewport", "Rect2", "Transform2D", "TextureRect", "Marker2D", "GpuParticles2D", "CpuParticles2D", "DirAccess",
}
UI_VOCAB_RE = re.compile(
    r"\b(?:\w*(?:Vfx|Sfx)\w*|TriggerAnim|WithAttackerAnim|WithHitFx|WithAttackerFx|CreatureAnimator|AnimState"
    r"|MegaSpine\w*|MegaSprite|MegaSkeleton\w*|MegaSkin|MegaAnimationState|ScreenShake|ShakeStrength|ShakeDuration"
    r"|PreloadManager|SceneHelper|ImageHelper|HoverTipFactory|NonInteractiveMode)\b")
UI_CMD_RE = re.compile(r"\b(?:VfxCmd|SfxCmd|TalkCmd|ThinkCmd)\.\w+|(?<![\w.])Cmd\.(?:Wait|CustomScaledWait)\b")
UI_METHOD_RE = re.compile(
    r"(Vfx|Sfx|Anim|Skin|Bestiary|Camera|Visual|Portrait|Icon|HoverTip|Speech|Dialogue|Texture|Color|Display"
    r"|AssetPath|Description|Ambient|Music|Shake|Tween|Spine|Node|Delay)")
LOGIC_PROPERTIES = {"IsPlayable"}
GUARD_IF_RE = re.compile(r"\bif\s*\(\s*(?:TestMode\.IsOff|!\s*TestMode\.IsOn)\b")
GUARD_ON_RE = re.compile(r"\bif\s*\(\s*TestMode\.IsOn\s*\)")
BOILERPLATE_RES = [re.compile(p) for p in [
    r"^[{}();,\s]*$",
    r"^(try|finally|else|do)$",
    r"^catch\s*\(\s*(System\.)?Exception\s+\w+\s*\)$",
    r"^int num = _003C_003E1__state;$",
    r"^\w+ \w+ = _003C_003E4__this;$",
    r"^(num = )?\(?_003C_003E1__state = -?\d+\)?;$",
    r"^num = \(_003C_003E1__state = -?\d+\);$",
    r"_003C_003Eu__\d+",
    r"AwaitUnsafeOnCompleted|AwaitOnCompleted",
    r"_003C_003Et__builder",
    r"^if \(!.*IsCompleted\)$",
    r"^\(\(\w+(<.*>)?\)\(ref \w+\)\)\.GetResult\(\);$",
    r"^\w+\.GetResult\(\);$",
    r"^goto (end_)?IL_\w+;$",
    r"^(end_)?IL_\w+:;?$",
    r"^return;$",
    r"^(System\.Runtime\.CompilerServices\.)?(Configured)?\w*(TaskAwaiter|ValueTaskAwaiter)(<.*>)? \w+;$",
    r"^if \(num [!=<>]=? -?\d+\)$",
    r"^switch \(num\)$",
    r"^\[DebuggerHidden\]$",
    r"Dispose\(\);$",
    r"^return System\.Threading\.Tasks\.Task\.CompletedTask;$",
    r"^_003C_003E7__wrap\d+ = (null|default\(.*\));$",
]]
NUM_LIT_RE = re.compile(r"^-?\d+(\.\d+)?[mMfFdDuUlL]*$")
ASC_RE = re.compile(r"AscensionHelper\.GetValueIfAscension\s*\(\s*AscensionLevel\.(\w+)\s*,\s*(-?[\d.]+)[mMfF]?\s*,\s*(-?[\d.]+)[mMfF]?\s*\)")
RUN_RNG_STREAMS = ["UpFront", "Shuffle", "UnknownMapPoint", "CombatCardGeneration", "CombatPotionGeneration",
                   "CombatCardSelection", "CombatEnergyCosts", "CombatTargets", "MonsterAi", "Niche",
                   "CombatOrbGeneration", "TreasureRoomRelics"]
PLAYER_RNG_STREAMS = ["Rewards", "Shops", "Transformations"]
RNG_ROOT_RE = re.compile(r"\b(?:Rng|RunRng|PlayerRng)\b")
RNG_TAIL_RE = re.compile(r"(?:\??\.[A-Za-z_][A-Za-z0-9_]*)*")
LOCAL_RNG_RE = re.compile(r"(?<![\w.])([a-z_]\w*[rR]ng)\.(Next\w*|Shuffle)\b")
RNG_TYPE_RE = re.compile(r"\b(RunRngType|PlayerRngType)\.(\w+)")


def rel_ns_dir(dirname):
    return dirname[len(NS_PREFIX):] if dirname.startswith(NS_PREFIX) else dirname


def uniq(seq):
    seen = set()
    out = []
    for x in seq:
        if x not in seen:
            seen.add(x)
            out.append(x)
    return out


def norm_ws(s):
    return re.sub(r"\s+", " ", s).strip()


def norm_num(s):
    s = s.strip()
    if NUM_LIT_RE.match(s):
        s = re.sub(r"[mMfFdDuUlL]+$", "", s)
    return s


def strip_casts(s):
    return re.sub(r"\bdecimal\.op_Implicit\((.*)\)$", r"\1", s.strip())


class Ctx(object):
    pass


def read_text(path, encoding="utf-8-sig"):
    with open(path, encoding=encoding, errors="replace") as fh:
        return fh.read()


def list_cs(root, ns_dir):
    full = os.path.join(root, NS_PREFIX + ns_dir)
    if not os.path.isdir(full):
        return []
    return sorted(fn for fn in os.listdir(full) if fn.endswith(".cs"))


def load_file(root, ns_dir, fn):
    return CsFile(os.path.join(root, NS_PREFIX + ns_dir, fn), ns_dir + "/" + fn)


def member_text(f, m):
    return f.masked[m.decl_start:m.end]


def member_body(f, m):
    return f.body_of(m)


def expr_of(f, m):
    """Expression of an expression-bodied member, or the single `return x;` of a block getter."""
    body = member_body(f, m).strip()
    if body.startswith("=>"):
        e = body[2:].strip()
        if e.endswith(";"):
            e = e[:-1]
        return e.strip()
    rets = re.findall(r"\breturn\s+([^;]*);", body)
    if len(rets) == 1 and body.count("{") <= 3:
        return rets[0].strip()
    return None


def owned_text(f, td, name):
    """Masked text of member `name` plus every compiler-generated type/method attributed to it."""
    parts = []
    for m in td.members:
        if m.name == name and m.kind != "type":
            parts.append(member_text(f, m))
        elif m.kind == "type" and generated_owner(m.name) == name:
            parts.append(f.masked[m.start:m.end])
        elif m.kind == "type":
            for sub in iter_types(m.type_decl):
                for mm in sub.members:
                    if mm.kind == "method" and generated_owner(mm.name) == name:
                        parts.append(member_text(f, mm))
        elif m.kind == "method" and generated_owner(m.name) == name:
            parts.append(member_text(f, m))
    return "\n".join(parts)


COMBAT_ROOM_RE = re.compile(r"\bCombatRoom\b|\bRoomType\.(?:Monster|Elite|Boss)\b")


def top_member(td, name, kinds=None):
    for m in td.members:
        if m.name == name and (kinds is None or m.kind in kinds):
            return m
    return None


# ---------------------------------------------------------------- context

def load_context(root, sim_root):
    ctx = Ctx()
    ctx.root = root
    ctx.sim_root = sim_root
    ctx.files = {}
    ctx.non_type_files = {}
    for kind, dirs, _ in KINDS:
        lst = []
        for d in dirs:
            for fn in list_cs(root, d):
                lst.append(load_file(root, d, fn))
        ctx.files[kind] = lst
    ctx.pool_files = {}
    for pk, d in POOL_DIRS.items():
        ctx.pool_files[pk] = [load_file(root, d, fn) for fn in list_cs(root, d)]
    ctx.other_files = {}
    for d in OTHER_MODEL_DIRS:
        ctx.other_files[d] = [load_file(root, d, fn) for fn in list_cs(root, d)]
    ctx.base_files = dict((fn[:-3], load_file(root, "Models", fn)) for fn in list_cs(root, "Models"))
    ctx.hook_file = load_file(root, "Hooks", "Hook.cs")
    _index_classes(ctx)
    _load_registered(ctx)
    _load_hooks(ctx)
    _load_node_classes(ctx)
    _load_dynvar_names(ctx)
    _load_modeldb(ctx)
    _load_pools(ctx)
    _load_sim_ported(ctx)
    return ctx


def _index_classes(ctx):
    ctx.class_index = {}
    ctx.kind_classes = dict((k, set()) for k in KIND_NAMES)
    ctx.type_index = {}
    ctx.kind_type_index = {}
    for name, f in sorted(ctx.base_files.items()):
        for td in f.types:
            ctx.type_index.setdefault(type_key(f, td), (f, td))
    for kind in KIND_NAMES:
        for f in ctx.files[kind]:
            for td in f.types:
                ctx.class_index.setdefault(td.name, []).append((kind, f, td))
                ctx.kind_classes[kind].add(td.name)
                ctx.type_index.setdefault(type_key(f, td), (f, td))
                ctx.kind_type_index.setdefault(type_key(f, td), (kind, f, td))
    for pk, lst in ctx.pool_files.items():
        for f in lst:
            for td in f.types:
                ctx.type_index.setdefault(type_key(f, td), (f, td))


def file_ns(f):
    return f.rel.split("/")[0]


def type_key(f, td):
    return (file_ns(f), td.name)


def enclosing_namespaces(ns_dir):
    """`ns_dir` and its enclosing namespaces, innermost first, in decompile-dir form."""
    parts = (NS_PREFIX + ns_dir).split(".")
    return [rel_ns_dir(".".join(parts[:i])) for i in range(len(parts), 0, -1)]


def resolve_type(ctx, f, name):
    """Bind a type name used in file f the way C# does for a file-scoped namespace.

    Lookup order: the file's namespace, then each enclosing namespace outward, then the
    compilation unit's `using` namespaces (an ambiguous using match binds nothing). Only types
    declared in the scanned dirs (`Models/`, kind dirs, pool dirs) are known; returns the
    `(ns_dir, name)` key or None.
    """
    if not name:
        return None
    name = re.sub(r"<.*>$", "", name.strip())
    name = re.sub(r"^global::", "", name)
    if "." in name:
        ns, nm = name.rsplit(".", 1)
        key = (rel_ns_dir(ns), nm)
        return key if key in ctx.type_index else None
    for ns in enclosing_namespaces(file_ns(f)):
        if (ns, name) in ctx.type_index:
            return (ns, name)
    hits = sorted(ns for ns in file_usings(f) if (ns, name) in ctx.type_index)
    if len(hits) == 1:
        return (hits[0], name)
    return None


def base_key(ctx, key):
    f, td = ctx.type_index[key]
    return resolve_type(ctx, f, td.bases[0]) if td.bases else None


ABSTRACT_MODEL_KEY = ("Models", "AbstractModel")


def category_type(ctx, key):
    """Name of the class whose base is AbstractModel (`Models/ModelDb.cs:470-478`), walking bases by key."""
    seen = set()
    cur = key
    while cur is not None and cur not in seen and cur in ctx.type_index:
        seen.add(cur)
        b = base_key(ctx, cur)
        if b is None:
            return None
        if b == ABSTRACT_MODEL_KEY:
            return cur[1]
        cur = b
    return None


def chain_of(ctx, key):
    """Base class names of `key` up to (not including) AbstractModel, nearest first."""
    out = []
    cur = base_key(ctx, key) if key in ctx.type_index else None
    seen = set()
    while cur and cur not in seen and cur != ABSTRACT_MODEL_KEY:
        seen.add(cur)
        out.append(cur[1])
        cur = base_key(ctx, cur) if cur in ctx.type_index else None
    return out


def _load_registered(ctx):
    f = ctx.base_files["AbstractModelSubtypes"]
    ctx.registered_names = set()
    ctx.registered_qualified = set()
    ctx.registered_total = 0
    for m in re.finditer(r"typeof\(([\w.]+)\)", f.masked):
        full = m.group(1)
        ctx.registered_total += 1
        if "." in full:
            ns, nm = full.rsplit(".", 1)
            ctx.registered_qualified.add((rel_ns_dir(ns), nm))
        else:
            ctx.registered_names.add(full)
    cm = re.search(r"public static int Count => (\d+);", f.masked)
    ctx.registered_declared_count = int(cm.group(1)) if cm else None


def is_registered(ctx, f, td):
    d = f.rel.split("/")[0]
    if (d, td.name) in ctx.registered_qualified:
        return True
    return td.name in ctx.registered_names


def _load_hooks(ctx):
    am = ctx.base_files["AbstractModel"]
    td = am.main_type()
    ctx.hooks = {}
    ctx.hook_order = []
    for m in td.members:
        if m.kind == "method" and "virtual" in m.modifiers and m.access == "public" and m.name != "CompareTo":
            head = norm_ws(m.head)
            ret = re.sub(r"^public virtual\s+", "", head)
            ret = ret[: ret.rfind(" " + m.name)] if (" " + m.name) in ret else None
            if m.name not in ctx.hooks:
                ctx.hooks[m.name] = {"name": m.name, "line": m.line, "returns": ret, "params": norm_ws(m.params),
                                     "dispatchers": [], "scopes": []}
                ctx.hook_order.append(m.name)
    hf = ctx.hook_file
    htd = hf.main_type()
    disp = {}
    for m in htd.members:
        if m.kind == "method" and "static" in m.modifiers and m.name != "IterateCombatHookListeners":
            d = disp.setdefault(m.name, {"name": m.name, "lines": [], "params": [], "texts": [], "public": False})
            d["lines"].append(m.line)
            d["params"].append(norm_ws(m.params))
            d["texts"].append(member_text(hf, m))
            if m.access == "public":
                d["public"] = True
        elif m.kind == "type":
            owner = generated_owner(m.name)
            if owner and owner != "IterateCombatHookListeners":
                d = disp.setdefault(owner, {"name": owner, "lines": [], "params": [], "texts": [], "public": False})
                d["texts"].append(hf.masked[m.start:m.end])
    ctx.dispatchers = {}
    for name in sorted(disp):
        d = disp[name]
        text = "\n".join(d["texts"])
        it = set()
        if re.search(r"\brunState\.IterateHookListeners\(\s*null\s*\)", text):
            it.add("run")
        if re.search(r"\brunState\.IterateHookListeners\(\s*combatState\s*\)", text):
            it.add("both")
        if re.search(r"\b(?:combatState|CombatState)\.IterateHookListeners\(\s*\)|\bIterateCombatHookListeners\(", text):
            it.add("combat")
        calls = sorted(set(c for c in re.findall(r"\.(\w+)\(", text) if c in ctx.hooks))
        scope = "|".join(sorted(it)) if it else "direct"
        helpers = sorted(set(c for c in re.findall(r"(?<![\w.])(\w+)\(", text) if c in disp and c != name
                             and not disp[c]["public"]))
        ctx.dispatchers[name] = {"name": name, "lines": d["lines"], "scope": scope, "iterates": sorted(it),
                                 "calls_hooks": calls, "calls_helpers": helpers, "public": d["public"]}
        for c in calls:
            h = ctx.hooks[c]
            if name not in h["dispatchers"]:
                h["dispatchers"].append(name)
            for s in (sorted(it) if it else ["direct"]):
                if s not in h["scopes"]:
                    h["scopes"].append(s)
    for h in ctx.hooks.values():
        h["scopes"] = sorted(h["scopes"])
        h["scope"] = "|".join(h["scopes"]) if h["scopes"] else "unknown"
    _load_sim_hooks(ctx)


def _load_sim_hooks(ctx):
    ctx.sim_hook_virtuals = set()
    ctx.sim_hook_dispatchers = set()
    am = os.path.join(ctx.sim_root, "Sts2Sim.Core", "Combat", "Pipeline", "AbstractModel.cs")
    hk = os.path.join(ctx.sim_root, "Sts2Sim.Core", "Combat", "Pipeline", "Hook.cs")
    if os.path.isfile(am):
        txt = read_text(am)
        ctx.sim_hook_virtuals = set(re.findall(r"public virtual [\w<>?,\[\] ]+? (\w+)\s*\(", txt))
    if os.path.isfile(hk):
        txt = read_text(hk)
        ctx.sim_hook_dispatchers = set(re.findall(r"public static [\w<>?,\[\] ]+? (\w+)\s*\(", txt))


def _load_node_classes(ctx):
    ctx.node_classes = set()
    for d in os.listdir(ctx.root):
        if ".Core.Nodes" in d and os.path.isdir(os.path.join(ctx.root, d)):
            for fn in os.listdir(os.path.join(ctx.root, d)):
                if fn.endswith(".cs"):
                    try:
                        txt = read_text(os.path.join(ctx.root, d, fn))
                    except OSError:
                        continue
                    for m in re.finditer(r"(?m)^\s*(?:\w+\s+)*(?:class|struct|enum|interface|record)\s+(\w+)", txt):
                        ctx.node_classes.add(m.group(1))


def _load_dynvar_names(ctx):
    ctx.dynvar_default = {}
    d = "Localization.DynamicVars"
    for fn in list_cs(ctx.root, d):
        txt = read_text(os.path.join(ctx.root, NS_PREFIX + d, fn))
        cm = re.search(r"class (\w+)", txt)
        dm = re.search(r'const string defaultName = "([^"]*)"', txt)
        if cm and dm:
            ctx.dynvar_default[cm.group(1)] = dm.group(1)


def _modeldb_list(f, td, member, accessor):
    m = top_member(td, member)
    if m is None:
        return None
    text = member_text(f, m)
    return re.findall(r"\b" + accessor + r"<(\w+)>\(\)", text)


def _load_modeldb(ctx):
    f = ctx.base_files["ModelDb"]
    td = f.main_type()
    md = {}
    specs = [
        ("AllCharacters", "Character"), ("AllSharedCardPools", "CardPool"), ("AllSharedEvents", "Event"),
        ("AllSharedAncients", "AncientEvent"), ("EventEncounters", "Encounter"),
        ("AllSharedPotionPools", "PotionPool"), ("AllSharedRelicPools", "RelicPool"), ("Orbs", "Orb"),
        ("Acts", "Act"), ("GoodModifiers", "Modifier"), ("BadModifiers", "Modifier"),
        ("MutuallyExclusiveModifiers", "Modifier"),
    ]
    for member, acc in specs:
        lst = _modeldb_list(f, td, member, acc)
        mm = top_member(td, member)
        md[member] = {"items": lst, "line": mm.line if mm else None}
    ctx.modeldb = md
    ctx.modeldb_file = f


def _pool_label(class_name, suffix):
    e = slugify(class_name)
    return e[: -len(suffix)] if e.endswith(suffix) else e


def _declared_count(text):
    dm = re.search(r"new \w+\[(\d+)\]", text)
    if dm:
        return int(dm.group(1))
    lm = re.search(r"int num = (\d+);\s*List<\w+> \w+ = new List<\w+>\(num\);", text)
    if lm:
        return int(lm.group(1))
    if "ReadOnlySingleElementList" in text:
        return 1
    if "Array.Empty" in text:
        return 0
    return None


def _epoch_list(ctx, epoch, member, acc):
    d = "Timeline.Epochs"
    fn = epoch + ".cs"
    if fn not in list_cs(ctx.root, d):
        return [], None, None
    f = load_file(ctx.root, d, fn)
    td = f.main_type()
    m = top_member(td, member) if td else None
    if m is None:
        return [], None, None
    text = member_text(f, m)
    return re.findall(acc, text), _declared_count(text), "%s:%d" % (f.rel, m.line)


def _load_pools(ctx):
    ctx.pools = {}
    spec = {"card": ("GenerateAllCards", r"(?:ModelDb\.Card|MockCard)<(\w+)>\(", "_CARD_POOL"),
            "relic": ("GenerateAllRelics", r"ModelDb\.Relic<(\w+)>\(", "_RELIC_POOL"),
            "potion": ("GenerateAllPotions", r"ModelDb\.Potion<(\w+)>\(", "_POTION_POOL")}
    for pk, files in ctx.pool_files.items():
        gen, acc, suffix = spec[pk]
        out = {}
        for f in files:
            td = f.main_type()
            if td is None:
                continue
            m = top_member(td, gen, ("method",))
            items = []
            declared = None
            line = None
            via = None
            if m is not None:
                text = member_text(f, m)
                items = re.findall(acc, text)
                declared = _declared_count(text)
                line = m.line
                if not items:
                    ref = re.search(r"\b(\w+Epoch)\.(\w+)\b", text)
                    if ref:
                        items, declared, via = _epoch_list(ctx, ref.group(1), ref.group(2), acc)
            gates = []
            for um in td.members:
                if um.kind == "method" and um.name and um.name.startswith("GetUnlocked"):
                    gates.extend(re.findall(r"IsEpochRevealed<(\w+)>", member_text(f, um)))
            is_mock = bool(re.search(r"override bool IsMock => true", f.masked))
            out[td.name] = {"class": td.name, "id": _pool_label(td.name, suffix), "model_id": model_id(
                category_type(ctx, type_key(f, td)) or (pk.capitalize() + "PoolModel"), td.name),
                "file": f.rel, "generator_line": line, "members": items, "declared_count": declared,
                "members_via": via, "unlock_gates": uniq(gates), "is_mock": is_mock}
        ctx.pools[pk] = out
    chars = {}
    for f in ctx.files["characters"]:
        td = f.main_type()
        info = {}
        for prop, acc in (("CardPool", "CardPool"), ("RelicPool", "RelicPool"), ("PotionPool", "PotionPool")):
            m = top_member(td, prop, ("property",))
            if m is not None:
                found = re.findall(r"ModelDb\." + acc + r"<(\w+)>\(\)", member_text(f, m))
                info[prop] = found[-1] if found else None
        chars[td.name] = info
    ctx.char_pools = chars
    order_chars = ctx.modeldb["AllCharacters"]["items"] or []
    ctx.card_pool_order = [chars[c]["CardPool"] for c in order_chars if c in chars] + \
        (ctx.modeldb["AllSharedCardPools"]["items"] or [])
    ctx.relic_pool_order = [chars[c]["RelicPool"] for c in order_chars if c in chars] + \
        (ctx.modeldb["AllSharedRelicPools"]["items"] or [])
    ctx.potion_pool_order = [chars[c]["PotionPool"] for c in order_chars if c in chars] + \
        (ctx.modeldb["AllSharedPotionPools"]["items"] or [])
    ctx.pool_character = {}
    for c in order_chars:
        for prop in ("CardPool", "RelicPool", "PotionPool"):
            p = chars.get(c, {}).get(prop)
            if p:
                ctx.pool_character.setdefault(p, slugify(c))


def _load_sim_ported(ctx):
    core = os.path.join(ctx.sim_root, "Sts2Sim.Core")
    ported = {"cards": [], "powers": [], "monsters": [], "encounters": []}
    base_kind = {"CardDef": "cards", "PowerModel": "powers", "MonsterDef": "monsters", "EncounterDef": "encounters"}
    if os.path.isdir(core):
        for dirpath, dirnames, filenames in os.walk(core):
            dirnames[:] = [d for d in dirnames if d not in ("bin", "obj")]
            rel_dir = os.path.relpath(dirpath, ctx.sim_root)
            leaf = os.path.basename(dirpath)
            if leaf not in ("Defs", "Encounters"):
                continue
            for fn in sorted(filenames):
                if not fn.endswith(".cs"):
                    continue
                txt = read_text(os.path.join(dirpath, fn))
                for cm in re.finditer(r"public sealed class (\w+)\s*:\s*(CardDef|PowerModel|MonsterDef|EncounterDef)\b",
                                      txt):
                    cls, base = cm.group(1), cm.group(2)
                    body = txt[cm.end():]
                    nxt = re.search(r"\n(?:public|internal) (?:sealed |abstract |static )*class ", body)
                    if nxt:
                        body = body[:nxt.start()]
                    idm = re.search(r'const string (?:CardId|PowerId|MonsterIdEntry|EncounterIdEntry|Id)\s*=\s*"([A-Z0-9_]+)"',
                                    body)
                    sim_id = idm.group(1) if idm else None
                    excluded = bool(re.search(r"\[SimExclude\]\s*public sealed class " + cls + r"\b", txt))
                    ported[base_kind[base]].append({
                        "id": sim_id, "class": cls, "sim_file": "sim/" + os.path.join(rel_dir, fn).replace(os.sep, "/"),
                        "id_matches_slugify": sim_id == slugify(cls), "sim_exclude": excluded})
    for k in ported:
        ported[k].sort(key=lambda e: (e["id"] or "", e["class"]))
    ctx.ported = ported
    ctx.ported_ids = dict((k, set(e["id"] for e in v if e["id"] and not e["sim_exclude"])) for k, v in ported.items())


# ---------------------------------------------------------------- common analysis

def testmode_guard_ranges(f):
    s = f.masked
    ranges = []
    for m in GUARD_IF_RE.finditer(s):
        p = s.find("(", m.start())
        _, close = balanced_call_args(s, p)
        if close == -1:
            continue
        cond = s[p:close + 1]
        if "||" in cond:
            continue
        j = close + 1
        while j < len(s) and s[j] in " \t\r\n":
            j += 1
        if j < len(s) and s[j] == "{":
            e = _match_brace(s, j)
            ranges.append((j, e + 1))
        else:
            e = s.find(";", j)
            ranges.append((j, e + 1))
    for m in GUARD_ON_RE.finditer(s):
        j = m.end()
        while j < len(s) and s[j] in " \t\r\n":
            j += 1
        if j < len(s) and s[j] == "{":
            e = _match_brace(s, j)
        else:
            e = s.find(";", j)
        k = e + 1
        while k < len(s) and s[k] in " \t\r\n":
            k += 1
        if s.startswith("else", k) and not re.match(r"else\s+if\b", s[k:k + 12]):
            k2 = k + 4
            while k2 < len(s) and s[k2] in " \t\r\n":
                k2 += 1
            if k2 < len(s) and s[k2] == "{":
                e2 = _match_brace(s, k2)
                ranges.append((k2, e2 + 1))
    return ranges


def _match_brace(s, i):
    depth = 0
    n = len(s)
    while i < n:
        c = s[i]
        if c == "{":
            depth += 1
        elif c == "}":
            depth -= 1
            if depth == 0:
                return i
        i += 1
    return n - 1


def masked_without(s, ranges, base=0):
    if not ranges:
        return s
    chars = list(s)
    for x, y in ranges:
        for i in range(max(0, x - base), min(len(chars), y - base)):
            if chars[i] != "\n":
                chars[i] = " "
    return "".join(chars)


def ui_tokens_in(text, ctx):
    toks = set()
    for m in UI_CMD_RE.finditer(text):
        toks.add(m.group(0))
    for m in UI_VOCAB_RE.finditer(text):
        toks.add(m.group(0))
    for ident in set(re.findall(r"\b[A-Za-z_][A-Za-z0-9_]*\b", text)):
        if ident in ctx.node_classes and ident.startswith("N") and len(ident) > 1 and ident[1].isupper():
            toks.add(ident)
        elif ident in GODOT_TYPES:
            toks.add(ident)
    return sorted(toks)


def count_logic_lines(text):
    n = 0
    for raw in text.split("\n"):
        line = raw.strip()
        if not line:
            continue
        if any(r.search(line) for r in BOILERPLATE_RES):
            continue
        n += 1
    return n


def is_stub_body(body):
    return ("_003C_003Et__builder" in body) or bool(re.search(r"return new _003C\w+_003Ed__\d+\(", body))


def logic_members(f, td):
    """Return {owner_member_name: logic_line_count} and the set of (start,end) ranges counted."""
    counts = {}
    ranges = []

    def add(owner, text, a, b):
        if owner is None or UI_METHOD_RE.search(owner):
            return
        c = count_logic_lines(text)
        if c:
            counts[owner] = counts.get(owner, 0) + c
            ranges.append((a, b))

    for m in td.members:
        if m.kind in ("method", "operator", "indexer"):
            owner = generated_owner(m.name) or m.name
            body = member_body(f, m)
            if is_stub_body(body):
                continue
            add(owner, body, m.decl_start + len(m.head), m.end)
        elif m.kind == "property" and m.name in LOGIC_PROPERTIES:
            add(m.name, member_body(f, m), m.decl_start + len(m.head), m.end)
        elif m.kind == "type":
            owner = generated_owner(m.name)
            nested = m.type_decl
            if owner is not None:
                for sub in iter_types(nested):
                    sub_owner = generated_owner(sub.name) or owner
                    for mm in sub.members:
                        if mm.kind == "method" and mm.name == "MoveNext":
                            add(sub_owner, member_body(f, mm), mm.decl_start + len(mm.head), mm.end)
            else:
                for sub in iter_types(nested):
                    for mm in sub.members:
                        if mm.kind == "method":
                            o = generated_owner(mm.name)
                            if o is None and generated_owner(sub.name):
                                if mm.name != "MoveNext":
                                    continue
                                o = generated_owner(sub.name)
                            if o is None:
                                continue
                            body = member_body(f, mm)
                            if is_stub_body(body):
                                continue
                            add(o, body, mm.decl_start + len(mm.head), mm.end)
    return counts, ranges


def file_usings(f):
    cached = getattr(f, "_usings", None)
    if cached is None:
        cached = set(rel_ns_dir(m.group(1)) for m in re.finditer(r"(?m)^using\s+([\w.]+)\s*;", f.masked))
        f._usings = cached
    return cached


def classify_type_name(ctx, name, f=None, hint=None):
    """Kind of a type name as C# would bind it inside file f: current namespace, then usings, then hint."""
    cands = ctx.class_index.get(name)
    if not cands:
        return None
    kinds = sorted(set(k for k, _, _ in cands))
    if len(kinds) == 1:
        return kinds[0]
    if f is not None:
        here = f.rel.split("/")[0]
        local = sorted(set(k for k, ff, _ in cands if ff.rel.split("/")[0] == here))
        if len(local) == 1:
            return local[0]
        imported = file_usings(f)
        via = sorted(set(k for k, ff, _ in cands if ff.rel.split("/")[0] in imported))
        if len(via) == 1:
            return via[0]
    if hint:
        for word, k in HINT_WORDS:
            if word in hint and k in kinds:
                return k
    return None


QUALIFIED_RE = re.compile(r"\bMegaCrit\.Sts2\.Core\.(Models\.\w+(?:\.Mocks?)?)\.(\w+)\b")


def collect_refs(ctx, f, td, text):
    refs = dict((k, []) for k in REF_KINDS)
    ambiguous = []
    for m in QUALIFIED_RE.finditer(text):
        d, nm = m.group(1), m.group(2)
        for k, ff, _ in ctx.class_index.get(nm, []):
            if ff.rel.split("/")[0] == d:
                refs[k].append(nm)
    unq = re.sub(QUALIFIED_RE, " ", text)
    for m in re.finditer(r"\bModelDb\.(\w+)<(\w+)>", unq):
        k = ACCESSOR_KIND.get(m.group(1))
        nm = m.group(2)
        if k and nm in ctx.kind_classes.get(k, ()):
            refs[k].append(nm)
    for m in re.finditer(r"(\b\w+)?\s*<([\w ,.]+)>", unq):
        hint = m.group(1) or ""
        for nm in [x.strip().split(".")[-1] for x in m.group(2).split(",")]:
            if not nm or nm not in ctx.class_index:
                continue
            k = classify_type_name(ctx, nm, f, hint)
            if k:
                refs[k].append(nm)
            else:
                ambiguous.append(nm)
    for m in re.finditer(r"\b(?:typeof\(|is\s+|as\s+|\(\()(\w+)\b", unq):
        nm = m.group(1)
        k = classify_type_name(ctx, nm, f)
        if k:
            refs[k].append(nm)
    for m in re.finditer(r"\b(\w+Power)\b", unq):
        nm = m.group(1)
        if nm in ctx.kind_classes["powers"]:
            refs["powers"].append(nm)
    out = {}
    for k in REF_KINDS:
        vals = sorted(set(refs[k]))
        if vals:
            out[k] = vals
    return out, sorted(set(ambiguous))


def rng_usage(text):
    out = set()
    for m in RNG_ROOT_RE.finditer(text):
        a = m.start()
        while a > 0 and (text[a - 1].isalnum() or text[a - 1] in "_.?"):
            a -= 1
        tail = RNG_TAIL_RE.match(text, m.end())
        chain = text[a:tail.end()].replace("?.", ".").lstrip(".")
        parts = chain.split(".")
        token = None
        for i, p in enumerate(parts):
            if p == "Rng" and i > 0 and parts[i - 1] == "RunState":
                rest = parts[i + 1:]
                if rest and rest[0] in RUN_RNG_STREAMS:
                    token = "RunRng." + rest[0]
                elif rest and rest[0] == "Seed":
                    token = "RunRng.Seed"
                else:
                    token = "RunRng"
                break
            if p == "RunRng":
                rest = parts[i + 1:]
                token = "RunRng." + rest[0] if rest and rest[0] in RUN_RNG_STREAMS else "RunRng"
                break
            if p == "PlayerRng":
                rest = parts[i + 1:]
                token = "PlayerRng." + rest[0] if rest and rest[0] in PLAYER_RNG_STREAMS else "PlayerRng"
                break
            if p == "Rng":
                rest = parts[i + 1:]
                if rest and rest[0] == "Chaotic":
                    token = "Rng.Chaotic"
                elif i > 0 and parts[i - 1] in ("Random",):
                    token = "Rng.Chaotic" if rest and rest[0] == "Chaotic" else "Rng(static)"
                elif i == 0 and not rest:
                    token = None
                else:
                    token = "ModelRng." + rest[0] if rest and rest[0].startswith("Next") or (
                        rest and rest[0] in ("Shuffle", "Seed")) else "ModelRng"
                break
        if token:
            out.add(token)
    for m in RNG_TYPE_RE.finditer(text):
        out.add("%s.%s" % (m.group(1), m.group(2)))
    for m in LOCAL_RNG_RE.finditer(text):
        out.add("local:%s.%s" % (m.group(1), m.group(2)))
    return sorted(out)


def parse_dynamic_vars(ctx, f, td):
    m = top_member(td, "CanonicalVars", ("property",))
    if m is None:
        return None, None
    text = member_text(f, m)
    details = []
    for vm in re.finditer(r"\bnew\s+(\w+Var|DynamicVar)(?:<(\w+)>)?\s*\(", text):
        vtype, garg = vm.group(1), vm.group(2)
        p = vm.end() - 1
        inner, close = balanced_call_args(text, p)
        if inner is None:
            continue
        args = split_args(inner)
        name = None
        rest = list(args)
        if rest and STRING_PLACEHOLDER_RE.fullmatch(rest[0].strip() or "x"):
            name = f.literal(STRING_PLACEHOLDER_RE.fullmatch(rest[0].strip()).group(1))
            rest = rest[1:]
        elif vtype == "PowerVar" and garg:
            name = garg
        elif vtype in ctx.dynvar_default:
            name = ctx.dynvar_default[vtype]
        value = None
        props = None
        extra = []
        for a in rest:
            a2 = a.strip()
            if not a2:
                continue
            if value is None and not a2.startswith("ValueProp."):
                value = norm_num(strip_casts(f.resolve_strings(a2)))
            elif a2.startswith("ValueProp.") or "ValueProp." in a2:
                props = norm_ws(a2.replace("ValueProp.", ""))
            else:
                extra.append(norm_ws(f.resolve_strings(a2)))
        d = {"name": name, "type": vtype + ("<%s>" % garg if garg else ""), "value": value}
        if props:
            d["props"] = props
        if extra:
            d["extra_args"] = extra
        d["line"] = f.line_of(m.decl_start + text.find(vm.group(0)))
        details.append(d)
    simple = {}
    for d in details:
        if d["name"] is not None and d["name"] not in simple:
            simple[d["name"]] = d["value"]
    return simple, details


def parse_constants(f, td):
    out = {}
    for m in td.members:
        if m.kind in ("const", "field") and ("const" in m.modifiers or ("static" in m.modifiers and
                                                                       "readonly" in m.modifiers)):
            text = member_text(f, m)
            em = re.search(r"=\s*(.*);\s*$", text, re.S)
            if not em:
                continue
            val = norm_ws(f.resolve_strings(em.group(1)))
            if NUM_LIT_RE.match(val):
                out[m.name] = norm_num(val)
            elif len(val) <= 120:
                out[m.name] = val
            else:
                out[m.name] = "<expr@%d>" % m.line
    return out


def literal_overrides(f, td):
    bools = {}
    ints = {}
    for m in td.members:
        if m.kind != "property" or not m.is_override:
            continue
        e = expr_of(f, m)
        if e is None:
            continue
        if re.search(r"\bbool\b", m.head) and e in ("true", "false"):
            bools[m.name] = (e == "true")
        elif re.search(r"\b(int|decimal|float)\b", m.head) and NUM_LIT_RE.match(e):
            ints[m.name] = norm_num(e)
    return bools, ints


def enum_prop(ctx, f, td, prop, enum, dynamic):
    """Literal enum value of an overridden property, searching td then its in-tree base classes.

    Returns (value, source_tag). A non-literal getter records dynamic[field-less key prop] =
    {"at": "file:line", "candidates": [...]} and returns (None, tag).
    """
    sources = [(f, td)] + [(x[1], x[2]) for x in inherited_td(ctx, f, td)]
    for ff, tt in sources:
        m = top_member(tt, prop, ("property",))
        if m is None or not m.is_override:
            continue
        tag = "override" if tt is td else "override(%s)" % tt.name
        ex = expr_of(ff, m)
        if ex is not None and re.fullmatch(enum + r"\.\w+", ex.strip()):
            return ex.strip().split(".")[-1], tag
        body = member_text(ff, m)
        dynamic[prop] = {"at": "%s:%d" % (ff.rel, m.line),
                         "candidates": sorted(set(re.findall(enum + r"\.(\w+)", body)))}
        return None, tag
    return None, None


def inherited_td(ctx, f, td):
    """Intermediate base classes (inside the model dirs) between td and the root model base."""
    out = []
    seen = set()
    cur_f, cur_td = f, td
    while cur_td.bases:
        key = resolve_type(ctx, cur_f, cur_td.bases[0])
        if key is None or key in seen:
            break
        seen.add(key)
        ent = ctx.kind_type_index.get(key)
        if ent is None:
            break
        out.append(ent)
        cur_f, cur_td = ent[1], ent[2]
    return out


def common(ctx, kind, f, td):
    s = f.masked
    root_base = KIND_ROOT[kind]
    base = td.bases[0] if td.bases else None
    base_class = base if base and not re.match(r"^I[A-Z]", base) else None
    cat = category_type(ctx, type_key(f, td))
    e = {}
    e["id"] = slugify(td.name)
    e["model_id"] = model_id(cat, td.name) if cat else None
    e["class"] = td.name
    e["file"] = f.rel
    e["class_line"] = td.line
    e["base_class"] = base_class
    e["is_model"] = cat is not None
    e["is_abstract"] = "abstract" in td.modifiers
    e["registered"] = is_registered(ctx, f, td)
    e["is_mock"] = bool(re.search(r"\boverride bool IsMock => true", s)) or f.rel.split("/")[0].endswith(
        (".Mocks", ".Mock")) or td.name.startswith("Mock")
    e["is_deprecated"] = td.name.startswith("Deprecated")
    pub = []
    prot = []
    lines = {}
    for m in td.members:
        if m.is_override and m.name:
            if m.access == "public":
                pub.append(m.name)
            else:
                prot.append(m.name)
            lines.setdefault(m.name, m.line)
    pub = uniq(pub)
    prot = uniq(prot)
    inh = []
    for (ik, ifile, itd) in inherited_td(ctx, f, td):
        for m in itd.members:
            if m.is_override and m.name:
                inh.append(m.name)
    inh = uniq(x for x in inh if x not in pub and x not in prot)
    e["hooks_overridden"] = pub
    e["protected_overrides"] = prot
    e["inherited_overrides"] = inh
    e["abstract_model_hooks"] = [h for h in uniq(pub + inh) if h in ctx.hooks]
    e["override_lines"] = lines
    api = set()
    for m in API_RE.finditer(s):
        api.add("%s.%s" % (m.group(1), m.group(2)))
    for m in HOOK_CALL_RE.finditer(s):
        api.add("Hook.%s" % m.group(1))
    e["api_calls"] = sorted(api)
    choice = set()
    for r in CHOICE_RES:
        for m in r.finditer(s):
            tok = m.group(0)
            if CHOICE_EXCLUDE_RE.search(tok):
                continue
            choice.add(tok)
    e["needs_player_choice"] = bool(choice)
    e["choice_tokens"] = sorted(choice)
    e["offers_rewards"] = sorted(set(m.group(0) for m in re.finditer(r"\bRewardsCmd\.\w+", s)))
    guards = testmode_guard_ranges(f)
    unguarded = masked_without(s, guards)
    e["has_testmode_guard"] = bool(guards)
    toks = ui_tokens_in(unguarded, ctx)
    e["references_godot_or_ui"] = bool(toks)
    e["ui_tokens"] = toks
    counts, lranges = logic_members(f, td)
    logic_text = "\n".join(masked_without(s[a:b], guards, a) for a, b in lranges)
    e["ui_tokens_in_logic"] = ui_tokens_in(logic_text, ctx)
    refs, amb = collect_refs(ctx, f, td, s)
    if td.name in refs.get(kind, []):
        refs[kind] = [x for x in refs[kind] if x != td.name]
        if not refs[kind]:
            del refs[kind]
    e["refs"] = refs
    if amb:
        e["refs_ambiguous"] = amb
    e["rng_usage"] = rng_usage(s)
    simple, details = parse_dynamic_vars(ctx, f, td)
    e["dynamic_vars"] = simple
    e["dynamic_var_details"] = details
    e["constants"] = parse_constants(f, td)
    bools, ints = literal_overrides(f, td)
    e["bool_overrides"] = bools
    e["number_overrides"] = ints
    e["lines_total"] = f.line_count
    e["lines_logic"] = sum(counts.values())
    e["logic_members"] = dict(sorted(counts.items()))
    scopes = {}
    for h in e["abstract_model_hooks"]:
        sc = ctx.hooks[h]["scope"]
        scopes.setdefault(sc, []).append(h)
    e["hook_scopes"] = scopes
    e["ported"] = False
    return e


# ---------------------------------------------------------------- kinds

def card_pools_for(ctx, name):
    pools = []
    for pname, p in sorted(ctx.pools["card"].items()):
        if name in p["members"]:
            pools.append(pname)
    return pools


def resolve_primary(order, member_pools):
    for p in order:
        if p in member_pools:
            return p
    return None


def kind_cards(ctx, f, td, e):
    sources = [(f, td)] + [(x[1], x[2]) for x in inherited_td(ctx, f, td)]
    ctor = None
    ctor_owner = None
    base_args = None
    for ff, tt in sources:
        for m in tt.members:
            if m.kind == "ctor":
                bm = re.search(r":\s*base\s*\(", m.head)
                if bm:
                    p = m.head.find("(", bm.start())
                    inner, _ = balanced_call_args(m.head, p)
                    if inner is not None:
                        ctor, ctor_owner, base_args = m, tt, split_args(inner)
                break
        if ctor is not None:
            break
    vals = {"cost": None, "type": None, "rarity": None, "target_type": None}
    src = {}
    shows = None
    if base_args and len(base_args) >= 4:
        c0 = base_args[0].strip()
        vals["cost"] = int(c0) if re.fullmatch(r"-?\d+", c0) else norm_ws(c0)
        vals["type"] = base_args[1].strip().replace("CardType.", "")
        vals["rarity"] = base_args[2].strip().replace("CardRarity.", "")
        vals["target_type"] = base_args[3].strip().replace("TargetType.", "")
        tag = "ctor" if ctor_owner is td else "ctor(%s)" % ctor_owner.name
        for k in vals:
            src[k] = tag
        shows = True
        for a in base_args[4:]:
            a = a.strip()
            if "shouldShowInCardLibrary" in a or a in ("true", "false"):
                shows = a.split(":")[-1].strip() == "true"
    dynamic = {}
    for field, prop, enum in (("type", "Type", "CardType"), ("rarity", "Rarity", "CardRarity"),
                              ("target_type", "TargetType", "TargetType"), ("cost", "CanonicalEnergyCost", None)):
        for ff, tt in sources:
            m = top_member(tt, prop, ("property",))
            if m is None or not m.is_override:
                continue
            ex = expr_of(ff, m)
            tag = "override" if tt is td else "override(%s)" % tt.name
            lit = None
            if ex is not None:
                if enum and re.fullmatch(enum + r"\.\w+", ex):
                    lit = ex.split(".")[-1]
                elif enum is None and re.fullmatch(r"-?\d+", ex):
                    lit = int(ex)
            if lit is not None:
                vals[field] = lit
                src[field] = tag
            else:
                dynamic[field] = "%s@%s:%d" % (norm_ws(ex) if ex and len(ex) <= 60 else "<getter>", ff.rel, m.line)
            break
    e["rarity"] = vals["rarity"]
    e["type"] = vals["type"]
    e["cost"] = vals["cost"]
    e["costs_x"] = e["bool_overrides"].get("HasEnergyCostX", False)
    star = e["number_overrides"].get("CanonicalStarCost")
    e["star_cost"] = int(star) if star is not None and re.fullmatch(r"-?\d+", str(star)) else (star if star else None)
    e["star_costs_x"] = e["bool_overrides"].get("HasStarCostX", False)
    e["target_type"] = vals["target_type"]
    e["field_sources"] = src
    e["dynamic_fields"] = dynamic
    e["shows_in_card_library"] = shows
    e["ctor_line"] = ctor.line if ctor is not None and ctor_owner is td else None
    kw = top_member(td, "CanonicalKeywords", ("property",))
    e["keywords"] = re.findall(r"CardKeyword\.(\w+)", member_text(f, kw)) if kw else []
    tg = top_member(td, "CanonicalTags", ("property",))
    e["tags"] = re.findall(r"CardTag\.(\w+)", member_text(f, tg)) if tg else []
    up = top_member(td, "OnUpgrade", ("method",))
    upgrade = []
    if up is not None:
        body = member_body(f, up)
        for st in re.findall(r"[^;{}]+;", body):
            st = norm_ws(f.resolve_strings(st))
            st = re.sub(r"\bbase\.", "", st)
            if st and st not in ("return;",):
                upgrade.append(st)
    e["upgrade"] = upgrade
    mul = e["number_overrides"].get("MaxUpgradeLevel")
    e["max_upgrade_level"] = int(mul) if mul is not None else None
    pools = card_pools_for(ctx, td.name)
    e["pools"] = [ctx.pools["card"][p]["id"] for p in pools]
    chain = chain_of(ctx, type_key(f, td))
    if "MockCardModel" in chain or td.name == "MockCardModel":
        prim = "DeprivedCardPool"
        e["pool_rule"] = "mock_override"
    else:
        prim = resolve_primary(ctx.card_pool_order, pools)
        e["pool_rule"] = "modeldb_order" if prim else None
        if prim is None and "MockCardPool" in pools:
            prim = "MockCardPool"
            e["pool_rule"] = "mock_pool_fallback"
        if prim is None and pools:
            prim = pools[0]
            e["pool_rule"] = "outside_modeldb_pools"
    e["pool"] = ctx.pools["card"][prim]["id"] if prim else "unlisted"
    e["visual_pool_override"] = "VisualCardPool" in e["hooks_overridden"]
    e["character"] = ctx.pool_character.get(prim) if prim else None
    e["powers_referenced"] = e["refs"].get("powers", [])
    e["cards_referenced"] = [c for c in e["refs"].get("cards", []) if c != td.name]


def kind_powers(ctx, f, td, e):
    pm = ctx.base_files["PowerModel"].main_type()
    defaults = {}
    for prop, enum in (("InstanceType", "PowerInstanceType"),):
        bm = top_member(pm, prop, ("property",))
        if bm is not None:
            ex = expr_of(ctx.base_files["PowerModel"], bm)
            defaults[prop] = ex.split(".")[-1] if ex else None
    bm = top_member(pm, "AllowNegative", ("property",))
    defaults["AllowNegative"] = (expr_of(ctx.base_files["PowerModel"], bm) == "true") if bm else None
    inh = inherited_td(ctx, f, td)
    sources = [(f, td)] + [(x[1], x[2]) for x in inh]

    dynamic = {}
    defaulted = []
    ptype, ptag = enum_prop(ctx, f, td, "Type", "PowerType", dynamic)
    stack, stag = enum_prop(ctx, f, td, "StackType", "PowerStackType", dynamic)
    inst, itag = enum_prop(ctx, f, td, "InstanceType", "PowerInstanceType", dynamic)
    if inst is None and itag is None:
        inst = defaults.get("InstanceType")
        defaulted.append("instance_type")
    pinh = bool(ptag and ptag.startswith("override("))
    sinh = bool(stag and stag.startswith("override("))
    iinh = bool(itag and itag.startswith("override("))
    allow = None
    for (ff, tt) in sources:
        bools, _ = literal_overrides(ff, tt)
        if "AllowNegative" in bools:
            allow = bools["AllowNegative"]
            break
    if allow is None:
        allow = defaults.get("AllowNegative")
        defaulted.append("allow_negative")
    e["power_type"] = ptype
    e["stack_type"] = stack
    e["instance_type"] = inst
    e["allow_negative"] = allow
    e["defaulted"] = defaulted
    e["dynamic_fields"] = dynamic
    e["inherited_from_base"] = [x for x, b in (("power_type", pinh), ("stack_type", sinh),
                                               ("instance_type", iinh)) if b]
    e["is_temporary"] = any("ITemporaryPower" in b for b in td.bases) or any(
        "ITemporaryPower" in b for (_, _, t) in inh for b in t.bases)
    e["powers_referenced"] = [p for p in e["refs"].get("powers", []) if p != td.name]
    e["referenced_by"] = None


def kind_relics(ctx, f, td, e):
    dynamic = {}
    e["rarity"], _ = enum_prop(ctx, f, td, "Rarity", "RelicRarity", dynamic)
    e["dynamic_fields"] = dynamic
    pools = [p for p, info in sorted(ctx.pools["relic"].items()) if td.name in info["members"]]
    prim = resolve_primary(ctx.relic_pool_order, pools)
    e["pools"] = [ctx.pools["relic"][p]["id"] for p in pools]
    e["pool"] = ctx.pools["relic"][prim]["id"] if prim else ("unlisted" if not pools else ctx.pools["relic"][pools[0]]["id"])
    e["character"] = ctx.pool_character.get(prim) if prim else None
    starters = []
    for cf in ctx.files["characters"]:
        ctd = cf.main_type()
        m = top_member(ctd, "StartingRelics", ("property",))
        if m is not None and td.name in re.findall(r"ModelDb\.Relic<(\w+)>", member_text(cf, m)):
            starters.append(slugify(ctd.name))
    e["starting_relic_of"] = starters
    combat = [h for h in e["abstract_model_hooks"] if any(s in ("combat", "both") for s in ctx.hooks[h]["scopes"])]
    run = [h for h in e["abstract_model_hooks"] if any(s in ("run", "both") for s in ctx.hooks[h]["scopes"])]
    run_members = [m for m in ("AfterObtained", "AfterRemoved", "IsAllowed", "IsAllowedAtNeow") if
                   m in e["hooks_overridden"]]
    if e["bool_overrides"].get("HasUponPickupEffect"):
        run_members.append("HasUponPickupEffect")
    room_hooks = [h for h in ("BeforeRoomEntered", "AfterRoomEntered") if h in e["abstract_model_hooks"] and
                  COMBAT_ROOM_RE.search(owned_text(f, td, h))]
    e["combat_room_entry_hooks"] = room_hooks
    e["combat_relevant"] = bool(combat or room_hooks)
    e["run_relevant"] = bool(run or run_members)
    e["run_members"] = run_members


def kind_potions(ctx, f, td, e):
    dynamic = {}
    e["rarity"], _ = enum_prop(ctx, f, td, "Rarity", "PotionRarity", dynamic)
    usage, _ = enum_prop(ctx, f, td, "Usage", "PotionUsage", dynamic)
    e["usage"] = usage
    table = {"CombatOnly": (True, False), "AnyTime": (True, True), "Automatic": (False, False)}
    inc, outc = table.get(usage, (None, None))
    e["usable_in_combat"] = inc
    e["usable_out_of_combat"] = outc
    e["target_type"], _ = enum_prop(ctx, f, td, "TargetType", "TargetType", dynamic)
    e["dynamic_fields"] = dynamic
    pools = [p for p, info in sorted(ctx.pools["potion"].items()) if td.name in info["members"]]
    prim = resolve_primary(ctx.potion_pool_order, pools)
    e["pools"] = [ctx.pools["potion"][p]["id"] for p in pools]
    e["pool"] = ctx.pools["potion"][prim]["id"] if prim else ("unlisted" if not pools else ctx.pools["potion"][pools[0]]["id"])
    e["character"] = ctx.pool_character.get(prim) if prim else None
    e["can_be_generated_in_combat"] = e["bool_overrides"].get("CanBeGeneratedInCombat", True)


def _hp_expr(ctx, f, td, prop):
    for (ff, tt) in [(f, td)] + [(x[1], x[2]) for x in inherited_td(ctx, f, td)]:
        m = top_member(tt, prop, ("property",))
        if m is not None:
            ex = expr_of(ff, m)
            raw = norm_ws(member_body(ff, m)) if ex is None else norm_ws(ex)
            return raw, ff.rel + ":" + str(m.line)
    return None, None


def _hp_parse(raw):
    if raw is None:
        return None
    r = raw.strip()
    if re.fullmatch(r"-?\d+", r):
        return {"a0": int(r), "ascended": int(r), "level": None}
    m = ASC_RE.fullmatch(r)
    if m:
        return {"a0": int(float(m.group(3))), "ascended": int(float(m.group(2))), "level": m.group(1)}
    return None


def kind_monsters(ctx, f, td, e):
    s = f.masked
    mn, mn_c = _hp_expr(ctx, f, td, "MinInitialHp")
    mx, mx_c = _hp_expr(ctx, f, td, "MaxInitialHp")
    e["min_hp_raw"] = mn
    e["max_hp_raw"] = mx
    e["hp_citation"] = [c for c in (mn_c, mx_c) if c]
    e["min_hp"] = _hp_parse(mn)
    if mx is not None and mx.strip() == "MinInitialHp":
        e["max_hp"] = e["min_hp"]
    else:
        e["max_hp"] = _hp_parse(mx)
    e["ascension_branches"] = len(re.findall(r"\bHasAscension\s*\(", s)) + len(
        re.findall(r"\bGetValueIfAscension\s*\(", s))
    e["ascension_levels"] = sorted(set(re.findall(r"AscensionLevel\.(\w+)", s)))
    reads = []
    for mm in re.finditer(r"(?<![\w.])(?:base|this|[a-z_][A-Za-z0-9_]*)\??\.Rng\??\.(\w+)", s):
        reads.append((f.line_of(mm.start()), mm.group(1)))
    plain = [(f.line_of(mm.start()), mm.group(1)) for mm in re.finditer(r"(?<![\w.])Rng\.(Next\w*|Shuffle)\b", s)]
    plain += [(f.line_of(mm.start()), "<passed as argument>")
              for mm in re.finditer(r"(?<![\w.])base\.Rng\b(?!\s*\.)", s)]
    allreads = sorted(set(reads + plain))
    owners = []
    for ln, _ in allreads:
        for m in td.members:
            if m.attr_line <= ln <= m.end_line:
                owners.append(generated_owner(m.name) or m.name)
                break
    e["uses_private_rng"] = bool(allreads)
    e["private_rng_reads"] = [{"line": ln, "call": c} for ln, c in allreads]
    e["private_rng_members"] = uniq(owners)
    e["run_rng_streams"] = sorted(set(t for t in e["rng_usage"] if t.startswith("RunRng.")))
    e["chaotic_rng"] = "Rng.Chaotic" in e["rng_usage"]
    e["powers_referenced"] = e["refs"].get("powers", [])
    moves = []
    e["move_states_from"] = None
    for ff, tt in [(f, td)] + [(x[1], x[2]) for x in inherited_td(ctx, f, td)]:
        gm = top_member(tt, "GenerateMoveStateMachine", ("method",))
        if gm is None:
            continue
        text = member_text(ff, gm)
        for mm in re.finditer(r"\bnew\s+(MoveState|RandomBranchState|ConditionalBranchState)\s*\(\s*\"STR(\d+)\"", text):
            moves.append({"state": ff.literal(mm.group(2)), "kind": mm.group(1)})
        e["move_states_from"] = "%s:%d" % (ff.rel, gm.line)
        break
    e["move_states"] = moves
    e["intents"] = sorted(set(re.findall(r"\bnew\s+(\w+Intent)\s*\(", s)))
    e["encounters"] = []


def kind_encounters(ctx, f, td, e):
    s = f.masked
    dynamic = {}
    e["room_type"], _ = enum_prop(ctx, f, td, "RoomType", "RoomType", dynamic)
    e["dynamic_fields"] = dynamic
    e["is_weak"] = e["bool_overrides"].get("IsWeak", False)
    tg = top_member(td, "Tags", ("property",))
    e["tags"] = re.findall(r"EncounterTag\.(\w+)", member_text(f, tg)) if tg else []
    e["monsters"] = uniq(re.findall(r"ModelDb\.Monster<(\w+)>", s))
    gm = top_member(td, "GenerateMonsters", ("method",))
    e["monsters_in_generate"] = uniq(re.findall(r"ModelDb\.Monster<(\w+)>", member_text(f, gm))) if gm else []
    e["generate_line"] = gm.line if gm else None
    sl = top_member(td, "Slots", ("property",))
    e["slots"] = f.string_values(member_text(f, sl)) if sl else []
    reads = re.findall(r"(?<![\w.])(?:base\.)?Rng\.(\w+)", s)
    if re.search(r"(?<![\w.])base\.Rng\b(?!\s*\.)", s):
        reads.append("<passed as argument>")
    e["uses_encounter_rng"] = bool(reads)
    e["encounter_rng_calls"] = sorted(set(reads))
    e["min_gold_reward"] = e["number_overrides"].get("MinGoldReward")
    e["max_gold_reward"] = e["number_overrides"].get("MaxGoldReward")
    e["should_give_rewards"] = e["bool_overrides"].get("ShouldGiveRewards", True)
    suffix = None
    for suf, k in (("Weak", "weak"), ("Normal", "normal"), ("Elite", "elite"), ("Boss", "boss")):
        if td.name.endswith(suf):
            suffix = k
    if "EventEncounter" in td.name or td.name.endswith("EventEncounter"):
        suffix = "event"
    e["name_suffix_kind"] = suffix
    rt = e["room_type"]
    if rt == "Monster":
        rk = "weak" if e["is_weak"] else "normal"
    elif rt == "Elite":
        rk = "elite"
    elif rt == "Boss":
        rk = "boss"
    else:
        rk = None
    e["room_kind"] = rk
    e["act"] = None
    e["acts"] = []
    e["act_index"] = None
    e["kind"] = None
    e["referenced_by_events"] = []


def kind_events(ctx, f, td, e):
    s = f.masked
    chain = [td.name] + chain_of(ctx, type_key(f, td))
    e["is_ancient"] = "AncientEventModel" in chain
    e["is_shared"] = e["bool_overrides"].get("IsShared", False)
    opts = re.findall(r"\bnew\s+EventOption\s*\(", s)
    relic_opts = re.findall(r"(?<![\w.])RelicOption<(\w+)>\s*\(", s)
    relic_opts_dyn = [m for m in re.finditer(r"(?<![\w.<])RelicOption\s*\(", s)
                      if not re.search(r"EventOption\s+$", s[max(0, m.start() - 40):m.start()])]
    helper_opts = re.findall(r"(?<![\w.])(CreateProceedOption|CreateLockedOption)\s*\(", s)
    e["options_count"] = len(opts) + len(relic_opts) + len(relic_opts_dyn)
    e["option_constructs"] = {"new EventOption": len(opts), "RelicOption<T>": len(relic_opts),
                              "RelicOption(relic)": len(relic_opts_dyn), "helpers": len(helper_opts)}
    keys = []
    for m in re.finditer(r"\bnew\s+EventOption\s*\(", s):
        inner, _ = balanced_call_args(s, m.end() - 1)
        if inner is None:
            continue
        for a in split_args(inner):
            sm = STRING_PLACEHOLDER_RE.fullmatch(a.strip())
            if sm:
                keys.append(f.literal(sm.group(1)))
                break
    e["option_keys"] = uniq(keys)
    e["relic_options"] = uniq(relic_opts)
    pages = set()
    for lit in f.literals:
        for pm in re.finditer(r"\.pages\.([A-Z0-9_]+)", lit.value):
            pages.add(pm.group(1))
    e["pages"] = sorted(pages)
    e["encounters_referenced"] = e["refs"].get("encounters", [])
    ce = top_member(td, "CanonicalEncounter", ("property",))
    e["canonical_encounter"] = (re.findall(r"ModelDb\.Encounter<(\w+)>", member_text(f, ce)) or [None])[0] if ce else None
    e["cards_referenced"] = e["refs"].get("cards", [])
    e["relics_referenced"] = e["refs"].get("relics", [])
    e["potions_referenced"] = e["refs"].get("potions", [])
    ia = top_member(td, "IsAllowed", ("method",))
    e["is_allowed_raw"] = None
    e["is_allowed_line"] = ia.line if ia is not None else None
    e["is_allowed_refs"] = []
    if ia is not None:
        ex = expr_of(f, ia)
        if ex is not None and len(ex) <= 160:
            e["is_allowed_raw"] = norm_ws(f.resolve_strings(ex))
        body = member_body(f, ia)
        e["is_allowed_refs"] = sorted(set(re.findall(r"\.([A-Z]\w+)", body)))
    e["acts"] = []
    e["shared_pool"] = None
    e["pool_status"] = None


def kind_acts(ctx, f, td, e):
    num = e["number_overrides"]
    idx = num.get("Index")
    e["index"] = int(idx) if idx is not None and re.fullmatch(r"-?\d+", str(idx)) else idx
    e["is_default"] = e["bool_overrides"].get("IsDefault")
    nw = num.get("NumberOfWeakEncounters")
    e["number_of_weak_encounters"] = int(nw) if nw is not None else None
    br = num.get("BaseNumberOfRooms")
    e["base_number_of_rooms"] = int(br) if br is not None else None
    ge = top_member(td, "GenerateAllEncounters", ("method",))
    e["encounters"] = re.findall(r"ModelDb\.Encounter<(\w+)>", member_text(f, ge)) if ge else []
    e["encounters_declared_count"] = None
    if ge is not None:
        dm = re.search(r"new \w+\[(\d+)\]", member_text(f, ge))
        e["encounters_declared_count"] = int(dm.group(1)) if dm else (0 if "Array.Empty" in member_text(f, ge) else None)
    bd = top_member(td, "BossDiscoveryOrder", ("property",))
    e["boss_discovery_order"] = re.findall(r"ModelDb\.Encounter<(\w+)>", member_text(f, bd)) if bd else []
    an = top_member(td, "AllAncients", ("property",))
    e["ancients"] = re.findall(r"ModelDb\.AncientEvent<(\w+)>", member_text(f, an)) if an else []
    ev = top_member(td, "AllEvents", ("property",))
    e["events"] = re.findall(r"ModelDb\.Event<(\w+)>", member_text(f, ev)) if ev else []
    e["events_declared_count"] = None
    if ev is not None:
        dm = re.search(r"new \w+\[(\d+)\]", member_text(f, ev))
        e["events_declared_count"] = int(dm.group(1)) if dm else None
    mp = top_member(td, "GetMapPointTypes", ("method",))
    stm = []
    if mp is not None:
        for st in re.findall(r"[^;{}]+;", member_body(f, mp)):
            stm.append(norm_ws(st))
    e["map_params"] = {
        "number_of_weak_encounters": e["number_of_weak_encounters"],
        "base_number_of_rooms": e["base_number_of_rooms"],
        "get_map_point_types": stm,
        "get_map_point_types_line": mp.line if mp else None,
    }
    ad = top_member(td, "ApplyActDiscoveryOrderModifications", ("method",))
    swaps = []
    if ad is not None:
        for m in re.finditer(r"SwapToOrCreateAtIndex<(\w+), (\w+)>\(_rooms\.(\w+), (\d+)\)", member_text(f, ad)):
            swaps.append({"set": m.group(3), "model": m.group(2), "index": int(m.group(4))})
    e["first_run_order"] = swaps
    ua = top_member(td, "GetUnlockedAncients", ("method",))
    e["ancient_unlock_gates"] = re.findall(r"IsEpochRevealed<(\w+)>", member_text(f, ua)) if ua else []
    iu = top_member(td, "IsUnlocked", ("method",))
    e["is_unlocked_raw"] = norm_ws(member_body(f, iu)) if iu else None
    for k in ("weak", "normal", "elite", "boss"):
        e["encounters_" + k] = []
    e["encounters_unclassified"] = []


def kind_characters(ctx, f, td, e):
    num = e["number_overrides"]
    cm = ctx.base_files["CharacterModel"].main_type()

    def base_default(prop):
        m = top_member(cm, prop, ("property",))
        if m is None:
            return None
        ex = expr_of(ctx.base_files["CharacterModel"], m)
        return int(ex) if ex and re.fullmatch(r"-?\d+", ex) else ex

    defaulted = []

    def num_or_default(prop):
        if prop in num:
            v = num[prop]
            return int(v) if re.fullmatch(r"-?\d+", str(v)) else v
        defaulted.append(prop)
        return base_default(prop)

    e["starting_hp"] = num_or_default("StartingHp")
    e["starting_gold"] = num_or_default("StartingGold")
    e["max_energy"] = num_or_default("MaxEnergy")
    e["base_orb_slot_count"] = num_or_default("BaseOrbSlotCount")
    e["defaulted"] = defaulted
    sd = top_member(td, "StartingDeck", ("property",))
    e["starting_deck"] = [slugify(x) for x in re.findall(r"ModelDb\.Card<(\w+)>", member_text(f, sd))] if sd else []
    sr = top_member(td, "StartingRelics", ("property",))
    e["starting_relics"] = [slugify(x) for x in re.findall(r"ModelDb\.Relic<(\w+)>", member_text(f, sr))] if sr else []
    sp = top_member(td, "StartingPotions", ("property",))
    e["starting_potions"] = [slugify(x) for x in re.findall(r"ModelDb\.Potion<(\w+)>", member_text(f, sp))] if sp else []
    pools = ctx.char_pools.get(td.name, {})
    e["card_pool"] = ctx.pools["card"][pools["CardPool"]]["id"] if pools.get("CardPool") in ctx.pools["card"] else None
    e["relic_pool"] = ctx.pools["relic"][pools["RelicPool"]]["id"] if pools.get("RelicPool") in ctx.pools["relic"] else None
    e["potion_pool"] = ctx.pools["potion"][pools["PotionPool"]]["id"] if pools.get("PotionPool") in ctx.pools["potion"] else None
    e["card_pool_class"] = pools.get("CardPool")
    e["relic_pool_class"] = pools.get("RelicPool")
    e["potion_pool_class"] = pools.get("PotionPool")
    e["is_playable"] = e["bool_overrides"].get("IsPlayable", True)
    order = ctx.modeldb["AllCharacters"]["items"] or []
    e["all_characters_index"] = order.index(td.name) if td.name in order else None


def kind_plain(ctx, f, td, e):
    pass


def kind_orbs(ctx, f, td, e):
    for prop in ("PassiveVal", "EvokeVal"):
        m = top_member(td, prop, ("property",))
        key = "passive_val_raw" if prop == "PassiveVal" else "evoke_val_raw"
        e[key] = norm_ws(expr_of(f, m) or member_body(f, m)) if m else None
    order = ctx.modeldb["Orbs"]["items"] or []
    e["modeldb_orbs_index"] = order.index(td.name) if td.name in order else None


def kind_modifiers(ctx, f, td, e):
    good = ctx.modeldb["GoodModifiers"]["items"] or []
    bad = ctx.modeldb["BadModifiers"]["items"] or []
    e["modifier_list"] = "good" if td.name in good else ("bad" if td.name in bad else None)
    e["mutually_exclusive"] = td.name in (ctx.modeldb["MutuallyExclusiveModifiers"]["items"] or [])
    e["clears_player_deck"] = e["bool_overrides"].get("ClearsPlayerDeck", False)


KIND_FN = {
    "cards": kind_cards, "powers": kind_powers, "relics": kind_relics, "potions": kind_potions,
    "monsters": kind_monsters, "encounters": kind_encounters, "events": kind_events, "acts": kind_acts,
    "characters": kind_characters, "enchantments": kind_plain, "afflictions": kind_plain,
    "modifiers": kind_modifiers, "orbs": kind_orbs,
}


# ---------------------------------------------------------------- cross-kind resolution

def build_entries(ctx):
    entries = {}
    non_model = {}
    for kind in KIND_NAMES:
        lst = []
        nm = []
        for f in ctx.files[kind]:
            td = f.main_type()
            if td is None:
                nm.append({"file": f.rel, "reason": "no top-level type"})
                continue
            cat = category_type(ctx, type_key(f, td))
            if cat is None:
                nm.append({"file": f.rel, "class": td.name, "bases": td.bases,
                           "reason": "type does not derive from AbstractModel"})
                continue
            e = common(ctx, kind, f, td)
            KIND_FN[kind](ctx, f, td, e)
            lst.append(e)
        lst.sort(key=lambda x: (x["id"], x["file"]))
        entries[kind] = lst
        non_model[kind] = nm
    resolve_cross(ctx, entries)
    external_refs(ctx, entries)
    return entries, non_model


def resolve_cross(ctx, entries):
    by_class = dict((k, dict((e["class"], e) for e in entries[k])) for k in entries)
    for k in ("cards", "powers", "monsters", "encounters"):
        for e in entries[k]:
            e["ported"] = e["id"] in ctx.ported_ids.get(k, set())
    token_files = {}
    model_dirs = [d for d in os.listdir(ctx.root) if d.startswith(NS_PREFIX + "Models")]
    all_files = []
    for kind in KIND_NAMES:
        all_files.extend((kind, f) for f in ctx.files[kind])
    seen = set(f.rel for _, f in all_files)
    for d in sorted(model_dirs):
        nd = rel_ns_dir(d)
        for fn in list_cs(ctx.root, nd):
            rel = nd + "/" + fn
            if rel in seen:
                continue
            all_files.append((None, load_file(ctx.root, nd, fn)))
            seen.add(rel)
    power_names = ctx.kind_classes["powers"]
    for kind, f in all_files:
        toks = set(re.findall(r"\b\w+Power\b", f.masked)) & power_names
        for t in toks:
            token_files.setdefault(t, []).append((kind or _dir_kind(f.rel), f.rel))
    for e in entries["powers"]:
        lst = [x for x in token_files.get(e["class"], []) if x[1] != e["file"]]
        e["referenced_by"] = len(lst)
        kinds = {}
        for k, _ in lst:
            kinds[k] = kinds.get(k, 0) + 1
        e["referenced_by_kinds"] = dict(sorted(kinds.items()))
    acts = dict((e["class"], e) for e in entries["acts"])
    enc = by_class["encounters"]
    for aname, a in acts.items():
        for en in a["encounters"]:
            x = enc.get(en)
            if x is None:
                a["encounters_unclassified"].append(en)
                continue
            if aname not in x["acts"]:
                x["acts"].append(aname)
            rk = x["room_kind"]
            if rk:
                a["encounters_" + rk].append(en)
            else:
                a["encounters_unclassified"].append(en)
    event_enc = set(ctx.modeldb["EventEncounters"]["items"] or [])
    for ev in entries["events"]:
        for en in ev["encounters_referenced"]:
            if en in enc and ev["class"] not in enc[en]["referenced_by_events"]:
                enc[en]["referenced_by_events"].append(ev["class"])
    for x in entries["encounters"]:
        x["acts"].sort(key=lambda n: [e["class"] for e in entries["acts"]].index(n))
        if x["acts"]:
            x["act"] = x["acts"][0] if len(x["acts"]) == 1 else None
            idxs = sorted(set(acts[a]["index"] for a in x["acts"]))
            x["act_index"] = idxs[0] if len(idxs) == 1 else idxs
            x["kind"] = x["room_kind"]
        elif x["class"] in event_enc or x["referenced_by_events"]:
            x["kind"] = "event"
        elif x["is_mock"]:
            x["kind"] = "mock"
        elif x["is_abstract"]:
            x["kind"] = "abstract"
        else:
            x["kind"] = "unlisted"
        x["in_modeldb_event_encounters"] = x["class"] in event_enc
        sk = x["name_suffix_kind"]
        x["kind_name_mismatch"] = bool(sk and x["kind"] in ("weak", "normal", "elite", "boss") and sk != x["kind"])
    mon = by_class["monsters"]
    for x in entries["encounters"]:
        for mname in x["monsters"]:
            if mname in mon and x["class"] not in mon[mname]["encounters"]:
                mon[mname]["encounters"].append(x["class"])
    for m in entries["monsters"]:
        m["encounters"].sort()
        acts_set = set()
        for en in m["encounters"]:
            acts_set.update(enc[en]["acts"])
        m["acts"] = sorted(acts_set)
    shared_events = set(ctx.modeldb["AllSharedEvents"]["items"] or [])
    shared_anc = set(ctx.modeldb["AllSharedAncients"]["items"] or [])
    for ev in entries["events"]:
        for aname, a in acts.items():
            if ev["class"] in a["events"] or ev["class"] in a["ancients"]:
                ev["acts"].append(aname)
        if ev["class"] in shared_events:
            ev["shared_pool"] = "AllSharedEvents"
        elif ev["class"] in shared_anc:
            ev["shared_pool"] = "AllSharedAncients"
        if ev["acts"] or ev["shared_pool"]:
            ev["pool_status"] = "listed"
        elif ev["is_mock"]:
            ev["pool_status"] = "mock"
        elif ev["is_deprecated"]:
            ev["pool_status"] = "deprecated"
        else:
            ev["pool_status"] = "unlisted"
    rev = {}
    for k in KIND_NAMES:
        for e in entries[k]:
            for rk, names in e["refs"].items():
                for n in names:
                    if rk == k and n == e["class"]:
                        continue
                    rev.setdefault((rk, n), []).append("%s/%s" % (k, e["class"]))
    for k in KIND_NAMES:
        for e in entries[k]:
            e["referenced_by_models"] = sorted(set(rev.get((k, e["class"]), [])))
    combat_kinds = ("cards/", "powers/", "monsters/", "orbs/", "afflictions/", "enchantments/", "potions/")
    for r in entries["relics"]:
        cons = [x for x in r["referenced_by_models"] if x.startswith(combat_kinds)]
        r["consulted_by"] = cons
        r["combat_relevant_indirect"] = bool(cons)
    deck_of = {}
    for c in entries["characters"]:
        for cid in c["starting_deck"]:
            deck_of.setdefault(cid, [])
            if c["id"] not in deck_of[cid]:
                deck_of[cid].append(c["id"])
    for card in entries["cards"]:
        card["starting_deck_of"] = deck_of.get(card["id"], [])


def external_refs(ctx, entries):
    """For entries no pool/act/encounter lists, find code references outside the model kind dirs."""
    targets = {}
    for k in ("cards", "relics", "potions", "monsters", "encounters", "events"):
        for e in entries[k]:
            unresolved = (
                (k in ("cards", "relics", "potions") and e.get("pool") == "unlisted") or
                (k == "monsters" and not e.get("encounters")) or
                (k == "encounters" and not e.get("acts")) or
                (k == "events" and e.get("pool_status") != "listed"))
            if unresolved and not e["is_mock"]:
                targets.setdefault(e["class"], []).append(e)
                e["external_refs"] = []
    if not targets:
        return
    kind_dirs = set()
    for k, dirs, _ in KINDS:
        kind_dirs.update(NS_PREFIX + d for d in dirs)
    names_re = re.compile(r"\b(" + "|".join(sorted(map(re.escape, targets), key=len, reverse=True)) + r")\b")
    for d in sorted(os.listdir(ctx.root)):
        full = os.path.join(ctx.root, d)
        if not os.path.isdir(full) or d in kind_dirs:
            continue
        for fn in sorted(os.listdir(full)):
            if not fn.endswith(".cs") or fn == "AbstractModelSubtypes.cs":
                continue
            path = os.path.join(full, fn)
            try:
                raw = read_text(path)
            except OSError:
                continue
            if not names_re.search(raw):
                continue
            cf = CsFile(path, rel_ns_dir(d) + "/" + fn)
            hits = {}
            for m in names_re.finditer(cf.masked):
                hits.setdefault(m.group(1), cf.line_of(m.start()))
            for name, line in hits.items():
                for e in targets[name]:
                    e["external_refs"].append("%s:%d" % (cf.rel, line))


def _dir_kind(rel):
    d = rel.split("/")[0]
    for k, dirs, _ in KINDS:
        if d in dirs:
            return k
    return d


# ---------------------------------------------------------------- outputs

def source_digest(files):
    h = hashlib.sha256()
    for f in sorted(files, key=lambda x: x.rel):
        h.update(f.rel.encode("utf-8"))
        h.update(b"\0")
        h.update(f.text.encode("utf-8"))
        h.update(b"\0")
    return h.hexdigest()


def manifest(ctx, kind, entries, non_model):
    files = ctx.files[kind]
    return {
        "schema": SCHEMA,
        "kind": kind,
        "game_version": GAME_VERSION,
        "source_dirs": KIND_DIRS[kind],
        "source_digest": source_digest(files),
        "generated_by": "sim/tools/inventory/extract_models.py",
        "files_scanned": len(files),
        "count": len(entries),
        "non_model_files": non_model,
        "entries": entries,
    }


def hooks_manifest(ctx, entries):
    usage = {}
    for k in KIND_NAMES:
        for e in entries[k]:
            if e["is_mock"]:
                continue
            for h in e["abstract_model_hooks"]:
                usage.setdefault(h, {}).setdefault(k, 0)
                usage[h][k] += 1
    hooks = []
    for name in ctx.hook_order:
        h = dict(ctx.hooks[name])
        h["in_sim"] = name in ctx.sim_hook_virtuals
        by = usage.get(name, {})
        h["overridden_by_count"] = sum(by.values())
        h["overridden_by_kinds"] = dict(sorted(by.items()))
        hooks.append(h)
    disp = []
    for name in sorted(ctx.dispatchers):
        d = dict(ctx.dispatchers[name])
        d["in_sim"] = name in ctx.sim_hook_dispatchers
        disp.append(d)
    return {
        "schema": SCHEMA, "kind": "hooks", "game_version": GAME_VERSION,
        "abstract_model_file": "Models/AbstractModel.cs", "hook_file": "Hooks/Hook.cs",
        "hook_count": len(hooks), "hooks_in_sim": sum(1 for h in hooks if h["in_sim"]),
        "dispatcher_count": sum(1 for d in disp if d["public"]),
        "dispatchers_in_sim": sum(1 for d in disp if d["public"] and d["in_sim"]),
        "scope_legend": {
            "combat": "dispatcher iterates combatState.IterateHookListeners() / IterateCombatHookListeners(combatState)",
            "run": "dispatcher iterates runState.IterateHookListeners(null)",
            "both": "dispatcher iterates runState.IterateHookListeners(combatState) (run listeners, plus combat ones when a combat is live)",
            "direct": "dispatcher calls the hook on a specific model, no listener iteration",
            "unknown": "not called from any Hook.cs dispatcher",
        },
        "hooks": hooks, "dispatchers": disp,
    }


def pools_manifest(ctx):
    md = {}
    for k, v in ctx.modeldb.items():
        md[k] = {"items": v["items"], "line": v["line"]}
    return {
        "schema": SCHEMA, "kind": "pools", "game_version": GAME_VERSION,
        "modeldb_file": "Models/ModelDb.cs",
        "modeldb_lists": md,
        "card_pool_resolution_order": ctx.card_pool_order,
        "relic_pool_resolution_order": ctx.relic_pool_order,
        "potion_pool_resolution_order": ctx.potion_pool_order,
        "character_pools": ctx.char_pools,
        "card_pools": ctx.pools["card"], "relic_pools": ctx.pools["relic"], "potion_pools": ctx.pools["potion"],
    }


def ported_manifest(ctx):
    return {"schema": SCHEMA, "kind": "ported", "sim_root": "sim/Sts2Sim.Core",
            "scan": "public sealed class X : CardDef|PowerModel|MonsterDef|EncounterDef in **/Defs/*.cs and **/Encounters/*.cs",
            "counts": dict((k, len(v)) for k, v in ctx.ported.items()), "entries": ctx.ported}


def unresolved_of(kind, lst):
    """Entries with no pool/act/encounter link, and why (mock/deprecated/abstract/event/other)."""
    out = []
    why = {}
    for e in lst:
        if kind in ("cards", "relics", "potions"):
            bad = e["pool"] == "unlisted"
        elif kind == "encounters":
            bad = not e["acts"]
        elif kind == "events":
            bad = e["pool_status"] != "listed"
        elif kind == "monsters":
            bad = not e["encounters"]
        else:
            bad = False
        if not bad:
            continue
        out.append(e["class"])
        if e["is_mock"]:
            r = "mock"
        elif e["is_deprecated"]:
            r = "deprecated"
        elif e["is_abstract"]:
            r = "abstract"
        elif kind == "encounters" and e.get("kind") == "event":
            r = "event_encounter"
        elif kind == "monsters" and e.get("referenced_by_models"):
            r = "spawned_or_referenced_by_models"
        elif e.get("external_refs"):
            r = "referenced_outside_models"
        else:
            r = "no_reference_found"
        why.setdefault(r, []).append(e["class"])
    return out, why


def cross_kind_duplicate_ids(entries, pools=None):
    """`{model_id: [kind/file, ...]}` for every ModelDb id held by more than one entry or pool across all kinds."""
    by_id = {}
    for kind in sorted(entries):
        for e in entries[kind]:
            if e.get("model_id"):
                by_id.setdefault(e["model_id"], []).append("%s/%s" % (kind, e["file"]))
    for pk in sorted(pools or {}):
        for pname, p in sorted(pools[pk].items()):
            if p.get("model_id"):
                by_id.setdefault(p["model_id"], []).append("%s_pools/%s" % (pk, p["file"]))
    return dict((i, sorted(v)) for i, v in sorted(by_id.items()) if len(v) > 1)


def category_mismatches(entries):
    """Entries whose `model_id` category is not the slugified root model of their kind (`KIND_ROOT`)."""
    out = []
    for kind in KIND_NAMES:
        want = slugify_category(KIND_ROOT[kind]) + "."
        for e in entries.get(kind, []):
            if e.get("model_id") and not e["model_id"].startswith(want):
                out.append("%s/%s: %s" % (kind, e["file"], e["model_id"]))
    return out


def validate(ctx, entries, non_model):
    report = {}
    problems = []
    for kind in KIND_NAMES:
        files = [f.rel for f in ctx.files[kind]]
        acc = [e["file"] for e in entries[kind]] + [x["file"] for x in non_model[kind]]
        dup = sorted(set(x for x in acc if acc.count(x) > 1))
        missing = sorted(set(files) - set(acc))
        extra = sorted(set(acc) - set(files))
        classes = [e["class"] for e in entries[kind]]
        dup_cls = sorted(set(c for c in classes if classes.count(c) > 1))
        ids = [e["model_id"] for e in entries[kind] if e["model_id"]]
        dup_ids = sorted(set(i for i in ids if ids.count(i) > 1))
        concrete_unreg = [e["class"] for e in entries[kind] if not e["is_abstract"] and not e["registered"]]
        abstract_reg = [e["class"] for e in entries[kind] if e["is_abstract"] and e["registered"]]
        unresolved, breakdown = unresolved_of(kind, entries[kind])
        r = {"files": len(files), "entries": len(entries[kind]), "non_model_files": len(non_model[kind]),
             "accounted_exactly_once": not dup and not missing and not extra,
             "duplicate_files": dup, "missing_files": missing, "extra_files": extra,
             "duplicate_classes": dup_cls, "duplicate_model_ids": dup_ids,
             "abstract_entries": sum(1 for e in entries[kind] if e["is_abstract"]),
             "registered_entries": sum(1 for e in entries[kind] if e["registered"]),
             "concrete_not_registered": concrete_unreg, "abstract_but_registered": abstract_reg,
             "unresolved_pool_or_act": len(unresolved), "unresolved": unresolved,
             "unresolved_breakdown": breakdown,
             "mock_entries": sum(1 for e in entries[kind] if e["is_mock"])}
        if not r["accounted_exactly_once"] or dup_cls or dup_ids:
            problems.append("%s: accounting problem" % kind)
        report[kind] = r
    cross = cross_kind_duplicate_ids(entries, ctx.pools)
    report["_cross_kind_duplicate_model_ids"] = cross
    if cross:
        problems.append("model_id not unique across kinds")
    cat_bad = category_mismatches(entries)
    report["_model_id_category_mismatches"] = cat_bad
    if cat_bad:
        problems.append("model_id category differs from the kind's root model")
    defined = set()
    for kind in KIND_NAMES:
        for f in ctx.files[kind]:
            for td in f.types:
                defined.add((f.rel.split("/")[0], td.name))
    for lst in list(ctx.pool_files.values()) + list(ctx.other_files.values()):
        for f in lst:
            for td in f.types:
                defined.add((f.rel.split("/")[0], td.name))
    defined_names = set(n for _, n in defined)
    unacc = sorted(n for n in ctx.registered_names if n not in defined_names)
    unacc += sorted("%s.%s" % (d, n) for d, n in ctx.registered_qualified if (d, n) not in defined)
    report["_registered"] = {"declared_count": ctx.registered_declared_count, "typeof_entries": ctx.registered_total,
                             "registered_not_defined_in_scanned_dirs": unacc}
    if unacc:
        problems.append("registered model types not found in scanned dirs")
    slug_fail = slug_self_test()
    report["_slugify_self_test_failures"] = [list(x) for x in slug_fail]
    if slug_fail:
        problems.append("slugify self-test failed")
    sim_mismatch = [e for k in ctx.ported for e in ctx.ported[k] if not e["id_matches_slugify"]]
    report["_ported_id_mismatches"] = sim_mismatch
    act_decl = []
    for a in entries["acts"]:
        if a["encounters_declared_count"] is not None and a["encounters_declared_count"] != len(a["encounters"]):
            act_decl.append((a["class"], "encounters", a["encounters_declared_count"], len(a["encounters"])))
        if a["events_declared_count"] is not None and a["events_declared_count"] != len(a["events"]):
            act_decl.append((a["class"], "events", a["events_declared_count"], len(a["events"])))
    for pk, pools in ctx.pools.items():
        for pname, p in pools.items():
            if p["declared_count"] is not None and p["declared_count"] != len(p["members"]):
                act_decl.append((pname, "members", p["declared_count"], len(p["members"])))
    report["_array_size_mismatches"] = act_decl
    if act_decl:
        problems.append("declared array sizes differ from extracted list lengths")
    report["_problems"] = problems
    return report


def _is_scalar(v):
    return v is None or isinstance(v, (str, int, float, bool))


def dumps(obj, indent=0):
    """Deterministic JSON: dicts one key per line, lists of scalars (and small flat dicts) inline."""
    pad = " " * indent
    pad2 = " " * (indent + 1)
    if _is_scalar(obj):
        return json.dumps(obj, ensure_ascii=False)
    if isinstance(obj, dict):
        if not obj:
            return "{}"
        if all(_is_scalar(v) for v in obj.values()) and len(obj) <= 6:
            inline = json.dumps(obj, ensure_ascii=False)
            if len(inline) <= 100:
                return inline
        items = ["%s%s: %s" % (pad2, json.dumps(k, ensure_ascii=False), dumps(v, indent + 1)) for k, v in obj.items()]
        return "{\n" + ",\n".join(items) + "\n" + pad + "}"
    if isinstance(obj, (list, tuple)):
        if not obj:
            return "[]"
        if all(_is_scalar(v) for v in obj):
            return json.dumps(list(obj), ensure_ascii=False)
        items = [pad2 + dumps(v, indent + 1) for v in obj]
        return "[\n" + ",\n".join(items) + "\n" + pad + "]"
    raise TypeError(type(obj))


def kind_stats(ctx, entries):
    out = {}
    for k in KIND_NAMES:
        lst = entries[k]
        real = [e for e in lst if not e["is_mock"] and not e["is_deprecated"] and not e["is_abstract"]]
        hooks = set(h for e in real for h in e["abstract_model_hooks"])
        out[k] = {
            "entries": len(lst), "non_mock_non_deprecated_concrete": len(real),
            "ported": sum(1 for e in lst if e["ported"]),
            "needs_player_choice": sum(1 for e in real if e["needs_player_choice"]),
            "references_godot_or_ui": sum(1 for e in real if e["references_godot_or_ui"]),
            "ui_tokens_in_logic": sum(1 for e in real if e["ui_tokens_in_logic"]),
            "lines_total": sum(e["lines_total"] for e in real),
            "lines_logic": sum(e["lines_logic"] for e in real),
            "abstract_model_hooks_used": len(hooks),
            "abstract_model_hooks_used_not_in_sim": sorted(h for h in hooks if h not in ctx.sim_hook_virtuals),
        }
    return out


def write_json(path, obj):
    tmp = path + ".tmp"
    with open(tmp, "w", encoding="utf-8") as fh:
        fh.write(dumps(obj))
        fh.write("\n")
    os.replace(tmp, path)


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--root", default=os.environ.get("STS2_DECOMPILE_ROOT", DEFAULT_ROOT),
                    help="decompile root (one dir per namespace)")
    ap.add_argument("--sim-root", default=os.path.join(REPO_ROOT, "sim"), help="sim/ directory (for ported.json)")
    ap.add_argument("--out", default=os.path.join(REPO_ROOT, "sim", "specs", "inventory"), help="output directory")
    ap.add_argument("--kind", action="append", choices=KIND_NAMES + ["all"], help="kind(s) to write (default all)")
    ap.add_argument("--check", action="store_true", help="validate only; write nothing; exit 1 on problems")
    args = ap.parse_args(argv)
    if not os.path.isdir(args.root):
        print("decompile root not found: %s" % args.root, file=sys.stderr)
        return 2
    ctx = load_context(args.root, args.sim_root)
    entries, non_model = build_entries(ctx)
    report = validate(ctx, entries, non_model)
    kinds = KIND_NAMES if not args.kind or "all" in args.kind else args.kind
    for k in KIND_NAMES:
        r = report[k]
        print("%-13s files=%4d entries=%4d non_model=%d once=%s unresolved=%d mocks=%d abstract=%d" % (
            k, r["files"], r["entries"], r["non_model_files"], r["accounted_exactly_once"],
            r["unresolved_pool_or_act"], r["mock_entries"], r["abstract_entries"]))
    if report["_problems"]:
        print("PROBLEMS: %s" % "; ".join(report["_problems"]))
    if args.check:
        return 1 if report["_problems"] else 0
    os.makedirs(args.out, exist_ok=True)
    for k in kinds:
        write_json(os.path.join(args.out, k + ".json"), manifest(ctx, k, entries[k], non_model[k]))
    if not args.kind or "all" in args.kind:
        write_json(os.path.join(args.out, "hooks.json"), hooks_manifest(ctx, entries))
        write_json(os.path.join(args.out, "pools.json"), pools_manifest(ctx))
        write_json(os.path.join(args.out, "ported.json"), ported_manifest(ctx))
        summary = {"schema": SCHEMA, "kind": "summary", "game_version": GAME_VERSION,
                   "decompile_root": args.root, "stats": kind_stats(ctx, entries), "validation": report,
                   "other_model_dirs_not_inventoried": dict(
                       (d, len(ctx.other_files[d])) for d in OTHER_MODEL_DIRS),
                   "pool_dirs": dict((POOL_DIRS[k], len(v)) for k, v in ctx.pool_files.items())}
        write_json(os.path.join(args.out, "summary.json"), summary)
    return 1 if report["_problems"] else 0


if __name__ == "__main__":
    sys.exit(main())
