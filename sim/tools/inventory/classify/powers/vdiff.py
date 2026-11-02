"""Mechanical v0.107.1 vs v0.111.0 comparison of power files (scratch)."""

import os as _os
import sys as _sys
_sys.path.insert(0, _os.path.join(_os.path.dirname(_os.path.abspath(__file__)), ".."))
import common  # noqa: E402
import json
import os
import re
import sys
from collections import Counter, OrderedDict

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, common.INVENTORY_TOOLS)

import cs_source  # noqa: E402

V111 = common.DECOMPILE_ROOT
V107 = common.V107_ROOT

ENUMS = ("ValueProp", "CombatSide", "PowerType", "PowerStackType", "PowerInstanceType", "CardType", "CardKeyword",
         "CardTag", "PileType", "TargetType", "CardRarity", "AscensionLevel", "RunRngType", "PlayerRngType",
         "CardPilePosition", "IntentType", "ModifyDamageHookType", "HpLossHookPhase", "RoomType", "CardPreviewMode",
         "AutoPlayType", "UnplayableReason", "MoveRepeatType", "PlayerTurnPhase", "OrbEvokeType", "CardCreationSource")
BOILER = {"GetAwaiter", "IsCompleted", "GetResult", "AwaitUnsafeOnCompleted", "SetResult", "SetException",
          "SetStateMachine", "Start", "Create", "Task", "MoveNext", "op_Implicit", "op_Explicit", "Dispose",
          "GetEnumerator", "Current", "CompletedTask", "FromResult", "ConfigureAwait", "AwaitOnCompleted",
          "Where", "Select", "Any", "All", "Count", "Contains", "ToList", "ToArray", "First", "FirstOrDefault",
          "Sum", "Max", "Min", "OfType", "Cast", "Concat", "Distinct", "Except", "OrderBy", "Last", "LastOrDefault",
          "Empty", "Array", "Generic", "Collections", "Linq", "Threading", "Tasks", "Enumerable", "Func", "Action",
          "Exception", "Builder"}
LINQ = {"Where", "Select", "Any", "All", "Count", "Contains", "ToList", "ToArray", "First", "FirstOrDefault",
        "Sum", "Max", "Min", "OfType", "Cast", "Concat", "Distinct", "Except", "OrderBy", "Last", "LastOrDefault",
        "SelectMany", "Single", "SingleOrDefault", "Skip", "Take", "Reverse", "ToHashSet", "Aggregate"}


def strip_sm(masked):
    lines = []
    for ln in masked.split("\n"):
        s = ln.strip()
        if "_003C_003E1__state" in s or re.match(r"^case -?\d+:$", s) or re.search(r"\bnum [!=]= -?\d+", s):
            continue
        if "_003C_003Eu__" in s or "AwaitUnsafeOnCompleted" in s or "AsyncTaskMethodBuilder" in s:
            continue
        lines.append(ln)
    return "\n".join(lines)


STOP = BOILER | LINQ | {
    "IEnumerable", "IEnumerator", "IDisposable", "IReadOnlyList", "IReadOnlyCollection", "ICollection", "IList",
    "List", "HashSet", "Dictionary", "System", "MegaCrit", "Sts2", "Core", "Models", "Creatures", "Cards", "Entities",
    "GameActions", "Multiplayer", "Runtime", "CompilerServices", "Diagnostics", "InteropServices", "Collections",
    "Generic", "Linq", "Threading", "Tasks", "TaskAwaiter", "ValueTaskAwaiter", "AsyncTaskMethodBuilder",
    "IAsyncStateMachine", "StructLayout", "LayoutKind", "CompilerGenerated", "DebuggerHidden", "AsyncStateMachine",
    "IteratorStateMachine", "Task", "ValueTask", "Math", "Decimal", "String", "Int32", "Boolean", "Object", "Type",
    "Exception", "NotImplementedException", "InvalidOperationException", "ArgumentOutOfRangeException",
    "Unknown", "IL", "Expected", "O", "Func", "Action", "Predicate", "Nullable", "Enumerable", "Array", "Empty",
    "ReadOnlySpan", "Span", "MemoryExtensions", "CollectionExtensions", "Comparer", "EqualityComparer",
    "Powers", "Commands", "Hooks", "Combat", "ValueProps", "Helpers", "Localization", "DynamicVars", "HoverTips",
    "Extensions", "Rooms", "Runs", "Players", "Orbs", "Monsters", "Relics", "Potions", "Events", "Godot", "Nodes",
    "Vfx", "Audio", "Random", "Rngs", "Saves", "Context", "Ascension", "Encounters", "History", "Factories",
    "PlayerChoiceContext", "ThrowingPlayerChoiceContext", "BlockingPlayerChoiceContext", "Var",
    "Enumerator", "KeyValuePair", "DamageResult", "CardPileAddResult", "SummonResult", "Node", "MaxValue", "Enum",
    "GroupBy", "IGrouping", "TaskHelper", "RunSafely", "GodotTreeExtensions", "QueueFreeSafely",
    "CombatVfxContainer", "AddChildSafely", "SetNodeVisible", "AssertMutable", "IsValid", "SetActive",
    "Creature", "CardModel", "MonsterState", "AbstractModel", "Player", "ICombatState", "PowerModel", "OrbModel",
    "ModelId", "CardLocation",
}


def code_only(masked):
    out = []
    for ln in masked.split("\n"):
        s = ln.strip()
        if s.startswith(("using ", "namespace ")):
            continue
        out.append(ln)
    t = "\n".join(out)
    t = t.replace("global::", "")
    t = re.sub(r"\[(?:StructLayout|CompilerGenerated|DebuggerHidden|AsyncStateMachine|IteratorStateMachine|"
               r"DebuggerBrowsable|DebuggerNonUserCode|Nullable|NullableContext|IsReadOnly|"
               r"MethodImpl|SpecialName)[^\]]*\]", " ", t)
    t = re.sub(r"_003C\w*", " ", t)
    return t


def fingerprint(path):
    cf = cs_source.CsFile(path, os.path.basename(path))
    td = cf.main_type()
    m = code_only(strip_sm(cf.masked))
    fp = OrderedDict()
    fp["idents"] = Counter({x: 1 for x in re.findall(r"\b([A-Z]\w*)\b", m)
                            if x not in STOP and not re.match(r"^(IL_|CS_|STR\d|N[A-Z]\w*Vfx)", x)})
    fp["dotted"] = Counter({"%s.%s" % x: 1 for x in re.findall(r"\b([A-Z]\w*)\.([A-Z]\w*)\b", m)
                            if x[0] not in STOP and x[1] not in STOP})
    over = [mem.name for mem in td.members if mem.is_override]
    fp["overrides"] = Counter({x: 1 for x in over})
    nums = Counter()
    for x in re.findall(r"(?<![\w.])(\d+(?:\.\d+)?)[mMfFdDuUlL]?\b", m):
        if x in ("0", "1", "79228162514264337593543950335"):
            continue
        nums[x.rstrip("0").rstrip(".") if "." in x else x] = 1
    fp["numbers"] = nums
    props = OrderedDict()
    for mem in td.members:
        if mem.kind == "property" and mem.is_override:
            body = cf.body_of(mem).strip().replace("global::", "")
            body = re.sub(r"\s+", " ", body)
            if len(body) < 160:
                props[mem.name] = cf.resolve_strings(body)
    fp["props"] = props
    comps = Counter()
    for a, op, b in re.findall(r"([\w.()]+)\s(<=|>=|==|!=|<|>)\s([\w.()\-]+)", m):
        if "num" == a or a.startswith("_003C"):
            continue
        la = re.split(r"[.(]", a.strip("()"))[-1] or a
        lb = re.split(r"[.(]", b.strip("()"))[-1] or b
        la = re.sub(r"[mMfF]$", "", la) if re.match(r"^-?\d", la) else la
        lb = re.sub(r"[mMfF]$", "", lb) if re.match(r"^-?\d", lb) else lb
        comps["%s %s %s" % (la, op, lb)] = 1
    fp["comparisons"] = comps
    strs = Counter({k: 1 for k in cf.string_values(m) if not k.startswith(("event:/", "res://", "vfx/", "sfx/"))})
    fp["strings"] = strs
    return fp


def compare(a, b):
    diff = OrderedDict()
    for k in a:
        if k == "props":
            d = {}
            for p in set(a[k]) | set(b[k]):
                va, vb = a[k].get(p), b[k].get(p)
                if va != vb:
                    d[p] = {"v107": vb, "v111": va}
            if d:
                diff[k] = d
            continue
        ca, cb = a[k], b[k]
        only111 = sorted((ca - cb).elements())
        only107 = sorted((cb - ca).elements())
        if only111 or only107:
            diff[k] = {"only_v111": only111, "only_v107": only107}
    return diff


def main():
    res = OrderedDict()
    for sub111, sub107 in (("MegaCrit.Sts2.Core.Models.Powers", "Models/Powers"),
                           ("MegaCrit.Sts2.Core.Models.Powers.Mocks", "Models/Powers/Mocks")):
        d111 = os.path.join(V111, sub111)
        d107 = os.path.join(V107, sub107)
        for fn in sorted(os.listdir(d111)):
            if not fn.endswith(".cs"):
                continue
            p107 = os.path.join(d107, fn)
            if not os.path.exists(p107):
                res[fn[:-3]] = {"status": "new_in_v111"}
                continue
            a = fingerprint(os.path.join(d111, fn))
            b = fingerprint(p107)
            diff = compare(a, b)
            res[fn[:-3]] = {"status": "same_tokens" if not diff else "differs", "diff": diff}
        for fn in sorted(os.listdir(d107)):
            if fn.endswith(".cs") and not os.path.exists(os.path.join(d111, fn)):
                res[fn[:-3]] = {"status": "removed_in_v111"}
    with open(common.work_path("powers", "vdiff.json"), "w") as fh:
        json.dump(res, fh, indent=1)
    c = Counter(v["status"] for v in res.values())
    print(c)
    kc = Counter()
    for v in res.values():
        for k in v.get("diff", {}):
            kc[k] += 1
    print(kc)


if __name__ == "__main__":
    main()
