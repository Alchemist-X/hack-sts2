import os as _os
import sys as _sys
_sys.path.insert(0, _os.path.join(_os.path.dirname(_os.path.abspath(__file__)), ".."))
import common  # noqa: E402
import json, os, sys
HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import usage as usage_scan  # noqa: E402
ns = {}
for part in ["gen_part1.py", "gen_part2.py", "gen_part3.py", "gen_part4.py", "gen_part5.py"]:
    exec(open(os.path.join(HERE, part)).read(), ns)

OPEN_QUESTIONS = [
 {"id": "Q1", "topic": "RollingBoulderPower", "question": "In the shipping game, in which order does NRollingBoulderVfx emit HitCreature, and does one CreatureCmd.Damage per creature (shipping) dispatch hooks differently from the single multi-target call used in TestMode? Needs a read of Nodes.Vfx/NRollingBoulderVfx.cs or an oracle trace.", "refs": ["Models.Powers/RollingBoulderPower.cs:98-115"]},
 {"id": "Q2", "topic": "Godot Mathf", "question": "Exact GodotSharp semantics of Mathf.Round, Mathf.RoundToInt and Mathf.FloorToInt (float, midpoint rounding) used in shop prices and GalacticDust; GodotSharp is not part of the decompile tree.", "refs": ["Entities.Merchant/MerchantPotionEntry.cs:198", "Entities.Merchant/MerchantCardEntry.cs:181", "Entities.Merchant/MerchantCardEntry.cs:260", "Models.Relics/GalacticDust.cs:64"]},
 {"id": "Q3", "topic": "unlock state in recordings", "question": "Do the recorded saves in our corpus carry SerializablePlayer.UnlockState (unlocked_epochs, encounters_seen, number_of_runs)? The diff exporter must forward it; older saves deserialize null to UnlockState.all.", "refs": ["Saves.Runs/SerializablePlayer.cs:169", "Unlocks/UnlockState.cs:131-136"]},
 {"id": "Q4", "topic": "WongoPoints", "question": "WelcomeToWongos reads the live profile WongoPoints, which recordings do not capture. Default 0, or record it?", "refs": ["Models.Events/WelcomeToWongos.cs:419-433"]},
 {"id": "Q5", "topic": "prompts inside hooks", "question": "When a prompt opens inside a HookPlayerChoiceContext dispatcher, do the remaining listeners of the same dispatch run before or after the choice resolves (single player)? Combat-kernel study to confirm from Hooks/HookPlayerChoiceContext.", "refs": ["GameActions.Multiplayer/HookPlayerChoiceContext.cs", "Hooks/Hook.cs:8607"]},
 {"id": "Q6", "topic": "tutorial room", "question": "Which map point is entered with MapPointType.Unassigned (first-ever-run Event override in TryGetRoomTypeForTutorial)? Only matters for NumberOfRuns == 0 profiles.", "refs": ["Runs/RunManager.cs:4229-4258"]},
 {"id": "Q7", "topic": "AnyAlly cards", "question": "Are all 15 AnyAlly-target cards/potions MultiplayerOnly (filtered from single-player pools)? Any that are not are generated but never playable in single player.", "refs": ["Models/CardModel.cs:3347-3350", "Runs/IRunState.cs:74-84"]},
 {"id": "Q8", "topic": "history clone cost", "question": "Is an append-only shared log within the Clone budget (<= 1 ms) for long boss fights, or is a per-turn compaction needed? Compaction must keep every field the 45 queries read.", "refs": ["Combat.History/CombatHistory.cs:159-168"]},
 {"id": "Q9", "topic": "fire-and-forget ordering", "question": "For LordsParasol.PurchaseEverything, TreasureRoom/CombatRoom reward Offer and CrystalSphere ClearCell (TaskHelper.RunSafely), does any rule effect happen after the caller continues in the shipping game? The synchronous port runs them inline.", "refs": ["Models.Relics/LordsParasol.cs:555", "Rooms/TreasureRoom.cs:129", "Rooms/CombatRoom.cs:262", "Events.Custom.CrystalSphereEvent/CrystalSphereMinigame.cs:656"]},
 {"id": "Q10", "topic": "version drift", "question": "RelicSelectCmd has no callers and FromChooseABundleScreen has no selector in v0.111.0; the v0.107.1 corpus needs a separate check before reusing these prompt models for old recordings.", "refs": ["Commands/RelicSelectCmd.cs:162-200", "Commands/CardSelectCmd.cs:3798-3840"]}
]

def load_usage(name):
    d = usage_scan.scan(common.DECOMPILE_ROOT, json.load(open(os.path.join(HERE, name))))
    out = {}
    for key, per in d.items():
        cats = {c.split('.')[-1]: len(v) for c, v in per.items()}
        out[key] = {"total_files": sum(cats.values()), "by_category": cats, "files": {c.split('.')[-1]: v for c, v in per.items()}}
    return out

usage = {}
usage.update(load_usage("spec1.json"))
for k, v in load_usage("spec2.json").items():
    usage[k if k not in usage else k + " (spec2)"] = v

DROP = {"player_choice", "dynamic_vars", "godot_nodes", "Hook.", "modifiers", "TestMode", "History", "UnlockState", "LocalContext", "CardSelectCmd", "CardFactory", "PotionFactory", "RelicFactory"}
usage = {k: v for k, v in usage.items() if k not in DROP}

USAGE_PATTERNS = {}
for name in ["spec1.json", "spec2.json"]:
    USAGE_PATTERNS.update(json.load(open(os.path.join(HERE, name))))

subsystem_usage_keys = {
 "test_support": ["test_mode", "TestMode"],
 "card_selection_prompts": ["card_select", "CardSelectCmd", "PushSelector", "ThrowingPlayerChoice", "RelicSelectCmd"],
 "enchantments": ["enchant_apply", "enchantment_read"],
 "afflictions": ["afflict_apply", "affliction_read"],
 "modifiers": ["Modifiers ref"],
 "orbs": ["orb_cmd", "orb_queue", "OrbCmd.Channel", "OrbCmd.Evoke", "OrbCmd.Passive", "OrbCmd.Slots", "CombatOrbGeneration rng"],
 "osty_pets": ["osty_cmd", "osty_ref", "summon_pet", "pets_monster", "PlayerCmd.AddPet", "OstyCmd.Summon"],
 "stars": ["stars", "PlayerCmd.GainStars", "StarCost"],
 "forge": ["forge", "sovereign_blade", "ForgeCmd.Forge"],
 "unlocks_timeline": ["unlock", "UnlockState"],
 "multiplayer_scaling": ["multiplayer_scale"],
 "combat_history": ["history", "History"],
 "helpers_extensions": ["AscensionHelper", "UnstableShuffle", "StableShuffle", "TakeRandom", "Rng.Shuffle", "Rng.NextItem", "Rng.NextInt/Float/Double/Bool", "EggRelicHelper", "TaskHelper", "StringHelper"],
 "saves_profile": ["save_manager", "SaveManager"],
 "local_context": ["local_context", "LocalContext"],
 "model_db": ["card_factory", "potion_factory", "relic_factory", "CardFactory", "PotionFactory", "RelicFactory"]
}

for s in ns["SUBSYSTEMS"]:
    keys = subsystem_usage_keys.get(s["id"], [])
    s["content_usage"] = {}
    for k in keys:
        for cand in (k, k + " (spec2)"):
            if cand in usage:
                u = usage[cand]
                s["content_usage"][cand] = {"pattern": USAGE_PATTERNS.get(k, ""), "total_files": u["total_files"], "by_category": u["by_category"]}

CONTENT_TOTALS = {
 "note": "files per Models.<Category> directory (Mocks sub-namespaces excluded; Deprecated* counted)",
 "Cards": {"files": 596, "deprecated": 1, "mocks": 8},
 "Powers": {"files": 268, "deprecated": 0, "mocks": 18},
 "Relics": {"files": 300, "deprecated": 1, "mocks": 0, "non_model": ["VakuuCardSelector"]},
 "Potions": {"files": 65, "deprecated": 1, "mocks": 1},
 "Monsters": {"files": 121, "deprecated": 1, "mocks": 6},
 "Encounters": {"files": 91, "deprecated": 1, "mocks": 8},
 "Events": {"files": 68, "deprecated": 2, "mocks": 1},
 "Enchantments": {"files": 23, "deprecated": 1, "mocks": 1},
 "Afflictions": {"files": 7, "deprecated": 0, "mocks": 3},
 "Orbs": {"files": 5, "deprecated": 0, "mocks": 2},
 "Modifiers": {"files": 17, "deprecated": 1, "mocks": 0},
 "Badges": {"files": 29, "deprecated": 0, "mocks": 0},
 "Characters": {"files": 8, "deprecated": 1, "mocks": 0, "non_playable": ["Deprived", "RandomCharacter", "DeprecatedCharacter"]},
 "Acts": {"files": 5, "deprecated": 1, "mocks": 0}
}

DECISIONS = {s["id"]: s["decision"] for s in ns["SUBSYSTEMS"]}

UNL = ns["UNLOCK"]
UNL["content_files"] = usage.get("unlock", {}).get("files", {})
doc = {
 "schema": "sts2sim.inventory/v1",
 "kind": "subsystems",
 "game_version": "0.111.0",
 "decompile_root": common.DECOMPILE_ROOT,
 "citation_format": "<ns-dir>/<File>.cs:<line>, MegaCrit.Sts2.Core. prefix dropped",
 "companion_doc": "docs/sim-port/design-subsystems.md",
 "generated_by": common.generator("subsystems", "gen_main.py"),
 "scope": "standard single-player runs (GameMode.Standard, no modifiers, one player, no mods)",
 "decision_vocabulary": {
  "port": "port the game logic 1:1",
  "port-parameter": "port the logic and expose the external input as an explicit sim parameter",
  "port-as-sim-seams": "do not port the class; provide the equivalent seam in the sim API",
  "stub": "identity behaviour for single player; keep only the single-player branch",
  "exclude": "no effect on standard single-player rules"
 },
 "decisions": DECISIONS,
 "content_totals": CONTENT_TOTALS,
 "subsystems": ns["SUBSYSTEMS"],
 "test_seams": ns["TEST_SEAMS"],
 "autoslay": ns["AUTOSLAY"],
 "legal_action_reference": ns["LEGAL_ACTIONS"],
 "combat_history": ns["HISTORY"],
 "unlock_profile_dependence": ns["UNLOCK"],
 "traps": ns["TRAPS"],
 "open_questions": OPEN_QUESTIONS,
 "usage_index": {k: {"pattern": USAGE_PATTERNS.get(k.replace(" (spec2)", ""), ""), "total_files": v["total_files"], "by_category": v["by_category"], "files": v["files"]} for k, v in sorted(usage.items())}
}

out = common.stage1_path("subsystems.json")
with open(out, "w") as f:
    json.dump(doc, f, indent=1, ensure_ascii=False)
    f.write("\n")
print("wrote", out, len(doc["subsystems"]), "subsystems", len(doc["combat_history"]["queries"]), "history queries", len(doc["unlock_profile_dependence"]["paths"]), "unlock paths")
