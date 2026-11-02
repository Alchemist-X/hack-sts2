from annot_lib import A, P

A("TerritorialPower", 2, ["Byrdonis: owner side turn end gains Amount Strength"],
  req=["HookParam.SideTurnParticipants"],
  batch="monster:Byrdonis", deps=["StrengthPower"])

A("TheBombPower", 2, ["instanced fuse: at owner side turn end (before) Decrement while Amount > 1; at 1 deal Damage (DamageVar 40, Unpowered; SetDamage by the card) to all hittable enemies and remove itself"],
  req=["HookParam.SideTurnParticipants", "CreatureCmd.Damage(targets, DamageVar, dealer) overload", "mutable per-instance DynamicVar Damage (SetDamage)"],
  vd=("cosmetic", "VFX loop restructured; same logic"),
  batch="core")

A("TheGambitPower", 1, ["when the owner takes unblocked powered attack damage: remove itself and the owner dies (Kill)"],
  batch="core")

A("TheHuntPower", 1, ["marker only (TheHunt card adds its extra card reward itself)"],
  notes="No hooks and no readers.",
  batch="core")

A("TheSealedThronePower", 3, ["before each owner card play: gain Amount stars"],
  req=["Stars subsystem", "PlayerCmd.GainStars"],
  batch="stars")

A("ThieveryPower", 3, ["instanced on a thief: Steal() (called by Gremlin Merc) takes min(Amount, gold) gold from the Target player (GoldLossType.Stolen) and accumulates it in its Gold var"],
  req=["PlayerCmd.LoseGold (run gold)", "PlayerState.Gold", "PowerModel.Target", "mutable per-instance GoldVar"],
  batch="run")

A("ThornsPower", 1, ["before the owner takes damage from a dealer via a powered attack (or the Omnislice card): deal Amount back (Unpowered|SkipHurtAnim)"],
  notes="Already ported (sim Powers/Defs/ThornsPower.cs).",
  batch="core")

A("ThunderPower", 3, ["whenever the owner's player evokes a Lightning orb: deal Amount (Unpowered) to the evoke's still-living targets"],
  req=["Hook.AfterOrbEvoked", "OrbQueue / LightningOrb"],
  batch="orbs")

A("ToolsOfTheTradePower", 3, ["owner's player draws +Amount at hand draw", "at the owner's player turn start: choose exactly Amount cards in hand (clipped) to discard"],
  req=["CardSelectCmd.FromHandForDiscard (choice API)", "CardCmd.Discard (list overload)", "turn-setup choice deferral (SetupPlayerTurn context)"],
  prompts=[P("HandSelect", "CardSelectCmd.FromHandForDiscard", "Hand", "Amount", "Amount", "AfterPlayerTurnStart",
             extra={"prompt": "DiscardSelectionPrompt", "followup": "CardCmd.Discard(selected) when non-empty"})],
  batch="prompt")

A("ToricToughnessPower", 2, ["instanced: after the owner's block is cleared gain the Block var (BlockVar, Unpowered; SetBlock by the card), Decrement"],
  req=["CreatureCmd.GainBlock(BlockVar overload)", "mutable per-instance DynamicVar Block (SetBlock)"],
  batch="core")

A("TrackingPower", 2, ["owner's (and owner's pets') powered card attacks on Weak targets deal x(1 + Amount/100)"],
  req=["Creature.Pets (empty unless Necrobinder)"],
  vd=("rule-change", "v0.107.1 multiplied by Amount (Tracking applied 2, then +1); v0.111.0 multiplies by 1 + Amount/100 (Tracking applies 50)"),
  batch="core", deps=["WeakPower"])

A("TrashToTreasurePower", 3, ["whenever the owner's player generates a Status card: channel Amount random orbs"],
  req=["OrbModel.GetRandomOrb", "OrbCmd.Channel", "OrbQueue"],
  rng=["RunRng.CombatOrbGeneration (OrbModel.GetRandomOrb per channel)"],
  vd=("cosmetic", "loop counter hoisted; same logic"),
  batch="orbs")

A("TyrannyPower", 3, ["owner's player draws +Amount at hand draw", "at the owner's player turn start: choose exactly Amount cards in hand (clipped) and exhaust them"],
  req=["CardSelectCmd.FromHand (choice API)", "CardCmd.Exhaust (present)", "turn-setup choice deferral (SetupPlayerTurn context)"],
  prompts=[P("HandSelect", "CardSelectCmd.FromHand", "Hand", "Amount", "Amount", "AfterPlayerTurnStart",
             extra={"prompt": "ExhaustSelectionPrompt", "followup": "CardCmd.Exhaust per selected card, in selection order"})],
  vd=("cosmetic", "foreach became an enumerator state machine; same logic"),
  batch="prompt")

A("UnderworldPower", 2, ["powered attack damage dealt by allies on the owner's side (not the owner, not the owner's pets) applies (total damage x Amount) Doom", "removed at the end of the enemy turn"],
  req=["CreatureState.PetOwner"],
  notes="Single player: no other allies besides the owner's own pets, so it never fires.",
  vd=("new", "not in v0.107.1"),
  batch="doom", deps=["DoomPower"])

A("UnmovablePower", 3, ["the owner's first Amount card/monster-move block gains each turn from its own card plays are doubled (combat history BlockGainedEntry, current play excluded)"],
  req=["CombatHistory: BlockGainedEntry (CardPlay, Props, HappenedThisTurn)", "ValueProp.IsCardOrMonsterMove"],
  vd=("rule-change", "v0.107.1 counted every card/move block entry whose actor was the target; v0.111.0 only entries from the owner's card plays (CardPlay != null)"),
  batch="history")

A("VeilpiercerPower", 1, ["owner's Ethereal cards in hand or play pile cost 0 (Late cost hook)", "Decrement before each such Ethereal card is played"],
  req=["CardInstance current pile type (Hand/Play)", "CardInstance keyword query including in-combat modifiers"],
  batch="core")

A("ViciousPower", 1, ["whenever the owner applies a positive amount of Vulnerable: draw Amount"],
  batch="core", deps=["VulnerablePower"])

A("VigorPower", 1, ["the owner's next powered attack command (non-card or card source) gets +Amount on every hit; after that command Vigor loses the amount it had when the attack started"],
  req=["Hook.BeforeAttack / AfterAttack (present)", "per-instance data: remembered AttackCommand identity + amountWhenAttackStarted"],
  notes="AfterAttack never clears commandToModify: if Vigor survives (gained mid-attack), later card attacks get no bonus because the stale command blocks them. Port this exactly (store a command identity).",
  vd=("cosmetic", "ModifyDamageAdditive gained a CardPlay parameter; body identical"),
  batch="core")

A("VitalSparkPower", 3, ["Infested Prism: before combat start every player Skill gets Tainted (Amount); Skills entering combat later too (no owner check)",
                         "playing a Tainted card applies Amount TaintedPower to its owner; amount changes re-sync every Tainted affliction; removal clears them"],
  req=["Afflictions (Tainted: CardCmd.Afflict<T>, CardCmd.ClearAffliction, AfflictionModel.Amount)", "Hook.BeforeCombatStart (present)", "CombatState.Allies",
       "PlayerCombatState.AllCards"],
  vd=("cosmetic", "nested foreach became enumerator state machines; same logic"),
  batch="afflict", deps=["TaintedPower"])

A("VoidFormPower", 3, ["the owner's first Amount non-autoplay cards each turn (last in series counted) in hand/play cost 0 energy and 0 stars",
                       "being applied or re-stacked sets the counter to 999999999, so it starts working at the owner's next side turn start"],
  req=["Stars subsystem (TryModifyStarCost half)", "HookParam.SideTurnParticipants", "CardPlay.IsAutoPlay / IsLastInSeries", "CardInstance current pile type (Hand/Play)",
       "Hook.BeforePowerAmountChanged (present)", "per-instance data cardsPlayedThisTurn"],
  notes="HideTemporaryZeroCostVisual is named like UI but changes the rule counter. AfterApplied/AfterRemoved only drive a VFX node.",
  vd=("cosmetic", "v0.111.0 adds VFX-only AfterApplied/AfterRemoved; same rule"),
  batch="stars")
