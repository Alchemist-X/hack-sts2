from annot_lib import A, P

A("NecroMasteryPower", 3, ["whenever the owner's Osty loses HP: deal (HP lost x Amount) to all hittable enemies (Unblockable|Unpowered, dealer owner)"],
  req=["Osty / pets (creature.Monster is Osty, PetOwner)", "Hook.AfterCurrentHpChanged (present)"],
  batch="osty")

A("NemesisPower", 2, ["Test Subject: every owner side turn end toggles Intangible: apply 1 Intangible, next time remove it"],
  req=["HookParam.SideTurnParticipants", "per-instance field _shouldApplyIntangible"],
  batch="monster:TestSubject", deps=["IntangiblePower"])

A("NeurosurgePower", 2, ["owner side turn start: owner gains Amount Doom"],
  req=["HookParam.SideTurnParticipants"],
  notes="Self-applied Doom on a player is killed by DoomPower at AfterSideTurnEnd(Player).",
  batch="doom", deps=["DoomPower"])

A("NightmarePower", 2, ["instanced: before the owner's next hand draw add Amount clones of the remembered card (selected by the Nightmare card, affliction cleared) to hand, then remove itself"],
  req=["CardModel.CreateClone", "CardCmd.ClearAffliction", "CardPileCmd.AddGeneratedCardToCombat (Hand)", "per-instance data: detached card clone (SetSelectedCard)"],
  notes="The choice happens in the Nightmare card (prompt there), not in the power.",
  vd=("cosmetic", "loop counter hoisted; same logic"),
  batch="cardops")

A("NostalgiaPower", 3, ["the owner's first Amount Attack/Skill plays each turn (combat history, current play not yet recorded) go to the top of the draw pile instead of the discard pile"],
  req=["Hook.ModifyCardPlayResultLocation", "Hook.AfterModifyingCardPlayResultLocation", "CardLocation result struct",
       "CombatHistory: CardPlayStartedEntry (Attack/Skill, owner's player, this turn)", "ordering: ModifyCardPlayResultLocation runs before this play's CardPlayStarted entry"],
  vd=("cosmetic", "CardLocation hook refactor; predicate uses CardPlay.Player (equivalent in single player)"),
  batch="history")

A("NoxiousFumesPower", 2, ["owner side turn start: apply Amount Poison to all hittable enemies"],
  req=["HookParam.SideTurnParticipants"],
  vd=("cosmetic", "VFX loop restructured; same logic"),
  batch="core", deps=["PoisonPower"])

A("NoBlockPower", 1, ["owner gains no block from powered card sources (x0 unless Unpowered or no card)", "Decrement at the end of each enemy turn"],
  batch="core")

A("NoDrawPower", 2, ["owner's player cannot draw outside the hand draw (ShouldDraw false when not fromHandDraw)", "removed at owner side turn end"],
  req=["HookParam.SideTurnParticipants", "Hook.ShouldDraw fromHandDraw flag (present)"],
  batch="core")

A("NoEnergyGainPower", 2, ["owner's player energy gains are 0 (ModifyEnergyGain)", "removed at owner side turn end"],
  req=["HookParam.SideTurnParticipants"],
  notes="Unreferenced in v0.111.0 (v0.107.1 ExpectAFight applied it; v0.111.0 ExpectAFight no longer does).",
  oq=["NoEnergyGainPower lost its only applier (ExpectAFight) in v0.111.0; confirm nothing else reaches it."],
  batch="unreferenced")

A("OblivionPower", 1, ["per-applier debuff: each card the applier's player plays (snapshot at BeforeCardPlayed) applies Amount Doom to the owner (applier = the power's applier)",
                       "removed at the end of the player turn"],
  req=["PowerInstanceType.InstancedPerApplier stacking (FindExistingInstanceForStacking)", "per-instance data: card-instance-id -> amount map"],
  batch="doom", deps=["DoomPower"])

A("OneForAllPower", 1, ["owner's powered attacks from cards that cost 0 (EnergySpent 0 when played; non-X) deal +Amount"],
  req=["CardPlay.Resources.EnergySpent", "CardEnergyCost.CostsX / GetWithModifiers(All)"],
  vd=("new", "not in v0.107.1"),
  batch="core")

A("OneTwoPunchPower", 2, ["owner's Attacks are played one extra time (ModifyCardPlayCount +1); Decrement per use", "removed at owner side turn end"],
  req=["HookParam.SideTurnParticipants"],
  batch="core")

A("OrbitPower", 1, ["instanced: every 4 energy the owner spends on cards grants Amount energy (energySpent/4 - triggerCount triggers)"],
  req=["Hook.AfterEnergySpent (present)", "per-instance data energySpent / triggerCount"],
  vd=("cosmetic", "local renamed; same logic"),
  batch="core")

A("PagestormPower", 1, ["each Ethereal card the owner draws: draw Amount"],
  req=["CardInstance keyword query including in-combat keyword modifiers (card.Keywords)"],
  batch="core")

A("PainfulStabsPower", 2, ["Test Subject: after each of its powered attacks on the other side, each player hit for unblocked damage gets Amount x (hits that dealt unblocked damage) Wounds in discard",
                           "owner stays in combat after death; power survives owner death"],
  req=["CardPileCmd.AddToCombatAndPreview<Wound>", "AttackCommand.Results / TargetSide (present)", "Wound card def"],
  vd=("cosmetic", "conditions inverted by the state machine; same logic"),
  batch="monster:TestSubject")

A("PaleBlueDotPower", 3, ["once per turn, when the owner's player has finished 5 card plays this turn (combat history): apply Amount DrawCardsNextTurnPower", "flag resets at owner side turn end"],
  req=["CombatHistory: CardPlayFinishedEntry by owner's player, HappenedThisTurn (current play recorded before AfterCardPlayed)", "HookParam.SideTurnParticipants"],
  notes="AttacksPlayedThisTurn counts every card play, not only Attacks.",
  vd=("rule-change", "v0.107.1 raised the hand draw by Amount if 5+ cards were finished last player turn (ModifyHandDraw); v0.111.0 applies DrawCardsNextTurnPower once 5 plays finish this turn"),
  batch="history", deps=["DrawCardsNextTurnPower"])

A("PanachePower", 2, ["instanced: every 5 owner card plays after the first (CardsLeft countdown) deal Amount to all hittable enemies (Unpowered)", "countdown resets at owner side turn end"],
  req=["HookParam.SideTurnParticipants", "mutable per-instance DynamicVar CardsLeft", "per-instance data alreadyApplied"],
  notes="The play that applied the power only sets alreadyApplied and does not count.",
  vd=("cosmetic", "condition polarity inverted; same logic"),
  batch="core")

A("PaperCutsPower", 1, ["Scroll of Biting: its unblocked powered attacks on a player make that player lose Amount max HP"],
  req=["CreatureCmd.LoseMaxHp (present; the run layer carries max HP out of combat generically)"],
  batch="monster:ScrollOfBiting")

A("ParryPower", 1, ["marker read by the SovereignBlade card"],
  batch="forge")

A("PersonalHivePower", 2, ["Entomancer: when a powered attack hits the owner, shuffle Amount Dazed into the attacker's (or Osty's owner's) draw pile at random positions"],
  req=["CombatState.CreateCard<Dazed>(player) (fixed-card creation)", "CardPileCmd.AddGeneratedCardToCombat (Draw, CardPilePosition.Random)", "Osty redirect: dealer.PetOwner"],
  rng=["RunRng.Shuffle (CardPilePosition.Random insertion: NextInt(drawCount + 1) per Dazed, Commands/CardPileCmd.cs:311)"],
  vd=("cosmetic", "loop counter hoisted; same logic"),
  batch="monster:Entomancer")
