import os as _os
import sys as _sys
_sys.path.insert(0, _os.path.join(_os.path.dirname(_os.path.abspath(__file__)), ".."))
import common  # noqa: E402
import collections
import json
import os
import re
import sys

sys.path.insert(0, os.path.dirname(__file__))
from condense import condense, old_path, NEW, OLD, ROOT, SLICE  # noqa: E402

NOISE_CALLS = {
    "WithHitFx", "WithAttackerAnim", "WithAttackerFx", "WithNoAttackerAnim", "AfterAttackerAnim", "WithWaitBeforeHit",
    "SpawningHitVfxOnEachCreature", "WithHitVfxSpawnedAtBase", "WithHitVfxNode", "OnlyPlayAnimOnce", "TriggerAnim",
    "Wait", "CustomScaledWait", "Play", "PlayOnCreatureCenter", "PlayOnCreature", "PlayOnCreatureCenters", "PlayVfx",
    "PlayFullScreenInCombat", "GetAwaiter", "GetResult", "ThrowIfNull", "Create", "Start", "SetStateMachine",
    "MoveNext", "Dispose", "GetEnumerator", "ToString", "GetType", "Equals", "GetHashCode", "Preview",
    "PreviewCardPileAdd", "RunSafely", "Instantiate", "AddChildSafely", "GetSideCenter", "GetSideCenterFloor",
    "FromPower", "FromCard", "FromKeyword", "FromRelic", "FromPotion", "FromOrb", "Static", "ForEnergy",
    "FromCardWithCardHoverTips", "FromEnchantment", "FromAffliction", "AsyncStateMachine", "_002Ector",
    "GetPortraitPath", "GetPortraitFilename", "GetImagePath", "GetPortraitPngPath", "Capture", "Throw",
    "PlaySfx", "PlayLoop", "StopLoop", "SetParam", "GetBottomOfHitbox", "Shake", "AppendInterpolatedStringHandler",
    "AppendLiteral", "AppendFormatted", "ToStringAndClear", "ReadOnlySpan",
    "op_Implicit", "Where", "Count", "Any", "OfType", "Select", "ToList", "ToArray", "First", "FirstOrDefault",
    "Contains", "Add", "Invoke", "Empty", "DisposeAsync", "Concat", "Cast", "Sum", "Max", "Min", "OrderBy",
    "ThenBy", "Distinct", "Take", "Skip", "Last", "LastOrDefault", "ElementAt", "InitializeArray", "ToLowerInvariant",
    "GetCreatureNode", "CheckMissingWithAnim", "ToHashSet", "All", "Single", "SingleOrDefault", "Except", "Union",
    "Append", "Prepend", "Reverse", "AsEnumerable", "TryGetValue", "Remove", "Clear", "IndexOf", "Insert",
    "ArgumentNullException", "Task", "Color", "NGroundFireVfx", "Vector2", "Mathf", "IEnumerable", "IReadOnlyList", "List",
    "Func", "Action", "Log", "Warn", "Info", "Error", "Debug", "SceneHelper", "GetScenePath", "Node", "Node2D", "Variant",
    "ExtraHoverTips", "LocString", "Format", "Lerp", "Round", "RoundToInt", "Abs", "Floor", "Ceiling", "Ceil", "Clamp",
}
ENUM_RE = re.compile(r"\b(CardType|CardRarity|TargetType|CardKeyword|PileType|CardPilePosition|ValueProp|AscensionLevel|"
                     r"CardTag|PowerType|CombatSide|AutoPlayType|CardPreviewStyle|OrbEvokeType|RunRngType|PlayerRngType|"
                     r"CardMultiplayerConstraint|PowerStackType|GoldLossType|KeywordSources|CardCreationSource)\.(\w+)")
NUM_RE = re.compile(r"(?<![\w.])(\d+(?:\.\d+)?)m?(?![\w.])")
CALL_RE = re.compile(r"(?<!new )(?:\.|(?<![\w.]))([A-Z]\w*)\s*(?:<[\w.<>, ?]+>)?\s*\(")
CMD_RE = re.compile(r"\b(\w*Cmd|Hook)\.(\w+)(<[\w.<>, ?]+>)?")
MODELDB_RE = re.compile(r"\bModelDb\.(\w+)(<[\w.]+>)?")
DV_RE = re.compile(r"DynamicVars\.(\w+)|DynamicVars\[\"(\w+)\"\]")


def member_text(text, name):
    m = re.search(r"\b" + name + r"\b\s*(=>|\(\)\s*\{|\{)", text)
    if not m:
        return None
    i = m.end()
    depth = 1 if text[m.end() - 1] == "{" else 0
    j = i
    n = len(text)
    while j < n:
        c = text[j]
        if c in "({[":
            depth += 1
        elif c in ")}]":
            depth -= 1
            if depth < 0 or (depth == 0 and c == "}" and text[m.end() - 1] == "{"):
                return text[m.start():j + 1]
        elif c == ";" and depth == 0:
            return text[m.start():j + 1]
        j += 1
    return text[m.start():]


def norm(s):
    s = re.sub(r"//[^\n]*", "", s)
    s = re.sub(r"global::", "", s)
    s = re.sub(r"System\.Collections\.Generic\.", "", s)
    s = re.sub(r"_003C_003Ez__", "<>z__", s)
    s = re.sub(r"\s+", " ", s)
    return s.strip()


def strip_comments(text):
    return re.sub(r"//[^\n]*", "", text)


def signature(path):
    raw = open(path, encoding="utf-8-sig", errors="replace").read()
    text = strip_comments(raw)
    sig = {}
    m = re.search(r":\s*base\(([^;{]*?)\)\s*\{", text)
    sig["ctor"] = norm(m.group(1)) if m else None
    for name in ("CanonicalVars", "CanonicalKeywords", "CanonicalTags", "OnUpgrade", "MaxUpgradeLevel",
                 "HasEnergyCostX", "CanonicalStarCost", "HasStarCostX", "CanBeGeneratedInCombat",
                 "CanBeGeneratedByModifiers", "MultiplayerConstraint", "OrbEvokeType", "GainsBlock",
                 "HasTurnEndInHandEffect", "Type", "Rarity", "TargetType", "CanonicalEnergyCost"):
        t = member_text(text, name)
        sig[name] = norm(t) if t else None
    if sig["CanonicalVars"]:
        sig["vars_list"] = re.findall(r"new (\w+(?:<\w+>)?)\(([^()]*(?:\([^()]*\))?[^()]*)\)", sig["CanonicalVars"])
    rawb = raw
    for name in ("CanonicalVars", "CanonicalKeywords", "CanonicalTags", "OnUpgrade"):
        t = member_text(rawb, name)
        if t:
            rawb = rawb.replace(t, "\n" * t.count("\n"))
    rawb = re.sub(r":\s*base\([^;{]*?\)", "", rawb)
    body = "\n".join(condense(path, with_lines=False, text=rawb))
    toks = collections.Counter()
    for mm in CMD_RE.finditer(body):
        if mm.group(2) in NOISE_CALLS or mm.group(1) in ("VfxCmd", "SfxCmd", "TalkCmd", "ThinkCmd"):
            continue
        toks["cmd:%s.%s%s" % (mm.group(1), mm.group(2), mm.group(3) or "")] += 1
    for mm in MODELDB_RE.finditer(body):
        toks["db:%s%s" % (mm.group(1), mm.group(2) or "")] += 1
    for mm in ENUM_RE.finditer(body):
        toks["enum:%s.%s" % (mm.group(1), mm.group(2))] += 1
    for mm in DV_RE.finditer(body):
        toks["dv:%s" % (mm.group(1) or mm.group(2))] += 1
    for mm in CALL_RE.finditer(body):
        if mm.group(1) in NOISE_CALLS:
            continue
        toks["call:%s" % mm.group(1)] += 1
    nums = collections.Counter()
    for line in body.split("\n"):
        if re.search(r"vfx|sfx|\.mp3|\.tscn|\"", line, re.I):
            line = re.sub(r"\"[^\"]*\"", "", line)
        for mm in NUM_RE.finditer(line):
            v = mm.group(1)
            if v in ("0", "1"):
                continue
            nums[v] += 1
    sig["logic_tokens"] = dict(toks)
    sig["logic_nums"] = dict(nums)
    overrides = sorted(set(re.findall(r"\boverride\s+[\w.<>?, \[\]]+?\s+(\w+)\s*(?:\(|=>|\{)", text)) - {"ExtraHoverTips"})
    sig["overrides"] = overrides
    return sig


def hnorm(k, v):
    if v is None:
        return ""
    if k in ("CanonicalKeywords", "CanonicalTags"):
        return " ".join(re.findall(r"\b(?:CardKeyword|CardTag)\.\w+", v)) or ("UNREADABLE" if "InitializeArray" in v else v)
    if k == "CanonicalVars":
        v = re.sub(r"\.WithMultiplier\(.*", ".WithMultiplier(...)", v)
        return " ".join(re.findall(r"new (\w+(?:<\w+>)?)\(([^()]*)\)", v).__repr__().split())
    if k == "OnUpgrade":
        v = v.replace(".Invoke((CardModel)this)", "(this)")
    return v


def compare(a, b):
    diffs = []
    for k in ("ctor", "CanonicalVars", "CanonicalKeywords", "CanonicalTags", "OnUpgrade", "MaxUpgradeLevel",
              "HasEnergyCostX", "CanonicalStarCost", "HasStarCostX", "CanBeGeneratedInCombat",
              "CanBeGeneratedByModifiers", "MultiplayerConstraint", "OrbEvokeType", "GainsBlock",
              "HasTurnEndInHandEffect", "Type", "Rarity", "TargetType", "CanonicalEnergyCost"):
        if hnorm(k, a.get(k)) != hnorm(k, b.get(k)):
            diffs.append(("hdr", k, b.get(k), a.get(k)))
    if a["overrides"] != b["overrides"]:
        diffs.append(("ovr", sorted(set(b["overrides"]) - set(a["overrides"])), sorted(set(a["overrides"]) - set(b["overrides"]))))
    ta, tb = collections.Counter(a["logic_tokens"]), collections.Counter(b["logic_tokens"])
    if ta != tb:
        diffs.append(("tok", dict(tb - ta), dict(ta - tb)))
    na, nb = collections.Counter(a["logic_nums"]), collections.Counter(b["logic_nums"])
    if na != nb:
        diffs.append(("num", dict(nb - na), dict(na - nb)))
    return diffs


def main():
    d = json.load(open(common.inventory_path("cards.json")))
    res = {}
    for e in d["entries"]:
        if e["pool"] not in SLICE:
            continue
        p = os.path.join(NEW, "MegaCrit.Sts2.Core." + e["file"])
        op = old_path(e["file"])
        if not op:
            res[e["id"]] = {"status": "new", "old": None}
            continue
        a, b = signature(p), signature(op)
        df = compare(a, b)
        res[e["id"]] = {"status": "same" if not df else "diff", "old": op.replace(OLD + "/", ""), "diffs": df}
    out = common.work_path("cards-ironclad-colorless", "vdiff.json")
    json.dump(res, open(out, "w"), indent=1, default=str)
    c = collections.Counter(v["status"] for v in res.values())
    print(c)
    for k, v in res.items():
        if v["status"] != "same":
            print(k, v["status"])
            for x in v.get("diffs", []):
                print("   ", json.dumps(x, default=str)[:600])


if __name__ == "__main__":
    main()
