import os as _os
import sys as _sys
_sys.path.insert(0, _os.path.join(_os.path.dirname(_os.path.abspath(__file__)), ".."))
import common  # noqa: E402
import json, re, sys, collections, os
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import importlib.util
spec = importlib.util.spec_from_file_location("td", os.path.join(os.path.dirname(os.path.abspath(__file__)), "tree_diff.py"))
src = open(spec.origin).read().replace("\nmain()\n", "\n")
td = {"__file__": spec.origin}
exec(compile(src, spec.origin, "exec"), td)

NOISE_CTX = {"_", "op_Implicit", "Where", "Any", "Count", "Max", "Min", "LastOrDefault", "FirstOrDefault", "Select", "if", "for", "while",
             "-", "Rng", "CaptureMessage", "CallDeferred", "Connect", "Instantiate", "Load", "MoveChildSafely", "GetPortraitPath", "Insert",
             "Add", "switch", "return", "foreach", "Range", "Repeat", "Take", "Skip", "ElementAt", "GetChild", "GetNode", "Emit", "Format"}
BIG = {"79228162514264337593543950335", "79228162514264337593543950340", "-79228162514264337593543950335", "-79228162514264337593543950340",
       "2147483647", "-2147483648"}
NOISE_CALLS = {"AnimState", "CreatureAnimator", "GenerateAnimator", "TriggerAnim", "AddAnyState", "OnlyPlayAnimOnce", "GenerateBestiaryMoveList",
               "RemoveAll", "SetNodeVisible", "RevealWrigglersAfterDeathAnim", "RevealReplacementAfterDeathAnim", "Flash", "Wait",
               "CaptureMessage", "CallDeferred", "Connect", "Instantiate", "Load", "MoveChildSafely", "GetSpecialNode", "Static", "ToMutable",
               "BlockingPlayerChoiceContext", "ThrowingPlayerChoiceContext", "AssertMutable", "IsValid", "InvokeDisplayAmountChanged",
               "DisableInteractionForDeath", "IsLiveCombat", "GetMe", "ShouldShowMoveInBestiary", "WithNoAttackerAnim", "WithAttackerFx",
               "WithHitFx", "LocString", "GetEntry", "AssertValidForTargetedPotion", "PlaySfx", "PlayVfx", "SetAnimation", "QueueFree",
               "AddChild", "GetTree", "CreateTween", "TweenProperty", "SetParallel", "Chain", "SetEase", "SetTrans", "TweenCallback",
               "TweenInterval", "From", "GetNodeOrNull", "IsInstanceValid", "PlayAnimation", "SetSkin", "Shake", "Animate", "TalkCmd",
               "Play", "CreateVfx", "GetVfx", "FireAndForget", "DoNothing", "Reset", "SetVisible", "Show", "Hide", "SetTexture", "Modulate",
               "OnEffectTriggered", "SetActive", "Log", "Debug", "Info", "AssertNotNull", "Assert"}
def fnum(v):
    try:
        x = float(v)
        if "." in v:
            return format(round(x, 6), "g")
        return v
    except ValueError:
        return v
LAYOUT_CTX = {"ValueTuple", "List", "Dictionary", "HashSet", "GetConstructor", "GetProperty", "AppendInterpolatedStringHandler",
              "DefaultInterpolatedStringHandler", "InlineArrayAsReadOnlySpan", "InlineArrayElementRef", "TaskCompletionSource",
              "SetFingerprint", "SetAnchorsPreset", "SmoothDamp", "SetFocusMode", "Split", "Concat", "GetLength", "AddChild", "Clamp",
              "CardSelectorPrefs", "PreviewCardPileAdd", "Preview", "CustomScaledWait", "SummonResult"}
def filt_nums(nums):
    out = collections.Counter()
    for (ctx, val), n in nums.items():
        if td["PRESENT"].search(ctx) or val in BIG or ctx in LAYOUT_CTX:
            continue
        v = fnum(val)
        if v in ("0", "1", "-1"):
            continue
        out[("*", v)] += n
    return out
def filt_calls(calls):
    out = set()
    for c in calls:
        c2 = re.sub(r"MegaCrit\.Sts2\.Core\.[\w\.]*\.", "", c)
        name = c2.split("<")[0]
        if name in NOISE_CALLS:
            continue
        out.add(c2)
    return out

new = td["index_tree"](td["NEW"])
old = td["index_tree"](td["OLD"])
content_ns = {"MegaCrit.Sts2.Core.Models.Cards": "cards", "MegaCrit.Sts2.Core.Models.Powers": "powers",
              "MegaCrit.Sts2.Core.Models.Monsters": "monsters", "MegaCrit.Sts2.Core.Models.Relics": "relics",
              "MegaCrit.Sts2.Core.Models.Potions": "potions", "MegaCrit.Sts2.Core.Models.Encounters": "encounters",
              "MegaCrit.Sts2.Core.Models.Events": "events", "MegaCrit.Sts2.Core.Models.Enchantments": "enchantments",
              "MegaCrit.Sts2.Core.Models.Acts": "acts", "MegaCrit.Sts2.Core.Models.Characters": "characters",
              "MegaCrit.Sts2.Core.Models.Afflictions": "afflictions", "MegaCrit.Sts2.Core.Models.Orbs": "orbs",
              "MegaCrit.Sts2.Core.Models.CardPools": "card_pools", "MegaCrit.Sts2.Core.Models.RelicPools": "relic_pools",
              "MegaCrit.Sts2.Core.Models.PotionPools": "potion_pools", "MegaCrit.Sts2.Core.Models.Modifiers": "modifiers"}
engine_ns_prefixes = ["MegaCrit.Sts2.Core.Hooks", "MegaCrit.Sts2.Core.Combat", "MegaCrit.Sts2.Core.Commands", "MegaCrit.Sts2.Core.GameActions",
                      "MegaCrit.Sts2.Core.Entities.", "MegaCrit.Sts2.Core.Runs", "MegaCrit.Sts2.Core.Map", "MegaCrit.Sts2.Core.Rewards",
                      "MegaCrit.Sts2.Core.Factories", "MegaCrit.Sts2.Core.Random", "MegaCrit.Sts2.Core.Odds", "MegaCrit.Sts2.Core.Rooms",
                      "MegaCrit.Sts2.Core.MonsterMoves", "MegaCrit.Sts2.Core.Events", "MegaCrit.Sts2.Core.Extensions",
                      "MegaCrit.Sts2.Core.CardSelection", "MegaCrit.Sts2.Core.ValueProps", "MegaCrit.Sts2.Core.Helpers",
                      "MegaCrit.Sts2.Core.Localization.DynamicVars", "MegaCrit.Sts2.Core.Unlocks", "MegaCrit.Sts2.Core.Saves.Runs"]
skip_engine = ("Multiplayer", "Nodes")
res = {"content": {}, "engine": {}}
area_rows = collections.defaultdict(lambda: collections.Counter())
area_files = collections.defaultdict(list)
eng_files = []
for k in sorted(set(new) & set(old)):
    ns = k[0]
    is_content = ns in content_ns
    is_engine = (not is_content) and any(ns == p or ns.startswith(p) for p in engine_ns_prefixes) and not any(s in ns for s in skip_engine)
    if not (is_content or is_engine):
        continue
    own = k[1].split("#")[0][:-3]
    tn, to = new[k][1], old[k][1]
    gap = "LdMemberToken" in tn
    a = td["analyze"](td["clean"](tn), own)
    b = td["analyze"](td["clean"](to), own)
    nn, sn, en, cn, pn = a
    no, so, eo, co, po = b
    fn_, fo = filt_nums(nn), filt_nums(no)
    vals_n = collections.Counter(v for (c, v), n in fn_.items() for _ in range(n))
    vals_o = collections.Counter(v for (c, v), n in fo.items() for _ in range(n))
    val_changed = (vals_n != vals_o) or (pn != po)
    enum_changed = (en != eo) and not gap
    cfn, cfo = filt_calls(cn), filt_calls(co)
    call_changed = cfn != cfo
    any_rule = val_changed or enum_changed or call_changed
    area = content_ns.get(ns) or ("engine:" + ns.replace("MegaCrit.Sts2.Core.", ""))
    row = area_rows[area]
    row["common"] += 1
    if gap:
        row["ldmembertoken_gap"] += 1
    if val_changed:
        row["value_changed"] += 1
    if enum_changed:
        row["enum_changed"] += 1
    if call_changed:
        row["hook_or_command_changed"] += 1
    if any_rule:
        row["rule_diverging_estimate"] += 1
        entry = {"file": k[1]}
        if val_changed:
            rem = (fo - fn_); add = (fn_ - fo)
            entry["values"] = {"v0.107.1": sorted(x[1] for x in rem.elements())[:10] + sorted("%s=>%s" % x for x in (po - pn).elements()),
                               "v0.111.0": sorted(x[1] for x in add.elements())[:10] + sorted("%s=>%s" % x for x in (pn - po).elements())}
        if enum_changed:
            entry["enums"] = {"v0.107.1": sorted((eo - en).elements()), "v0.111.0": sorted((en - eo).elements())}
        if call_changed:
            entry["calls"] = {"v0.107.1_only": sorted(cfo - cfn)[:12], "v0.111.0_only": sorted(cfn - cfo)[:12]}
        if gap:
            entry["decompile_gap"] = "v0.111.0 array initializer unreadable (RuntimeHelpers.InitializeArray/LdMemberToken)"
        (area_files[area] if is_content else eng_files).append(dict(entry, namespace=ns.replace("MegaCrit.Sts2.Core.", "")) if not is_content else entry)
added = collections.Counter()
removed = collections.Counter()
added_files = collections.defaultdict(list)
removed_files = collections.defaultdict(list)
for k in sorted(set(new) - set(old)):
    if k[0] in content_ns:
        added[content_ns[k[0]]] += 1
        added_files[content_ns[k[0]]].append(k[1][:-3])
for k in sorted(set(old) - set(new)):
    if k[0] in content_ns:
        removed[content_ns[k[0]]] += 1
        removed_files[content_ns[k[0]]].append(k[1][:-3])
summary = {}
for area in list(content_ns.values()):
    r = area_rows.get(area, collections.Counter())
    summary[area] = {"common": r["common"], "only_v0.111.0": added[area], "only_v0.107.1": removed[area],
                     "value_changed": r["value_changed"], "enum_changed": r["enum_changed"],
                     "hook_or_command_changed": r["hook_or_command_changed"], "rule_diverging_estimate": r["rule_diverging_estimate"],
                     "ldmembertoken_gap": r["ldmembertoken_gap"]}
eng_summary = {}
for area, r in sorted(area_rows.items()):
    if area.startswith("engine:") and r["rule_diverging_estimate"]:
        eng_summary[area[7:]] = {"common": r["common"], "rule_diverging_estimate": r["rule_diverging_estimate"],
                                 "value_changed": r["value_changed"], "enum_changed": r["enum_changed"],
                                 "hook_or_command_changed": r["hook_or_command_changed"]}
out = {"content_summary": summary, "engine_summary": eng_summary,
       "content_files": {a: v for a, v in area_files.items()},
       "only_v0.111.0": {a: sorted(v) for a, v in added_files.items()}, "only_v0.107.1": {a: sorted(v) for a, v in removed_files.items()},
       "engine_files": eng_files}
json.dump(out, open(common.work_path("corpus", "version_diff.json"), "w"), indent=1)
tot = collections.Counter()
for a, s in summary.items():
    print(a, s)
    for kk, vv in s.items():
        tot[kk] += vv
print("TOTAL", dict(tot))
print("ENGINE", json.dumps(eng_summary))
