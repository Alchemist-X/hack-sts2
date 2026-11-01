"""Build sim/specs/inventory/run-layer-gaps.json (W0 run-layer gap inventory).

Usage: python3 sim/tools/inventory/classify/run-layer/build_gaps.py [--root <decompile>] [--inventory <dir>] [--work <dir>]
Stdlib only. Curated facts come from docs/sim-port/design-run-layer.md; counts and hook
implementer lists are recomputed from the decompile tree and the existing inventory.
"""

import os as _os
import sys as _sys
_sys.path.insert(0, _os.path.join(_os.path.dirname(_os.path.abspath(__file__)), ".."))
import common  # noqa: E402
import json
import os
import re
import sys

DECOMP = common.DECOMPILE_ROOT
NS = "MegaCrit.Sts2.Core."


def read(rel):
    with open(os.path.join(DECOMP, rel), encoding="utf-8") as f:
        return f.read()


def model_dirs():
    return sorted(d for d in os.listdir(DECOMP) if d.startswith(NS + "Models"))


def implementers(hook):
    pat = re.compile(r"override [^(\n]*\b" + re.escape(hook) + r"\(")
    found = []
    for d in model_dirs():
        full = os.path.join(DECOMP, d)
        for fn in sorted(os.listdir(full)):
            if not fn.endswith(".cs"):
                continue
            with open(os.path.join(full, fn), encoding="utf-8") as f:
                if pat.search(f.read()):
                    found.append(d[len(NS):] + "/" + fn[:-3])
    return found


def act_data(act):
    src = read(f"{NS}Models.Acts/{act}.cs")
    body = re.search(r"GenerateAllEncounters\(\)\s*\{(.*?)\n\t\}", src, re.S).group(1)
    encs = re.findall(r"Encounter<(\w+)>", body)
    tiers = {"weak": [], "normal": [], "elite": [], "boss": []}
    for e in encs:
        es = read(f"{NS}Models.Encounters/{e}.cs")
        rt = re.search(r"RoomType RoomType => RoomType\.(\w+)", es)
        r = rt.group(1) if rt else "?"
        weak = "IsWeak => true" in es
        key = "boss" if r == "Boss" else "elite" if r == "Elite" else ("weak" if weak else "normal")
        tiers[key].append(e)
    ev = re.search(r"AllEvents =>.*?\{(.*?)\}\);", src, re.S).group(1)
    anc = re.search(r"AllAncients =>(.*?);\n", src, re.S).group(1)
    bdo = re.search(r"BossDiscoveryOrder =>.*?\{(.*?)\}\);", src, re.S).group(1)
    return {
        "id": act,
        "file": f"Models.Acts/{act}.cs",
        "index": int(re.search(r"int Index => (\d+)", src).group(1)),
        "is_default": "IsDefault => true" in src,
        "base_rooms": int(re.search(r"BaseNumberOfRooms => (\d+)", src).group(1)),
        "weak_encounter_slots": int(re.search(r"NumberOfWeakEncounters => (\d+)", src).group(1)),
        "encounters": tiers,
        "events": re.findall(r"Event<(\w+)>", ev),
        "ancients": re.findall(r"AncientEvent<(\w+)>", anc),
        "boss_discovery_order": re.findall(r"Encounter<(\w+)>", bdo),
        "unlock": "UnderdocksEpoch" if "UnderdocksEpoch" in src else "always",
    }


with open(common.inventory_path("hooks.json"), encoding="utf-8") as f:
    hooks_inv = json.load(f)
run_missing = [h for h in hooks_inv["hooks"] if h["scope"] == "run" and not h["in_sim"]]
both_missing = [h for h in hooks_inv["hooks"] if h["scope"] == "both" and not h["in_sim"]]


def hook_entry(h):
    imps = implementers(h["name"])
    return {
        "name": h["name"],
        "abstract_model_line": f"Models/AbstractModel.cs:{h['line']}",
        "dispatchers": h["dispatchers"],
        "scope": h["scope"],
        "implementer_count": len(imps),
        "implementers": imps,
    }


acts = [act_data(a) for a in ("Overgrowth", "Underdocks", "Hive", "Glory")]
shared_events = re.findall(
    r"Event<(\w+)>",
    re.search(r"AllSharedEvents =>.*?\{(.*?)\}\)\);", read(f"{NS}Models/ModelDb.cs"), re.S).group(1),
)


def count_files(sub):
    d = os.path.join(DECOMP, NS + sub)
    return len([f for f in os.listdir(d) if f.endswith(".cs")])


content_counts = {
    "relics": count_files("Models.Relics"),
    "potions": count_files("Models.Potions"),
    "events": count_files("Models.Events"),
    "encounters": count_files("Models.Encounters"),
    "cards": count_files("Models.Cards"),
    "enchantments": count_files("Models.Enchantments"),
    "modifiers": count_files("Models.Modifiers"),
    "relics_with_saved_properties": len(
        [f for f in os.listdir(os.path.join(DECOMP, NS + "Models.Relics")) if "SavedProperty" in read(f"{NS}Models.Relics/{f}")]
    ),
    "events_overriding_is_allowed": len(
        [f for f in os.listdir(os.path.join(DECOMP, NS + "Models.Events")) if "override bool IsAllowed" in read(f"{NS}Models.Events/{f}")]
    ),
    "relics_overriding_is_allowed": len(
        [f for f in os.listdir(os.path.join(DECOMP, NS + "Models.Relics")) if "override bool IsAllowed(" in read(f"{NS}Models.Relics/{f}")]
    ),
}

SUBSYSTEMS = [
    {"id": "run_state", "status": "missing", "game_sources": ["Runs/RunState.cs", "Entities.Players/Player.cs", "Runs/ExtraRunFields.cs", "Entities.Players/ExtraPlayerFields.cs"], "sim_today": "none; PlayerState is combat-only, deck/HP are CombatSim.Create inputs", "target_dir": "sim/Sts2Sim.Core/Run/State/", "owner": "R1", "depends_on": ["K3", "K5"], "est_loc": 1500, "doc": "§2"},
    {"id": "rng_sets", "status": "ported", "game_sources": ["Runs/RunRngSet.cs", "Random/PlayerRngSet.cs", "Random/Rng.cs"], "sim_today": "complete in Random/RngSets.cs, RngSetSnapshots.cs, RngSeeds.cs", "target_dir": "sim/Sts2Sim.Core/Random/", "owner": "existing", "depends_on": [], "est_loc": 0, "doc": "Appendix A", "remaining": ["consumers of UpFront/UnknownMapPoint/TreasureRoomRelics/PlayerRng", "ad-hoc streams act_selection, act_{n}_map, spoils_map (formula = RngSeeds.Named)"]},
    {"id": "odds", "status": "missing", "game_sources": ["Odds/CardRarityOdds.cs", "Odds/PotionRewardOdds.cs", "Odds/UnknownMapPointOdds.cs", "Odds/RunOddsSet.cs", "Odds/PlayerOddsSet.cs", "Odds/AbstractOdds.cs"], "target_dir": "sim/Sts2Sim.Core/Run/Odds/", "owner": "R3", "depends_on": ["K2"], "est_loc": 500, "doc": "§5.1, §5.3, §3.5"},
    {"id": "relic_grab_bags", "status": "missing", "game_sources": ["Runs/RelicGrabBag.cs", "Factories/RelicFactory.cs"], "target_dir": "sim/Sts2Sim.Core/Run/Relics/", "owner": "R3", "depends_on": ["K3"], "est_loc": 500, "doc": "§5.4"},
    {"id": "map_generation", "status": "missing", "game_sources": ["Map/ActMap.cs", "Map/StandardActMap.cs", "Map/SpoilsActMap.cs", "Map/GoldenPathActMap.cs", "Map/SavedActMap.cs", "Map/MapPathPruning.cs", "Map/MapPostProcessing.cs", "Map/MapPoint.cs", "Map/MapCoord.cs", "Map/MapPointTypeCounts.cs", "Map/MapTravel.cs"], "target_dir": "sim/Sts2Sim.Core/Run/Map/", "owner": "R2", "depends_on": [], "est_loc": 1800, "doc": "§4"},
    {"id": "acts_room_sets", "status": "missing", "game_sources": ["Models/ActModel.cs", "Models.Acts/*.cs", "Rooms/RoomSet.cs", "Helpers/GrabBag.cs"], "target_dir": "sim/Sts2Sim.Core/Run/State/ + Run/Util/", "owner": "R1/R3", "depends_on": [], "est_loc": 700, "doc": "§6"},
    {"id": "room_flow", "status": "missing", "game_sources": ["Runs/RunManager.cs", "Rooms/AbstractRoom.cs", "Rooms/MapRoom.cs", "Multiplayer.Game/ActChangeSynchronizer.cs"], "target_dir": "sim/Sts2Sim.Core/Run/Loop/", "owner": "R6", "depends_on": ["R1", "R2", "R3", "R4", "R5"], "est_loc": 2200, "doc": "§3.1-3.4, §3.14"},
    {"id": "rewards", "status": "missing", "game_sources": ["Rewards/*.cs", "Commands/RewardsCmd.cs", "Factories/CardFactory.cs", "Factories/PotionFactory.cs", "Entities.CardRewardAlternatives/CardRewardAlternative.cs", "Multiplayer.Game/RewardsSetSynchronizer.cs", "Runs/CardCreationOptions.cs", "Runs/CardCreationFlags.cs"], "target_dir": "sim/Sts2Sim.Core/Run/Rewards/ + Run/Factories/", "owner": "R3", "depends_on": ["K1", "K2", "K3", "K4", "K5"], "est_loc": 1200, "doc": "§3.6, §5.2"},
    {"id": "merchant", "status": "missing", "game_sources": ["Entities.Merchant/*.cs", "Rooms/MerchantRoom.cs", "Nodes.Screens.Shops/NMerchantCardRemoval.cs"], "target_dir": "sim/Sts2Sim.Core/Run/Rooms/", "owner": "R4", "depends_on": ["R3", "K1", "K4"], "est_loc": 900, "doc": "§3.8"},
    {"id": "rest_site", "status": "missing", "game_sources": ["Entities.RestSite/*.cs", "Rooms/RestSiteRoom.cs", "Multiplayer.Game/RestSiteSynchronizer.cs", "Nodes.Rooms/NRestSiteRoom.cs"], "target_dir": "sim/Sts2Sim.Core/Run/Rooms/", "owner": "R4", "depends_on": ["K1", "K3", "K4"], "est_loc": 600, "doc": "§3.9"},
    {"id": "treasure", "status": "missing", "game_sources": ["Rooms/TreasureRoom.cs", "Multiplayer.Game/TreasureRoomRelicSynchronizer.cs", "Multiplayer.Game/OneOffSynchronizer.cs", "Entities.TreasureRelicPicking/*.cs", "Nodes.Rooms/NTreasureRoom.cs", "Nodes.Screens.TreasureRoomRelic/NTreasureRoomRelicCollection.cs"], "target_dir": "sim/Sts2Sim.Core/Run/Rooms/", "owner": "R4", "depends_on": ["R3", "K3"], "est_loc": 500, "doc": "§3.7"},
    {"id": "event_framework", "status": "missing", "game_sources": ["Models/EventModel.cs", "Events/EventOption.cs", "Rooms/EventRoom.cs", "Multiplayer.Game/EventSynchronizer.cs", "Multiplayer.Game/EventCombatSynchronizer.cs"], "target_dir": "sim/Sts2Sim.Core/Run/Events/", "owner": "R5", "depends_on": ["K1", "K3", "R3"], "est_loc": 1100, "doc": "§3.10", "note": "68 event defs are W3 content"},
    {"id": "neow_and_ancients", "status": "missing", "game_sources": ["Models/AncientEventModel.cs", "Models.Events/Neow.cs", "Models.Events/Orobas.cs", "Models.Events/Pael.cs", "Models.Events/Tezcatara.cs", "Models.Events/Nonupeipe.cs", "Models.Events/Tanx.cs", "Models.Events/Vakuu.cs", "Models.Events/Darv.cs", "Models.Events/TheArchitect.cs"], "sim_today": "arrival HP formula only (StartingDecks.HpAfterNeowArrival)", "target_dir": "sim/Sts2Sim.Core/Run/Events/", "owner": "R5", "depends_on": ["K3", "R3"], "est_loc": 900, "doc": "§3.11"},
    {"id": "ascension_run_effects", "status": "partial", "game_sources": ["Entities.Ascension/AscensionManager.cs", "Entities.Ascension/AscensionLevel.cs", "Helpers/AscensionHelper.cs"], "sim_today": "A5 bane for the starter deck; A8/A9 inside ported monsters", "target_dir": "spread over R1-R5", "owner": "R1-R5", "depends_on": [], "est_loc": 100, "doc": "§6.4"},
    {"id": "run_scope_hooks", "status": "missing", "game_sources": ["Hooks/Hook.cs", "Models/AbstractModel.cs"], "sim_today": "0/46 run-scope; 12 both-scope missing", "target_dir": "W1 kernel (Combat/Pipeline/Hook.cs owner)", "owner": "W1", "depends_on": [], "est_loc": 900, "doc": "§2.7, Appendix B"},
    {"id": "deck_prompts", "status": "missing", "game_sources": ["Commands/CardSelectCmd.cs", "CardSelection/CardSelectorPrefs.cs", "TestSupport/ICardSelector.cs", "Commands/RelicSelectCmd.cs"], "sim_today": "none; P0 has no prompts", "target_dir": "W1 kernel API; run call sites in R3-R5", "owner": "W1+R3-R5", "depends_on": ["K1"], "est_loc": 600, "doc": "§3.12"},
    {"id": "potions_out_of_combat", "status": "missing", "game_sources": ["GameActions/UsePotionAction.cs", "GameActions/DiscardPotionGameAction.cs", "Commands/PotionCmd.cs", "Nodes.Potions/NPotionPopup.cs"], "target_dir": "sim/Sts2Sim.Core/Run/Loop/", "owner": "R6", "depends_on": ["K3", "K4"], "est_loc": 300, "doc": "§3.13"},
    {"id": "history_and_score", "status": "missing", "game_sources": ["Runs.History/*.cs", "Runs/PlayerMapPointHistoryEntry.cs", "Runs/ScoreUtility.cs"], "target_dir": "sim/Sts2Sim.Core/Run/State/", "owner": "R1", "depends_on": [], "est_loc": 300, "doc": "§2.5"},
    {"id": "unlock_profile", "status": "missing", "game_sources": ["Unlocks/UnlockState.cs", "Timeline.Epochs/*.cs"], "sim_today": "implicit all-unlocked", "target_dir": "sim/Sts2Sim.Core/Run/State/", "owner": "R1", "depends_on": [], "est_loc": 250, "doc": "§5.6"},
    {"id": "save_import", "status": "missing", "game_sources": ["Saves/SerializableRun.cs", "Saves.Runs/*.cs", "Runs/RunState.cs", "Saves/SerializablePlayerRngSet.cs", "Saves/SerializableExtraPlayerFields.cs"], "sim_today": "RunRngSetSnapshot/PlayerRngSetSnapshot only", "target_dir": "sim/Sts2Sim.Core/Run/Save/ + sts2rec/sts2rec/run_save.py", "owner": "R7", "depends_on": ["R1", "R2"], "est_loc": 1400, "doc": "§7"},
    {"id": "run_oracles", "status": "missing", "game_sources": [".run history (schema 10)", "current_run.save copies", "recorder map/reward/shop snapshots"], "target_dir": "sim/Sts2Sim.Diff/Run/ + sts2rec", "owner": "R8", "depends_on": ["R6", "R7"], "est_loc": 1200, "doc": "§7.5"},
]

DECISIONS = [
    {"id": "map_travel", "phase": "Map", "recorder_state_type": "map", "option_source": ["Nodes.Screens.Map/NMapScreen.cs:1388-1425", "Map/MapTravel.cs:9-16"], "legality": "start point when no visited coord; second boss after first boss; boss from the last row; else children or (ShouldAllowFreeTravel) the next row", "rng": [], "mutation": "RunState.AddVisitedMapCoord then EnterMapPointInternal (Runs/RunManager.cs:4124-4159, :569-720)"},
    {"id": "unknown_point_resolution", "phase": "automatic", "option_source": ["Odds/UnknownMapPointOdds.cs:99-177", "Runs/RunManager.cs:3978-3986", "Runs/RunManager.cs:4207-4227"], "rng": ["UnknownMapPoint.NextFloat x1"], "mutation": "odds reset/increase; room type"},
    {"id": "combat", "phase": "Combat", "recorder_state_type": "monster|elite|boss|hand_select", "option_source": ["existing CombatSim", "Rooms/CombatRoom.cs:355-435", "Combat/CombatManager.cs:1648-1810"], "rng": ["encounter private", "Niche", "Shuffle", "MonsterAi", "Combat*"], "mutation": "HP, gold, potions, relic counters, deck via DeckVersion; victory -> rewards"},
    {"id": "rewards_screen", "phase": "Rewards", "recorder_state_type": "rewards", "option_source": ["Rewards/RewardsSet.cs:416-515", "Rewards/RewardsSet.cs:68-121", "Rooms/CombatRoom.cs:176-299"], "legality": "any unclaimed reward; potion fails when slots are full; proceed per Nodes.Screens/NRewardsScreen.cs:668-718", "rng": ["Rewards: potion odds NextFloat at construction", "Rewards: gold NextInt, potion NextFloat+NextItem, cards 3x(NextFloat, NextItem, NextFloat), elite relic NextFloat at Populate in list order"], "mutation": "PlayerCmd.GainGold, PotionCmd.TryToProcure, RelicCmd.Obtain, open card reward"},
    {"id": "card_reward", "phase": "CardReward", "recorder_state_type": "card_reward", "option_source": ["Rewards/CardReward.cs:549-739", "Entities.CardRewardAlternatives/CardRewardAlternative.cs:67-89"], "legality": "pick card i; Skip (reward stays); REROLL if CanReroll; hook alternatives (PaelsWing SACRIFICE); at most 2 alternatives", "rng": ["Rewards on reroll"], "mutation": "CardPileCmd.Add(card, Deck)"},
    {"id": "treasure", "phase": "Treasure", "recorder_state_type": "treasure", "option_source": ["Rooms/TreasureRoom.cs:236-263", "Multiplayer.Game/TreasureRoomRelicSynchronizer.cs:137-290", "Nodes.Rooms/NTreasureRoom.cs:172-330", "Nodes.Rooms/NTreasureRoom.cs:551-565"], "legality": "open chest (forced), then take relic 0 or skip", "rng": ["TreasureRoomRelics.NextFloat at room entry", "Rewards.NextInt(42,53) at chest open"], "mutation": "gold, RelicCmd.Obtain (Nodes.Screens.TreasureRoomRelic/NTreasureRoomRelicCollection.cs:505)"},
    {"id": "shop", "phase": "Shop", "recorder_state_type": "shop", "option_source": ["Entities.Merchant/MerchantInventory.cs:87-177", "Entities.Merchant/MerchantEntry.cs:165-339"], "legality": "stocked and Cost <= Gold; potions need a free slot and ShouldProcurePotion; removal once per visit unless ShouldAllowMerchantCardRemoval is false; leave any time", "rng": ["Shops.NextInt(5) sale slot", "per character card: Rewards.NextFloat rarity, Shops.NextItem, Rewards.NextFloat upgrade, Shops.NextFloat price (+1 for the sale slot)", "colorless x2: Shops.NextItem, Rewards.NextFloat, Shops.NextFloat", "relics: Rewards.NextFloat x2 then Shops.NextFloat per entry", "potions: (Shops.NextFloat + Shops.NextItem) x3 then Shops.NextFloat x3"], "mutation": "gold, deck, relics, potions, CardShopRemovalsUsed; restock via ShouldRefillMerchantEntry"},
    {"id": "rest_site", "phase": "RestSite", "recorder_state_type": "rest_site", "option_source": ["Entities.RestSite/RestSiteOption.cs:47-68", "Multiplayer.Game/RestSiteSynchronizer.cs:531-661"], "legality": "enabled options (Smith needs an upgradable card, Cook >=2 removable); leave after a successful option or when none remain (Nodes.Rooms/NRestSiteRoom.cs:188-190, :1056-1062)", "options": ["HEAL", "SMITH", "MEND (MP only)", "DIG (Shovel)", "LIFT (Girya, TimesLifted<3)", "COOK (MeatCleaver)", "KINDLE (PumpkinCandle)", "CLONE (PaelsGrowth)", "HATCH (ByrdonisEgg card)"], "rng": ["DIG: Rewards.NextFloat relic rarity"], "mutation": "heal MaxHp*0.3m (+hooks, nested rewards), upgrade prompt, relic, counters"},
    {"id": "event", "phase": "Event", "recorder_state_type": "event|fake_merchant", "option_source": ["Models/EventModel.cs:68-144", "Models/EventModel.cs:790-810", "Events/EventOption.cs:66", "Events/EventOption.cs:279"], "legality": "!IsLocked (OnChosen != null) and not (DisableOnChosen and WasChosen); LeaveEvent when finished", "rng": ["event private Rng = Seed + slot + XXH64(event id)", "PlayerRng.Rewards in some events"], "mutation": "content-defined; prompts, rewards, combats (EnterCombatWithoutExitingEvent)"},
    {"id": "neow", "phase": "Event", "recorder_state_type": "event", "option_source": ["Models.Events/Neow.cs:295-388", "Models/AncientEventModel.cs:76-105"], "rng": ["event private: NextItem curse, NextBool x2-3, UnstableShuffle positives"], "mutation": "arrival heal; RelicCmd.Obtain(chosen relic)"},
    {"id": "act_start_ancient", "phase": "Event", "recorder_state_type": "event", "option_source": ["Models/ActModel.cs:322", "Models.Events/{Orobas,Pael,Tezcatara,Nonupeipe,Tanx,Vakuu,Darv}.cs GenerateInitialOptions"], "note": "replaces a boss relic reward; boss rewards have no relic", "rng": ["event private only"], "mutation": "arrival heal x0.8 at A2; RelicCmd.Obtain"},
    {"id": "act_transition", "phase": "automatic", "option_source": ["Nodes.Screens/NRewardsScreen.cs:678-692", "Multiplayer.Game/ActChangeSynchronizer.cs:77-91", "Runs/RunManager.cs:821-872"], "rng": ["act_{n}_map ad-hoc stream"], "mutation": "ActFloor++, exit boss room, SetActInternal, GenerateMap, MapRoom; last act -> TheArchitect"},
    {"id": "potion_use_discard", "phase": "any non-combat phase", "option_source": ["Nodes.Potions/NPotionPopup.cs:285-345", "Commands/PotionCmd.cs:283"], "legality": "AnyTime potions usable out of combat; discard always unless CanUseOrRemovePotions is false", "rng": ["potion-defined"], "mutation": "slot emptied; potion effect"},
    {"id": "deck_prompts", "phase": "Prompt", "recorder_state_type": "card_select|bundle_select|relic_select|crystal_sphere", "option_source": ["Commands/CardSelectCmd.cs:3169-3830", "CardSelection/CardSelectorPrefs.cs:8-105"], "legality": "select candidates in increasing index order, confirm with min<=count<=max, cancel if cancelable; auto-resolve when candidates <= MinSelect and not RequireManualConfirmation (Commands/CardSelectCmd.cs:3451, :3495, :3633)", "rng": [], "mutation": "caller-defined (remove/upgrade/transform/enchant/add)"},
    {"id": "victory_defeat", "phase": "terminal", "option_source": ["Models.Events/TheArchitect.cs:1060-1063", "Runs/RunManager.cs:4684-4779", "Runs/RunState.cs:446-456"], "rng": [], "mutation": "OnEnded; score Runs/ScoreUtility.cs:18-35"},
]

RNG_STREAMS = [
    {"stream": "UpFront", "kind": "run", "consumers": ["Runs/RunManager.cs:3828 shared bag populate", "Runs/RunManager.cs:3835 player bag populate", "Runs/RunManager.cs:4057-4068 shared ancients", "Models/ActModel.cs:241-323 per-act rooms", "Runs/RunManager.cs:4084 A10 second boss", "Runs/RunManager.cs:3879 ValidateRoomsAfterLoad"]},
    {"stream": "UnknownMapPoint", "kind": "run", "consumers": ["Odds/UnknownMapPointOdds.cs:124"]},
    {"stream": "TreasureRoomRelics", "kind": "run", "consumers": ["Multiplayer.Game/TreasureRoomRelicSynchronizer.cs:160 rarity", ":183 bot votes", ":339 fight moves", ":354 consolation shuffle"]},
    {"stream": "Niche", "kind": "run", "consumers": ["monster HP (combat)", "Kaleidoscope pool shuffles (P0 recipe)", "Models.Relics/Astrolabe.cs:102"]},
    {"stream": "Rewards", "kind": "player", "consumers": ["Rewards/GoldReward.cs:145-149", "Odds/PotionRewardOdds.cs:36", "Rewards/PotionReward.cs:153-160", "Factories/CardFactory.cs:168-198 and :244-261", "Factories/RelicFactory.cs:49-63", "Odds/CardRarityOdds.cs:74 (merchant rarity)", "Multiplayer.Game/OneOffSynchronizer.cs:658 treasure gold", "Models.Events/{PotionCourier,TheLegendsWereTrue,BattlewornDummy,EndlessConveyor,Wellspring}.cs", "Models.Relics/{DustyTome,PaelsTooth,NeowsBones,ScrollBoxes}.cs"]},
    {"stream": "Shops", "kind": "player", "consumers": ["Entities.Merchant/MerchantInventory.cs:118, :163", "Entities.Merchant/MerchantCardEntry.cs:260", "Entities.Merchant/MerchantRelicEntry.cs:160", "Entities.Merchant/MerchantPotionEntry.cs:173, :198", "Factories/CardFactory.cs:53, :66"]},
    {"stream": "Transformations", "kind": "player", "consumers": ["Models.Relics/LeafyPoultice.cs:92", "Models.Relics/Claws.cs:88"]},
    {"stream": "act_selection", "kind": "ad-hoc", "seed": "XXH64(seedText) + XXH64(\"act_selection\")", "consumers": ["Multiplayer.Game.Lobby/StartRunLobby.cs:705-715"]},
    {"stream": "act_{n}_map", "kind": "ad-hoc", "seed": "RunRngSet.Seed + XXH64(\"act_{n}_map\")", "consumers": ["Map/StandardActMap.cs:99-102"]},
    {"stream": "spoils_map", "kind": "ad-hoc", "seed": "RunRngSet.Seed + XXH64(\"spoils_map\")", "consumers": ["Map/SpoilsActMap.cs:64"]},
    {"stream": "event private", "kind": "ad-hoc", "seed": "Seed + (IsShared ? 0 : slot) + XXH64(event id)", "consumers": ["Models/EventModel.cs:91"]},
    {"stream": "encounter/monster private", "kind": "ad-hoc", "seed": "existing (sim/specs/rng-encounter-private.md)", "consumers": ["Models/EncounterModel.cs:211-215", "Combat/CombatState.cs:721"]},
]

ASCENSION = [
    {"level": 1, "name": "SwarmingElites", "effect": "8 elites per map instead of 5", "citations": ["Map/MapPointTypeCounts.cs:29"]},
    {"level": 2, "name": "WearyTraveler", "effect": "ancient arrival heal x0.8", "citations": ["Models/AncientEventModel.cs:84-87"]},
    {"level": 3, "name": "Poverty", "effect": "encounter and treasure gold x0.75", "citations": ["Models/EncounterModel.cs:56-58", "Models/EncounterModel.cs:75-77", "Multiplayer.Game/OneOffSynchronizer.cs:659-662", "Helpers/AscensionHelper.cs:12"]},
    {"level": 4, "name": "TightBelt", "effect": "-1 potion slot at run start", "citations": ["Entities.Ascension/AscensionManager.cs:29-32"]},
    {"level": 5, "name": "AscendersBane", "effect": "Ascender's Bane in the starting deck", "citations": ["Entities.Ascension/AscensionManager.cs:33-38"]},
    {"level": 6, "name": "Inflation", "effect": "card removal 100 (+50/use) instead of 75 (+25)", "citations": ["Entities.Merchant/MerchantCardRemovalEntry.cs:319-321"]},
    {"level": 7, "name": "Scarcity", "effect": "rare base odds halved, offset growth 0.005, reward upgrade scaling 0.125", "citations": ["Odds/CardRarityOdds.cs:13-41", "Factories/CardFactory.cs:23"]},
    {"level": 8, "name": "ToughEnemies", "effect": "monster HP (per monster file)", "citations": ["Models.Monsters/*.cs (101 files)"]},
    {"level": 9, "name": "DeadlyEnemies", "effect": "monster damage (per monster file)", "citations": ["Models.Monsters/*.cs (96 files)"]},
    {"level": 10, "name": "DoubleBoss", "effect": "second boss after the final act's boss", "citations": ["Runs/RunManager.cs:4082-4086", "Map/StandardActMap.cs:84-87", "Rooms/RoomSet.cs:80-90"]},
]

SAVE_FIELDS = [
    {"json": "schema_version", "target": "gate (accept 20)", "needed_for": "format"},
    {"json": "rng.{seed,rngs.<stream>.{counter,s0,s1,s2,s3}}", "target": "RunRngSet.FromSnapshot", "needed_for": "all run streams"},
    {"json": "ascension, game_mode, modifiers", "target": "RunState", "needed_for": "rules; reject non-standard"},
    {"json": "current_act_index, visited_map_coords, events_seen", "target": "RunState", "needed_for": "travel, event pulls"},
    {"json": "odds.unknown_map_point_{monster,elite,treasure,shop}_odds_value", "target": "UnknownMapPointOdds (float32)", "needed_for": "unknown rolls"},
    {"json": "shared_relic_grab_bag.relic_id_lists.<rarity>", "target": "shared bag, RefreshAllowed=false", "needed_for": "treasure"},
    {"json": "acts[i].{id, rooms.*}", "target": "ActInstance/RoomSet", "needed_for": "encounter/event/boss/ancient pulls"},
    {"json": "acts[current].saved_map", "target": "SavedActMap", "needed_for": "travel"},
    {"json": "map_point_history", "target": "history", "needed_for": "TotalFloor, shop blacklist, score"},
    {"json": "extra_fields.{started_with_neow,test_subject_kills,freed_repy}", "target": "RunState.ExtraFields", "needed_for": "act start, content"},
    {"json": "pre_finished_room.*", "target": "resume phase B/C", "needed_for": "resume"},
    {"json": "players[0].{character_id,current_hp,max_hp,max_energy,max_potion_slot_count,base_orb_slot_count,gold,net_id}", "target": "RunPlayer", "needed_for": "-"},
    {"json": "players[0].deck[]{id,current_upgrade_level,enchantment,props,floor_added_to_deck}", "target": "deck CardInstances", "needed_for": "combat, prompts"},
    {"json": "players[0].relics[]{id,props,floor_added_to_deck}", "target": "RelicInstances", "needed_for": "relic counters"},
    {"json": "players[0].potions[]{id,slot_index}", "target": "PotionSlots", "needed_for": "-"},
    {"json": "players[0].rng.{seed,rngs.{rewards,shops,transformations}}", "target": "PlayerRngSet", "needed_for": "rewards, shops"},
    {"json": "players[0].odds.{card_rarity_odds_value,potion_reward_odds_value}", "target": "PlayerOdds (float32)", "needed_for": "card/potion rolls"},
    {"json": "players[0].relic_grab_bag.relic_id_lists", "target": "player bag", "needed_for": "elite/shop/dig relics"},
    {"json": "players[0].extra_fields.{card_shop_removals_used,wongo_points}", "target": "RunPlayer.ExtraFields", "needed_for": "removal price, Wongo"},
    {"json": "players[0].unlock_state.{unlocked_epochs,encounters_seen,number_of_runs}", "target": "UnlockProfile", "needed_for": "pools, events, ancients, boss discovery"},
]

SAVE_POINTS = [
    {"id": "A", "caller": "Runs/RunManager.cs:618", "state": "map point entered; previous rooms exited; room type not rolled; history entry not appended", "resume_phase": "EnteringMapPoint"},
    {"id": "B", "caller": "Combat/CombatManager.cs:1748", "state": "combat won; AfterCombatEnd/AfterCombatVictory done; rewards not generated; pre_finished_room = combat room", "resume_phase": "PostCombatRewards"},
    {"id": "C", "caller": "Rooms/EventRoom.cs:417", "state": "ancient event finished; pre_finished_room = event room", "resume_phase": "AncientFinished"},
]

UNLOCK_DEPS = [
    {"rule": "character card pools", "depends_on": "<Char>2/5/7Epoch", "citation": "Models.CardPools/IroncladCardPool.cs:122-138"},
    {"rule": "character relic pools", "depends_on": "<Char>3/6Epoch", "citation": "Models.RelicPools/IroncladRelicPool.cs:36-43"},
    {"rule": "character potion pools", "depends_on": "<Char>4Epoch", "citation": "Models.PotionPools/IroncladPotionPool.cs:23"},
    {"rule": "colorless pool", "depends_on": "Colorless1-5Epoch", "citation": "Models.CardPools/ColorlessCardPool.cs:100-119"},
    {"rule": "shared relic pool", "depends_on": "Relic1-5Epoch", "citation": "Models.RelicPools/SharedRelicPool.cs:142-161"},
    {"rule": "shared potion pool", "depends_on": "Potion1-2Epoch", "citation": "Models.PotionPools/SharedPotionPool.cs:73-92"},
    {"rule": "event pool", "depends_on": "Event1/2/3Epoch (TrashHeap, Reflections, ColorfulPhilosophers)", "citation": "Models/ActModel.cs:245-256"},
    {"rule": "act 1 = Underdocks possible", "depends_on": "UnderdocksEpoch", "citation": "Models.Acts/Underdocks.cs:107-110"},
    {"rule": "ancients", "depends_on": "NeowEpoch (and StartedWithNeow), OrobasEpoch, DarvEpoch", "citation": "Models.Acts/Overgrowth.cs:100-108; Models.Acts/Hive.cs:98-106; Unlocks/UnlockState.cs:56-67"},
    {"rule": "characters", "depends_on": "Silent1/Regent1/Necrobinder1/Defect1Epoch", "citation": "Unlocks/UnlockState.cs:31-54"},
    {"rule": "boss identity (Standard mode)", "depends_on": "encounters seen", "citation": "Models/ActModel.cs:338-358; Runs/RunManager.cs:4078-4081"},
    {"rule": "tutorial paths", "depends_on": "NumberOfRuns == 0, EpochUnlockCount() == 0", "citation": "Odds/UnknownMapPointOdds.cs:109-120; Rewards/RewardsSet.cs:527-601; Multiplayer.Game/TreasureRoomRelicSynchronizer.cs:409-419; Models.Acts/Overgrowth.cs:110-127"},
    {"rule": "act forcing (profile)", "depends_on": "Progress.DiscoveredActs", "citation": "Models/ActModel.cs:487"},
    {"rule": "floor-1 tutorial event (profile)", "depends_on": "Progress.NumberOfRuns", "citation": "Runs/RunManager.cs:4229-4255"},
    {"rule": "WelcomeToWongos badge relic (profile)", "depends_on": "Progress.WongoPoints", "citation": "Models.Events/WelcomeToWongos.cs:634-646"},
]

DECOMPILE_LOSSES = [
    {"site": "Map/GoldenPathActMap.cs:42", "what": "16 MapPointTypes of the golden path", "fallback": "[v107] Map/GoldenPathActMap.cs:9-27"},
    {"site": "Entities.Merchant/MerchantInventory.cs:199", "what": "5 merchant colored-card types", "fallback": "[v107] Entities/Merchant/MerchantInventory.cs:15-22 (Attack, Attack, Skill, Skill, Power)"},
    {"site": "Models.Cards/AscendersBane.cs:21", "what": "static array", "fallback": "[v107] same file"},
    {"site": "Models.Cards/Folly.cs:19", "what": "static array", "fallback": "[v107] same file"},
    {"site": "Models.Monsters/KnowledgeDemon.cs:773", "what": "static array", "fallback": "[v107] same file"},
    {"site": "Models.Events/ColossalFlower.cs:491, :494", "what": "static arrays", "fallback": "[v107] same file"},
    {"site": "Models.Events/TinkerTime.cs:189, :196, :203", "what": "static arrays", "fallback": "[v107] same file"},
    {"site": "Entities.Cards/CardKeywordOrder.cs:15", "what": "keyword order array", "fallback": "[v107] same file"},
    {"site": "Models.Relics/GlassEye.cs:58", "what": "static array", "fallback": "[v107] same file"},
]

NODE_LAYER_RULES = [
    {"site": "Nodes.Screens.Map/NMapScreen.cs:1388-1425", "rule": "travelable map points"},
    {"site": "Nodes.Screens/NRewardsScreen.cs:668-718", "rule": "rewards proceed, act change, skip"},
    {"site": "Nodes.Rooms/NTreasureRoom.cs:172-330, :551-565, :640-646", "rule": "chest open order, skip, proceed gating"},
    {"site": "Nodes.Screens.TreasureRoomRelic/NTreasureRoomRelicCollection.cs:505", "rule": "treasure relic RelicCmd.Obtain"},
    {"site": "Nodes.Rooms/NRestSiteRoom.cs:188-190, :1056-1062", "rule": "rest site leave condition"},
    {"site": "Nodes.Potions/NPotionPopup.cs:285-345", "rule": "potion use/discard availability"},
    {"site": "Nodes.Combat/NCombatUi.cs:343", "rule": "rewards offered after victory"},
    {"site": "Nodes.Screens.Shops/NMerchantCardRemoval.cs:153-156, :210-214", "rule": "removal gating and one removal per visit"},
    {"site": "Nodes/NGame.cs:1988-1991, :2091-2148", "rule": "new-run order"},
    {"site": "Nodes.Screens.CharacterSelect/NCharacterSelectScreen.cs:1436-1439", "rule": "act-1 choice"},
]

MP_STUBS = [
    {"site": "Entities.RestSite/RestSiteOption.cs:62-65", "what": "Mend rest option"},
    {"site": "Entities.TreasureRelicPicking/RelicPickingResult.cs:24-102; Multiplayer.Game/TreasureRoomRelicSynchronizer.cs:172-192, :292-377", "what": "relic voting and fights"},
    {"site": "Multiplayer.Game/EventSynchronizer.cs:244-285", "what": "shared-event voting rng"},
    {"site": "Runs/RelicGrabBag.cs:20, :225-260", "what": "multiplayer relic fallback list"},
    {"site": "Models.Singleton/MultiplayerScalingModel.cs:41-57", "what": "enemy block scaling (x1 in SP)"},
    {"site": "Models/ActModel.cs:185-193; Models/RelicModel.cs:541", "what": "room count -1, act-3 treasure threshold 38"},
    {"site": "Multiplayer.Game/*Synchronizer.cs", "what": "network synchronisation (choices, checksums, map votes, rewards sets)"},
]

TESTMODE_BRANCHES = [
    "Entities.Merchant/MerchantPotionEntry.cs:196-199 (potion price draw skipped in TestMode)",
    "Runs/RunManager.cs:4096-4099 (tutorial modifications off in TestMode)",
    "Runs/RunManager.cs:4793-4796 (history stats no-op)",
    "Runs/RunManager.cs:4232 (tutorial room type)",
    "Models/ActModel.cs:487 (act forcing)",
    "Rooms/MapRoom.cs:26-29; Rooms/MerchantRoom.cs:202-205",
    "GameActions/MoveToMapCoordAction.cs:64-75",
]

KERNEL_PREREQS = [
    {"id": "K1", "contract": "resumable PendingPrompt API shared with combat"},
    {"id": "K2", "contract": "46 run-scope + 12 both-scope dispatchers and AbstractModel virtuals; run listener iterator in game order (Runs/RunState.cs:830-933)"},
    {"id": "K3", "contract": "RelicInstance/PotionInstance runtime as listeners with SavedProperty state, owned by RunPlayer"},
    {"id": "K4", "contract": "run-scope Cmd facades (PlayerCmd gold, CreatureCmd heal/max HP, CardPileCmd deck add/remove, CardCmd upgrade/transform/enchant, RelicCmd, PotionCmd, RewardsCmd.OfferCustom)"},
    {"id": "K5", "contract": "deck CardInstance (upgrade level, enchantment, props, FloorAddedToDeck) + DeckVersion link (Entities.Players/Player.cs:1214-1233)"},
    {"id": "K6", "contract": "CombatState attached to RunPlayer and the live RunRngSet (no clone); the existing public CombatLoop.SetUpCombat/StartCombat split must accept the live set so AfterRoomEntered runs between them"},
]

OWNERSHIP = [
    {"agent": "R1 run-state", "owns": ["sim/Sts2Sim.Core/Run/State/", "sim/Sts2Sim.Tests/RunStateTests.cs", "sim/specs/run-state.md"], "needs": ["K3", "K5"], "est_loc": 1500, "order": 2},
    {"agent": "R2 map", "owns": ["sim/Sts2Sim.Core/Run/Map/", "sim/Sts2Sim.Tests/MapTests.cs", "sim/specs/run-map.md"], "needs": [], "est_loc": 1800, "order": 2},
    {"agent": "R3 odds & rewards", "owns": ["sim/Sts2Sim.Core/Run/Odds/", "sim/Sts2Sim.Core/Run/Rewards/", "sim/Sts2Sim.Core/Run/Factories/", "sim/Sts2Sim.Core/Run/Relics/RelicGrabBag.cs", "sim/Sts2Sim.Core/Run/Util/GrabBag.cs", "sim/Sts2Sim.Tests/RunRewardsTests.cs", "sim/specs/run-rewards.md"], "needs": ["K2", "K3", "K5"], "est_loc": 2200, "order": 3},
    {"agent": "R4 rooms", "owns": ["sim/Sts2Sim.Core/Run/Rooms/", "sim/Sts2Sim.Tests/RunRoomsTests.cs", "sim/specs/run-rooms.md"], "needs": ["R3", "K1", "K2", "K3", "K4", "K5", "K6"], "est_loc": 2300, "order": 4},
    {"agent": "R5 events", "owns": ["sim/Sts2Sim.Core/Run/Events/", "sim/Sts2Sim.Tests/RunEventsTests.cs", "sim/specs/run-events.md"], "needs": ["K1", "K3", "R3"], "est_loc": 2000, "order": 3},
    {"agent": "R6 run loop", "owns": ["sim/Sts2Sim.Core/Run/Loop/", "sim/Sts2Sim.Tests/RunLoopTests.cs", "sim/specs/run-loop.md"], "scaffold_frozen_first": ["sim/Sts2Sim.Core/Api/RunAction.cs"], "needs": ["R1", "R2", "R3", "R4", "R5"], "est_loc": 2200, "order": 5},
    {"agent": "R7 save import", "owns": ["sim/Sts2Sim.Core/Run/Save/", "sim/Sts2Sim.Tests/RunSaveImportTests.cs", "sim/specs/run-save-import.md", "sts2rec/sts2rec/run_save.py", "sts2rec/tests/test_run_save.py"], "needs": ["R1", "R2"], "est_loc": 1400, "order": 3},
    {"agent": "R8 run oracles", "owns": ["sim/Sts2Sim.Diff/Run/", "sts2rec export-run-oracles (new module)"], "needs": ["R6", "R7"], "est_loc": 1200, "order": 6},
]

OPEN_QUESTIONS = [
    "v0.111.0 values of the static arrays lost by the decompiler (GoldenPathActMap types, merchant colored-card types, 7 more sites); re-read from IL.",
    "Godot Mathf.RoundToInt / Mathf.Round midpoint behaviour for merchant card and potion prices (assumed ToEven).",
    "Can the player close the map after a terminal rewards proceed and still claim unclaimed rewards? Affects the legal set; verify in game.",
    "Confirm no path other than the UI node re-enables the shop removal entry within a visit (e.g. TheCourier restock).",
    "Confirm the recorder mod registers no ModRunHookSubscriber (passive Harmony only).",
    "EncounterTag sets per encounter (AddWithoutRepeatingTags) need extraction into encounters.json.",
    "Whether real saves ever carry a null unlock_state (UnlockState.FromSerializable(null) returns all).",
    "Cross-platform determinism of NextGaussianInt (Math.Log/Sin) used for map counts.",
    "Model or pin WelcomeToWongos' profile-dependent badge relic (Progress.WongoPoints).",
    "Correct the P1 plan's 'A1-A20' to A0-A10.",
]

doc = {
    "schema": "sts2sim.inventory/v1",
    "kind": "run-layer-gaps",
    "game_version": "0.111.0",
    "decompile_root": DECOMP,
    "design_doc": "docs/sim-port/design-run-layer.md",
    "generated_by": common.generator("run-layer", "build_gaps.py"),
    "target": {"players": 1, "game_mode": "Standard", "modifiers": [], "test_mode": "off", "ascension_range": [0, 10]},
    "content_counts": content_counts,
    "summary": {
        "subsystems_total": len(SUBSYSTEMS),
        "subsystems_missing": sum(1 for s in SUBSYSTEMS if s["status"] == "missing"),
        "subsystems_partial": sum(1 for s in SUBSYSTEMS if s["status"] == "partial"),
        "subsystems_ported": sum(1 for s in SUBSYSTEMS if s["status"] == "ported"),
        "decision_points": len(DECISIONS),
        "run_scope_hooks_missing": len(run_missing),
        "both_scope_hooks_missing": len(both_missing),
        "est_loc_w2": sum(o["est_loc"] for o in OWNERSHIP),
        "save_points": len(SAVE_POINTS),
        "decompile_loss_sites": len(DECOMPILE_LOSSES),
        "node_layer_rules": len(NODE_LAYER_RULES),
    },
    "subsystems": SUBSYSTEMS,
    "decision_points": DECISIONS,
    "rng_streams": RNG_STREAMS,
    "hooks": {
        "source": "sim/specs/inventory/hooks.json",
        "listener_order": "Runs/RunState.cs:830-933: per active player deck cards (+enchantment); if no combat: relics (non-melted), potions; modifiers; badges; MultiplayerScalingModel; mod subscribers; combat listeners when attached",
        "run_scope_missing": [hook_entry(h) for h in run_missing],
        "both_scope_missing": [hook_entry(h) for h in both_missing],
    },
    "acts": acts,
    "shared_events": shared_events,
    "shared_ancients": ["Darv"],
    "ascension": ASCENSION,
    "save_import": {"schema_version": 20, "save_points": SAVE_POINTS, "fields": SAVE_FIELDS, "id_format": "CATEGORY.ENTRY (strip category for sim ids)"},
    "unlock_dependencies": UNLOCK_DEPS,
    "decompile_losses": DECOMPILE_LOSSES,
    "node_layer_rules": NODE_LAYER_RULES,
    "multiplayer_stubs": MP_STUBS,
    "testmode_branches": TESTMODE_BRANCHES,
    "excluded": ["Daily and Custom modes (Daily/*.cs)", "17 modifiers (Models.Modifiers/*.cs)", "multiplayer", "achievements, badges, timeline UI", "VFX/SFX/animation", "save migrations (schema < 20)"],
    "kernel_prerequisites": KERNEL_PREREQS,
    "ownership_w2": OWNERSHIP,
    "open_questions": OPEN_QUESTIONS,
}

out = common.stage1_path("run-layer-gaps.json")
with open(out, "w", encoding="utf-8") as f:
    json.dump(doc, f, indent=1, ensure_ascii=False)
    f.write("\n")
print(out, doc["summary"])
