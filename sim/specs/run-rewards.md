# R3 odds and rewards — spec index

Decompile paths are given without the `MegaCrit.Sts2.Core.` prefix. One line per member: `member — decompile file:line — dropped points / gaps`.

## Odds (`Run/Odds/Odds.cs`)

- CardRarityOdds.Roll / RollWithoutChangingFutureOdds / RollWithBaseOdds / GetBaseOdds — Odds/CardRarityOdds.cs:53-152 — float32 throughout; static `regularCommonOdds` trap ported as a per-ascension property (no roll reads it); `Log.Info` dropped.
- CardRarityOdds constants (offset −0.05, cap 0.4, growth 0.01 / A7 0.005, rare 0.03/0.0149, elite 0.1/0.05, shop 0.09/0.045) — Odds/CardRarityOdds.cs:13-41.
- PotionRewardOdds.Roll — Odds/PotionRewardOdds.cs:26-44 — `ShouldForcePotionReward` short-circuits with no draw; ±0.1, no clamp.
- UnknownMapPointOdds.Roll / ResetToBase / SetBaseOdds / EventOdds — Odds/UnknownMapPointOdds.cs:23-202 — both dictionaries kept as insertion-ordered lists (Monster, Elite, Treasure, Shop); first-run branch reads `rs.Unlocks.NumberOfRuns` and the flat `MapPointHistory`.
- RunOddsSet / PlayerOddsSet (+ FromSerializable, LoadFromSerializable) — Odds/RunOddsSet.cs:22-50, Odds/PlayerOddsSet.cs:31-59 — clones re-bind to the cloned rng (`CloneFor`).

## Grab bags (`Run/Util/GrabBag.cs`, `Run/Relics/*`)

- GrabBag<T> — Helpers/GrabBag.cs:8-90 — exact weighted pick on `NextDouble`, rejection loop for predicates.
- RelicGrabBag.Populate(player) — Runs/RelicGrabBag.cs:61-108 — shared + character unlocked pools, rarities {Common, Uncommon, Rare, Shop}, deques in first-appearance order, each `UnstableShuffle`d; `RunConfig.Content` filter applied before the shuffle (sim-only soak mode).
- RelicGrabBag.Populate(ids) — Runs/RelicGrabBag.cs:110-154 — no rarity filter (shared bag).
- RelicGrabBag.PullFromFront / PullFromBack / GetAvailableDeque / RefreshRarity — Runs/RelicGrabBag.cs:156-197, :262-287, :425-456 — quirk kept: `GetDeque` of a never-created rarity returns a detached empty list, so a refresh of that rarity does not feed the current pull.
- RelicGrabBag.Remove / MoveToFallback / Contains / HasAvailableRelics — Runs/RelicGrabBag.cs:28-50, :204-260, :458-481.
- RelicGrabBag.FromSerializable — Runs/RelicGrabBag.cs:358-413 — `refreshAllowed: false`, no original list, unknown ids dropped (so a loaded bag never refreshes).
- RelicCatalog rarity table / epoch gates — Models.Relics/*.cs `Rarity =>`, Models.RelicPools/SharedRelicPool.cs:139-161, Models.RelicPools/<Char>RelicPool.cs:33-43, Timeline.Epochs/Relic1-5Epoch, <Char>3/6Epoch — generated from the decompile (`RelicCatalog.Data.cs`).
- RelicCatalog.IsAllowed — Models/RelicModel.cs:529-543 + the 20 overrides — registered relics use their own `IsAllowed`; unported ones use the fallback table (16 × IsBeforeAct3TreasureChest, LastingCandy, MassiveScroll, SilverCrucible, WingedBoots).

## Factories (`Run/Factories/*`)

- RelicFactory.PullNextRelicFromFront/Back, RollRarity — Factories/RelicFactory.cs:13-63 — `TestRngInjector` overrides dropped; also removes from the shared bag; `*Id*` variants return ids so unported relics fail only when instantiated.
- CardCreationOptions (ForRoom, ForNonCombatWith*Odds, GetPossibleCards, With*, TryGetSingleRarityInPool) — Runs/CardCreationOptions.cs:70-169 — mutable, `With*` return `this` (Options/RerollOptions alias kept); `RunConfig.Content` filter added to `GetPossibleCards`.
- CardCreationFlags / CardCreationSource — Runs/CardCreationFlags.cs:6-19, Runs/CardCreationSource.cs:3-9.
- CardCreationResult — Entities.Cards/CardCreationResult.cs:6-34 — modifying relics stored as ids.
- CardFactory.CreateForReward — Factories/CardFactory.cs:71-91, :168-261 — per card: ModifyCardRewardCreationOptions, Except(offered), player-count filter, rarity roll (`Roll` iff ForceRarityOddsChange or Encounter + Regular/Elite/Boss), GetNextAllowedRarity, `NextItem`, upgrade float (`(decimal)NextFloat`, act × 0.25 / A7 0.125, `<=`); then TryModifyCardRewardOptions + AfterModifyingCardRewardOptions.
- CardFactory.CreateForMerchant(type / rarity) — Factories/CardFactory.cs:34-69 — pick on `Shops`, the upgrade float on `Rewards` (base −999999999 never upgrades).
- CardFactory.CreateRunCard — Runs/RunState.cs CreateCard — `_allCards` registration not ported.

## Rewards (`Run/Rewards/*`)

- Reward.OnSelectWrapper (SelectUnsynchronized) — Rewards/Reward.cs:241-258 — AfterRewardTaken then SuccessfullySelected; LinkedRewardSet parent handling dropped (no constructor call in v0.111.0).
- GoldReward — Rewards/GoldReward.cs:85-169 — Populate always draws `NextInt(min, max+1)`, even for a fixed amount.
- PotionReward — Rewards/PotionReward.cs:107-203 — stores the id at Populate, instantiates at claim; TooFull leaves it claimable, NotAllowed counts as taken.
- RelicReward — Rewards/RelicReward.cs:92-213 — stores the id at Populate; claim notifies open card rewards (CardReward.OnRelicObtained).
- SpecialCardReward — Rewards/SpecialCardReward.cs:93-183 — `PreviewCardPileAdd` dropped.
- CardRemovalReward — Rewards/CardRemovalReward.cs:84-112 — OnSelect is a stub (needs the run deck-removal prompt).
- CardReward — Rewards/CardReward.cs:528-782 — the OnSelect loop is split into OnSelectWrapper (opens, generates alternatives) / PickCard / ChooseAlternative; after a reroll the alternatives are the regenerated list (what the screen shows, :570) not the stale loop list (:600); the `RelicObtained` event is ported only for relic rewards claimed through `RelicReward` (other `RelicCmd.Obtain` sites do not notify — gap); history entries dropped.
- CardRewardAlternative.Generate — Entities.CardRewardAlternatives/CardRewardAlternative.cs:67-89 — OnSelect takes `(rs, reward)` so clones need no re-binding; hotkeys dropped.
- RewardsSet.WithRewardsFromRoom / GenerateRewardsFor / RollForPotionAndAddTo — Rewards/RewardsSet.cs:416-525 — encounter gold from Models/EncounterModel.cs:43-81 (+ FakeMerchantEventEncounter 300, BattlewornDummy V1-3 no rewards); `GoldProportion` is decimal in the sim, converted to float before the multiply; tutorial tables (:527-667) are a stub.
- RewardsSet.GenerateWithoutOffering — Rewards/RewardsSet.cs:45-131 — populate in list order, ModifyRewards, populate added, AfterModifyingRewards, the same `List.Sort` by RewardsSetIndex (all inside `!_isGenerated`).
- RewardsSet.Offer — Rewards/RewardsSet.cs:159-339 — dead player → no-op; shown iff rewards or combat room or EmptyForRoom; shown = pushed on `rs.RewardsStack`, `IsTerminal = Room is CombatRoom`.
- RewardsCmd.OfferForRoomEnd / OfferCustom / GenerateForRoomEnd / GenerateCustom — Commands/RewardsCmd.cs:294-339.
