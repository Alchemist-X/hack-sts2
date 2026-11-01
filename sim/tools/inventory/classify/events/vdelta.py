import os as _os
import sys as _sys
_sys.path.insert(0, _os.path.join(_os.path.dirname(_os.path.abspath(__file__)), ".."))
import common  # noqa: E402
import collections
import json
import os
import re
import sys

V111 = common.DECOMPILE_ROOT + "/"
V107 = common.V107_ROOT + "/"

def read(p):
    with open(p, encoding="utf-8-sig", errors="replace") as f:
        return f.read()

def strip_comments(s):
    s = re.sub(r"//[^\n]*", "", s)
    s = re.sub(r"/\*.*?\*/", "", s, flags=re.S)
    return s

STR = re.compile(r'\$?@?"(?:[^"\\]|\\.)*"')

def tokens(src):
    s = strip_comments(src)
    toks = []
    for m in STR.finditer(s):
        lit = m.group(0)
        if "event:/" in lit or "res://" in lit or ".png" in lit or ".tscn" in lit:
            toks.append("STRUI")
            continue
        lit = re.sub(r"^\$?@?", "", lit)
        lit = re.sub(r"\{[\w.]*?\.(\w+)", r"{\1", lit)
        toks.append("STR " + lit)
    s2 = STR.sub('""', s)
    s2 = re.sub(r"decimal\.op_Implicit\(", "(", s2)
    s2 = re.sub(r"\(decimal\)", "", s2)
    for m in re.finditer(r"\b(\w*Cmd)\.(\w+)(<\w+>)?", s2):
        toks.append("CMD %s.%s%s" % (m.group(1), m.group(2), m.group(3) or ""))
    for m in re.finditer(r"\bModelDb\.(\w+)(<\w+>)?", s2):
        toks.append("MDB %s%s" % (m.group(1), m.group(2) or ""))
    for m in re.finditer(r"\bnew (\w+Var)(<\w+>)?\(([^;\n]*?)\)(?=,|\s*$|\s*\}|\))", s2, flags=re.M):
        args = re.sub(r"\s+", "", m.group(3))
        toks.append("VAR %s%s(%s)" % (m.group(1), m.group(2) or "", args))
    for m in re.finditer(r"(?<![\w.])(\d+(?:\.\d+)?)m\b", s2):
        toks.append("DEC " + m.group(1))
    for m in re.finditer(r"([\w.\]\)]+)\s*(<=|>=|==|!=|<|>)\s*(-?\d+(?:\.\d+)?)m?\b", s2):
        if m.group(1) in ("num", "num2", "num3") or m.group(1).endswith("__state"):
            continue
        toks.append("CMP %s %s" % (m.group(2), m.group(3)))
    for m in re.finditer(r"\b(Next\w+|Shuffle|UnstableShuffle|NextItem|GetRandom\w*)\s*(<[\w<>]+>)?\(", s2):
        toks.append("RNG " + m.group(1))
    for m in re.finditer(r"\b(RunRngType|PlayerRngType|AscensionLevel|CardRarity|RelicRarity|PotionRarity|CardType|PileType|RoomType|CardKeyword|TargetType|MapPointType|CardTag)\.(\w+)", s2):
        toks.append("ENUM %s.%s" % (m.group(1), m.group(2)))
    for m in re.finditer(r"\bHook\.(\w+)", s2):
        toks.append("HOOK " + m.group(1))
    for m in re.finditer(r"\b(Rewards|Shops|Transformations|Niche|UpFront|CombatCardGeneration|CombatPotionGeneration|CombatCardSelection|CombatEnergyCosts|CombatTargets|MonsterAi|TreasureRoomRelics|UnknownMapPoint|Shuffle|CombatOrbGeneration)\b\s*\.\s*(Next\w+|Shuffle|UnstableShuffle)", s2):
        toks.append("STREAM %s.%s" % (m.group(1), m.group(2)))
    for m in re.finditer(r"\b(PlayerRng|RunRng|\.Rng)\b", s2):
        toks.append("RNGREF " + m.group(1))
    for m in re.finditer(r"\b(\w+Epoch)\b", s2):
        toks.append("EPOCH " + m.group(1))
    for m in re.finditer(r"\b(override)\s+[\w<>\[\]?.]+\s+(\w+)", s2):
        toks.append("OVR " + m.group(2))
    for m in re.finditer(r"\bnew (\w+Reward)\(", s2):
        toks.append("REWARD " + m.group(1))
    for m in re.finditer(r"\b(\w+)\s*<\s*([A-Z]\w+)\s*>\s*\(", s2):
        if m.group(1) in ("RelicOption", "Encounter", "Card", "Relic", "Potion", "Power", "Monster", "EnterCombatWithoutExitingEvent", "Enchantment", "Affliction", "Event", "Apply", "Enchant", "Afflict", "WithRelic", "CreateCard", "Character"):
            toks.append("GEN %s<%s>" % (m.group(1), m.group(2)))
    return toks

def v107_path(name):
    p = os.path.join(V107, "Models/Events", name + ".cs")
    if os.path.exists(p):
        return p
    p = os.path.join(V107, "Models/Events/Mocks", name + ".cs")
    return p if os.path.exists(p) else None

def v111_path(name):
    for d in ("MegaCrit.Sts2.Core.Models.Events", "MegaCrit.Sts2.Core.Models.Events.Mocks"):
        p = os.path.join(V111, d, name + ".cs")
        if os.path.exists(p):
            return p
    return None

def compare(name):
    a = v107_path(name)
    b = v111_path(name)
    if not a or not b:
        return {"name": name, "missing": [x for x, y in (("v107", a), ("v111", b)) if not y]}
    ta = collections.Counter(tokens(read(a)))
    tb = collections.Counter(tokens(read(b)))
    only_a = ta - tb
    only_b = tb - ta
    return {"name": name, "only_v107": sorted(only_a.elements()), "only_v111": sorted(only_b.elements())}

if __name__ == "__main__":
    names = list(common.REST)
    if not names:
        names = sorted(os.path.splitext(f)[0] for f in os.listdir(os.path.join(V111, "MegaCrit.Sts2.Core.Models.Events")))
        names.append("MockEventModel")
    res = [compare(n) for n in names]
    json.dump(res, sys.stdout, indent=1)
