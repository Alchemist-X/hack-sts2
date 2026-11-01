# relics-3-2 (W3 wave A) — index

Decompile root: `output/server-bootstrap/linux-decompiled/MegaCrit.Sts2.Core.Models.Relics/`. Flash/VFX/hover tips dropped everywhere. `AfterRoomEntered` bodies read `rs.Combat`; that hook is dispatched by W2.

- MUSIC_BOX — Models.Relics/MusicBox.cs:19 — `CardBeingPlayed` stored as card InstanceId (clone-safe), not a reference; `CreateClone` = `cs.CloneCard` + `ExhaustOnNextPlay = false` (CardModel.cs:3571-3583), non-combat-pile throw dropped.
- NINJA_SCROLL — Models.Relics/NinjaScroll.cs:17 — `Shiv.CreateInHand` (Models.Cards/Shiv.cs:355) inlined: count 0 → nothing, `IsOverOrEnding` gate, `CreateCard("SHIV")` ×3 → `AddGeneratedCardsToCombat(Hand, owner)`.
- ODDLY_SMOOTH_STONE — Models.Relics/OddlySmoothStone.cs:17 — none.
- PAELS_TEARS — Models.Relics/PaelsTears.cs:19 — none (flag not reset at turn start, as in the game).
- PANTOGRAPH — Models.Relics/Pantograph.cs:14 — STUB: both hooks need run layer (`Map.BossMapPoint`, `CurrentRoom.RoomType == Boss`).
- PAPER_KRANE — Models.Relics/PaperKrane.cs:10 — W1 pilot, not touched.
- PAPER_PHROG — Models.Relics/PaperPhrog.cs:10 — W1 pilot, not touched.
- PERMAFROST — Models.Relics/Permafrost.cs:18 — none.
- PHILOSOPHERS_STONE — Models.Relics/PhilosophersStone.cs:20 — none.
- POWER_CELL — Models.Relics/PowerCell.cs:19 — StableShuffle uses `CardPileCmd.CompareForShuffle` as the stable key (sim convention, as Whetstone).
- RADIANT_PEARL — Models.Relics/RadiantPearl.cs:19 — Luminesce by id `LUMINESCE`.
- RAZOR_TOOTH — Models.Relics/RazorTooth.cs:9 — none.
- RED_SKULL — Models.Relics/RedSkull.cs:19 — `AfterCurrentHpChanged` has no creature filter in the game; kept.
- RUINED_HELMET — Models.Relics/RuinedHelmet.cs:11 — none.
- SEAL_OF_GOLD — Models.Relics/SealOfGold.cs:17 — v0.111 gold cost 3.
- SLING_OF_COURAGE — Models.Relics/SlingOfCourage.cs:17 — STUB: `AfterRoomEntered` needs `AbstractRoom.RoomType` (W2).
- STONE_CRACKER — Models.Relics/StoneCracker.cs:18 — `CardCmd.Preview` and `CustomScaledWait` dropped.
- SWORD_OF_JADE — Models.Relics/SwordOfJade.cs:17 — applier null as in the game.
- TEA_OF_DISCOURTESY — Models.Relics/TeaOfDiscourtesy.cs:17 — `CombatsLeft` is the saved property (public for W2 save load); `InvokeDisplayAmountChanged` dropped.
- THROWING_AXE — Models.Relics/ThrowingAxe.cs:8 — none.
- UNSETTLING_LAMP — Models.Relics/UnsettlingLamp.cs:16 — `TriggeringCard` stored as InstanceId; `DoubledPowers` stored as each power's `ITemporaryPower.InternallyAppliedPowerId` (null otherwise), which is all `HasDoubledTemporaryPowerSource` reads; `PowerModel.IsVisible` added to kernel without the `LocalContext.IsMe` multiplayer branch.
- VAJRA — Models.Relics/Vajra.cs:17 — none.
