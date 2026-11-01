import os as _os
import sys as _sys
_sys.path.insert(0, _os.path.join(_os.path.dirname(_os.path.abspath(__file__)), ".."))
import common  # noqa: E402
import json, os, re
D = os.path.join(common.DECOMPILE_ROOT, "MegaCrit.Sts2.Core.Models.Powers")
kg = json.load(open(common.stage1_path("kernel-gaps.json")))
files = kg["content_readiness"]["files"]
ported = {x["class"] for x in json.load(open(common.inventory_path("ported.json")))["entries"]["powers"]}
FEATS = [
    ("afflictions", r"CardCmd\.Afflict|ClearAffliction|\.Affliction\b"),
    ("summon", r"CreatureCmd\.Add\b|CreatureCmd\.Add<"),
    ("run_layer", r"LoseGold|GainGold|AddExtraReward|RemoveFromDeck|RunState\.AddCard|MarkLoot"),
    ("side_turn_participants", r"participants\.Contains|participants\b"),
    ("revive", r"SetMoveImmediate|TriggerDeadState|TriggerAboutToBlowState|DoReattach|ReviveMove"),
    ("dead_present_flags", r"ShouldCreatureBeRemovedFromCombatAfterDeath|ShouldAllowHitting|ShouldStopCombatFromEnding"),
    ("stun_from_power", r"CreatureCmd\.Stun"),
    ("combat_history", r"History\."),
    ("prompt", r"CardSelectCmd|RelicSelectCmd"),
    ("card_generation", r"AddToCombatAndPreview|AddGeneratedCardToCombat|CreateCard"),
    ("instanced_target", r"base\.Target\b|\.Target\s*="),
    ("internal_data", r"InitInternalData"),
    ("card_upgrade_downgrade", r"CardCmd\.Downgrade|CardCmd\.Upgrade"),
    ("osty_pets", r"\bOsty\b|PetOwner|\.Pets\b"),
    ("kill_player", r"CreatureCmd\.Kill\("),
    ("energy_cost_mod", r"TryModifyEnergyCostInCombat"),
    ("keyword_mod", r"TryModifyKeywordsInCombat"),
    ("should_play", r"ShouldPlay\("),
    ("block_rules", r"ShouldClearBlock|AfterBlockBroken"),
]


def classify(name):
    p = os.path.join(D, name + ".cs")
    if not os.path.exists(p):
        return None
    t = open(p, encoding="utf-8").read()
    t = re.sub(r"//[^\n]*", "", t)
    feats = [f for f, rx in FEATS if re.search(rx, t)]
    kgf = files.get("Powers/" + name, {})
    missing = kgf.get("missing_hooks", []) + kgf.get("missing_cmds", [])
    tier = 1
    if missing or any(f in feats for f in ("side_turn_participants", "stun_from_power", "instanced_target", "internal_data",
                                            "card_generation", "energy_cost_mod", "keyword_mod", "should_play", "block_rules",
                                            "card_upgrade_downgrade", "kill_player", "osty_pets", "dead_present_flags")):
        tier = 2
    if any(f in feats for f in ("afflictions", "summon", "run_layer", "revive", "combat_history", "prompt")):
        tier = 3
    if name in ported:
        tier = 0
    return {"tier": tier, "features": feats, "missing": missing, "ported": name in ported}


if __name__ == "__main__":
    import sys
    sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
    import annot_m1
    allp = set()
    for k, v in annot_m1.M.items():
        for p in v.get("deps", {}).get("powers", []):
            if p.endswith("Power"):
                allp.add(p)
    out = {p: classify(p) for p in sorted(allp)}
    json.dump(out, open(common.work_path("monsters", "powerdeps.json"), "w"), indent=1)
    for p, v in out.items():
        print(p, v and v["tier"], v and v["features"], v and v["missing"])
