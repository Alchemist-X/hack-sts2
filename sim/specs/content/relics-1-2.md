# W3 relics-1-2 (wave A) — port index

Decompile paths are `Models.Relics/<Class>.cs:<class line>`. Common drops for every entry: `Flash()`, SFX, VFX
(`VfxCmd.PlayOnCreatureCenter`), `Cmd.Wait`, `InvokeDisplayAmountChanged`, `ExtraHoverTips`, `AssertMutable`,
`choiceContext`. `DoActivateVisuals` (async `IsActivating = true; Wait(1f); = false`) runs synchronously, so
`IsActivating` is true only inside the call. `ShowCounter => CombatManager.Instance.IsInProgress` overrides are
UI-only and dropped. Card references held across hooks (`PenNib.AttackToDouble`, `Vambrace.TriggeringCard`) are
stored as `InstanceId` (clone-safe; the game compares by reference). `participants.Contains(Owner.Creature)` uses
`CombatStatus.ContainsById`. `HittableEnemies` = `cs.GetHittableEnemies` (`Combat/CombatState.cs:622`).
`Owner.RunState.Rng.CombatTargets` = `cs.Rng.CombatTargets`.

- MINIATURE_CANNON — Models.Relics/MiniatureCannon.cs:10 — none
- MR_STRUGGLES — Models.Relics/MrStruggles.cs:17 — none
- NUNCHAKU — Models.Relics/Nunchaku.cs:19 — W1 pilot, already ported; not touched
- ORICHALCUM — Models.Relics/Orichalcum.cs:19 — none
- ORNAMENTAL_FAN — Models.Relics/OrnamentalFan.cs:22 — `UpdateDisplay` reads `cs.IsInProgress` passed in (AfterCombatEnd: `rs.Combat?.IsInProgress ?? false`)
- PAELS_BLOOD — Models.Relics/PaelsBlood.cs:8 — none
- PAELS_FLESH — Models.Relics/PaelsFlesh.cs:15 — BeforeCombatStart / BeforeSideTurnStart are display-only, dropped
- PARRYING_SHIELD — Models.Relics/ParryingShield.cs:18 — none
- PENDULUM — Models.Relics/Pendulum.cs:19 — none
- PEN_NIB — Models.Relics/PenNib.cs:17 — `cardSource.Pile` = `GetCombatPile(rs.Combat)`; `Owner.Osty` = `GetOsty(rs.Combat)` (null without combat, compared by reference like the game)
- POCKETWATCH — Models.Relics/Pocketwatch.cs:15 — AfterModifyingHandDraw is Flash-only, dropped
- POLLINOUS_CORE — Models.Relics/PollinousCore.cs:19 — none
- RAINBOW_RING — Models.Relics/RainbowRing.cs:21 — none
- RED_MASK — Models.Relics/RedMask.cs:19 — none
- REGALITE — Models.Relics/Regalite.cs:20 — `GainBlock(fast: true)` flag is visual, dropped
- RINGING_TRIANGLE — Models.Relics/RingingTriangle.cs:9 — none
- RING_OF_THE_DRAKE — Models.Relics/RingOfTheDrake.cs:8 — none
- RING_OF_THE_SNAKE — Models.Relics/RingOfTheSnake.cs:8 — none
- ROYAL_POISON — Models.Relics/RoyalPoison.cs:17 — none
- RUNIC_PYRAMID — Models.Relics/RunicPyramid.cs:6 — none
- SAI — Models.Relics/Sai.cs:17 — none
- SCREAMING_FLAGON — Models.Relics/ScreamingFlagon.cs:19 — none
- SHURIKEN — Models.Relics/Shuriken.cs:22 — none
- SPARKLING_ROUGE — Models.Relics/SparklingRouge.cs:17 — `ThrowingPlayerChoiceContext` dropped
- SPIKED_GAUNTLETS — Models.Relics/SpikedGauntlets.cs:10 — owner check via `CombatStatus.CardOwnerCreature`
- STONE_CALENDAR — Models.Relics/StoneCalendar.cs:20 — AfterRoomEntered ported (run layer dispatches it later); DisplayAmount dropped (needs combat + IsCanonical)
- STRIKE_DUMMY — Models.Relics/StrikeDummy.cs:11 — `Tags` = `HasTag` (CanonicalTags, CardModel.cs:1942)
- STURDY_CLAMP — Models.Relics/SturdyClamp.cs:17 — `BlockingPlayerChoiceContext` dropped; Flash after LoseBlock dropped
- THE_ABACUS — Models.Relics/TheAbacus.cs:17 — none
- THE_BOOT — Models.Relics/TheBoot.cs:10 — AfterModifyingHpLostAfterOsty is Flash-only, dropped
- TINGSHA — Models.Relics/Tingsha.cs:16 — none
- TOUGH_BANDAGES — Models.Relics/ToughBandages.cs:16 — none
- TUNGSTEN_ROD — Models.Relics/TungstenRod.cs:11 — AfterModifyingHpLostAfterOsty is Flash-only, dropped
- TUNING_FORK — Models.Relics/TuningFork.cs:19 — none
- UNCEASING_TOP — Models.Relics/UnceasingTop.cs:14 — IsValidPhase `(uint)(phase - 2) <= 2` = AutoPrePlay/Play/AutoPostPlay
- VAMBRACE — Models.Relics/Vambrace.cs:13 — none
- VELVET_CHOKER — Models.Relics/VelvetChoker.cs:16 — AfterRoomEntered ported; DisplayAmount IsCanonical branch dropped
- VERY_HOT_COCOA — Models.Relics/VeryHotCocoa.cs:17 — none
- VITRUVIAN_MINION — Models.Relics/VitruvianMinion.cs:11 — none
- WONGO_CUSTOMER_APPRECIATION_BADGE — Models.Relics/WongoCustomerAppreciationBadge.cs:5 — no behaviour (rarity only)
