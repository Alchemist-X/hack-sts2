# relics-9-1 (wave C) index

Shared: deck prompts go through `Relics/Defs/RelicDeckPromptsR91.cs` (run-side ports of `Commands/CardSelectCmd.cs` FromDeckForTransformation:3485, FromDeckForEnchantment:3533-3605, FromDeckGeneric:3614, FromChooseACardScreen:3169, FromSimpleGridForRewards:3236, FromChooseABundleScreen:3798; upgrade/removal delegate to `RoomPrompts`); `RunState.CloneCard` (Runs/RunState.cs:711) is `CloneForCombat(new id)` with `DeckVersionId` reset. Dropped everywhere: Flash, CardCmd.Preview / PreviewCardPileAdd, NCardEnchantVfx, Cmd.Wait, LogChoice, HoverTips, Selector/LocalSelector/TestMode branches, multiplayer choice sync, `CardChoiceHistoryEntry` logging.

- ASTROLABE — Models.Relics/Astrolabe.cs:17 — [SimExclude] gap: deck CardCmd.Transform + CreateRandomCardForTransform(isInCombat:false) not in kernel; prompt ported, throws W3 gap after it.
- BIIIG_HUG — Models.Relics/BiiigHug.cs:20 — AfterShuffle Soot to draw pile Random; Cmd.Wait(1f) dropped.
- CLAWS — Models.Relics/Claws.cs:18 — [SimExclude] gap: deck CardCmd.Transform; Maul creation (upgrade + enchant copy) ported, preview path dropped.
- DOLLYS_MIRROR — Models.Relics/DollysMirror.cs:15 — FromDeckGeneric filter not Quest; CloneCard → Add Deck.
- ELECTRIC_SHRYMP — Models.Relics/ElectricShrymp.cs:16 — Imbued(1).
- EMPTY_CAGE — Models.Relics/EmptyCage.cs:15 — remove 2.
- GNARLED_HAMMER — Models.Relics/GnarledHammer.cs:17 — Sharp(SharpAmount 3) on 0..3, RMC.
- HEFTY_TABLET — Models.Relics/HeftyTablet.cs:21 — 3 Rare uniform NoUpgradeRoll from character pool; chosen inserted before Injury; unpicked history dropped.
- KIFUDA — Models.Relics/Kifuda.cs:17 — Adroit(3) on 0..3, RMC.
- LEAD_PAPERWEIGHT — Models.Relics/LeadPaperweight.cs:19 — 2 Colorless RegularEncounter odds, skippable.
- NEW_LEAF — Models.Relics/NewLeaf.cs:17 — [SimExclude] gap: deck CardCmd.TransformToRandom.
- PAELS_GROWTH — Models.Relics/PaelsGrowth.cs:19 — Clone(4); adds CloneRestSiteOption (its OnSelect is still an R4/W3 stub); EnchantmentName StringVar holds the id, not the loc title.
- PAELS_TOOTH — Models.Relics/PaelsTooth.cs:21 — SerializableCard stored as detached CardInstance clones; restore = clone with new instance id (FromSerializable); RunState.AddCard/ContainsCard bookkeeping dropped; CardTitles var holds ids.
- POMANDER — Models.Relics/Pomander.cs:15 — upgrade 1.
- PRECARIOUS_SHEARS — Models.Relics/PrecariousShears.cs:18 — [SimExclude] gap: CreatureCmd.Damage outside combat (only the in-combat path is wired).
- PRECISE_SCISSORS — Models.Relics/PreciseScissors.cs:15 — remove 1.
- PRESERVED_FOG — Models.Relics/PreservedFog.cs:17 — remove 3 then AddCurseToDeck FOLLY.
- PUNCH_DAGGER — Models.Relics/PunchDagger.cs:17 — Momentum(5).
- ROYAL_STAMP — Models.Relics/RoyalStamp.cs:23 — UnstableShuffle(RunRng.Niche) of enchantable cards then FromDeckForEnchantment (reorders by deck index), RoyallyApproved(1).
- SCROLL_BOXES — Models.Relics/ScrollBoxes.cs:22 — PlayerRng.Rewards bundles; Defect 1% Claw×3 via NextInt(100); TestMode Deprived pool swap dropped; IsAllowedAtNeow(CanGenerateBundles).
- SEA_GLASS — Models.Relics/SeaGlass.cs:24 — 5/5/5 Common/Uncommon/Rare (Uniform, NoUpgradeRoll|NoRarityModification|NoCardPoolModifications) from CharacterId pool (null → IRONCLAD); Title override dropped; Character var holds the id.
- TRI_BOOMERANG — Models.Relics/TriBoomerang.cs:21 — Instinct(1) on 3.
- YUMMY_COOKIE — Models.Relics/YummyCookie.cs:17 — upgrade 4; IconBaseName dropped.
