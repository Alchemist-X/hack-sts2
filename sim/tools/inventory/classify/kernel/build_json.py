import os as _os
import sys as _sys
_sys.path.insert(0, _os.path.join(_os.path.dirname(_os.path.abspath(__file__)), ".."))
import common  # noqa: E402
import json, os, collections, datetime
K = common.work_dir("kernel")
OUT = common.stage1_path("kernel-gaps.json")
vrows = json.load(open(f"{K}/vrows.json"))
hooks = json.load(open(f"{K}/hooks.json"))
cmds = json.load(open(f"{K}/cmds2.json"))
ready = json.load(open(f"{K}/readiness.json"))
sites = json.load(open(f"{K}/sites.json"))
kindvirt = json.load(open(f"{K}/kindvirt.json"))

def dispatcher_rows():
    out = []
    for h in hooks:
        flags = h["flags"]
        ctx = "none"
        if any("per-listener" in f for f in flags): ctx = "per_listener_deferral"
        elif any("PushModel" in f for f in flags): ctx = "caller_push_pop"
        elif "ctx param" in flags: ctx = "caller"
        srcs = h["sources"] if h["name"] != "ModifyDamage" else ["Run+Combat", "Run+Combat", "Run+Combat"]
        calls = h["calls"] if h["name"] != "ModifyDamage" else ["ModifyDamageAdditive", "ModifyDamageMultiplicative", "ModifyDamageCap"]
        out.append(dict(name=h["name"], line=h["line"], ported=h["ported"], kind=h["kind"], returns=h["ret"],
                        params=h["params"], sources=srcs, passes=calls, choice_context=ctx,
                        net_id_gate=any("NetId" in f for f in flags), modifier_filtered=any("modifier-filtered" in f for f in flags),
                        enchantment_prepass=any("enchantment" in f for f in flags), out_params=h["outs"],
                        relevance=("run" if set(srcs) <= {"RunOnly"} and srcs else "combat")))
    return out

virt_out = []
for r in vrows:
    virt_out.append(dict(name=r["name"], line=r["line"], ported=r["ported"], relevance=r["relevance"], returns=r["ret"],
                         params=r["params"], default=r["default"], dispatch=r["dispatch"],
                         overrides_by_kind=r["overrides"], content_override_total=r["content_total"],
                         override_files=r["override_files"], sim_dropped_param=r["sim_dropped_param"],
                         participants_readers=r["participants_readers"]))

cmd_out = {}
for c, rows in cmds.items():
    cmd_out[c] = [dict(name=r["name"], generic=r["generic"], line=r["line"], params=r["params"], returns=r["ret"],
                       status=r["status"], scope=r["scope"], uses_models=r["uses_models"], uses_by_kind=r["uses"]) for r in rows]

listener_order = {
    "combat_list": {
        "source": "Combat/CombatState.cs:855-956 (IterateHookListeners)",
        "build_time": "whole list built at the first MoveNext of the iterator; liveness filter evaluated per element at yield (Combat/CombatState.cs:928-940)",
        "order": [
            {"step": "for each creature in _allies then _enemies", "cite": "Combat/CombatState.cs:858-860"},
            {"step": "creature.Powers in list order", "cite": "Combat/CombatState.cs:862"},
            {"step": "monster creature (incl. pets on the player side, e.g. Osty): creature.Monster (MonsterModel)", "cite": "Combat/CombatState.cs:864-867"},
            {"step": "player creature: stop here if !player.IsActiveForHooks", "cite": "Combat/CombatState.cs:870-873"},
            {"step": "player relics in list order, skipping IsMelted", "cite": "Combat/CombatState.cs:874-881"},
            {"step": "player potion slots in slot order, skipping null", "cite": "Combat/CombatState.cs:882-889"},
            {"step": "stop if PlayerCombatState == null", "cite": "Combat/CombatState.cs:890-893"},
            {"step": "orbs in OrbQueue order", "cite": "Combat/CombatState.cs:894"},
            {"step": "for each pile in AllPiles = [Hand, Draw, Discard, Exhaust, Play]: each card, then its Affliction, then its Enchantment", "cite": "Combat/CombatState.cs:895-914; Entities.Players/PlayerCombatState.cs AllPiles"},
            {"step": "CombatState.Modifiers (run modifiers)", "cite": "Combat/CombatState.cs:916-919"},
            {"step": "BadgeModels (non-rule)", "cite": "Combat/CombatState.cs:920-923"},
            {"step": "MultiplayerScalingModel (inert with one player)", "cite": "Combat/CombatState.cs:924-927; Models.Singleton/MultiplayerScalingModel.cs"},
            {"step": "mod subscribers (none in vanilla)", "cite": "Combat/CombatState.cs:944-955"}],
        "liveness": {
            "power": "Owner.CombatState != null && (Owner.Player?.IsActiveForHooks ?? true)",
            "relic|potion|card|orb": "!HasBeenRemovedFromState && Owner.IsActiveForHooks",
            "affliction|enchantment": "HasCard && !Card.HasBeenRemovedFromState && Card.Owner.IsActiveForHooks",
            "monster": "Creature.CombatState != null",
            "achievement|badge|modifier|scaling": "true",
            "cite": "Combat/CombatState.cs:1010-1062"}},
    "guarded": {"source": "Hooks/Hook.cs:8792-8810 (IterateCombatHookListeners)", "rule": "yields nothing when CombatManager.IsOverOrEnding && !IsStarting (:8794); otherwise the combat list"},
    "run": {"source": "Runs/RunState.cs:829-933 (IterateHookListeners(childCombatState))",
            "order": ["deck cards (+Enchantment, no Affliction) of every active player (:843-855)",
                      "only when childCombatState == null: relics (not melted), potions, run modifiers, badges, scaling model (:866-880)",
                      "filter RunState.Contains (:1037-1073)",
                      "run mod subscribers (:905)",
                      "the combat list of childCombatState, built only after the deck listeners were yielded and executed (:917-921)"],
            "single_player_note": "in combat the deck-card listeners are inert for v0.111.0: the only card overriding a Run+Combat hook, Melancholy.AfterDeath, returns unless Pile.IsCombatPile (Models.Cards/Melancholy.cs:114-123); deck enchantments override no Run-dispatched hook"},
    "sim_today": "powers only: player powers then each Monsters[i].Powers (Combat/Pipeline/ListenerSnapshot.cs); sources Guarded/Combat/Run match the game for all 77 ported dispatchers (script check)",
    "should_receive_combat_hooks": "AbstractModel.ShouldReceiveCombatHooks (Models/AbstractModel.cs:74) is never read by rules code: do not port it as a gate"}

choice_mechanics = {
    "contexts": [
        {"type": "GameActionPlayerChoiceContext", "where": "PlayCardAction (GameActions/PlayCardAction.cs:166-167), UsePotionAction (GameActions/UsePotionAction.cs:121)",
         "signal_begun": "ActionQueueSet.PauseActionForPlayerChoice(action) (GameActions.Multiplayer/GameActionPlayerChoiceContext.cs:120): the running action pauses in place", "sim": "Inline"},
        {"type": "BlockingPlayerChoiceContext", "where": "KnowledgeDemon.ChooseCurse (Models.Monsters/KnowledgeDemon.cs:124), relic AfterObtained, EventModel grids, AutoSlay",
         "signal_begun": "no-op (GameActions.Multiplayer/BlockingPlayerChoiceContext.cs:11-13); also AttackCommand.Execute(null) fallback for damage and AfterAttack (Commands.Builders/AttackCommand.cs:1132, :1146)", "sim": "Inline"},
        {"type": "ThrowingPlayerChoiceContext", "where": "commands issued by monster code (96 monster files)", "signal_begun": "throws NotImplementedException (GameActions.Multiplayer/ThrowingPlayerChoiceContext.cs)", "sim": "assert: no prompt possible"},
        {"type": "BranchingPlayerChoiceContext", "where": "CardModel.OnPlayWrapper around OnPlay (Models/CardModel.cs:667-669), PotionModel.OnUseWrapper (Models/PotionModel.cs:180), CardPileCmd.DrawWithoutBlockingOnOtherPlayers (Commands/CardPileCmd.cs:3990)",
         "signal_begun": "same chooser as owner → delegates to the original context (GameActions.Multiplayer/BranchingPlayerChoiceContext.cs:140-171); a new HookPlayerChoiceContext only for another player", "sim": "transparent"},
        {"type": "HookPlayerChoiceContext (StartTurn, CombatPlayPhaseOnly)", "where": "SetupPlayerTurn (Combat/CombatManager.cs:5059-5061) then RunAutoPrePlayPhase on the same context (:5204-5205)",
         "signal_begun": "first pause: GenerateHookAction, set pausedBeforeTaskAssigned (StartTurn resumes: AfterSideTurnStart :5080, OrbQueue.AfterTurnStart :5118-5120), wait for task assignment, enqueue hook action, set paused, wait for the hook action to start (deferred to the play phase), PauseActionForPlayerChoice (GameActions.Multiplayer/HookPlayerChoiceContext.cs:225-274)",
         "sim": "TurnSetup (deferral emulation)"},
        {"type": "HookPlayerChoiceContext (end of turn, CombatPlayPhaseOnly)", "where": "AfterAutoPostPlayPhaseEntered (Combat/CombatManager.cs:2387-2435)", "signal_begun": "pause, then WaitForCompletion before Phase = End; nothing intervenes in single player", "sim": "Inline"},
        {"type": "HookPlayerChoiceContext (one per listener)", "where": "AfterDeath, AfterDiedToDoom, BeforeSideTurnStart, BeforeSideTurnEnd(VeryEarly/Early), AfterSideTurnEnd(Late), BeforeFlush(Late) (Hooks/Hook.cs:2364, 2467, 8607, 8398/8441/8484, 6170/6225, 7776/7819)",
         "signal_begun": "paused listener's remainder runs after the remaining listeners of the pass (Task.WhenAll)", "sim": "PerListener: unreachable in v0.111.0 (no prompt site in these hooks) → throw NotSupportedException"}],
    "selector_seam": "CardSelectCmd.Selector (static stack, Commands/CardSelectCmd.cs:3084-3150): when set, no SignalPlayerChoiceBegun, answers come from ICardSelector.GetSelectedCards(options, min, max); used by TestMode (TestSupport/TestCardSelector.cs: empty selection when nothing queued), AutoSlay (AutoSlay/AutoSlayer.cs:1919-1920) and WhisperingEarring's turn-1 VakuuCardSelector (Models.Relics/WhisperingEarring.cs:83-86; Models.Relics/VakuuCardSelector.cs:14 = Take(max))",
    "auto_rules": [
        "n == 0 → empty result without UI (FromChooseACardScreen returns before pausing; FromHand/FromSimpleGrid/FromCombatPile/FromHandForUpgrade pause first)",
        "!RequireManualConfirmation && n <= MinSelect → all options, no UI (Commands/CardSelectCmd.cs:3257, 3316, 3397, 3633, 3693)",
        "FromHandForUpgrade: n <= 1 → the single option or null (Commands/CardSelectCmd.cs:3765-3768)",
        "RequireManualConfirmation = MinSelect >= 0 && MinSelect != MaxSelect (CardSelection/CardSelectorPrefs.cs:114)",
        "FromChooseACardScreen: n > 3 throws; canSkip allows a null result",
        "IsEnding (FromCombatPile/FromSimpleGrid/FromSimpleGridForRewards/FromChooseABundleScreen) or IsOverOrEnding (FromHand/FromHandForUpgrade) → empty without pausing"],
    "cancel_skip": "CardSelectorPrefs.Cancelable is read only by deck screens (Nodes.Screens.CardSelection/NDeckCardSelectScreen.cs:142, NDeckEnchantSelectScreen.cs:196) and set true only by rest-site options (Entities.RestSite/SmithRestSiteOption.cs, CookRestSiteOption.cs); in combat a prompt is declined only by a 0-pick confirm when MinSelect = 0 or by the skip of FromChooseACardScreen(canSkip: true)",
    "any_number_sites": "CardSelectorPrefs(prompt, 0, 999999999): Guards, Ashwater, GamblersBrew, GamblingChip; (0, N): NeowsFury, Purity",
    "ui_semantics": ["hand selection: result order = click order (Nodes.Combat/NPlayerHand.cs:1173, 1257-1260); the player always confirms; confirm enabled iff MinSelect <= picked <= MaxSelect (:1270-1281); a pick beyond MaxSelect replaces the last pick (:1167-1177)",
                     "no selectable hand card and nothing picked → empty result (Nodes.Combat/NPlayerHand.cs:1247-1250)",
                     "grid screens: a click toggles; without manual confirmation the selection completes at MaxSelect picks (combat-pile screen clips to the shown count); with it, confirm enabled at MinSelect picks (clipped only on the combat-pile screen) (Nodes.Screens.CardSelection/NCombatPileCardSelectScreen.cs:322-364, NSimpleCardSelectScreen.cs:319-354)",
                     "draw-pile screen sorts by rarity then localized title (Nodes.Screens.CardSelection/NCombatPileCardSelectScreen.cs:411-432); the Selector branch sorts by rarity then ModelId (Commands/CardSelectCmd.cs:3405, 3415)",
                     "choose-a-card screen shows cards in list order and completes on the first click (Nodes.Screens.CardSelection/NChooseACardSelectionScreen.cs:280-284)"]}

prompt_types = [
    {"kind": "HandSelect", "game": "CardSelectCmd.FromHand / FromHandForDiscard (Commands/CardSelectCmd.cs:3668-3742)", "options": "hand cards passing filter, hand order", "select": "ordered subset, MinSelect..MaxSelect (clipped)", "result_type": "CombatCard", "autoslay": "AutoSlayCardSelector: shuffle with AutoSlay Rng, take min(max,n) (AutoSlay.Helpers/AutoSlayCardSelector.cs:22-38)", "in_combat_sites": 31},
    {"kind": "HandUpgrade", "game": "CardSelectCmd.FromHandForUpgrade (Commands/CardSelectCmd.cs:3744-3796)", "options": "upgradable hand cards", "select": "exactly 1 (auto when n <= 1)", "result_type": "CombatCard", "autoslay": "selector, 1 random", "in_combat_sites": 1},
    {"kind": "PileSelect", "game": "CardSelectCmd.FromCombatPile (Commands/CardSelectCmd.cs:3353-3439)", "options": "Draw (sorted rarity, id) or Discard pile cards passing filter", "select": "ordered subset MinSelect..MaxSelect", "result_type": "CombatCard", "autoslay": "selector random subset of max size", "in_combat_sites": 18},
    {"kind": "ChooseCard", "game": "CardSelectCmd.FromChooseACardScreen (Commands/CardSelectCmd.cs:3169-3234)", "options": "1-3 generated cards (or curses for KnowledgeDemon), list order", "select": "pick 1; skip when canSkip", "result_type": "Index", "autoslay": "selector picks first of shuffled (0..1 → takes 1); screen handler random", "in_combat_sites": 10},
    {"kind": "SimpleGrid", "game": "CardSelectCmd.FromSimpleGrid (Commands/CardSelectCmd.cs:3295-3351)", "options": "generated cards (ChoicesParadox)", "select": "MinSelect..MaxSelect", "result_type": "Index", "autoslay": "selector random", "in_combat_sites": 1},
    {"kind": "Potion/target/discard decisions", "game": "UsePotionAction / DiscardPotionGameAction", "options": "potion slot × valid targets", "select": "action", "result_type": "n/a", "autoslay": "UseAllPotions at turn start with random targets (AutoSlay.Handlers.Rooms/CombatRoomHandler.cs)", "in_combat_sites": None},
    {"kind": "Run layer (not combat kernel)", "game": "FromDeckForUpgrade/Transformation/Enchantment/Removal/Generic, FromSimpleGridForRewards, FromChooseABundleScreen, card rewards (Rewards/CardReward.cs), rewards screen, events, rest site, shop, map, treasure, crystal sphere", "options": "deck cards / generated cards / bundles / options", "select": "varies", "result_type": "DeckCard/Index/CanonicalCard", "autoslay": "AutoSlay screen handlers pick uniformly at random (AutoSlay.Handlers.Screens/*.cs: random.NextItem) and confirm; card reward random index, never skip", "in_combat_sites": 0}]

design_choice_api = {
    "recommendation": "a: answer-tape re-execution, built on an internal answerer seam (b) used for fast rollouts; reject c",
    "action_kinds": "enum ActionKind : byte { PlayCard, EndTurn, UsePotion, DiscardPotion, ChoosePick, ChooseConfirm } (append only)",
    "game_action_encoding": {"UsePotion": "CardIndex = potion slot; TargetIndex >= 0 enemy index (State.Monsters), -1 none/self, <= -2 ally Allies[-2 - t]",
                             "DiscardPotion": "CardIndex = slot", "ChoosePick": "CardIndex = option index", "ChooseConfirm": "no indices"},
    "prompt_state": "ChoicePrompt(Kind, SiteId, SourceId, SourceInstanceId, Pile?, Options[(CardInstanceId, CardId, UpgradeLevel)], MinSelect, MaxSelect, RequireConfirm, CanSkip, Ordered, ResultType) + partial picks; CombatSim keeps (S0, action, tape, prompt, picks, observation)",
    "legality": ["at a prompt only ChoosePick/ChooseConfirm are legal",
                 "Pick(i): 0 <= i < n, not yet picked, picks < MaxSelect; if !Ordered only i > last pick is listed (Step accepts any order and sorts)",
                 "prompt completes on Confirm, or automatically when !RequireConfirm && picks == MaxSelect",
                 "Confirm legal iff (RequireConfirm || CanSkip) && MinSelect <= picks <= MaxSelect",
                 "a prompt is exposed iff the game would open UI (n >= 1 and no auto rule); ChooseCard with n == 1 is still exposed"],
    "hash_clone_at_prompt": "StateHash = H(S0 ‖ action ‖ tape ‖ picks); Clone copies S0, tape, picks, prompt; observation is regenerated or cloned",
    "enemy_turn_prompt": "Inline context; exposed mid enemy turn (CurrentSide == Enemy); answer re-runs EndTurn from S0",
    "turn_start_prompt": "TurnSetup context: first pause runs AfterSideTurnStart + OrbQueue.AfterTurnStart + CheckWinCondition before the answer; the remainder of SetupPlayerTurn and RunAutoPrePlayPhase run after it",
    "create_prompt": "Create() may stop at a turn-1 prompt (Toolbox BeforeHandDraw, GamblingChip AfterPlayerTurnStart, ...): S0 = post-SetUpCombat state, action = internal Begin",
    "diff_mapping": "bundle v2 'choose' decisions from recorder player_choice (mod/src/HumanFunnels.cs:662-735); CombatCard → identity match (id, upgraded), first unpicked equal option; Index → index; empty/-1 → Confirm with 0 picks; auto-resolved prompts have no record and no sim decision"}

subsystems = [
    {"name": "Relics runtime", "game": "Models/RelicModel.cs (22 base virtuals; IsMelted, Status, StackCount, HasBeenRemovedFromState); listener after player powers", "content": "300 relics; 177 override combat hooks; 73 with per-instance fields", "sim": "Relics/RelicModel.cs : AbstractModel (MemberwiseClone + CopyMutableReferenceState, WriteExtraCanonical); PlayerState.Relics; RelicRegistry"},
    {"name": "Potions runtime", "game": "Models/PotionModel.cs (OnUse, Usage, TargetType, PassesCustomUsabilityCheck, OnUseWrapper), GameActions/UsePotionAction.cs (CombatPlayPhaseOnly), Commands/PotionCmd.cs", "content": "65 potions; 9 prompt; FairyInABottle is a ShouldDie/AfterPreventingDeath listener", "sim": "Potions/PotionModel.cs; PlayerState.PotionSlots (nullable array) + MaxPotionCount; ActionKind.UsePotion/DiscardPotion"},
    {"name": "Orbs (Defect)", "game": "Entities.Orbs/OrbQueue.cs (Capacity, maxCapacity 10, TryEnqueue, BeforeTurnEnd/AfterTurnStart triggers), Models/OrbModel.cs (Passive/Evoke/BeforeTurnEndOrbTrigger/AfterTurnStartOrbTrigger), Commands/OrbCmd.cs", "content": "5 orbs; 33 cards, 10 powers, 7 relics touch orbs", "sim": "Orbs/OrbModel.cs, Orbs/OrbQueue.cs; listeners after potions; hooks ModifyOrbValue, ModifyOrbPassiveTriggerCount(+After…), AfterOrbChanneled, AfterOrbEvoked; Rng.CombatOrbGeneration"},
    {"name": "Osty / pets (Necrobinder)", "game": "Commands/OstyCmd.cs:336-395 (Summon), PlayerCmd.AddPet, PlayerCombatState pets (Entities.Players/PlayerCombatState.cs AddPetInternal/OnPetDied), CreatureCmd.Damage PetOwner block redirect (Commands/CreatureCmd.cs:3697) + ModifyUnblockedDamageTarget + HpLost Before/AfterOsty phases, AttackCommand.FromOsty, OstyDamageVar, TargetType.Osty, DieForYouPower", "content": "23 cards, 12 powers, 7 relics", "sim": "CombatState.Allies (player + pets in insertion order) replacing the implicit single player; MonsterState with Side = Player and PetOwnerId; pet killed with its owner (Commands/CreatureCmd.cs:2704-2717)"},
    {"name": "Stars (Regent)", "game": "PlayerCombatState.Stars (>= 0; setter records History.StarsModified), PlayerCmd.GainStars/LoseStars/SetStars, CardModel star costs (CanonicalStarCost 22 cards, HasStarCostX Stardust, temporary star costs), Hook.ModifyStarCost (VoidFormPower, BrilliantScarf), ShouldPayExcessEnergyCostWithStars (2 stars per missing energy, no overrides), ShouldGainStars (no overrides), AfterStarsGained (BlackHolePower), AfterStarsSpent (ChildOfTheStarsPower, GalacticDust, MiniRegent), StarsVar; HasEnoughResourcesFor (Entities.Players/PlayerCombatState.cs); X star cost captures current stars (LastStarsSpent); Radiate reads StarsModifiedEntry history", "content": "16 GainStars sources, 22+1 star-cost cards", "sim": "fields exist (PlayerState.Stars, CardInstance star costs); add StarsCmd + 4 hooks + history entry"},
    {"name": "Forge (Regent)", "game": "Commands/ForgeCmd.cs:153-170: if no non-exhausted SovereignBlade, create one in hand (CreatedThroughForge); add damage to every SovereignBlade incl. exhausted; Hook.AfterForge", "content": "10 cards, 2 powers, 1 relic, 1 potion", "sim": "Combat/Pipeline/ForgeCmd.cs; SovereignBlade card def + AfterCardChangedPiles override"},
    {"name": "Summons (monsters)", "game": "CreatureCmd.Add<T>/Add (Commands/CreatureCmd.cs:3554-3586), CombatState.CreateCreature (Combat/CombatState.cs:709-724), SetEnemyIndex (no-slot encounters)", "content": "6 monsters, 4 powers spawn creatures", "sim": "MonsterSpawner mid-combat path + SetUpForCombat + AfterCreatureAdded order"},
    {"name": "Doom", "game": "Models.Powers/DoomPower.cs (BeforeSideTurnEnd → DoomKill → Kill each + Hook.AfterDiedToDoom), MonsterModel.ShouldDisappearFromDoom / OnDieToDoom", "content": "14 cards, 7 powers", "sim": "power content + AfterDiedToDoom dispatcher (per-listener context)"},
    {"name": "Enchantments", "game": "Models/EnchantmentModel.cs (19 virtuals): EnchantDamage/Block Additive/Multiplicative pre-pass in Hook.ModifyDamage/ModifyBlock (Hooks/Hook.cs:10075-10080, :10332-10342), EnchantPlayCount (replay count), OnPlay after card OnPlay, ShouldStartAtBottomOfDrawPile (turn 1), listener after its card", "content": "23 enchantments, mostly applied by events/relics (run layer)", "sim": "CardInstance.Enchantment (cloned with the card); EnchantmentRegistry"},
    {"name": "Afflictions", "game": "Models/AfflictionModel.cs; CardCmd.Afflict (ShouldAfflict, History.CardAfflicted), OnPlay after card OnPlay, listener between card and enchantment", "content": "7 afflictions, applied by 11 powers (monster debuffs)", "sim": "CardInstance.Affliction; AfflictionRegistry"},
    {"name": "Run modifiers in combat", "game": "CombatState.Modifiers listeners; only Hoarder (AfterCardChangedPiles) and Murderous (AfterCreatureAddedToCombat) override combat hooks", "content": "17 modifiers (custom/daily runs)", "sim": "CombatState.Modifiers list, empty for standard runs"},
    {"name": "Multiplayer scaling", "game": "MultiplayerScalingModel.ModifyBlockMultiplicative returns player count (1); PowerCmd scaling gated by Players.Count > 1 (Commands/PowerCmd.cs:550); monster HP scaling gated by playerCount != 1 (Entities.Creatures/Creature.cs:986-993)", "content": "1 singleton, 5 powers override GetScaledAmountForMultiplayer", "sim": "stub: single player constant; no listener"},
    {"name": "Card creation", "game": "CombatState.CreateCard (ToMutable + AfterCreated, Combat/CombatState.cs:671-677), CardFactory.GetDistinctForCombat/GetForCombat/FilterForCombat (Factories/CardFactory.cs:93-121), CardPoolModel.GetUnlockedCards(UnlockState, multiplayer constraint) (Models/CardPoolModel.cs:68-86), Runs/CardCreationFlags.cs (rewards)", "content": "34 combat generation call sites; 37 MultiplayerOnly cards", "sim": "Cards/CardFactory.cs + ordered pool data + UnlockState input (default all unlocked)"},
    {"name": "Temporary powers", "game": "Models/ITemporaryPower.cs (OriginModel, InternallyAppliedPower) on 5 powers; read by Misery, Rend, UnsettlingLamp", "content": "5 powers", "sim": "interface on PowerModel subclasses with ids instead of references"},
    {"name": "DynamicVar subclasses", "game": "Localization.DynamicVars/* (26 types): DamageVar 209, CardsVar 170, PowerVar 169, BlockVar 104, EnergyVar 82, CalculationBaseVar 43, StringVar 42, RepeatVar 30, GoldVar 27, CalculationExtraVar 24, HealVar 24, MaxHpVar 21, CalculatedDamageVar 19, ExtraDamageVar 19, HpLossVar 18, CalculatedVar 17, StarsVar 17, SummonVar 14, ForgeVar 13, OstyDamageVar 11, IntVar 8, CalculatedBlockVar 7 (files using new X)", "content": "CalculatedVar = Base + Extra × static multiplier(card, target) when in combat (Localization.DynamicVars/CalculatedVar.cs)", "sim": "Cards/DynamicVars/*; CalculatedVar multiplier as static Func<CombatState, CardInstance, CreatureState?, decimal>"},
    {"name": "Combat history", "game": "Combat.History/CombatHistory.cs (17 entry types, round/side/turn stamps); read by 29 cards, 12 powers, 8 relics", "content": "49 rule readers", "sim": "State/CombatHistory.cs: append-only value-struct log with structural sharing between clones; part of the hash"},
    {"name": "Run-state effects inside combat", "game": "DeckVersion mutations (TheScythe, DeprecatedCard, GeneticAlgorithm, Goopy, ThievingHopper, SwipePower); CardPileCmd.RemoveFromDeck in combat (DeprecatedCard.OnPlay, SwipePower); PlayerCmd.GainGold/LoseGold (3 cards, 1 power); max HP; relic/potion procurement; AfterCombatEnd (45) and AfterCombatVictory hooks at combat end", "content": "≈60 files", "sim": "CardInstance.DeckVersionId + hashed RunEffects log on CombatState for the run layer; PlayerState.Gold input"},
    {"name": "Side-turn participants", "game": "creatures starting/ending the turn passed to 8 side-turn hooks (Combat/CombatManager.cs:4914, :5080, :2457, :2967, :2055-2088)", "content": "≈144 listener files read participants (e.g. Kunai, PoisonPower, ShrinkPower)", "sim": "restore the parameter (contract change); remove ShrinkPower.IsTurnParticipant approximation"}]

ownership = [
    {"agent": "choice", "files": ["Combat/Choice/*.cs (new)", "Combat/Loop/CombatSim.cs", "Combat/Loop/LegalActionGenerator.cs", "Sts2Sim.Tests/ChoiceTests.cs", "sim/specs/choice.md"], "frozen_against": ["ActionKind/GameAction additions", "CombatState.Choice runtime field", "CombatLoop.Step/StartCombat", "CardInstance/CardPile read API"], "loc": 1600},
    {"agent": "hooks", "files": ["Combat/Pipeline/AbstractModel.cs", "Combat/Pipeline/Hook.cs", "Combat/Pipeline/ListenerSnapshot.cs", "Combat/Pipeline/HookMask.cs (new)", "Sts2Sim.Tests/HookTests.cs", "sim/specs/hooks.md"], "frozen_against": ["listener container members on CombatState/PlayerState/CardInstance", "ChoiceContext push API"], "loc": 3200},
    {"agent": "state", "files": ["State/*.cs", "State/CombatHistory.cs (new)", "State/RunEffects.cs (new)", "Sts2Sim.Tests/DeterminismTests.cs (append)", "sim/specs/state.md (append-only section)"], "frozen_against": ["RelicModel/PotionModel/OrbModel/EnchantmentModel/AfflictionModel/ModifierModel base stubs"], "loc": 1300},
    {"agent": "pipeline", "files": ["Combat/Pipeline/CreatureCmd.cs", "Combat/Pipeline/PowerCmd.cs", "Combat/Pipeline/AttackCommand.cs", "Combat/Pipeline/EnergyCmd.cs", "Combat/Pipeline/CombatStatus.cs", "Combat/Pipeline/CreatureInternals.cs", "Combat/Pipeline/DamageResult.cs", "Powers/PowerModel.cs", "Powers/MultiplierModifiers.cs"], "frozen_against": ["Hook dispatchers (new names)", "CombatState.Allies", "CombatHistory append API", "DamageVar/BlockVar types"], "loc": 1100},
    {"agent": "cards-kernel", "files": ["Cards/CardDef.cs", "Cards/CardInstance.cs", "Cards/CardEnergyCost.cs", "Cards/DynamicVar.cs", "Cards/DynamicVars/*.cs (new)", "Cards/Enchantments/*.cs (new)", "Cards/Afflictions/*.cs (new)", "Cards/CardFactory.cs (new)", "Cards/Pools/*.cs (new)", "Combat/Pipeline/CardCmd.cs (new, partial class: upgrade/transform/enchant/afflict/keywords)"], "frozen_against": ["Hook.ModifyKeywordsInCombat", "CombatState.CreateCard"], "loc": 2000},
    {"agent": "loop", "files": ["Combat/Loop/CombatLoop.cs", "Combat/Loop/ActionExecutor.cs", "Combat/Loop/CardPileCmd.cs", "Combat/Loop/PlayerCmd.cs", "Combat/Loop/CardCmd.Play.cs (new, partial: AutoPlay, Discard, DiscardAndDraw, Exhaust)", "Combat/Loop/TurnFlow*.cs (new)"], "frozen_against": ["ChoiceContext kinds", "PotionActions entry points", "OrbQueue triggers", "Hook dispatchers"], "loc": 1700},
    {"agent": "relics-potions", "files": ["Relics/RelicModel.cs", "Relics/Defs/* (pilot relics only)", "Potions/PotionModel.cs", "Potions/Defs/* (pilots)", "Combat/Pipeline/RelicCmd.cs", "Combat/Pipeline/PotionCmd.cs", "Combat/Loop/PotionActions.cs"], "frozen_against": ["PlayerState.Relics/PotionSlots", "Hook potion dispatchers"], "loc": 1300},
    {"agent": "character-mechanics", "files": ["Orbs/*.cs", "Orbs/Defs/*.cs (5 orbs)", "Combat/Pipeline/OrbCmd.cs", "Combat/Pipeline/OstyCmd.cs", "Combat/Pipeline/ForgeCmd.cs", "Combat/Pipeline/StarsCmd.cs", "Monsters/Defs/Osty.cs"], "frozen_against": ["PlayerState.OrbQueue/Pets", "CombatState.Allies", "Hook orb/star/forge/summon dispatchers"], "loc": 1400},
    {"agent": "monsters-kernel", "files": ["Monsters/MonsterDef.cs", "Monsters/MonsterLifecycle.cs", "Monsters/MoveStateMachine.cs", "Monsters/Intents.cs", "Monsters/MonsterListener.cs (new)"], "frozen_against": ["MonsterState listener slot", "CreatureCmd.Add"], "loc": 800},
    {"agent": "diff-v2", "files": ["sim/Sts2Sim.Diff/*", "sts2rec exporter (bundle v2, later wave)"], "frozen_against": ["CombatSim choice API", "bundle v2 contract"], "loc": 1500}]

shared_files = {"Combat/Pipeline/Hook.cs": "hooks", "Combat/Pipeline/AbstractModel.cs": "hooks (signature phase 0 coordinated by orchestrator)",
                "State/CombatState.cs": "state", "State/CreatureState.cs": "state", "Combat/Loop/CombatLoop.cs": "loop", "Combat/Loop/CombatSim.cs": "choice",
                "Api/GameAction.cs": "orchestrator (frozen)", "Registry/Registries.cs": "orchestrator (frozen)", "GlobalUsings.cs": "orchestrator (frozen)", "Enums/*": "orchestrator (frozen)",
                "Cards/CardPlay.cs": "orchestrator (frozen)", "sim/CONTRACTS.md": "orchestrator"}

phase0_contract_members = [
    "Api/GameAction.cs: enum ActionKind : byte { PlayCard, EndTurn, UsePotion, DiscardPotion, ChoosePick, ChooseConfirm }; GameAction.UsePotion(slot, target), DiscardPotion(slot), Pick(option), Confirm",
    "CombatState: IReadOnlyList<CreatureState> Allies ([0] = Player, then pets); List<ModifierModel> Modifiers; CombatHistory History; RunEffects RunEffects; ChoiceRuntime Choice; int CardOrPotionEffectDepth; List<int> PlayersTakingExtraTurn; UnlockState Unlocks; AddAlly/RemoveAlly; CreateCard(CardDef)",
    "PlayerState: List<RelicModel> Relics; PotionModel?[] PotionSlots; OrbQueue OrbQueue; List<int> PetIds; int Gold",
    "MonsterState: int PetOwnerId; AbstractModel? Listener",
    "CardInstance: EnchantmentModel? Enchantment; AfflictionModel? Affliction; AbstractModel? Listener; bool HasBeenRemovedFromState; int DeckVersionId",
    "AbstractModel: all 182 hook virtuals (game names/defaults, cs first, ctx dropped); side-turn hooks take IReadOnlyList<CreatureState> participants; ShouldPlay takes AutoPlayType",
    "Hook: all 148 dispatcher names; ref-modifier/out-preventer overloads (ModifyCardPlayResultLocation, ModifyOrbPassiveTriggerCount, ModifyGoldGained added)",
    "ChoiceRuntime: PushContext(ChoiceContextKind, Action? onFirstPause) : IDisposable; PushSelector/PopSelector; IChoiceAnswerer Answerer",
    "CardSelectCmd: FromHand, FromHandForDiscard, FromHandForUpgrade, FromCombatPile, FromChooseACardScreen, FromSimpleGrid (cs first, ambient ctx)",
    "stubs: abstract RelicModel, PotionModel, OrbModel, EnchantmentModel, AfflictionModel, ModifierModel : AbstractModel; sealed OrbQueue, CombatHistory, RunEffects, UnlockState"]

integration_order = [
    "0 contract freeze (orchestrator): ActionKind append, enums, base-class stubs (RelicModel, PotionModel, OrbModel, EnchantmentModel, AfflictionModel, ModifierModel), all 182 hook virtual signatures in AbstractModel incl. participants/ctx changes, registries, GlobalUsings, CombatState/PlayerState member stubs, Choice runtime stubs",
    "1 parallel: hooks, state, pipeline, cards-kernel, monsters-kernel, character-mechanics, relics-potions, choice runtime (CardSelectCmd + contexts) — each with local doubles",
    "2 loop wiring (turn flow, auto-play, potions in Step, deferral orchestration, history recording)",
    "3 choice facade (CombatSim re-execution, LegalActions at prompts, Clone/Hash at prompts)",
    "4 integration: 367 existing tests + new suites green; diff gate 69/69 unchanged; adversarial verification",
    "5 diff-v2 + content waves"]

risks = [
    {"area": "sync rewrite", "risk": "turn-setup prompt deferral: AfterSideTurnStart, orb turn-start triggers and a win check run before the answer of a SetupPlayerTurn prompt; also for auto-resolved/empty selections because FromHand/FromSimpleGrid/FromCombatPile/FromHandForUpgrade pause before counting options", "cite": "Combat/CombatManager.cs:5059-5226; Commands/CardSelectCmd.cs:3375-3378, 3682-3685, 3758-3761", "mitigation": "TurnSetup context with pause callback; diff-gate cases with GamblingChip/Toolbox/ToolsOfTheTrade recordings"},
    {"area": "sync rewrite", "risk": "per-listener deferral dispatchers (Task.WhenAll) reorder a paused listener behind the rest of its pass", "cite": "Hooks/Hook.cs:6098-6265 and 5 others", "mitigation": "assert unreachable (no prompt site in those hooks in v0.111.0)"},
    {"area": "sync rewrite", "risk": "listener snapshots: list built at first MoveNext, liveness at yield; listeners added mid-pass are not visited, removed ones may still be (powers: owner attached); two-pass dispatchers take a fresh snapshot per pass; Run+Combat builds the combat list after the deck listeners ran", "cite": "Combat/CombatState.cs:855-956; Runs/RunState.cs:829-933", "mitigation": "ListenerSnapshot keeps the game build/yield split; per-hook filtering must not change order"},
    {"area": "sync rewrite", "risk": "mid-resolution death/win: guarded listeners go silent once the last primary enemy dies (IsOverOrEnding); moves into combat piles refused while ending; OnPlayWrapper dead-player exit skips result pile and cleanup; monster dying during its own move stays listed until the move ends; win checks only at fixed points", "cite": "Hooks/Hook.cs:8794; Commands/CardPileCmd.cs:3291; Models/CardModel.cs:779-909; Commands/CreatureCmd.cs:2815-2822", "mitigation": "keep existing loop/pipeline rules; add tests for multi-hit/multi-target kills with hooks"},
    {"area": "sync rewrite", "risk": "nested effects: auto-play inside hooks (Mayhem, HistoryCourse, Hellraiser, ImitationLearning, Sly discard) re-enters OnPlayWrapper; card/potion effect depth gates CheckForEmptyHand", "cite": "Commands/CardCmd.cs:284-520, :843-917; Combat/CombatManager.cs:6517-6531", "mitigation": "port effect depth counter; recursion tests"},
    {"area": "sync rewrite", "risk": "hook parameters dropped by P0 are read by content: participants (≈144 files), autoPlayType (Enthralled), deathAnimLength (VFX only)", "cite": "see virtual table", "mitigation": "restore participants and autoPlayType in phase 0"},
    {"area": "sync rewrite", "risk": "fire-and-forget tasks (TaskHelper.RunSafely) are visual-only in combat content (DoActivateVisuals, death-anim reveals); event PunchOff/merchant ones are run layer", "cite": "Models.Relics/*.cs DoActivateVisuals; Models.Powers/StockPower.cs:76", "mitigation": "strip; note in specs"},
    {"area": "rng", "risk": "all combat randomness must use the same stream, call and list order: CombatTargets (random targets, auto-play targets, WhisperingEarring), CombatCardGeneration (TakeRandom/NextItem over ordered, unlock-filtered pools), CombatCardSelection, CombatEnergyCosts, CombatPotionGeneration, CombatOrbGeneration, Niche, Shuffle (random pile insert), monster/encounter private Rng", "cite": "Factories/CardFactory.cs:93-121; Commands/CardCmd.cs AutoPlay; Models.Relics/WhisperingEarring.cs:222-226", "mitigation": "port pool order data + UnlockState; RNG counter in the diff projection"},
    {"area": "rng", "risk": "VFX/cosmetic randomness uses the global Rng.Chaotic (not a run stream): skins, screen shake, dialogue; MonsterModel.Rng returns Rng.Chaotic for canonical models", "cite": "Models/MonsterModel.cs:395-404; 21 Rng.Chaotic sites", "mitigation": "never model Chaotic; never read Rng from a def"},
    {"area": "rng", "risk": "TheArchitect (event) shuffles a character-specific VFX list with the event Rng, so the list length changes later draws", "cite": "Models.Events/TheArchitect.cs:284-285, :1104-1105; Models/CharacterModel.cs:215", "mitigation": "run-layer wave: port GetArchitectAttackVfx list lengths per character as data"},
    {"area": "static state", "risk": "CardSelectCmd selector stacks are static; VakuuCardSelector is pushed during WhisperingEarring's turn-1 auto-play", "cite": "Commands/CardSelectCmd.cs:3084-3150; Models.Relics/WhisperingEarring.cs:86", "mitigation": "per-CombatState selector stack (cloned)"},
    {"area": "static state", "risk": "CardRarityOdds.regularCommonOdds static field initialized from the ascension at type init; YummyCookie._cachedRandomCharacter static", "cite": "Odds/CardRarityOdds.cs:13; Models.Relics/YummyCookie.cs:100", "mitigation": "run layer: compute from run ascension, no statics"},
    {"area": "static state", "risk": "canonical vs mutable models: content reads canonical instances (ModelDb.Card<T>() options before CreateCard, PowerCmd canonicalPower for TryModifyPowerAmountReceived)", "cite": "Combat/CombatState.cs:666-677; Hooks/Hook.cs:10928", "mitigation": "def/instance split keeps canonical reads on defs"},
    {"area": "performance", "risk": "listener count grows from ~10 powers to 100+ (relics, cards in piles); ~50 dispatches per card play", "cite": "", "mitigation": "per-type override masks, cached ordered listener arrays invalidated by a version counter, liveness still lazy"},
    {"area": "decompile", "risk": "state-machine goto order, missing Stun Wrapper body, Unknown-result noise; async continuation scheduling under Godot's synchronization context for paused hooks", "cite": "Commands/CreatureCmd.cs:4105-4124", "mitigation": "open questions; verify pause ordering on recordings"}]

open_questions = [
    "Turn-setup pause: does the game's Godot synchronization context ever resume SetupPlayerTurn before AfterSideTurnStart? The code forces the order (the setup awaits task assignment at HookPlayerChoiceContext.cs:229 until RunAutoPrePlayPhase is assigned at CombatManager.cs:5204-5205), but it must be confirmed on recordings with GamblingChip/Toolbox/ToolsOfTheTradePower.",
    "If the deferred CheckWinCondition (Combat/CombatManager.cs:5226) ends the combat before a turn-setup prompt is answered, is the queued CombatPlayPhaseOnly hook action cancelled (ActionQueueSet.CombatEnded) so the rest of SetupPlayerTurn never runs?",
    "RequireManualConfirmation prompts with fewer options than MinSelect (n < min, min != max): the confirm button stays disabled (Nodes.Combat/NPlayerHand.cs:1270-1281). Does any v0.111.0 site reach this? The sim plans MinSelect' = min(MinSelect, n).",
    "Which pick orders are semantically relevant per site (Ordered flag)? PutBack sites (Glimmer, PhotonCut) are ordered; others need a per-site proof.",
    "Potion legality in combat: exact UI gating (Usage, PassesCustomUsabilityCheck, phase, queue state) in Nodes.Potions; AnyAlly/AnyPlayer targets in single player.",
    "AnyAlly card targets in single player: CardModel.IsValidTarget accepts any same-side creature (Models/CardModel.cs:3380-3383); which creatures does NTargetManager offer (player, Osty)?",
    "UnlockState: card pools filter through epochs (CardPoolModel.FilterThroughEpochs); the run must provide the unlock set for exact card generation. Source of truth in recordings?",
    "CardPileCmd.DrawWithoutBlockingOnOtherPlayers in single player: identical to Draw?",
    "CreatureCmd.Stun Wrapper state machine body is missing in the decompile (pipeline spec §9).",
    "RelicCmd.Melt and in-combat relic obtain paths: can relics be obtained or removed during combat in v0.111.0 content?",
    "Bundle v2 contract: the recorder records only card ids for CombatCard choices; duplicates with hidden differences (temporary costs, enchantments) need disambiguation by post-state.",
    "Selector-path vs UI-path ordering differences (draw pile sort, no pause) mean TestMode/AutoSlay runs are not valid oracles for prompt ordering; only human UI recordings are."]

data = dict(
    meta=dict(generated="2026-11-01", game_version="v0.111.0",
              decompile_root=common.DECOMPILE_ROOT,
              citation_format="<ns-dir>/<File>.cs:<line> with MegaCrit.Sts2.Core. dropped",
              sim_baseline="commit 819d7c6; build 0 errors; 367/367 tests",
              companion_doc="docs/sim-port/design-combat-kernel.md",
              scripts="sim/tools/inventory/classify/kernel/*.py (Python 3 stdlib; order in sim/tools/inventory/classify/README.md)",
              notes=["override counts are per file (a file overriding a hook once counts 1), mocks excluded from totals",
                     "Cmd usage counts are per method name across overloads, models directories only (uses_by_kind keeps core:* callers separately)",
                     "readiness checks only the hook-virtual and Cmd surfaces; listener kinds other than powers are not wired in the sim yet"]),
    counts=dict(content={k: v["files"] for k, v in ready["kind_summary"].items()},
                abstract_model_virtuals=len(vrows), hook_virtuals=len(vrows) - 3, virtuals_ported=sum(r["ported"] for r in vrows),
                virtuals_missing_combat=sum(1 for r in vrows if not r["ported"] and r["relevance"] == "combat"),
                virtuals_missing_run=sum(1 for r in vrows if not r["ported"] and r["relevance"] == "run"),
                dispatchers=len(hooks), dispatchers_ported=sum(h["ported"] for h in hooks),
                cmd_methods=sum(len(r) for r in cmds.values()),
                cmd_status=dict(collections.Counter(r["status"] for rows in cmds.values() for r in rows)),
                in_combat_prompt_sites=sum(1 for s in sites if s["site"] != "AfterObtained"),
                listener_files_overriding_combat_hooks={"Powers": 237, "Relics": 177, "Cards": 23, "Monsters": 12, "Enchantments": 7, "Modifiers": 2, "Potions": 1, "Afflictions": 1, "Singleton": 1, "Badges(non-rule)": 2, "Achievements(non-rule)": 9}),
    hook_virtuals=virt_out,
    model_kind_virtuals={k: dict(files=v["files"], virtuals=[dict(name=x["name"], line=x["line"], kind=x["kind"], returns=x["ret"], params=x["params"], is_property=x["prop"], overrides=x["overrides"]) for x in v["virtuals"]]) for k, v in kindvirt.items()},
    hook_dispatchers=dispatcher_rows(),
    listener_order=listener_order,
    cmd_facades=cmd_out,
    choice_mechanics=choice_mechanics,
    prompt_types=prompt_types,
    prompt_sites=sites,
    design_choice_api=design_choice_api,
    subsystems=subsystems,
    ownership=dict(agents=ownership, shared_files=shared_files, phase0_contract_members=phase0_contract_members, integration_order=integration_order),
    risks=risks,
    open_questions=open_questions,
    content_readiness=dict(kind_summary=ready["kind_summary"], blocking_hooks=ready["blocking_hooks"], blocking_cmds=ready["blocking_cmds"],
                           files={k: {kk: vv for kk, vv in v.items() if kk in ("missing_hooks", "missing_cmds", "features") and vv} for k, v in ready["files"].items() if v["missing_hooks"] or v["missing_cmds"] or v["features"]}))
os.makedirs(os.path.dirname(OUT), exist_ok=True)
with open(OUT, "w", encoding="utf-8") as f:
    json.dump(data, f, indent=1, ensure_ascii=False)
    f.write("\n")
print("wrote", OUT, os.path.getsize(OUT))
