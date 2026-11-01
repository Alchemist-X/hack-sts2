# W3 events-12-1 (wave C) index

Decompile paths are relative to `MegaCrit.Sts2.Core.Models.Events/`. Dropped everywhere: VFX/SFX/ambience, waits, hover tips, `ThatDoesDamage`/`ThatWillKillPlayerIf`/`ThatDecreasesMaxHp` (UI tags), card previews, loc formatting (StringVars kept only when they hold state). DynamicVars are private fields/consts (EventModel has no DynamicVarSet). Out-of-combat Heal/GainMaxHp/LoseMaxHp/Damage/Kill go through `Run/Events/Defs/Events121Support.cs` (hooks with `cs = null`, same order as `Commands/CreatureCmd.cs`). Relic/card ids not yet registered are skipped with a Fidelity `ApproximatedRule` entry (`Events121Support.ObtainRelic/AddCardToDeck/AddCurseToDeck`).

- ABYSSAL_BATHS — AbyssalBaths.cs:16 — damage var mutated per immerse (+1)
- BUGSLAYER — Bugslayer.cs:17 — none
- BYRDONIS_NEST — ByrdonisNest.cs:19 — HasEventPet copied from Player.cs:675
- DOLL_ROOM — DollRoom.cs:26 — ambience dropped; option keys `DOLL_ROOM.pages.TAKE.options.<RELIC_ID>` (game uses relic title), doll id in StringArg; StableShuffle sorted by relic id ordinal
- HUNGRY_FOR_MUSHROOMS — HungryForMushrooms.cs:13 — BIG_MUSHROOM / FRAGRANT_MUSHROOM not yet ported (obtain skipped until they land)
- JUNGLE_MAZE_ADVENTURE — JungleMazeAdventure.cs:25 — fx StableShuffle kept for Rng consumption (shuffles 3 ints); IsShared
- LOST_WISP — LostWisp.cs:17 — none
- SLIPPERY_BRIDGE — SlipperyBridge.cs:25 — RandomCardToLose / SkippedRemovals stored as deck indices; rain VFX (OnRoomEnter) dropped
- SUNKEN_STATUE — SunkenStatue.cs:18 — SWORD_OF_STONE not yet ported
- SUNKEN_TREASURY — SunkenTreasury.cs:16 — gold monologue loc dropped
- TEA_MASTER — TeaMaster.cs:19 — none
- TRASH_HEAP — TrashHeap.cs:22 — DARKSTONE_PERIAPT / DREAM_CATCHER / MAW_BANK not yet ported
- INFESTED_AUTOMATON — InfestedAutomaton.cs:18 — none
- RANWID_THE_ELDER — RanwidTheElder.cs:19 — offered potion/relic stored as slot/relic index (game closure)
- REFLECTIONS — Reflections.cs:21 — mirror VFX dropped
- RELIC_TRADER — RelicTrader.cs:19 — owned relics as relic indices, new relics as ids (re-created on trade = ToMutable); hover-tip NewRelics access kept (grab-bag pull order)
- ROUND_TEA_PARTY — RoundTeaParty.cs:22 — none
- TABLET_OF_TRUTH — TabletOfTruth.cs:20 — none
- THIS_OR_THAT — ThisOrThat.cs:19 — none
- UNREST_SITE — UnrestSite.cs:21 — none
- WELCOME_TO_WONGOS — WelcomeToWongos.cs:22 — profile WongoPoints read from `RunConfig.Progress` (Veteran = 0 when no config); WONGOS_MYSTERY_TICKET not yet ported
- Skipped (exist under Run/Events/Ancients, R5): DARV, NEOW, NONUPEIPE, OROBAS, PAEL, TANX, TEZCATARA, THE_ARCHITECT, VAKUU
