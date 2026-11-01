# relics-3-1 (W3 wave A) — port index

Dropped everywhere: Flash(), hover tips, InvokeDisplayAmountChanged, AssertMutable, CardCmd.Preview*, Cmd.Wait, FlashSfx. AfterRoomEntered bodies are ported but the hook is not dispatched by Core yet (run:room_flow, W2).

- ART_OF_WAR — Models.Relics/ArtOfWar.cs:21 — none.
- BELLOWS — Models.Relics/Bellows.cs:12 — CardPreviewStyle dropped.
- BLACK_BLOOD — Models.Relics/BlackBlood.cs:14 — heal via rs.Combat (victory hooks run while combat state is attached).
- BLESSED_ANTLER — Models.Relics/BlessedAntler.cs:19 — DAZED by id via cs.CreateCard; PreviewCardPileAdd + Wait(3f) dropped.
- BONE_TEA — Models.Relics/BoneTea.cs:15 — SavedProperty CombatsLeft kept as public int (written in extra canonical).
- BOOKMARK — Models.Relics/Bookmark.cs:13 — CombatCardSelection stream.
- BOOK_REPAIR_KNIFE — Models.Relics/BookRepairKnife.cs:16 — none.
- BOOMING_CONCH — Models.Relics/BoomingConch.cs:19 — gap: RunState.CurrentRoom.RoomType missing; elite check throws "W3 gap" (reached only on the owner's turn 1).
- BRONZE_SCALES — Models.Relics/BronzeScales.cs:17 — AfterRoomEntered not dispatched yet.
- BURNING_BLOOD — Models.Relics/BurningBlood.cs:14 — as BLACK_BLOOD.
- BURNING_STICKS — Models.Relics/BurningSticks.cs:16 — CreateClone = cs.CloneCard + ExhaustOnNextPlay=false (_cloneOf back-link not modelled).
- CENTENNIAL_PUZZLE — Models.Relics/CentennialPuzzle.cs — W1 pilot, untouched.
- CHOSEN_CHEESE — Models.Relics/ChosenCheese.cs:14 — GainMaxHp via rs.Combat.
- DATA_DISK — Models.Relics/DataDisk.cs:17 — AfterRoomEntered not dispatched yet.
- EMBER_TEA — Models.Relics/EmberTea.cs:18 — applier null as in the game; AfterRoomEntered not dispatched yet.
- GHOST_SEED — Models.Relics/GhostSeed.cs:14 — AfterRoomEntered half not dispatched yet; AfterCardEnteredCombat half live.
- GORGET — Models.Relics/Gorget.cs:17 — PLATING_POWER by registry id; AfterRoomEntered not dispatched yet.
- INTIMIDATING_HELMET — Models.Relics/IntimidatingHelmet.cs:16 — none.
- IVORY_TILE — Models.Relics/IvoryTile.cs:16 — none.
- JEWELED_MASK — Models.Relics/JeweledMask.cs:17 — none.
- JOSS_PAPER — Models.Relics/JossPaper.cs:22 — DoActivateVisuals collapsed to set/reset (async Wait dropped).
- MEAT_ON_THE_BONE — Models.Relics/MeatOnTheBone.cs:16 — CombatManager.IsInProgress read as rs.Combat.IsInProgress.
- MUMMIFIED_HAND — Models.Relics/MummifiedHand.cs:13 — none.
