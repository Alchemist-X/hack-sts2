SUBSYSTEMS += [
 {
  "id": "unlocks_timeline",
  "name": "Unlock state and timeline epochs",
  "namespaces": ["Unlocks/UnlockState", "Unlocks/SerializableUnlockState", "Timeline", "Timeline.Epochs", "Saves/ProgressState"],
  "summary": "UnlockState = {revealed epoch ids, encounters seen, NumberOfRuns}, snapshotted per player at run creation from the profile (SaveManager.GenerateUnlockStateFromProgress) and stored in SerializablePlayer.UnlockState; RunState.UnlockState merges players (union of sets, max runs). UnlockState.all = every epoch, every encounter, NumberOfRuns 999999999; FromSerializable(null) = all. 57 epochs: 48 gate concrete content (3 cards/relics/potions each per character tier, 5 colorless card tiers, 2 potion tiers, 5 relic tiers, 3 event epochs, 4 character epochs), plus NeowEpoch, UnderdocksEpoch, DarvEpoch, OrobasEpoch (rule gates without lists) and Act2B/Act3B/CustomAndSeeds/DailyRun (no rule gate in v0.111.0). See unlock_profile_dependence for every rule path.",
  "rules_dependent": ["card/relic/potion pools", "event pool per act", "ancients per act and shared ancient Darv", "act list (Underdocks)", "character set (Orobas, Kaleidoscope, Splash, PrismaticGem, ColorfulPhilosophers)", "boss discovery order", "first-run tutorial behaviours", "Neow vs. monster start"],
  "game_refs": ["Unlocks/UnlockState.cs:16-139", "Unlocks/SerializableUnlockState.cs:10-73", "Saves.Runs/SerializablePlayer.cs:169", "Entities.Players/Player.cs:191", "Entities.Players/Player.cs:707-716", "Runs/RunState.cs:679", "Nodes/NGame.cs:3138", "Timeline/EpochModel.cs:102-234", "Timeline.Epochs/*.cs"],
  "decision": "port-parameter",
  "justification": "Unlock state changes which content can appear and therefore RNG outcomes; it is a run input (in the save), not UI. Timeline UI, epoch reveal flow and stories are excluded.",
  "sim_notes": ["Default: UnlockState.all semantics plus 'all acts discovered' (DiscoveredActs is a live profile read outside UnlockState)", "Replay of a recorded run: load each SerializablePlayer.UnlockState from the save"]
 },
 {
  "id": "multiplayer_scaling",
  "name": "Multiplayer scaling and player-count branches",
  "namespaces": ["Models.Singleton/MultiplayerScalingModel", "Entities.Creatures/Creature (HP scaling)", "Commands/PowerCmd (power scaling)", "Runs/IRunState.CardMultiplayerConstraint", "Models/CardPoolModel.GetUnlockedCards"],
  "summary": "MultiplayerScalingModel is a combat hook listener whose only override (ModifyBlockMultiplicative on enemies) returns Players.Count when <= 2, i.e. 1m in single player, and 1m multipliers are not recorded as modifiers. Monster HP scaling and power-amount scaling are skipped when playerCount <= 1. Single-player is not a pure no-op: IRunState.CardMultiplayerConstraint = SingleplayerOnly removes the 37 MultiplayerOnly cards from every pool via CardPoolModel.GetUnlockedCards; ActModel.GetNumberOfRooms subtracts 1 room only in multiplayer; AnyAlly-target cards are unplayable in single player (NoLivingAllies: PlayerCreatures excludes pets).",
  "rules_dependent": ["every pool lookup (CardMultiplayerConstraint)", "card playability for TargetType.AnyAlly (Models/CardModel.cs:3347-3350)"],
  "game_refs": ["Models.Singleton/MultiplayerScalingModel.cs:19-77", "Hooks/Hook.cs:10096-10110", "Entities.Creatures/Creature.cs:986-993", "Entities.Creatures/Creature.cs:1332-1339", "Commands/PowerCmd.cs:550-552", "Runs/IRunState.cs:74-84", "Models/CardPoolModel.cs:68-81", "Models/ActModel.cs:185-193", "Combat/CombatState.cs:560", "Combat/CombatState.cs:923-926"],
  "decision": "stub",
  "justification": "Single-player identity confirmed by code; keep the SingleplayerOnly pool filter and the Count<=1 branch of every player-count check (33 rule files contain such checks).",
  "sim_notes": ["Do not register MultiplayerScalingModel as a listener; it cannot change any value at Players.Count == 1"]
 },
 {
  "id": "combat_history",
  "name": "Combat history",
  "namespaces": ["Combat.History", "Combat.History.Entries"],
  "summary": "Append-only per-combat log owned by CombatManager (cleared at combat end/reset). 17 entry types; each entry stores Actor, RoundNumber, CurrentSide and a snapshot of every player's TurnNumber. Content reads it through 45 query sites in 45 files (29 cards, 12 powers, 4 relics); 3 entry types are write-only. See combat_history section for every query.",
  "rules_dependent": ["damage/hit-count multipliers (CalculatedVar)", "cost reductions on cards entering combat", "playability gates", "power triggers and modifiers"],
  "game_refs": ["Combat.History/CombatHistory.cs:24-168", "Combat.History/CombatHistoryEntry.cs:46-125", "Combat/CombatManager.cs:1759", "Combat/CombatManager.cs:7084", "Combat/CombatState.cs:1077-1080"],
  "decision": "port",
  "justification": "Queries change damage, costs, playability and triggers; generated cards compute their state from history on entering combat, so counters alone are fragile.",
  "sim_notes": ["Store an immutable-entry log with structural sharing (clone = share prefix + length) and port each query as written", "HappenedThisTurn requires equal RoundNumber, CurrentSide and every player's TurnNumber; HappenedLastPlayerTurn compares only the given player's TurnNumber - 1"]
 },
 {
  "id": "card_entities",
  "name": "Card entity helpers (Entities.Cards)",
  "namespaces": ["Entities.Cards", "CardSelection"],
  "summary": "38 files. Rule-bearing: CardPile (list order, MaxCardsInHand 10, top = index 0, RandomizeOrderInternal = UnstableShuffle + TestRngInjector override + Hook.ModifyShuffleOrder(isInitialShuffle: true)), CardPlay (Player, Target, Resources, PlayIndex/IsFirstInSeries, IsAutoPlay), CardEnergyCost + LocalCostModifier (Absolute/Relative, reduce-only, expiration EndOfCombat=0/EndOfTurn=2/WhenPlayed=4) + TemporaryCardCost/Offset (UntilPlayed/ThisTurn/ThisCombat), CardTransformation (fixed replacement, option list or CardFactory random with transform rng), CardCreationResult (modified-by-relic wrapper for rewards), ResourceInfo, CardLocation (player, pile, position), CardPileAddResult, enums (CardKeyword, CardType, CardRarity, CardTag, PileType, TargetType, UnplayableReason flags, AutoPlayType, CostModifiers/KeywordSources flags, CardScope, OrbEvokeType, CardMultiplayerConstraint), extensions (IsSingleTarget, IsCombatPile, GetNextHighestRarityWithWrapping, HasResourceCostReason). UI-only: CardPreviewMode, CardCostColor, CardKeywordOrder, CardUpgradePreviewType, CardKeywordExtensions loc helpers, PileTypeExtensions.GetTargetPosition.",
  "rules_dependent": ["every card play and pile move"],
  "game_refs": ["Entities.Cards/CardPile.cs:35", "Entities.Cards/CardPile.cs:220-320", "Entities.Cards/CardPlay.cs:24-98", "Entities.Cards/LocalCostModifier.cs", "Entities.Cards/TemporaryCardCost.cs", "Entities.Cards/TemporaryCardCostOffset.cs", "Entities.Cards/CardTransformation.cs:139-195", "Entities.Cards/ActionTargetExtensions.cs", "Entities.Cards/PileTypeExtensions.cs", "Entities.Cards/CardRarityExtensions.cs", "Entities.Cards/UnplayableReason.cs", "CardSelection/CardSelectorPrefs.cs"],
  "decision": "port",
  "justification": "Mostly already partially ported in P0 (CardPile, CardEnergyCost, CardPlay, enums); the remainder is required by the content kernel.",
  "sim_notes": ["CardPrefs.RequireManualConfirmation = MinSelect >= 0 && MinSelect != MaxSelect decides auto-resolution of prompts", "PileType.Deck is the run deck; IsCombatPile = Draw..Play"]
 },
 {
  "id": "helpers_extensions",
  "name": "Helpers and extensions with rule semantics",
  "namespaces": ["Helpers", "Helpers.Models", "Extensions"],
  "summary": "Rule-bearing: AscensionHelper.GetValueIfAscension/HasAscension (reads RunManager.Instance; 102 monster files), ListExtensions.UnstableShuffle (Fisher-Yates from the end, NextInt(i+1)), StableShuffle (List.Sort with IComparable then UnstableShuffle), IEnumerableExtensions.TakeRandom (shuffle a full copy then Take), GrabBag (weighted roulette with rejection resampling; ActModel encounters), StringHelper (Slugify, SnakeCase, GetDeterministicHashCode XXH64, GetDeterministicHashCodeOld), SeedHelper (CanonicalizeSeed; GetRandomSeed with BadWordChecker rejection), EggRelicHelper (upgrade reward cards of one type), Really.bigNumber 999999999, TaskHelper.RunSafely (fire-and-forget tasks inside rule code), NonInteractiveMode, OneTimeInitialization (ModelDb.Init order). UI-only: CardCostHelper, Ease, EnergyIconHelper, GlyphConverter, GodotTreeExtensions, HandLayoutHelper, HandPosHelper, ImageHelper, MathHelper, SceneHelper, ScrollHelper, SpineNodeExtensions, StsColors, TimeFormatting, DisplayVars, CommandLineHelper, ReflectionHelper (type discovery, mods), SignalAwaiterExtensions.",
  "rules_dependent": ["ascension values", "every shuffle and random subset"],
  "game_refs": ["Helpers/AscensionHelper.cs:9-36", "Extensions/ListExtensions.cs:10-62", "Extensions/IEnumerableExtensions.cs:9-12", "Helpers/GrabBag.cs:7-90", "Helpers/StringHelper.cs:63-135", "Helpers/SeedHelper.cs:13-43", "Helpers.Models/EggRelicHelper.cs", "Helpers/Really.cs", "Helpers/TaskHelper.cs:210"],
  "decision": "port",
  "justification": "The shuffles and hashing are already ported in P0 (RngCollectionExtensions, DeterministicHash); AscensionHelper maps to cs.GetValueIfAscension; GrabBag and EggRelicHelper are needed by the run layer and relics.",
  "sim_notes": ["StableShuffle depends on .NET List.Sort (introsort, unstable for equal keys): keep List<T>.Sort with the game's comparer (CardModel: ModelId ordinal, then upgrade level), never OrderBy", "TakeRandom consumes Count-1 draws regardless of count", "Rng.NextItem consumes one NextInt even for a single item and nothing for an empty sequence (Random/Rng.cs:213-223)"]
 },
 {
  "id": "dynamic_vars",
  "name": "Localization.DynamicVars (numeric card/power/relic variables)",
  "namespaces": ["Localization.DynamicVars"],
  "summary": "24 DynamicVar types + DynamicVarSet. BaseValue clamps to 999999999; EnchantedValue/PreviewValue are display values. Rule-bearing types: DamageVar/BlockVar (value + ValueProp), CalculatedVar/CalculatedDamageVar/CalculatedBlockVar (CalculationBase.BaseValue + CalculationExtra.BaseValue x static multiplier(card, target); multiplier is 0 unless CombatManager.IsInProgress and card in combat; FromOsty), CalculationBaseVar, CalculationExtraVar, ExtraDamageVar, OstyDamageVar, PowerVar<T> (power amount), SummonVar, CardsVar, EnergyVar, StarsVar, ForgeVar, GoldVar, HealVar, HpLossVar, MaxHpVar, RepeatVar, IntVar, BoolVar (0/1), IfUpgradedVar (display enum), StringVar (text). DynamicVarSet exposes named accessors (Damage, Block, Cards, Energy, Strength, Vulnerable, Weak, Poison, Doom, Dexterity, Stars, Summon, Forge, Gold, Heal, HpLoss, MaxHp, Repeat, OstyDamage, ExtraDamage, CalculatedDamage, CalculatedBlock, CalculationBase, CalculationExtra) and RecalculateForUpgradeOrEnchant/FinalizeUpgrade.",
  "rules_dependent": ["every numeric constant of content", "history-driven multipliers"],
  "game_refs": ["Localization.DynamicVars/DynamicVar.cs:37", "Localization.DynamicVars/CalculatedVar.cs:28-48", "Localization.DynamicVars/DynamicVarSet.cs:26-72", "Localization.DynamicVars/DynamicVarSet.cs:169-206"],
  "decision": "port",
  "justification": "Content constants and upgrade deltas live here; the P0 sim has DynamicVar/DynamicVarSet with Damage/Block only.",
  "sim_notes": ["Content construction counts in Models.*: DamageVar 210, PowerVar 194, DynamicVar 182, CardsVar 171, BlockVar 105, EnergyVar 86, StringVar 65, CalculationBaseVar 43, GoldVar 31, RepeatVar 30, HealVar 24, CalculationExtraVar 24, MaxHpVar 21, ExtraDamageVar 19, CalculatedDamageVar 19, HpLossVar 18, StarsVar 17, CalculatedVar 17, SummonVar 15, ForgeVar 13, IntVar 12, OstyDamageVar 11, CalculatedBlockVar 7 (raw construction-site counts, async duplicates included)", "UpdateCardPreview paths are display-only; actual damage goes through Hook.ModifyDamage with the enchantment applied there"]
 },
 {
  "id": "saves_profile",
  "name": "Saves: profile/progress state read by rules",
  "namespaces": ["Saves/ProgressState", "Saves/SaveManager", "Saves.Managers/ProgressSaveManager", "Saves.Runs/SerializablePlayer"],
  "summary": "ProgressState holds epochs, character/card/encounter/enemy/ancient stats, discovered cards/relics/potions/events/acts, WongoPoints, TestSubjectKills, ArchitectDamage, UniqueId, FTUE flags. Rules read the profile in two ways: (1) through the run's UnlockState snapshot (preferred, saved), (2) live through SaveManager.Instance.Progress in 5 paths (act discovery, first-run tutorial room, WelcomeToWongos points, single-player ascension clamp in the lobby, TheArchitect dialogue after victory) plus display-only reads (Vakuu visits, TestSubject title, LoomingFruit icon, MaxAscensionWhenRunStarted, ancient dialogue). PrefsSave.FastMode only changes VFX timing.",
  "rules_dependent": ["see unlock_profile_dependence.paths"],
  "game_refs": ["Saves/ProgressState.cs:70-225", "Saves/SaveManager.cs:1588-1590", "Saves.Managers/ProgressSaveManager.cs:146-148", "Saves.Runs/SerializablePlayer.cs:169-321"],
  "decision": "port-parameter",
  "justification": "Only a handful of profile facts matter; expose them as explicit run parameters with all-unlocked defaults instead of porting the save system. Save import (read path) belongs to the run-layer study.",
  "sim_notes": ["Profile facts parameter: {UnlockState per player, DiscoveredActs, ProfileNumberOfRuns, WongoPoints}"]
 },
 {
  "id": "model_db",
  "name": "ModelDb registry, ids, pools and lookups",
  "namespaces": ["Models/ModelDb", "Models/ModelId", "Models/AbstractModelSubtypes", "Models.CardPools", "Models.RelicPools", "Models.PotionPools"],
  "summary": "Static registry keyed by ModelId(Category, Entry): Category = Slugify(name of the first subclass under AbstractModel) minus '_MODEL' (CARD, POWER, RELIC, ...), Entry = Slugify(class name). Init instantiates every type in AbstractModelSubtypes.All plus mod subtypes; canonical instances are shared, ToMutable clones. Pools: 5 characters (Ironclad, Silent, Regent, Necrobinder, Defect); shared card pools Colorless, Curse, Deprecated, Event, Quest, Status, Token; shared relic pools Deprecated, Event, Fallback, Shared; shared potion pools Deprecated, Event, Mock, Shared, Token. Pool order = GenerateAllCards/Relics/Potions array order (+ mods); epoch filters use RemoveAll (order preserved). Acts: Overgrowth(0, default), Underdocks(0), Hive(1), Glory(2). AllRelics/AllPotions are sorted by Id.Entry with the default culture-sensitive comparer; ModelId.CompareTo is ordinal. ModelDb has no randomized lookup itself; content draws through CardFactory (GetDistinctForCombat = player-count filter + FilterForCombat + TakeRandom; GetForCombat = NextItem with replacement; CreateForReward = rarity roll then NextItem on PlayerRng.Rewards; CreateRandomCardForTransform), PotionFactory (character + Shared unlocked potions, rarity then NextItem), RelicFactory (grab-bag pulls, RollRarity 0.5/0.83, fallback Circlet) and OrbModel.GetRandomOrb.",
  "rules_dependent": ["every random pick from a pool depends on pool order", "ids appear in saves and recordings"],
  "game_refs": ["Factories/CardFactory.cs:25-200", "Factories/PotionFactory.cs:22-66", "Factories/RelicFactory.cs:13-67", "Models/ModelDb.cs:112-218", "Models/ModelDb.cs:220-275", "Models/ModelDb.cs:353-384", "Models/ModelDb.cs:460-488", "Models/ModelId.cs:39-91", "Helpers/StringHelper.cs:68-73", "Models/CardPoolModel.cs:35-46", "Models/RelicPoolModel.cs:21-43", "Models/PotionPoolModel.cs:23-43"],
  "decision": "port",
  "justification": "The sim registry (Registry/Registries.cs) already mirrors the id rule; pools must reproduce the game's array order exactly.",
  "sim_notes": ["Per-model inventories are in sibling files sim/specs/inventory/{cards,powers,relics,potions,...}.json and pools.json", "Never iterate HashSet/Dictionary keyed by ModelId or string for ordered choices (string hashes are randomized per process)"]
 },
 {
  "id": "local_context",
  "name": "LocalContext (local-player identity)",
  "namespaces": ["Context/LocalContext"],
  "summary": "Static NetId of the local player. IsMe(player) is true for the single-player player (netId 1); IsMe(creature) is false for monsters and pets without Player; IsMine(card) requires a mutable card owned by the local player. 27 content files gate on it; rule-relevant cases include MembershipCard price discount (ModifyMerchantPrice requires IsMe(Owner)), ThievingHopper target fallback, SoulFysh Beckon handling, Vakuu/TheArchitect display.",
  "rules_dependent": ["shop prices (MembershipCard)", "a few monster target choices"],
  "game_refs": ["Context/LocalContext.cs", "Models.Relics/MembershipCard.cs:18-29", "Models.Monsters/ThievingHopper.cs:589", "Models.Monsters/SoulFysh.cs:779"],
  "decision": "stub",
  "justification": "Single player: IsMe(player) == true, IsMe(non-player creature) == false; no state needed.",
  "sim_notes": []
 },
 {
  "id": "achievements_timeline_ui",
  "name": "Achievements, stories, epoch reveal UI, stats",
  "namespaces": ["Achievements", "Models.Achievements", "Timeline.Stories", "Timeline (reveal flow)", "Platform", "Runs.Metrics", "Leaderboard"],
  "summary": "Progress bookkeeping after combats/runs (UpdateProgressAfterCombatWon, UpdateProgressWithRunData, MarkCardAsSeen/MarkRelicAsSeen), achievements and Steam stats, metrics upload.",
  "rules_dependent": [],
  "game_refs": ["Combat/CombatManager.cs:1793-1795", "Runs/RunManager.cs:4724-4762", "Saves.Managers/ProgressSaveManager.cs:146-170"],
  "decision": "exclude",
  "justification": "Writes only; no rule reads them during a run except through the UnlockState snapshot taken at run start.",
  "sim_notes": []
 }
]
