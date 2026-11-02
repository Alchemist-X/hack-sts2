import os as _os
import sys as _sys
_sys.path.insert(0, _os.path.join(_os.path.dirname(_os.path.abspath(__file__)), ".."))
import common  # noqa: E402
import os, re, sys, json, collections
from decimal import Decimal, InvalidOperation

NEW = common.DECOMPILE_ROOT
OLD = common.V107_TREE
OUT = common.work_path("corpus", "tree_diff.json")

NS_RE = re.compile(r"^namespace\s+([\w\.]+)\s*;?", re.M)

def index_tree(root):
    idx = {}
    for dp, dn, fn in os.walk(root):
        for f in fn:
            if not f.endswith(".cs"):
                continue
            p = os.path.join(dp, f)
            txt = open(p, encoding="utf-8", errors="replace").read()
            m = NS_RE.search(txt)
            ns = m.group(1) if m else ""
            key = (ns, f)
            n = 1
            while key in idx:
                n += 1
                key = (ns, f + "#" + str(n))
            idx[key] = (p, txt)
    return idx

PLUMB = re.compile(r"_003C_003E1__state|_003C_003E2__current|_003C_003El__initialThreadId|CurrentManagedThreadId|^\s*case\s+-?\d+\s*:|switch\s*\(num\d*\)|goto IL_|^\s*IL_\w+\s*:|default\(TaskAwaiter|default\(ValueTaskAwaiter|AwaitUnsafeOnCompleted|SetStateMachine|SetException\(|SetResult\(|_003C_003Et__builder|^\s*int num\d* = _003C|num\d* = \(_003C|if \(num\d* [!=<>]=? -?\d+\)|_003C_003Eu__\d+ = (val|default)|\.IsCompleted\)")
COMMENT = re.compile(r"//.*$")
ATTR = re.compile(r"^\s*\[[^\]]*\]\s*$")
NUM_RE = re.compile(r"(?<![\w\.])(-?)(0x[0-9A-Fa-f]+|\d+\.\d+|\d+)([mMfFdDuUlL]{0,2})(?![\w\.])")
STR_RE = re.compile(r'"((?:[^"\\]|\\.)*)"')
IDENT_BEFORE = re.compile(r"([A-Za-z_]\w*)\s*(?:<[^<>()]*(?:<[^<>()]*>[^<>()]*)*>)?\s*$")
ENUMS = ("CardType", "CardRarity", "TargetType", "RelicRarity", "PotionRarity", "PotionUsage", "PowerType", "PowerStackType",
         "AscensionLevel", "IntentType", "ValueProp", "CardKeyword", "CardTag", "MoveRepeatType", "PowerInstanceType",
         "RoomType", "MapPointType", "OrbType", "PileType", "CardPilePosition", "RewardType", "EncounterTag",
         "CardMultiplayerConstraint", "CardRarityOddsType", "CardCreationSource", "EventOptionType")
ENUM_RE = re.compile(r"\b(" + "|".join(ENUMS) + r")\.([A-Z]\w*)\b")
RULE_CTX = re.compile(r"(Var|Base|^base$|^this$|UpgradeValueBy|UpgradeBy|GetValueIfAscension|Attack|WithHitCount|Intent|GainBlock|Apply|Heal|Damage|GainMaxHp|LoseMaxHp|SetCurrentHp|GainEnergy|Draw|GainGold|LoseGold|Gold|Stars|Summon|Channel|Add|Next|Range|Weight|StateWeight|AddBranch|Repeat|Cooldown|SetMaxHp|HpLoss|Lose|Gain|Times|Hits|Count|Amount|Cost|Odds|Chance|Multiplier)", re.I)
ASSET = re.compile(r"(\.mp3|\.png|\.tscn|\.tres|\.ogg|\.wav|^event:/|^res://|^vfx/|^sfx/|^ui/|/)", re.I)
CALL_RE = re.compile(r"(?:\b([A-Za-z_]\w*)\s*\.\s*|\.\s*)?([A-Za-z_]\w*)\s*(<[^<>()]*(?:<[^<>()]*>[^<>()]*)*>)?\s*\(")
STOP_RECV = {"Enumerable", "Queryable", "MemoryExtensions", "CollectionExtensions", "AsyncTaskMethodBuilder", "AsyncValueTaskMethodBuilder",
             "AsyncVoidMethodBuilder", "Math", "Mathf", "string", "String", "Log", "Logger", "GD", "Task", "TaskHelper", "Callable", "Variant",
             "Enum", "Convert", "Unsafe", "RuntimeHelpers", "MathF", "ArgumentNullException", "ArgumentOutOfRangeException", "Debug"}
STOP_NAME = {"ValueTuple", "Tuple", "Dictionary", "List", "HashSet", "Queue", "Stack", "KeyValuePair", "Func", "Action", "Lazy",
             "StringBuilder", "Exception", "InvalidOperationException", "NotImplementedException", "Nullable", "Span", "ReadOnlySpan",
             "Vector2", "Color", "ArgumentException", "IndexOutOfRangeException", "NotSupportedException", "GetAwaiter", "GetResult", "MoveNext", "Dispose", "GetEnumerator", "SetStateMachine", "AwaitUnsafeOnCompleted", "Start",
             "SetResult", "SetException", "op_Implicit", "op_Explicit", "ConfigureAwait", "if", "while", "for", "foreach", "switch", "catch",
             "using", "lock", "return", "nameof", "typeof", "sizeof", "default", "new", "base", "this", "Equals", "GetHashCode", "ToString",
             "ToList", "ToArray", "Select", "Where", "Any", "All", "First", "FirstOrDefault", "Last", "LastOrDefault", "Count", "Sum", "Max",
             "Min", "OrderBy", "OrderByDescending", "ThenBy", "Concat", "Distinct", "Contains", "Cast", "OfType", "Skip", "Take", "Except",
             "Union", "Intersect", "Aggregate", "Single", "SingleOrDefault", "Reverse", "ToHashSet", "ToDictionary", "Append", "Prepend",
             "SelectMany", "Zip", "AsEnumerable", "GroupBy", "ElementAt", "Empty", "Average", "SequenceEqual", "Repeat", "Range",
             "ThrowIfNull", "Invoke", "Add", "Remove", "Clear", "AddRange", "Insert", "IndexOf", "RemoveAt", "TryGetValue", "ContainsKey",
             "RunSafely", "Format", "Concat", "Join", "IsNullOrEmpty", "Abs", "Max", "Min", "Floor", "Ceiling", "Round", "Clamp", "Sign",
             "Ceil", "Pow", "Sqrt", "WhenAll", "Delay", "FromResult", "CompletedTask", "Yield", "Create", "Info", "Warn", "Error"}
KEEP_GENERIC = {"Apply", "Remove", "GetPower", "HasPower", "GetPowerAmount", "FromPower", "FromRelic", "FromCard", "FromPotion", "FromOrb",
                "FromEnchantment", "FromAffliction", "Card", "Relic", "Potion", "Power", "Monster", "Encounter", "Event", "Orb", "Enchantment",
                "Affliction", "GetById", "HasRelic", "GetRelic", "GetPet", "Summon", "Afflict", "Enchant", "SetMoveImmediate", "Act", "Character"}
PROP_CONST = re.compile(r"\b([A-Z]\w*)\s*=>\s*(-?\d+(?:\.\d+)?[mMfF]?)\s*;")

def norm_num(sign, body, suffix):
    try:
        v = Decimal(int(body, 16)) if body.lower().startswith("0x") else Decimal(body)
    except InvalidOperation:
        return sign + body + suffix
    if sign:
        v = -v
    return format(v.normalize(), "f")

def clean(txt):
    out = []
    for line in txt.splitlines():
        if ATTR.match(line):
            continue
        line = COMMENT.sub("", line)
        if not line.strip():
            continue
        st = line.strip()
        if st.startswith("using ") and st.endswith(";") and "(" not in st:
            continue
        if PLUMB.search(line):
            continue
        out.append(line)
    return out

def strip_strings(s):
    spans = [(m.start(), m.end()) for m in STR_RE.finditer(s)]
    return spans

def analyze(lines, own=""):
    text = "\n".join(lines)
    spans = strip_strings(text)
    span_starts = {a: b for a, b in spans}
    nums = collections.Counter()
    strs = collections.Counter()
    enums = collections.Counter()
    calls = set()
    props = collections.Counter()
    stack = []
    i = 0
    n = len(text)
    masked = list(text)
    for a, b in spans:
        for k in range(a, b):
            masked[k] = " "
    masked = "".join(masked)
    events = []
    for m in NUM_RE.finditer(masked):
        events.append((m.start(), 2, "num", norm_num(m.group(1), m.group(2), m.group(3))))
    for a, b in spans:
        events.append((a, 2, "str", text[a + 1:b - 1]))
    for idx, ch in enumerate(masked):
        if ch == "(":
            mm = IDENT_BEFORE.search(masked[max(0, idx - 200):idx])
            nm = mm.group(1) if mm else "_"
            if nm.startswith("_"):
                nm = "_"
            events.append((idx, 0, "open", nm))
        elif ch == ")":
            events.append((idx, 1, "close", None))
    events.sort(key=lambda e: (e[0], e[1]))
    for pos, _, kind, val in events:
        if kind == "open":
            stack.append(val)
        elif kind == "close":
            if stack:
                stack.pop()
        elif kind == "num":
            ctx = stack[-1] if stack else "-"
            nums[(ctx, val)] += 1
        else:
            if not ASSET.search(val):
                strs[val] += 1
    for m in ENUM_RE.finditer(masked):
        enums[m.group(0)] += 1
    for m in CALL_RE.finditer(masked):
        recv, name, gen = m.group(1), m.group(2), m.group(3)
        if recv in STOP_RECV or name in STOP_NAME or name.startswith("_003C") or (recv or "").startswith("_003C") or name == own:
            continue
        if name[0].islower():
            continue
        g = ""
        if gen and name in KEEP_GENERIC:
            g = re.sub(r"\s+", "", gen)
        calls.add(name + g)
    for m in PROP_CONST.finditer(masked):
        props[(m.group(1), norm_num("", m.group(2).lstrip("-").rstrip("mMfF"), "") if not m.group(2).startswith("-") else "-" + norm_num("", m.group(2)[1:].rstrip("mMfF"), ""))] += 1
    return nums, strs, enums, calls, props

PRESENT = re.compile(r"(Vector|Color|Tween|Vfx|Sfx|Wait|Delay|Shake|Anim|Spine|Scale|Position|Modulate|Lerp|Godot|Seconds|Duration|Time|Fade|Particle|Sound|Audio|Camera|Flash|Rumble|Haptic|Play(?!er)|Rect|Size|Offset|Rotation|Alpha|Font|Text|Label|Icon|Image|Node|Emit|Signal|Bus|Volume|Pitch|Ease|Trans|Curve)", re.I)
CONTROL = {"if", "while", "for", "switch", "return", "foreach", "-", "_"}
def rule_nums(nums):
    out = collections.Counter()
    for (ctx, val), v in nums.items():
        if PRESENT.search(ctx):
            continue
        if ctx in CONTROL and val in ("0", "1", "-1"):
            continue
        out[val] += v
    return out

def main():
    new = index_tree(NEW)
    old = index_tree(OLD)
    common = set(new) & set(old)
    added = set(new) - set(old)
    removed = set(old) - set(new)
    per_ns = collections.defaultdict(lambda: collections.Counter())
    for k in added:
        per_ns[k[0]]["added"] += 1
    for k in removed:
        per_ns[k[0]]["removed"] += 1
    content_ns = {"MegaCrit.Sts2.Core.Models.Cards": "cards", "MegaCrit.Sts2.Core.Models.Powers": "powers",
                  "MegaCrit.Sts2.Core.Models.Monsters": "monsters", "MegaCrit.Sts2.Core.Models.Relics": "relics",
                  "MegaCrit.Sts2.Core.Models.Potions": "potions", "MegaCrit.Sts2.Core.Models.Encounters": "encounters",
                  "MegaCrit.Sts2.Core.Models.Events": "events", "MegaCrit.Sts2.Core.Models.Enchantments": "enchantments",
                  "MegaCrit.Sts2.Core.Models.Acts": "acts", "MegaCrit.Sts2.Core.Models.Characters": "characters",
                  "MegaCrit.Sts2.Core.Models.Afflictions": "afflictions", "MegaCrit.Sts2.Core.Models.Orbs": "orbs",
                  "MegaCrit.Sts2.Core.Models.CardPools": "card_pools", "MegaCrit.Sts2.Core.Models.RelicPools": "relic_pools",
                  "MegaCrit.Sts2.Core.Models.PotionPools": "potion_pools", "MegaCrit.Sts2.Core.Models.Modifiers": "modifiers"}
    changes = collections.defaultdict(list)
    file_flags = {}
    for k in sorted(common):
        ns = k[0]
        own = k[1].split("#")[0][:-3]
        a = analyze(clean(new[k][1]), own)
        b = analyze(clean(old[k][1]), own)
        nn, sn, en, cn, pn = a
        no, so, eo, co, po = b
        rn, ro = rule_nums(nn), rule_nums(no)
        f_rule = (rn != ro) or (pn != po)
        f_num = nn != no
        f_enum = en != eo
        f_str = sn != so
        f_call = cn != co
        c = per_ns[ns]
        c["common"] += 1
        for flag, name in ((f_rule, "rule_constant_changed"), (f_num, "any_number_changed"), (f_enum, "enum_changed"),
                           (f_str, "string_changed"), (f_call, "call_set_changed")):
            if flag:
                c[name] += 1
        if f_rule or f_enum or f_call or f_str:
            c["any_signal"] += 1
        file_flags[ns + "/" + k[1]] = {"rule": f_rule, "num": f_num, "enum": f_enum, "str": f_str, "call": f_call}
        if ns in content_ns and (f_rule or f_enum or f_call or f_str or f_num):
            entry = {"file": k[1]}
            if f_rule:
                entry["rule_removed"] = sorted((ro - rn).elements()) + sorted("%s=>%s" % kk for kk in (po - pn).elements())
                entry["rule_added"] = sorted((rn - ro).elements()) + sorted("%s=>%s" % kk for kk in (pn - po).elements())
                entry["rule_ctx_removed"] = sorted("%s(%s)" % kk for kk in (no - nn).elements() if not PRESENT.search(kk[0]))[:16]
                entry["rule_ctx_added"] = sorted("%s(%s)" % kk for kk in (nn - no).elements() if not PRESENT.search(kk[0]))[:16]
            if f_num and not f_rule:
                entry["other_num_removed"] = sorted("%s(%s)" % kk for kk in (no - nn).elements())[:12]
                entry["other_num_added"] = sorted("%s(%s)" % kk for kk in (nn - no).elements())[:12]
            if f_enum:
                entry["enum_removed"] = sorted((eo - en).elements())
                entry["enum_added"] = sorted((en - eo).elements())
            if f_str:
                entry["str_removed"] = sorted((so - sn).elements())[:12]
                entry["str_added"] = sorted((sn - so).elements())[:12]
            if f_call:
                entry["calls_removed"] = sorted(co - cn)
                entry["calls_added"] = sorted(cn - co)
            changes[content_ns[ns]].append(entry)
    summary = {}
    for ns, area in content_ns.items():
        c = per_ns.get(ns, collections.Counter())
        summary[area] = {"namespace_dir": ns, "common": c.get("common", 0), "added": c.get("added", 0), "removed": c.get("removed", 0),
                         "rule_constant_changed": c.get("rule_constant_changed", 0), "enum_changed": c.get("enum_changed", 0),
                         "call_set_changed": c.get("call_set_changed", 0), "string_changed": c.get("string_changed", 0),
                         "any_number_changed": c.get("any_number_changed", 0), "any_signal": c.get("any_signal", 0)}
    out = {"trees": {"new": NEW, "old": OLD}, "files_new": len(new), "files_old": len(old), "common": len(common),
           "added": sorted("%s/%s" % k for k in added), "removed": sorted("%s/%s" % k for k in removed),
           "per_namespace": {ns: dict(c) for ns, c in sorted(per_ns.items())}, "content_summary": summary,
           "content_changes": changes, "file_flags": file_flags}
    json.dump(out, open(OUT, "w"), indent=1)
    print(json.dumps(summary, indent=1))

main()
