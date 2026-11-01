# relics-8-1 (wave C) index

Common to all: Flash / AfterModifying* flash-only overrides, hover tips, TestMode branches and LocalContext.IsMe (single player: true) dropped. Standalone guard: pickup relics that offer rewards or pull relics skip that part when `Owner.PlayerRng == null` (no run state, standalone combats / soak). `IsBeforeAct3TreasureChest(rs)` is called through the protected `(playerCount, totalFloor)` overload because the `RunState` overload still throws. `RunState.CloneCard` is inlined as `card.CloneForCombat(rs.AllocateCardInstanceId())` + `DeckVersionId = NoDeckVersion` (mirrors `CombatState.CloneCard`).

- AMETHYST_AUBERGINE — Models.Relics/AmethystAubergine.cs:12 — AfterModifyingRewards (flash) dropped
- ARCANE_SCROLL — Models.Relics/ArcaneScroll.cs:17 — CardCmd.PreviewCardPileAdd dropped
- BLACK_STAR — Models.Relics/BlackStar.cs:9 — none
- BOWLER_HAT — Models.Relics/BowlerHat.cs:10 — AfterModifyingGoldGained (flash) dropped
- CALLING_BELL — Models.Relics/CallingBell.cs:17 — Cmd.Wait(0.75) and TestMode reward list dropped; curse added before the guard
- CAULDRON — Models.Relics/Cauldron.cs:16 — TestMode potion list dropped
- DINGY_RUG — Models.Relics/DingyRug.cs:11 — none
- DREAM_CATCHER — Models.Relics/DreamCatcher.cs:14 — Flash dropped; extra heal text is the loc key string `DREAM_CATCHER.additionalRestSiteHealText`
- DRIFTWOOD — Models.Relics/Driftwood.cs:12 — game class is non-sealed; ported sealed
- ECTOPLASM — Models.Relics/Ectoplasm.cs:10 — AfterModifyingGoldGained (flash) dropped
- FRESNEL_LENS — Models.Relics/FresnelLens.cs:14 — CanEnchant through a static canonical Nimble
- FROZEN_EGG — Models.Relics/FrozenEgg.cs:13 — EggRelicHelper.UpgradeValidCards (Helpers.Models/EggRelicHelper.cs) inlined as a private method; CardPreviewStyle dropped
- GIRYA — Models.Relics/Girya.cs:20 — TimesLifted in canonical extra; kernel edit LiftRestSiteOption.OnSelect (Entities.RestSite/LiftRestSiteOption.cs:114) increments it; AfterRoomEntered requires rs.Combat for combat rooms
- GLASS_EYE — Models.Relics/GlassEye.cs:15 — rarity array recovered from IL (field 18B1B068…, bytes 2,2,3,3,4 = Common, Common, Uncommon, Uncommon, Rare)
- GLITTER — Models.Relics/Glitter.cs:13 — none
- JUZU_BRACELET — Models.Relics/JuzuBracelet.cs:10 — none
- KALEIDOSCOPE — Models.Relics/Kaleidoscope.cs:21 — pools StableShuffle(Niche) re-run per reward, ordered by pool id ordinal (ModelId.CompareTo, same category)
- LARGE_CAPSULE — Models.Relics/LargeCapsule.cs:19 — PreviewCardPileAdd and TestMode Deprived branch dropped
- LASTING_CANDY — Models.Relics/LastingCandy.cs:23 — IsActivating / DoActivateVisuals dropped (DisplayAmount = CombatRewardsSeen % 2); CombatRewardsSeen in canonical extra
- LAVA_LAMP — Models.Relics/LavaLamp.cs:17 — TookDamageThisCombat in canonical extra
- LAVA_ROCK — Models.Relics/LavaRock.cs:11 — Flash dropped; HasTriggered in canonical extra
- LOST_COFFER — Models.Relics/LostCoffer.cs:14 — none
- MEAT_CLEAVER — Models.Relics/MeatCleaver.cs:9 — none
- MEMBERSHIP_CARD — Models.Relics/MembershipCard.cs:10 — LocalContext.IsMe dropped
- MINIATURE_TENT — Models.Relics/MiniatureTent.cs:6 — Flash dropped
