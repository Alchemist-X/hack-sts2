TEST_SEAMS = {
 "switches": [
  {"name": "TestMode.IsOn", "ref": "TestSupport/TestMode.cs:11-20", "effect": "static flag; set by TurnOnInternal (:45-48); IsTestRunFromCmdline checks for RiderTestRunner/ in Godot OS.GetCmdlineArgs (:22-25)"},
  {"name": "NonInteractiveMode.IsActive", "ref": "Helpers/NonInteractiveMode.cs:10-28", "effect": "TestMode.IsOn || AutoSlayerCheck(); AutoSlayer installs AutoSlayerCheck = () => IsActive in its static ctor (AutoSlay/AutoSlayer.cs:2529-2532)"}
 ],
 "injection_points": [
  {"name": "ICardSelector / CardSelectCmd selector stacks", "ref": "TestSupport/ICardSelector.cs:9-14; Commands/CardSelectCmd.cs:3084-3150", "effect": "UseSelector (only if empty) / PushSelector / SuspendSelectorForTest; global and local-only stacks; Selector answers every CardSelectCmd prompt except FromChooseABundleScreen, plus CardReward.GetSelectedCardReward"},
  {"name": "TestCardSelector", "ref": "TestSupport/TestCardSelector.cs:221-319", "effect": "queued answers by card or by index (selected cards must be among options, else throws); no queued answer -> empty selection; default card reward = first option; PrepareToBlock makes the next prompt block (pause/resume tests)"},
  {"name": "TestRngInjector", "ref": "TestSupport/TestRngInjector.cs:8-70", "effect": "one-shot overrides consumed at: RelicFactory.PullNextRelicFromFront/Back (Factories/RelicFactory.cs:32, :44), relic rarity roll short-circuits before rng.NextFloat (:56), CardFactory.GetDistinctForCombat (Factories/CardFactory.cs:95), initial shuffle reorder after UnstableShuffle (Entities.Cards/CardPile.cs:244)"},
  {"name": "RunRngSet.MockRng / GetMockInstance", "ref": "Runs/RunRngSet.cs:57-65; :191-194", "effect": "replace one named stream with new Rng(seed); a shared mock set exists only in TestMode"},
  {"name": "RunState.CreateForTest", "ref": "Runs/RunState.cs:597-604", "effect": "default player = Deprived (1000 HP, 100 energy, mock card pool, Ironclad relic/potion pools) with UnlockState.all; default acts = ActModel.GetDefaultList(); random seed via SeedHelper"},
  {"name": "ModelDb.Init(injectedTypes) / Inject / Remove / ResetForTest", "ref": "Models/ModelDb.cs:353-384", "effect": "tests register mock models (Models.*.Mocks) into the static registry"},
  {"name": "SaveManager mock instance", "ref": "Saves/SaveManager.cs:950-981; :1097-1104", "effect": "Instance returns a mock; TestMode uses MockGodotFileIo('user://test')"},
  {"name": "RewardsSet.testSelector", "ref": "Rewards/RewardsSet.cs:290-302; :348", "effect": "in TestMode the rewards screen is replaced by a delegate"},
  {"name": "RestSiteOption.generateForTests", "ref": "Entities.RestSite/RestSiteOption.cs:19; :49-52", "effect": "override rest site options in TestMode"},
  {"name": "Per-model test overrides", "ref": "Models.Relics/SneckoEye.cs:269-275; Models.Powers/ConfusedPower.cs:25; Models.Potions/SneckoOil.cs:132; Models.Enchantments/Slither.cs:23; Models.Cards/MadScience.cs:431, :873", "effect": "fixed energy costs instead of Rng.CombatEnergyCosts rolls; fixed chaos card; assert TestMode"}
 ],
 "testmode_divergences": [
  {"ref": "Commands/CardSelectCmd.cs:3813", "test": "FromChooseABundleScreen picks bundles[0]", "shipping": "player chooses a bundle (no selector seam)", "sim": "decision point"},
  {"ref": "Runs/RunManager.cs:4096", "test": "ShouldApplyTutorialModifications = false", "shipping": "true for GameMode.Standard: boss discovery order and first-run room order applied", "sim": "apply with the UnlockState parameter"},
  {"ref": "Runs/RunManager.cs:4232", "test": "no tutorial room type", "shipping": "first-ever run: Unassigned map point -> Event", "sim": "apply only if profile NumberOfRuns == 0"},
  {"ref": "Models/ActModel.cs:487", "test": "no forced undiscovered act", "shipping": "an unlocked, not-yet-discovered non-default act is forced and no rng draw is made for that act index", "sim": "parameter DiscoveredActs"},
  {"ref": "Entities.Merchant/MerchantPotionEntry.cs:196-199", "test": "potion price not jittered, PlayerRng.Shops not consumed", "shipping": "cost = (int)Mathf.Round(cost * Shops.NextFloat(0.95f, 1.05f))", "sim": "shipping path"},
  {"ref": "Models.Powers/RollingBoulderPower.cs:98-115", "test": "one CreatureCmd.Damage on all HittableEnemies", "shipping": "one CreatureCmd.Damage per creature, triggered by the boulder VFX HitCreature signal, awaited with WhenAll", "sim": "open question: order and grouping of per-creature hits"},
  {"ref": "Models.Relics/Cauldron.cs:114; Models.Relics/CallingBell.cs:162", "test": "fixed test potions/relics", "shipping": "random rewards", "sim": "shipping path"},
  {"ref": "Rewards/RewardsSet.cs:290", "test": "testSelector replaces the rewards screen", "shipping": "rewards screen", "sim": "decision point"},
  {"ref": "Models.Relics/LargeCapsule.cs:185, :194; Models.Relics/ScrollBoxes.cs:240", "test": "Deprived maps to Ironclad cards", "shipping": "not reachable (Deprived is not playable)", "sim": "ignore"}
 ],
 "execution_order_facts": [
  {"ref": "GameActions/ActionExecutor.cs:197-209", "fact": "NonInteractiveMode: each dequeued GameAction is awaited to completion before the next one is dequeued; interactive mode may continue with other ready actions while an action is paused for a player choice (:151-161)"},
  {"ref": "GameActions/ActionExecutor.cs:130-139", "fact": "after every executed action except EndPlayerTurnAction and ReadyToBeginEnemyTurnAction, CombatManager.CheckWinCondition() runs while the combat is in progress"},
  {"ref": "GameActions/GameAction.cs:107", "fact": "NonInteractiveMode throws if an action finishes in a non-Executing state: no action may be left paused, i.e. every choice must resolve synchronously"},
  {"ref": "Commands/Cmd.cs:75; :172", "fact": "Cmd.Wait / CustomScaledWait are skipped in NonInteractiveMode (and in FastMode Instant)"},
  {"ref": "GameActions/ActionExecutor.cs:303-322; :505-512; Combat/CombatManager.cs:7504-7518", "fact": "pausing is disabled under AutoSlay/NonInteractive; TestMode polls unpause with Task.Delay(50)"},
  {"ref": "Combat/CombatStateTracker.cs:360-366", "fact": "in TestMode nothing may subscribe to CombatStateChanged: rule code does not depend on UI state-change events"},
  {"ref": "Commands/CardSelectCmd.cs:3236-3796", "fact": "with a Selector installed no choice id is reserved and SignalPlayerChoiceBegun/Ended are not called; auto-resolution happens before the selector is consulted"},
  {"ref": "Combat/CombatState.cs:221-232", "fact": "GodotTimerTask uses Task.Delay in TestMode instead of a SceneTree timer (no rule effect)"},
  {"ref": "Hooks/Hook.cs:2364, :2467, :6170, :7776, :8398, :8607; Combat/CombatManager.cs:2388, :2502, :2900, :5059, :5118", "fact": "HookPlayerChoiceContext is created in these dispatchers: prompts can occur inside AfterDeath, AfterDiedToDoom, AfterSideTurnEnd, BeforeFlush, BeforeSideTurnEnd, BeforeSideTurnStart, end-turn phases one/two and StartTurn"},
  {"ref": "Models.Monsters/* (ThrowingPlayerChoiceContext referenced by 173 content files); Commands.Builders/AttackCommand.cs:423, :434, :1132, :1146", "fact": "commands that monster code issues with ThrowingPlayerChoiceContext can never prompt (it throws); monster attacks executed with a null context use BlockingPlayerChoiceContext (no-op signals), so listeners reacting to an enemy attack may prompt during the enemy turn"}
 ]
}

AUTOSLAY = {
 "purpose": "The game's own autoplay bot. It drives the real UI (Godot nodes) but reads model-level legality for combat, so it documents which predicates define legal actions.",
 "setup": [
  {"ref": "AutoSlay/AutoSlayer.cs:1911-1920", "what": "DebugSeedOverride = seed; FastMode Fast; FTUEs off; reveals Silent1/Regent1/Defect1/Necrobinder1 epochs; own Rng = new Rng(GetDeterministicHashCode(seed)) (independent of game streams); CardSelectCmd.UseSelector(new AutoSlayCardSelector(rng))"},
  {"ref": "AutoSlay/AutoSlayer.cs:2534-2563", "what": "room handlers by RoomType (Monster/Elite/Boss -> CombatRoomHandler, Event, Shop, Treasure, RestSite) and screen handlers by overlay type (Rewards, CardReward, DeckUpgrade, DeckTransform, DeckEnchant, DeckCardSelect, SimpleCardSelect, ChooseACard, ChooseABundle, ChooseARelic, GameOver, CrystalSphere)"},
  {"ref": "AutoSlay/AutoSlayer.cs:2012; AutoSlay/AutoSlayConfig.cs:27-31", "what": "loop while TotalFloor < 49: handle room, wait for rewards after combat rooms, drain overlay screens (top of NOverlayStack -> handler), click proceed, map handler; then abandon"}
 ],
 "card_selector": {"ref": "AutoSlay.Helpers/AutoSlayCardSelector.cs:23-51", "rule": "n = min(maxSelect, count); if n < minSelect then n = min(minSelect, count); shuffle options with AutoSlay rng; take n. Card reward: uniform random card, never an alternative"},
 "rooms": [
  {"room": "Combat", "ref": "AutoSlay.Handlers.Rooms/CombatRoomHandler.cs:278-408, :602-652", "behaviour": "cheats: Plating 999 + Regen 999 at start, +200 Strength from turn 3; each turn: use every potion (EnqueueManualUse, target by TargetType: AnyEnemy random hittable enemy, AnyAlly/AnyPlayer/Self first living player creature, single-target with no target skipped); then up to 50 cards: random card with CanPlay(out reason, out preventer) not yet attempted this turn, target random HittableEnemies for AnyEnemy else null, CardCmd.AutoPlay; then PlayerCmd.EndTurn(canBackOut: false); max 100 turns"},
  {"room": "Event", "ref": "AutoSlay.Handlers.Rooms/EventRoomHandler.cs:532-640, :921-1091", "behaviour": "options = buttons with !Option.IsLocked; prefer options whose WillKillPlayer is null or false for the owner; random choice; up to 50 iterations; ancient dialogue clicked through; event combats: buffs then kill all enemies (removing powers with ShouldStopCombatFromEnding)"},
  {"room": "RestSite", "ref": "AutoSlay.Handlers.Rooms/RestSiteRoomHandler.cs:167-172", "behaviour": "random option with Option.IsEnabled, then proceed"},
  {"room": "Shop", "ref": "AutoSlay.Handlers.Rooms/ShopRoomHandler.cs:216-221", "behaviour": "repeat up to 50: random slot with Entry.IsStocked && Entry.EnoughGold, excluding the card-removal slot; OnTryPurchaseWrapper; then leave"},
  {"room": "Treasure", "ref": "AutoSlay.Handlers.Rooms/TreasureRoomHandler.cs:187-195, :249-251", "behaviour": "open chest, click every enabled visible relic holder, proceed"},
  {"room": "Map", "ref": "AutoSlay.Handlers.Screens/MapScreenHandler.cs:269-286", "behaviour": "deterministic: first row-0 point at act start, else first child of the last visited coordinate"}
 ],
 "screens": [
  {"screen": "Rewards", "ref": "AutoSlay.Handlers.Screens/RewardsScreenHandler.cs:51-57, :155-179", "behaviour": "click each enabled reward button once (potion rewards only with an open potion slot), then proceed"},
  {"screen": "CardReward", "ref": "AutoSlay.Handlers.Screens/CardRewardScreenHandler.cs:104-109", "behaviour": "random card holder"},
  {"screen": "ChooseACard", "ref": "AutoSlay.Handlers.Screens/ChooseACardScreenHandler.cs:57-63", "behaviour": "random card holder"},
  {"screen": "ChooseABundle", "ref": "AutoSlay.Handlers.Screens/ChooseABundleScreenHandler.cs:98-149", "behaviour": "random bundle, confirm"},
  {"screen": "ChooseARelic", "ref": "AutoSlay.Handlers.Screens/ChooseARelicScreenHandler.cs:51-57", "behaviour": "random clickable"},
  {"screen": "DeckUpgrade/DeckCardSelect/DeckEnchant/DeckTransform/SimpleCardSelect", "ref": "AutoSlay.Handlers.Screens/DeckUpgradeScreenHandler.cs:150-205; DeckCardSelectScreenHandler.cs:296-312; DeckEnchantScreenHandler.cs:307-326; DeckTransformScreenHandler.cs:312; SimpleCardSelectScreenHandler.cs:168", "behaviour": "fallback UI path: click random distinct holders up to min(count, 5) until confirm is enabled; normally unused because the selector answers first"},
  {"screen": "CrystalSphere", "ref": "AutoSlay.Handlers.Screens/CrystalSphereScreenHandler.cs:398-417", "behaviour": "random visible hidden cell with the current tool, up to 20 clicks, stop after 5 clicks without progress"},
  {"screen": "GameOver/Victory", "ref": "AutoSlay.Handlers.Screens/GameOverScreenHandler.cs; AutoSlay.Handlers.Rooms/VictoryRoomHandler.cs", "behaviour": "proceed"}
 ],
 "lessons_for_sim": [
  "Combat legality = CardModel.CanPlay(out reason, out preventer) per hand card plus a target from the valid set; AutoSlay uses HittableEnemies, the loop spec (sim/specs/loop.md:383-393) already uses IsValidTarget && ShouldAllowHitting && ShouldAllowTargeting from NTargetManager",
  "Potions are part of the combat action space (use with target, discard)",
  "Prompts with a Selector never create UI; the sim's LegalActionGenerator is the selector for the agent",
  "AutoSlay cheats and its own rng are not rules and must not be copied"
 ]
}

LEGAL_ACTIONS = [
 {"prompt": "combat_main", "api": "PlayCardAction / UsePotionAction / DiscardPotionGameAction / EndPlayerTurnAction", "options": "hand cards with CanPlay; potions in slots; end turn", "legality": "Models/CardModel.cs:3330-3360 (Unplayable keyword, HasEnoughResourcesFor energy+stars, AnyAlly needs another living player, Hook.ShouldPlay, IsPlayable); targets per sim/specs/loop.md:383-393", "count": "1", "auto_resolve": "none", "skip": "EndTurn always legal at a decision point", "ordering": "hand index, target index, EndTurn last (current sim contract)"},
 {"prompt": "choose_a_card", "api": "CardSelectCmd.FromChooseACardScreen (Commands/CardSelectCmd.cs:3169-3234)", "options": "<= 3 cards (throws if more)", "legality": "any option", "count": "0..1 on the selector path; UI enforces canSkip", "auto_resolve": "0 options -> null", "skip": "only if canSkip", "ordering": "given order"},
 {"prompt": "simple_grid", "api": "FromSimpleGrid / FromSimpleGridForRewards (3236-3351)", "options": "given list", "legality": "any option", "count": "[MinSelect, MaxSelect]", "auto_resolve": "count <= MinSelect && !RequireManualConfirmation -> all; 0 -> empty", "skip": "MinSelect == 0", "ordering": "given order"},
 {"prompt": "combat_pile", "api": "FromCombatPile (3361-3439)", "options": "pile cards passing filter", "legality": "any option", "count": "[MinSelect, MaxSelect]", "auto_resolve": "0 -> empty; count <= MinSelect && !RequireManualConfirmation -> all", "skip": "MinSelect == 0", "ordering": "Draw pile: OrderBy(Rarity).ThenBy(Id) on the selector path (:3403-3405); other piles: pile order"},
 {"prompt": "hand_select", "api": "FromHand / FromHandForDiscard (3668-3742)", "options": "hand cards passing filter", "legality": "any option", "count": "[MinSelect, MaxSelect]", "auto_resolve": "as combat_pile", "skip": "MinSelect == 0", "ordering": "hand order"},
 {"prompt": "hand_upgrade", "api": "FromHandForUpgrade (3744-3796)", "options": "upgradable hand cards", "legality": "IsUpgradable", "count": "exactly 1", "auto_resolve": "<= 1 option -> that option or null", "skip": "no", "ordering": "hand order"},
 {"prompt": "deck_upgrade", "api": "FromDeckForUpgrade (3441-3483)", "options": "deck cards with IsUpgradable", "legality": "any option", "count": "[MinSelect, MaxSelect]", "auto_resolve": "0 -> empty; count <= MinSelect && !RMC -> all", "skip": "MinSelect == 0; Cancelable for Smith (Entities.RestSite/SmithRestSiteOption.cs:142, :260)", "ordering": "deck order"},
 {"prompt": "deck_transform", "api": "FromDeckForTransformation (3485-3531)", "options": "deck cards, Type != Quest && IsTransformable", "legality": "any option", "count": "[MinSelect, MaxSelect]", "auto_resolve": "as deck_upgrade", "skip": "MinSelect == 0", "ordering": "deck order"},
 {"prompt": "deck_enchant", "api": "FromDeckForEnchantment (3533-3605)", "options": "deck cards with enchantment.CanEnchant && additional filter", "legality": "any option", "count": "[MinSelect, MaxSelect]", "auto_resolve": "count <= MinSelect -> all (no RequireManualConfirmation check)", "skip": "MinSelect == 0", "ordering": "deck order"},
 {"prompt": "deck_removal", "api": "FromDeckForRemoval -> FromDeckGeneric (3607-3666)", "options": "deck cards with IsRemovable && filter", "legality": "any option", "count": "[MinSelect, MaxSelect]", "auto_resolve": "count <= MinSelect && !RMC -> all; dead player -> empty", "skip": "MinSelect == 0; Cancelable for reward removal (Multiplayer.Game/RewardSynchronizer.cs:157, :525)", "ordering": "curses first (sort key -999999999), then deck index"},
 {"prompt": "choose_a_bundle", "api": "FromChooseABundleScreen (3798-3840)", "options": "bundles (lists of cards)", "legality": "any bundle", "count": "exactly 1", "auto_resolve": "0 bundles -> empty", "skip": "no", "ordering": "given order"},
 {"prompt": "card_reward", "api": "CardReward.OnSelect (Rewards/CardReward.cs:590-700)", "options": "offered cards + alternatives from CardRewardAlternative.Generate (Skip if CanSkip, REROLL if CanReroll, plus Hook.ModifyCardRewardAlternatives; max 2)", "legality": "index into cards then alternatives; null = close", "count": "loop: after a pick, continue while Hook.ShouldAllowSelectingMoreCardRewards", "auto_resolve": "none", "skip": "Skip alternative ends without completing; null closes", "ordering": "cards then alternatives"},
 {"prompt": "rewards_screen", "api": "RewardsSet.Offer / NRewardsScreen", "options": "unclaimed rewards (potion only with an open slot)", "legality": "per reward type", "count": "any subset, each once", "auto_resolve": "none", "skip": "proceed", "ordering": "reward list order"},
 {"prompt": "event_option", "api": "EventModel options (Events/EventOption.cs)", "options": "current page options", "legality": "!IsLocked (IsLocked = OnChosen == null, Events/EventOption.cs:279, :292)", "count": "1", "auto_resolve": "none", "skip": "IsProceed options", "ordering": "option order"},
 {"prompt": "rest_site", "api": "RestSiteOption.Generate", "options": "options", "legality": "Option.IsEnabled", "count": "1 then proceed", "auto_resolve": "none", "skip": "proceed after", "ordering": "option order"},
 {"prompt": "shop", "api": "MerchantInventory entries", "options": "card/relic/potion entries and card removal", "legality": "Entry.IsStocked && Entry.EnoughGold", "count": "any number", "auto_resolve": "none", "skip": "leave", "ordering": "inventory order"},
 {"prompt": "treasure", "api": "TreasureRoom", "options": "chest then relic holders", "legality": "holder IsEnabled", "count": "per room rules", "auto_resolve": "none", "skip": "proceed", "ordering": "holder order"},
 {"prompt": "map", "api": "MoveToMapCoordAction", "options": "children of the current map point (row 0 at act start)", "legality": "reachable child (WingedBoots/Flight modify travel)", "count": "1", "auto_resolve": "none", "skip": "no", "ordering": "child order"},
 {"prompt": "crystal_sphere", "api": "CrystalSphereMinigame.SetTool / CellClicked (Events.Custom.CrystalSphereEvent/CrystalSphereMinigame.cs:25-31, :819-829)", "options": "tool {Small, Big} and hidden cells", "legality": "cell.IsHidden", "count": "DivinationCount clicks (Big clears adjacent cells)", "auto_resolve": "none", "skip": "proceed when finished", "ordering": "grid order"},
 {"prompt": "choose_a_relic", "api": "RelicSelectCmd.FromChooseARelicScreen (Commands/RelicSelectCmd.cs:162-200)", "options": "relics", "legality": "any", "count": "1", "auto_resolve": "none", "skip": "no", "ordering": "given order", "note": "no callers in v0.111.0"}
]
