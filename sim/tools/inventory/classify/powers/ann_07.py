from annot_lib import A, P

MOCK_VD = ("cosmetic", "v0.111.0 adds IsMock => true")

A("MockDoNotScaleInMultiplayerPower", 1, ["mock: debuff that opts out of multiplayer scaling"], notes="Test-only mock; not ported.", vd=MOCK_VD, batch="mock")

A("MockExtraTurnPower", 1, ["mock: extra turn for the owner's player; Decrement after each"], notes="Test-only mock; not ported.", vd=MOCK_VD, batch="mock")

A("MockFreeCardsPower", 1, ["mock: every card costs 0 (Late cost hook)"], notes="Test-only mock; not ported.", vd=MOCK_VD, batch="mock")

A("MockGainBlockOnAttackPower", 1, ["mock: owner gains 1 block after each of its Move-prop attacks"], notes="Test-only mock; not ported.", vd=MOCK_VD, batch="mock")

A("MockGatedTurnStartPower", 1, ["mock: test gate that blocks the owner's player turn start on a static TaskCompletionSource"],
  notes="Test seam for turn-start pause ordering (static mutable state); not ported.", vd=("new", "not in v0.107.1"), batch="mock")

A("MockInvincibleOnDeathPower", 1, ["mock: on owner death sets max and current HP to 999999999, keeps combat going, owner stays in combat"],
  notes="Test-only mock; not ported.", vd=MOCK_VD, batch="mock")

A("MockModifyEnergyCostPower", 1, ["mock: every card costs +Amount"], notes="Test-only mock; not ported.", vd=MOCK_VD, batch="mock")

A("MockModifyStarCostPower", 3, ["mock: every star cost +Amount"], req=["Stars subsystem", "TryModifyStarCost (present)"],
  notes="Test-only mock; not ported.", vd=MOCK_VD, batch="mock")

A("MockPhaseObserverPower", 2, ["mock: records (hook, PlayerTurnPhase) for BeforeHandDraw, AfterPlayerTurnStart, auto pre/post play phase, BeforeSideTurnEnd, BeforeFlush, AfterSideTurnEnd"],
  req=["Hook.AfterAutoPrePlayPhaseEntered", "Hook.AfterAutoPostPlayPhaseEntered", "HookParam.SideTurnParticipants"],
  notes="Test seam documenting the turn-phase order (useful reference for the loop port); not ported.", vd=MOCK_VD, batch="mock")

A("MockPreventDeathPower", 1, ["mock: prevents the owner's death once, heals Amount, removes itself"], notes="Test-only mock; not ported.", vd=MOCK_VD, batch="mock")

A("MockRecordCardChangedPilesPower", 2, ["mock: records (old pile, new pile) on every AfterCardChangedPiles"], req=["Hook.AfterCardChangedPiles"],
  notes="Test-only mock (static list); not ported.", vd=("new", "not in v0.107.1"), batch="mock")

A("MockRemoveDrawnCardsFromCombatPower", 1, ["mock: every drawn card is removed from combat"], notes="Test-only mock; not ported.", vd=MOCK_VD, batch="mock")

A("MockResetCombatOnShufflePower", 2, ["mock: the first discard->draw move resets the combat (CombatManager.Reset)"], req=["Hook.AfterCardChangedPiles"],
  notes="Test-only mock; not ported.", vd=MOCK_VD, batch="mock")

A("MockRevivePower", 1, ["mock: prevents owner death, heals 1, then the owner is untargetable, immune to damage and to new powers while reviving"],
  notes="Test-only mock; not ported. Note ShouldAllowHitting returns !IsReviving for every creature.", vd=MOCK_VD, batch="mock")

A("MockScaleInMultiplayerPower", 1, ["mock: configurable multiplayer scaling"], notes="Test-only mock; not ported.", vd=MOCK_VD, batch="mock")

A("MockTemporaryStrengthLossPower", 2, ["mock: temporary Strength loss (origin MockSkillCard)"], req=["TemporaryStrengthPower base"],
  notes="Test-only mock; not ported.", vd=MOCK_VD, batch="mock", deps=["TemporaryStrengthPower"])

A("MockUnhittablePower", 1, ["mock: owner cannot be hit"], notes="Test-only mock; not ported.", vd=MOCK_VD, batch="mock")

A("MonarchsGazePower", 1, ["owner's powered attack damage applies Amount MonarchsGazeStrengthDownPower (temporary Strength loss) to the target"],
  batch="temp", deps=["MonarchsGazeStrengthDownPower"])

A("MonarchsGazeStrengthDownPower", 2, ["temporary Strength loss (TemporaryStrengthPower, IsPositive false; origin card MonarchsGaze)"],
  req=["TemporaryStrengthPower base", "ITemporaryPower"],
  batch="temp", deps=["TemporaryStrengthPower"])

A("MonologuePower", 2, ["instanced, this turn: after each owner card play gain Strength (var, default 1; snapshot at BeforeCardPlayed), tracking StrengthApplied",
                        "at owner side turn end: remove itself, then lose all Strength it applied"],
  req=["HookParam.SideTurnParticipants", "StackType computed from StrengthApplied (None until it applied Strength, then Counter)", "mutable per-instance DynamicVars",
       "per-instance data: card-instance-id -> strength map"],
  notes="Strength is applied silently (silent: true). The power is removed before the negative Strength is applied.",
  batch="core", deps=["StrengthPower"])
