SUBSYSTEMS += [
 {
  "id": "modifiers",
  "name": "Run modifiers (custom and daily runs)",
  "namespaces": ["Models/ModifierModel", "Models.Modifiers"],
  "summary": "16 modifiers (+DeprecatedModifier). ModelDb.GoodModifiers = Draft, SealedDeck, Hoarder, Specialized, Insanity, AllStar, Flight, Vintage, CharacterCards; BadModifiers = DeadlyEvents, CursedRun, BigGameHunter, Midas, Murderous, NightTerrors, Terminal; SealedDeck/Draft/Insanity mutually exclusive. Modifiers are run-level hook listeners (after cards in the combat listener order) plus OnRunCreated/OnRunLoaded and GenerateNeowOption.",
  "rules_dependent": ["Neow branches on RunState.Modifiers.Count (Models.Events/Neow.cs:120, :297, :365)", "combat listener order (Combat/CombatState.cs:915-918)", "RunManager.InitializeNewRun OnRunCreated loop (Runs/RunManager.cs:3843-3850)"],
  "game_refs": ["Models/ModelDb.cs:317-351", "Models/ModifierModel.cs:26-135", "Nodes.Screens.CharacterSelect/NCharacterSelectScreen.cs:966", "Nodes.Screens.CharacterSelect/NCharacterSelectScreen.cs:1794-1799"],
  "decision": "exclude",
  "justification": "A standard run is started with System.Array.Empty<ModifierModel>() and GameMode.Standard (Nodes.Screens.CharacterSelect/NCharacterSelectScreen.cs:966; BeginRun logs an error and ignores non-empty modifiers, :1794-1799). Only Custom and Daily runs carry modifiers, and no content adds a modifier mid-run.",
  "sim_notes": ["Keep RunState.Modifiers as an always-empty list so the Neow Count<=0 branches take the standard path", "The 12 cards with CanBeGeneratedByModifiers => false only matter to modifier generators"]
 },
 {
  "id": "daily",
  "name": "Daily runs",
  "namespaces": ["Daily", "Nodes.Screens.DailyRun"],
  "summary": "Daily seed/character/modifiers are derived in UI code from the server date (Nodes.Screens.DailyRun/NDailyRunScreen.cs:917-922: Rng(GetDeterministicHashCode('dd_MM_yyyy')) then three child Rngs). TimeServer fetches network time. DailyRunUtility only uploads scores and names leaderboards.",
  "rules_dependent": ["GameMode.Daily disables tutorial/discovery-order modifications (Runs/RunManager.cs:4104)", "multiplayer daily lobby uses UnlockState.all (Multiplayer.Game.Lobby/StartRunLobby.cs:1115-1121)"],
  "game_refs": ["Daily/DailyRunUtility.cs:232-269", "Daily/TimeServer.cs", "Nodes.Screens.DailyRun/NDailyRunScreen.cs:917-950"],
  "decision": "exclude",
  "justification": "Not part of standard single-player runs; seed derivation is UI-side and network-time dependent.",
  "sim_notes": []
 },
 {
  "id": "game_mode",
  "name": "GameMode (None/Standard/Daily/Custom)",
  "namespaces": ["Runs/GameMode", "Runs/GameModeExtension"],
  "summary": "Enum stored on RunState. In rule code it only gates RunManager.ShouldApplyTutorialModifications (Standard => apply discovery-order modifications, unless TestMode). AreAchievementsAndEpochsLocked (non-Standard) only affects progress/achievements.",
  "rules_dependent": ["ActModel.ApplyDiscoveryOrderModifications (boss discovery order, first-run room order)"],
  "game_refs": ["Runs/GameMode.cs", "Runs/GameModeExtension.cs:5-8", "Runs/RunManager.cs:4090-4108", "Runs/RunManager.cs:4078-4081"],
  "decision": "port-parameter",
  "justification": "Constant GameMode.Standard for the sim, but its single rule effect (discovery-order modifications) must be ported because it is ON in every standard run.",
  "sim_notes": ["ShouldApplyTutorialModifications returns false under TestMode: the sim must follow the TestMode.IsOff branch"]
 },
 {
  "id": "badges",
  "name": "Badges (end-of-run score badges)",
  "namespaces": ["Models/BadgeModel", "Models.Badges", "Runs/ScoreUtility"],
  "summary": "23 Badge classes evaluated on the finished SerializableRun for score (Models.Badges/BadgePool.cs; Runs/ScoreUtility.cs:127). Two BadgeModel hook listeners (CccComboModel counts cards played per turn, DebufferModel counts debuffs applied) write only Player.ExtraFields.CccomboBadgeUnlocked / DebuffsApplied, which only badges read.",
  "rules_dependent": [],
  "game_refs": ["Models/BadgeModel.cs:5", "Models.Badges/Badge.cs", "Models.Badges/BadgePool.cs", "Models.Badges/CccComboModel.cs", "Models.Badges/DebufferModel.cs", "Combat/CombatState.cs:919-922", "Runs/RunState.cs:652"],
  "decision": "exclude",
  "justification": "No rule reads badge state; BadgeModel listeners return CompletedTask and touch only ExtraFields that are read exclusively by Badge.IsObtained.",
  "sim_notes": ["Excluding them does not shift any other listener (they come after modifiers in the listener list)"]
 },
 {
  "id": "orbs",
  "name": "Orbs (Defect)",
  "namespaces": ["Models/OrbModel", "Models.Orbs", "Entities.Orbs/OrbQueue", "Commands/OrbCmd"],
  "summary": "5 orbs (Lightning 3/8 dmg to random enemy via Rng.CombatTargets; Frost 2/5 block; Dark passive +6 to its evoke (start 6), evoke hits the weakest hittable enemy; Plasma 1 energy at turn start / 2 on evoke; Glass passive 4 decreasing by 1, evoke = 2x passive to all enemies). OrbQueue: capacity (max 10), Channel = add 1 slot for a character with BaseOrbSlotCount 0 and capacity 0, evoke front when full, enqueue, history OrbChanneled, Hook.AfterOrbChanneled. EvokeNext = first orb, EvokeLast = last orb. Lightning/Frost/Glass values and Dark's passive pass through Hook.ModifyOrbValue; Dark's accumulated evoke value and Plasma's values do not. Orbs are hook listeners placed after potions and before cards.",
  "rules_dependent": ["turn hooks: OrbQueue.BeforeTurnEnd (Combat/CombatManager.cs:1198), OrbQueue.AfterTurnStart under HookPlayerChoiceContext (Combat/CombatManager.cs:5119)", "PlayerCombatState: new OrbQueue with capacity Player.BaseOrbSlotCount each combat (Entities.Players/PlayerCombatState.cs:274-276)", "history OrbChanneled (Voltaic)", "random orb: OrbModel.GetRandomOrb(Rng.CombatOrbGeneration) over 5 ids incl. GlassOrb (Chaos, TrashToTreasurePower)"],
  "game_refs": ["Commands/OrbCmd.cs:146-219", "Commands/OrbCmd.cs:647-667", "Commands/OrbCmd.cs:699-745", "Entities.Orbs/OrbQueue.cs:386-461", "Models/OrbModel.cs:197-204", "Models/OrbModel.cs:402-405", "Models/OrbModel.cs:334", "Models/ModelDb.cs:212-218", "Models.Orbs/LightningOrb.cs:336-338", "Models.Orbs/FrostOrb.cs:349-351", "Models.Orbs/DarkOrb.cs:164-214", "Models.Orbs/PlasmaOrb.cs:220-259", "Models.Orbs/GlassOrb.cs:234-283", "Models.Characters/Defect.cs:63"],
  "decision": "port",
  "justification": "Core Defect mechanic; 43 content files call OrbCmd (30 cards, 6 powers, 5 relics, 2 potions) and any character can channel (slot auto-added).",
  "sim_notes": ["ModelDb.Orbs lists 4 orbs (no Glass) but GetRandomOrb draws from 5", "Orb state (DarkOrb _evokeVal, GlassOrb _passiveVal) is per-instance mutable and must be cloned/hashed", "RemoveCapacity evicts from the back (Entities.Orbs/OrbQueue.cs:419-427)"]
 },
 {
  "id": "osty_pets",
  "name": "Osty and pets (Necrobinder summon)",
  "namespaces": ["Commands/OstyCmd", "Commands/PlayerCmd.AddPet", "Models.Monsters/Osty", "Models.Powers/DieForYouPower", "Entities.Players/PlayerCombatState (Pets)"],
  "summary": "Osty is a MonsterModel pet (1 HP, single NOTHING_MOVE) owned by the player (PetOwner). OstyCmd.Summon: amount = Hook.ModifySummonAmount; if Osty alive -> GainMaxHp(amount); else revive (AddPetInternal) or create (PlayerCmd.AddPet<Osty> + DieForYouPower 1), then SetMaxHp(amount), Heal(amount), Hook.AfterOstyRevived on revive; then History.Summoned and Hook.AfterSummon. DieForYouPower redirects powered attacks aimed at the owner to Osty (ModifyUnblockedDamageTarget); overkill flows back to the owner (HpLossHookPhase.BeforeOsty/AfterOsty in CreatureCmd damage). Osty is killed when the player dies.",
  "rules_dependent": ["damage pipeline phases BeforeOsty/AfterOsty (Commands/CreatureCmd.cs:3699-3719)", "TargetType.Osty, CardTag.OstyAttack, OstyDamageVar, CalculatedDamageVar.FromOsty", "history CreatureAttacked with Actor == Owner.Osty (Flatten, Rattle)", "pets are allies but not PlayerCreatures (AnyAlly targeting still sees no living ally in single player)"],
  "game_refs": ["Commands/OstyCmd.cs:336-394", "Commands/PlayerCmd.cs:947-967", "Models.Powers/DieForYouPower.cs:15-49", "Commands/CreatureCmd.cs:2705-2707", "Commands/CreatureCmd.cs:3699-3719", "Entities.Players/Player.cs:247-251", "Entities.Players/PlayerCombatState.cs:39", "Models.Monsters/Osty.cs"],
  "decision": "port",
  "justification": "Core Necrobinder mechanic; OstyCmd used by 15 files, Osty referenced by 24; other pets are spawned by 7 monsters and 4 relics (Byrdpip, PaelsLegion, BoundPhylactery, PhylacteryUnbound).",
  "sim_notes": ["Pets live in PlayerCombatState.Pets and in CombatState allies; PlayerCombatState.AfterCombatEnd clears pets", "Summoned history entries are write-only (no reader)"]
 },
 {
  "id": "stars",
  "name": "Stars (Regent resource)",
  "namespaces": ["Entities.Players/PlayerCombatState.Stars", "Commands/PlayerCmd (GainStars/LoseStars/SetStars)", "Models/CardModel star cost"],
  "summary": "Integer per-combat resource (starts at 0 in each new PlayerCombatState). Setter records History.StarsModified(delta). Cards carry CanonicalStarCost (-1 = none), BaseStarCost, TemporaryStarCost, HasStarCostX and LastStarsSpent; Hook.ModifyStarCost adjusts. HasEnoughResourcesFor: if energy cost exceeds energy and Hook.ShouldPayExcessEnergyCostWithStars, each missing energy costs 2 stars. GainStars is gated by Hook.ShouldGainStars and followed by Hook.AfterStarsGained; spending fires Hook.AfterStarsSpent. Lose/Gain clamp at 0.",
  "rules_dependent": ["card playability (UnplayableReason.StarCostTooHigh)", "history StarsModified (Radiate sums positive deltas this turn)", "X-cost from stars (Hook.ModifyXValue(LastStarsSpent), Models/CardModel.cs:2772)"],
  "game_refs": ["Entities.Players/PlayerCombatState.cs:138-154", "Entities.Players/PlayerCombatState.cs:334-373", "Commands/PlayerCmd.cs:844-878", "Commands/PlayerCmd.cs:390-393", "Hooks/Hook.cs:9963", "Hooks/Hook.cs:9978", "Hooks/Hook.cs:11071", "Hooks/Hook.cs:11512", "Hooks/Hook.cs:11533", "Models/CardModel.cs:1848-1904", "Models/CardModel.cs:2914-2919"],
  "decision": "port",
  "justification": "44 content files (32 cards, 4 powers, 7 relics, 1 potion); the sim's PlayerState already has a Stars field.",
  "sim_notes": ["PlayerCmd.LoseStars is a no-op once the combat is ending (Commands/PlayerCmd.cs:857-866)"]
 },
 {
  "id": "forge",
  "name": "Forge (Regent Sovereign Blade)",
  "namespaces": ["Commands/ForgeCmd", "Models.Cards/SovereignBlade"],
  "summary": "ForgeCmd.Forge(amount): no-op if combat over/ending; if the player has no non-dupe SovereignBlade outside the exhaust pile, create one (CreatedThroughForge = true) and add it to hand via AddGeneratedCardToCombat; then every non-dupe SovereignBlade including exhausted ones gets AddDamage(amount) and AfterForged; then Hook.AfterForge.",
  "rules_dependent": ["Hook.AfterForge listeners (FurnacePower, HammerTimePower, FencingManual...)", "SovereignBlade damage var and CreatedThroughForge reset (Models.Cards/SovereignBlade.cs:263-359)"],
  "game_refs": ["Commands/ForgeCmd.cs:153-171", "Commands/ForgeCmd.cs:174-209", "Models.Cards/SovereignBlade.cs:263-359"],
  "decision": "port",
  "justification": "15 files call ForgeCmd.Forge (11 cards, 2 powers, 1 relic, 1 potion); 19 reference SovereignBlade.",
  "sim_notes": ["The VFX/preview half (PreviewSovereignBlade, PlayCombatRoomForgeVfx) has no rule effect"]
 },
 {
  "id": "ancients",
  "name": "Ancient events and dialogue",
  "namespaces": ["Models/AncientEventModel", "Entities.Ancients", "Models.Events (Neow, Darv, Orobas, Pael, Tezcatara, Nonupeipe, Tanx, Vakuu, TheArchitect)"],
  "summary": "AncientEventModel subclasses: Neow (acts Overgrowth/Underdocks), Orobas/Pael/Tezcatara (Hive), Nonupeipe/Tanx/Vakuu (Glory), Darv (shared). On enter: heal missing HP (x0.8 at WearyTraveler); Neow first sets HP to 0, so Neow heals to MaxHp (x0.8). Options come from GenerateInitialOptions unless Hook.ShouldAllowAncient blocks (then a single PROCEED). Dialogue (Entities.Ancients) is picked in UI code with Rng.Chaotic from profile visit counts and has no rule effect. TheArchitect is a separate EventModel entered after the last act (victory); its dialogue/VFX consume the event Rng but nothing follows.",
  "rules_dependent": ["ancient selection per act: ActModel.GenerateRooms picks _rooms.Ancient from GetUnlockedAncients + shared subset (Models/ActModel.cs:322)", "shared ancient subset distribution with Rng.UpFront (Runs/RunManager.cs:4055-4072)"],
  "game_refs": ["Models/AncientEventModel.cs:78-105", "Models/AncientEventModel.cs:246", "Models/AncientEventModel.cs:275-289", "Nodes.Rooms/NEventRoom.cs:208-213", "Entities.Ancients/AncientDialogueSet.cs", "Entities.Ancients/ArchitectAttackers.cs", "Models.Events/TheArchitect.cs:285-286", "Models.Events/TheArchitect.cs:1042-1048", "Runs/RunManager.cs:872", "Models.Monsters/Architect.cs:13-20"],
  "decision": "port",
  "justification": "Ancients are on every act's map (MapPointType.Ancient) and grant relics/upgrades; dialogue and TheArchitect are excluded (no rule effect / post-victory terminal state).",
  "sim_notes": ["Treat entering TheArchitect event as run victory (terminal)", "DebugOption replaces the first option only in debug (Models/AncientEventModel.cs:280-284)"]
 },
]
