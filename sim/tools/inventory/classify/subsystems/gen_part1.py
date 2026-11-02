SUBSYSTEMS = [
 {
  "id": "test_support",
  "name": "Official test seams (TestSupport, NonInteractiveMode, mocks)",
  "namespaces": ["TestSupport", "Helpers (NonInteractiveMode, OneTimeInitialization)", "Models.*.Mocks", "Models.Characters/Deprived"],
  "summary": "Static switches and injection points the game's own test suite uses to run the rules headless: TestMode.IsOn (static flag), NonInteractiveMode.IsActive (TestMode or AutoSlay), ICardSelector stacks inside CardSelectCmd, TestRngInjector one-shot overrides for the four high-entropy draws, RunRngSet.MockRng per-stream replacement, RunState.CreateForTest (Deprived test character, UnlockState.all), ModelDb.Init(injectedTypes)/Inject/Remove/ResetForTest, SaveManager mock instance, RewardsSet.testSelector, RestSiteOption.generateForTests and per-model test overrides.",
  "rules_dependent": [
   "~39 content files branch on TestMode; almost all only skip VFX/SFX/Godot nodes",
   "TestMode branches that change rule outcomes or RNG consumption are listed in test_seams.testmode_divergences and must take the TestMode.IsOff branch in the sim"
  ],
  "game_refs": [
   "TestSupport/TestMode.cs:11-48",
   "Helpers/NonInteractiveMode.cs:10-28",
   "TestSupport/ICardSelector.cs:9-14",
   "TestSupport/TestCardSelector.cs:221-319",
   "TestSupport/TestRngInjector.cs:8-70",
   "Runs/RunRngSet.cs:57-65",
   "Runs/RunRngSet.cs:191-194",
   "Runs/RunState.cs:597-604",
   "Models/ModelDb.cs:353-384",
   "Saves/SaveManager.cs:950-981",
   "Rewards/RewardsSet.cs:290-292",
   "Rewards/RewardsSet.cs:348",
   "Entities.RestSite/RestSiteOption.cs:19",
   "Entities.RestSite/RestSiteOption.cs:49-52"
  ],
  "decision": "port-as-sim-seams",
  "justification": "The seams themselves are test infrastructure, not rules. The sim needs equivalents (decision API in place of ICardSelector, explicit RNG state in place of TestRngInjector, populateMonsters-style factories in place of CreateForTest) but must follow the shipping (TestMode.IsOff) rule paths. Mock models (48 files under Models.*.Mocks and Models.Orbs.Mock plus Deprived/MockCardPool/MockPotionPool) are excluded from the registry.",
  "sim_notes": [
   "Treat NonInteractiveMode.IsActive == true for waits/pauses (Cmd.Wait no-ops, ActionExecutor awaits each action to completion) but TestMode.IsOn == false for every rule branch",
   "MockPotionPool is registered in ModelDb.AllSharedPotionPools (Models/ModelDb.cs:187-194); exclude it from any sim pool list (rules only read character + SharedPotionPool, Factories/PotionFactory.cs:65)"
  ]
 },
 {
  "id": "card_selection_prompts",
  "name": "Player-choice prompts (CardSelectCmd, selector stack, CardSelectorPrefs, PlayerChoiceContext)",
  "namespaces": ["Commands/CardSelectCmd", "CardSelection/CardSelectorPrefs", "GameActions.Multiplayer/*PlayerChoiceContext", "Commands/RelicSelectCmd", "Rewards/CardReward", "Entities.CardRewardAlternatives"],
  "summary": "All in-combat and deck card prompts go through 13 CardSelectCmd entry points. Each first applies auto-resolution (0 options -> empty; count <= MinSelect && !RequireManualConfirmation -> all), then asks Selector (global stack), else LocalSelector, else the Godot screen. A selector answer is synchronous and skips ReserveChoiceId/SignalPlayerChoiceBegun. CardReward.OnSelect consults Selector.GetSelectedCardReward. Content can push its own selector (WhisperingEarring pushes VakuuCardSelector = take first maxSelect options). FromChooseABundleScreen has no selector seam (TestMode -> bundles[0]). RelicSelectCmd.FromChooseARelicScreen has no callers in v0.111.0.",
  "rules_dependent": ["107 content files call CardSelectCmd (42 cards, 5 powers, 29 relics, 9 potions, 1 monster, 20 events, 1 modifier)", "card rewards (Rewards/CardReward.cs:590-700) and reward alternatives (Skip/Reroll/hook-added, max 2)"],
  "game_refs": [
   "Commands/CardSelectCmd.cs:3084-3150",
   "Commands/CardSelectCmd.cs:3169-3234",
   "Commands/CardSelectCmd.cs:3236-3351",
   "Commands/CardSelectCmd.cs:3361-3439",
   "Commands/CardSelectCmd.cs:3441-3666",
   "Commands/CardSelectCmd.cs:3668-3796",
   "Commands/CardSelectCmd.cs:3798-3840",
   "CardSelection/CardSelectorPrefs.cs",
   "Rewards/CardReward.cs:590-700",
   "Entities.CardRewardAlternatives/CardRewardAlternative.cs",
   "Models.Relics/WhisperingEarring.cs:86",
   "Models.Relics/VakuuCardSelector.cs",
   "Commands/RelicSelectCmd.cs:162-200",
   "Hooks/Hook.cs:2364",
   "Combat/CombatManager.cs:5059"
  ],
  "decision": "port",
  "justification": "Every prompt is a decision point of the environment (P1 plan: each player-choice prompt is a search node). The auto-resolution rules decide whether a decision point exists at all, so they must be ported exactly.",
  "sim_notes": [
   "Model a selector stack in CombatState/RunState: the agent is the bottom selector; content-pushed selectors (VakuuCardSelector) resolve prompts without a decision",
   "Prompts can open inside HookPlayerChoiceContext dispatchers (AfterDeath, AfterDiedToDoom, AfterSideTurnEnd, BeforeFlush, BeforeSideTurnEnd, BeforeSideTurnStart; CombatManager end-turn phases and StartTurn); monster code passes ThrowingPlayerChoiceContext to many of its own commands (a prompt there would throw; 173 content files reference it), but monster attacks fall back to BlockingPlayerChoiceContext (Commands.Builders/AttackCommand.cs:423, :434, :1132, :1146), so a player listener reacting to an enemy attack can open a prompt during the enemy turn",
   "Draw-pile prompts sort options by (Rarity, Id) on the selector path (Commands/CardSelectCmd.cs:3403-3405)"
  ]
 },
 {
  "id": "enchantments",
  "name": "Enchantments (card-attached modifiers, persistent on deck cards)",
  "namespaces": ["Models/EnchantmentModel", "Models.Enchantments", "Entities.Enchantments", "Commands/CardCmd.Enchant"],
  "summary": "22 concrete enchantments (+DeprecatedEnchantment, +1 mock). One per card (stackable only with the same type), stored on the deck card and copied into combat. Integrates via EnchantDamageAdditive/Multiplicative (Hook.ModifyDamage), EnchantBlockAdditive/Multiplicative (Hook.ModifyBlock), EnchantPlayCount (CardModel replay count), OnPlay (after card effect), OnEnchant (keyword/cost changes), and ordinary hooks while ShouldReceiveCombatHooks (card is a listener). ShouldStartAtBottomOfDrawPile is applied at combat start.",
  "rules_dependent": ["damage/block pipeline (Hook.cs:10071-10080, :10336-10340)", "card play replay count (Models/CardModel.cs:2762)", "card OnPlay tail (Models/CardModel.cs:721)", "combat start draw-pile placement (Combat/CombatManager.cs:4249)", "DynamicVar previews (Localization.DynamicVars/*Var.cs)", "hook listener order: card, then its affliction, then its enchantment (Combat/CombatState.cs:903-910)"],
  "game_refs": ["Models/EnchantmentModel.cs:125-127", "Hooks/Hook.cs:10071-10080", "Models/EnchantmentModel.cs:289-366", "Models/EnchantmentModel.cs:395-418", "Commands/CardCmd.cs:2146-2180", "Commands/CardSelectCmd.cs:3533-3605", "Models.Enchantments/Slither.cs:23", "Models.Enchantments/Goopy.cs"],
  "decision": "port",
  "justification": "Applied in standard runs by 16 relics, 9 events and the card BladeOfInk; they change damage, block, play count, costs and keywords.",
  "sim_notes": ["Goopy increments both the combat copy and Card.DeckVersion.Enchantment.Amount: the deck/combat card link must exist in the sim", "Slither re-rolls cost with Rng.CombatEnergyCosts.NextInt(4) on draw", "Status = Disabled is per combat copy (Glam/Sown/Swift/Vigorous)", "CanEnchant base rule: not Status/Curse/Quest, not Unplayable deck cards, at most one enchantment unless IsStackable and same type (Models/EnchantmentModel.cs:294-315)"]
 },
 {
  "id": "afflictions",
  "name": "Afflictions (monster-applied card debuffs)",
  "namespaces": ["Models/AfflictionModel", "Models.Afflictions", "Commands/CardCmd.Afflict"],
  "summary": "7 afflictions (Bound, Entangled, Galvanized, Hexed, Ringing, Smog, Tainted) applied to cards in combat by 7 monster powers (ChainsOfBinding, Galvanic, Hex, Ringing, Smoggy, Tangled, VitalSpark) from 7 encounters. Most behaviour lives in the applying power; afflictions are hook listeners (ShouldReceiveCombatHooks true) and record CardAfflictedEntry in combat history.",
  "rules_dependent": ["Hook.ShouldAfflict gate", "CardAfflicted history entry (ChainsOfBindingPower query)", "random target pick AfflictionModel.PickRandomTargets with Rng.CombatCardGeneration"],
  "game_refs": ["Models/AfflictionModel.cs:117-252", "Commands/CardCmd.cs:2188-2276 (AfflictAndPreview 2188, Afflict 2228-2271, ClearAffliction 2273)", "Combat/CombatState.cs:903-906"],
  "decision": "port",
  "justification": "Part of act 2/3 monster content (CeremonialBeastBoss, GlobeHeadNormal, InfestedPrismsElite, KnightsElite, LivingFogNormal, QueenBoss, VineShamblerNormal).",
  "sim_notes": ["PickRandomTargets keeps the first min(count, n) shuffled cards via RemoveRange(Clamp(n-1, 0, count), Max(0, n-count)) (Models/AfflictionModel.cs:245-250)", "Cleared by the applying powers on removal and by NightmarePower"]
 },
]
