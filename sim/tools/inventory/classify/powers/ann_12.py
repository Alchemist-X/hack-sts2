from annot_lib import A, P

TEMP_VD = ("rule-change", "v0.107.1 had IgnoreNextInstance (Misery copies skipped the internal stat once); v0.111.0 removed it and Misery now skips the InternallyAppliedPower itself; standalone behaviour unchanged")

A("StormPower", 3, ["after each owner Power card play (snapshot at BeforeCardPlayed): channel Amount Lightning orbs"],
  req=["OrbCmd.Channel<LightningOrb>", "OrbQueue", "per-instance data: card-instance-id -> amount map"],
  vd=("cosmetic", "loop counter hoisted; same logic"),
  batch="orbs")

A("StranglePower", 2, ["per-applier debuff: after each card the applier's player plays (snapshot at BeforeCardPlayed), owner loses Amount HP (Unblockable|Unpowered, no dealer)", "removed at owner side turn end"],
  req=["HookParam.SideTurnParticipants", "PowerInstanceType.InstancedPerApplier", "per-instance data: card-instance-id -> amount map"],
  batch="core")

A("StratagemPower", 3, ["whenever the owner's player shuffles: choose exactly Amount cards from the draw pile (clipped) and put them into hand"],
  req=["CardSelectCmd.FromCombatPile (choice API)", "CardPileCmd.Add (draw -> hand)", "Hook.AfterShuffle (present)",
       "choice context: inline when the shuffle happens inside a card play, turn-setup deferral when it happens in the turn-start draw"],
  prompts=[P("PileSelect", "CardSelectCmd.FromCombatPile", "Draw", "Amount", "Amount", "AfterShuffle",
             extra={"prompt": "SelectionScreenPrompt", "followup": "CardPileCmd.Add(each selected, Hand) in selection order"})],
  vd=("cosmetic", "foreach became an enumerator state machine; same logic"),
  batch="prompt")

A("StrengthPower", 1, ["owner's powered attacks deal +Amount (negative allowed)"],
  notes="Already ported (sim Powers/Defs/StrengthPower.cs).",
  vd=("cosmetic", "ModifyDamageAdditive gained a CardPlay parameter; body identical"),
  batch="core")

A("SubroutinePower", 1, ["after each owner Power card play (snapshot at BeforeCardPlayed): gain 1 energy Amount times"],
  req=["per-instance data: card-instance-id -> amount map"],
  vd=("cosmetic", "loop counter hoisted; same logic"),
  batch="core")

A("SuckPower", 2, ["Fossil Stalker: after its powered attack on the other side, gains Amount Strength per hit that dealt unblocked damage; when a pet was hit, its owner's result in that hit is discarded first"],
  req=["CreatureState.IsPet / PetOwner", "AttackCommand.Results (mutable per-hit lists)"],
  notes="RemoveAll mutates the AttackCommand's own per-hit result lists, so AfterAttack listeners after this one see the trimmed results.",
  vd=("cosmetic", "loop/condition restructuring; same logic"),
  batch="monster:FossilStalker", deps=["StrengthPower"])

A("SummonNextTurnPower", 3, ["at the owner's player turn start (once AmountOnTurnStart != 0): summon Osty for Amount, remove itself"],
  req=["Osty / pets", "OstyCmd.Summon", "PowerModel.AmountOnTurnStart"],
  batch="osty")

A("SurprisePower", 3, ["Gremlin Merc: on death creates a Fat Gremlin (slot fat), gives it one HeistPower per ThieveryPower instance (gold stolen, Target = robbed player), then adds Sneaky Gremlin (slot sneaky) and the Fat Gremlin; marks gold stolen on GremlinMercNormal",
                       "keeps combat from ending"],
  req=["CombatState.CreateCreature (create before adding)", "CreatureCmd.Add<T>(combatState, slot) / CreatureCmd.Add(creature)", "PowerCmd.Apply(power instance with Target set) to a not-yet-added creature",
       "EncounterDef state: GremlinMercNormal.MarkGoldStolen"],
  vd=("rule-change", "v0.107.1 added Sneaky then Fat Gremlin and applied Heist afterwards; v0.111.0 creates the Fat Gremlin first, applies Heist before it joins, then adds Sneaky and Fat (creation/apply order changed)"),
  batch="summon", deps=["HeistPower", "ThieveryPower"])

A("SurroundedPower", 3, ["Kaiser Crab (Crusher/Rocket) debuff on the player: Facing Right/Left; attacks from the monster behind (BackAttackLeft when facing right, BackAttackRight when facing left) deal x1.5",
                         "player turns to face the target of each targeted card or potion; re-faces when only one side's crab remains"],
  req=["Hook.BeforePotionUsed (potions runtime)", "per-instance field Facing (enum Right=0 default)", "BackAttackLeftPower / BackAttackRightPower markers"],
  notes="Body flips, VFX holders and music parameters are UI.",
  vd=("cosmetic", "VFX holder flip added (UI); same rule"),
  batch="monster:CrusherRocket", deps=["BackAttackLeftPower", "BackAttackRightPower"])

A("SwipePower", 3, ["Thieving Hopper: Steal(card) removes the card's deck version from the deck and remembers it; on the thief's death the card returns to the deck and a SpecialCardReward is added"],
  req=["CardPileCmd.RemoveFromDeck (run deck)", "RunState.AddCard + SpecialCardReward + CombatRoom.AddExtraReward (run layer)", "MapPointHistory MarkLootStolen/Returned",
       "CardModel.DeckVersion link", "PowerModel.Target"],
  batch="run")

A("SwordSagePower", 2, ["owner's Sovereign Blades get +Amount replays (BaseReplayCount), including blades entering combat later (not clones); removed amount is subtracted on removal"],
  req=["SovereignBlade card def", "CardInstance.BaseReplayCount (present)", "CardModel.IsClone", "PlayerCombatState.AllCards"],
  vd=("cosmetic", "condition polarity inverted; same logic"),
  batch="forge")

A("SynchronizePower", 3, ["temporary Focus (TemporaryFocusPower subclass; origin card Synchronize)"],
  req=["TemporaryFocusPower base", "ITemporaryPower", "FocusPower (orbs)"],
  batch="orbs", deps=["TemporaryFocusPower"])

A("TagTeamPower", 1, ["instanced debuff: the next Attack aimed at the owner (AnyEnemy on it, or AllEnemies) from a card not owned by the applier is played Amount extra times; then removed"],
  notes="Multiplayer card: in single player every attack card is the applier's, so it never fires.",
  batch="core")

A("TaintedPower", 1, ["powered attacks on the owner deal +Amount", "removed at the end of the enemy turn"],
  vd=("cosmetic", "ModifyDamageAdditive gained a CardPlay parameter; body identical"),
  batch="core")

A("TangledPower", 3, ["Vine Shambler debuff on the player: every player Attack gets Entangled (on apply: all Attacks; later: unafflicted ones entering combat); Entangled cards cost +Amount",
                      "removed at owner side turn end; clears Entangled"],
  req=["Afflictions (Entangled: CardCmd.Afflict<T>, CardCmd.ClearAffliction)", "HookParam.SideTurnParticipants", "PlayerCombatState.AllCards"],
  notes="AfterApplied afflicts every Attack without checking for an existing affliction (AfterCardEnteredCombat does check).",
  vd=("cosmetic", "foreach became an enumerator state machine; same logic"),
  batch="afflict")

A("TankPower", 1, ["owner takes x1.5 (DamageIncrease) from powered attacks", "on apply gives GuardedPower (Amount) to every other alive player"],
  notes="Multiplayer card; in single player only the x1.5 drawback applies.",
  vd=("rule-change", "v0.107.1 multiplied incoming powered damage by 2; v0.111.0 by DamageIncrease 1.5"),
  batch="core", deps=["GuardedPower"])

A("TemporaryDexterityPower", 2, ["abstract base: applying it first applies Sign x amount Dexterity (BeforeApplied), re-stacks apply the delta (AfterPowerAmountChanged); at owner side turn end removes itself and reverts the Dexterity"],
  req=["HookParam.SideTurnParticipants", "ITemporaryPower (OriginModel, InternallyAppliedPower)", "PowerModel.BeforeApplied (present)", "Type computed from IsPositive"],
  notes="Abstract; 4 concrete subclasses (Anticipate, Fade, HelicalDart, SpeedPotion).",
  vd=TEMP_VD,
  batch="temp", deps=["DexterityPower"])

A("TemporaryFocusPower", 3, ["abstract base: temporary Focus with the same apply/revert pattern as TemporaryDexterityPower"],
  req=["HookParam.SideTurnParticipants", "ITemporaryPower", "FocusPower (orbs)", "Type computed from IsPositive"],
  notes="Abstract; 4 concrete subclasses (FocusedStrike, Hotfix, HyperbeamFocusDown, Synchronize).",
  vd=TEMP_VD,
  batch="orbs", deps=["FocusPower"])

A("TemporaryStrengthPower", 2, ["abstract base: temporary Strength with the same apply/revert pattern (BeforeApplied applies, re-stacks apply delta, side turn end removes and reverts)"],
  req=["HookParam.SideTurnParticipants", "ITemporaryPower", "PowerModel.BeforeApplied (present)", "Type computed from IsPositive"],
  notes="Abstract; 15 concrete subclasses incl. the mock MockTemporaryStrengthLossPower.",
  vd=TEMP_VD,
  batch="temp", deps=["StrengthPower"])

A("TenderPower", 2, ["Hunter Killer debuff on the player: each card the player plays costs it 1 Strength and 1 Dexterity this turn (applier = power's applier, silent); restored at owner side turn end"],
  req=["HookParam.SideTurnParticipants", "per-instance field _cardsPlayedThisTurn"],
  batch="monster:HunterKiller", deps=["StrengthPower", "DexterityPower"])
