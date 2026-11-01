from annot_lib import A, P

A("BorrowedTimePower", 2, ["owner's cards cost +Amount energy (TryModifyEnergyCostInCombat)", "removed at owner side turn end"],
  req=["HookParam.SideTurnParticipants"],
  batch="core")

A("BufferPower", 1, ["prevents the owner's HP loss entirely (ModifyHpLostAfterOstyLate -> 0)", "Decrement after each prevention (AfterModifyingHpLostAfterOsty)"],
  notes="GetScaledAmountForMultiplayer override is multiplayer-only (PowerCmd scales only when Players.Count > 1); inert in single player.",
  batch="core")

A("BurrowedPower", 1, ["Tunneler: owner's block is not cleared", "when the owner's block is broken: Tunneler.GetStunned, Stun into StillDizzyMove (next BITE_MOVE), remove itself",
                       "on removal the old owner loses all block (LoseBlock 999999999)"],
  req=["CreatureCmd.Stun(creature, stunMove, nextMoveId)", "MonsterDef coupling: Tunneler.GetStunned / StillDizzyMove"],
  vd=("cosmetic", "hook parameter renamed (creature -> target); same logic"),
  batch="monster:Tunneler")

A("BurstPower", 2, ["owner's Skills are played one extra time (ModifyCardPlayCount +1)", "Decrement per modified play; removed at owner side turn end"],
  req=["HookParam.SideTurnParticipants", "card replay loop driven by Hook.ModifyCardPlayCount (present)"],
  batch="core")

A("CacophonyPower", 1, ["instanced countdown: each card drawn decrements its Cards var (33); at 0 picks a random hittable enemy, resets to 33 and deals Amount damage (Unpowered, dealer owner)"],
  req=["mutable per-instance DynamicVar (Cards) as counter state", "PowerInstanceType.Instanced stacking"],
  rng=["RunRng.CombatTargets (NextItem over HittableEnemies, drawn before the damage)"],
  notes="No owner check on the drawn card (any card draw counts; single player = the player's draws).",
  vd=("new", "not in v0.107.1"),
  batch="core")

A("CalamityPower", 3, ["after each owner Attack play (snapshot at BeforeCardPlayed): add Amount random Attack cards from the owner's character pool to hand"],
  req=["CardFactory.GetForCombat", "CardPoolModel.GetUnlockedCards(UnlockState, CardMultiplayerConstraint)", "CardPileCmd.AddGeneratedCardToCombat (Hand)",
       "per-instance data: card-instance-id -> amount map"],
  rng=["RunRng.CombatCardGeneration (CardFactory.GetForCombat, Amount cards)"],
  vd=("cosmetic", "foreach became an enumerator state machine; same logic"),
  batch="cardgen")

A("CalcifyPower", 3, ["Osty's powered attacks deal +Amount damage (owner = Osty's pet owner)"],
  req=["Osty / pets (dealer.Monster is Osty, dealer.PetOwner)"],
  vd=("cosmetic", "ModifyDamageAdditive gained a CardPlay parameter; body identical"),
  batch="osty")

A("CallOfTheVoidPower", 3, ["before the owner's hand draw: create Amount random cards (character pool, not Basic/Ancient) one GetDistinctForCombat(…,1,…) call each, make them Ethereal, add to hand"],
  req=["CardFactory.GetDistinctForCombat", "CardPoolModel.GetUnlockedCards(UnlockState, CardMultiplayerConstraint)", "CardCmd.ApplyKeyword",
       "CardPileCmd.AddGeneratedCardsToCombat (Hand)", "Hook.BeforeHandDraw (present)"],
  rng=["RunRng.CombatCardGeneration (one GetDistinctForCombat draw per card, so duplicates across cards are possible)"],
  vd=("cosmetic", "condition polarity inverted by the state machine; same logic"),
  batch="cardgen")

A("ChainsOfBindingPower", 3, ["Queen debuff on the player: on the player's turn, each drawn card that Bound can afflict gets Bound (Amount) until Amount cards were afflicted by the owner this turn (combat history)",
                              "after one Bound card is played, other Bound cards cannot be played this turn (ShouldPlay)",
                              "owner side turn end: reset and clear Bound from all of the player's combat cards"],
  req=["Afflictions (Bound: CanAfflict, CardCmd.AfflictAndPreview, CardCmd.ClearAffliction)", "CombatHistory: CardAfflictedEntry by owner this turn",
       "HookParam.ShouldPlay.AutoPlayType", "HookParam.SideTurnParticipants", "PlayerCombatState.AllCards", "CardInstance.IsDupe"],
  vd=("cosmetic", "closure capture renamed (CS$<>8__locals); same logic"),
  batch="afflict")

A("ChildOfTheStarsPower", 3, ["whenever the owner's player spends stars: gain Amount x stars-spent block (Unpowered)"],
  req=["Stars subsystem", "Hook.AfterStarsSpent"],
  batch="stars")

A("ClarityPower", 2, ["owner's player draws +1 card at hand draw (ModifyHandDraw)", "Decrement at owner side turn start"],
  req=["HookParam.SideTurnParticipants"],
  notes="The bonus is a flat +1 regardless of Amount; Amount is the number of turns.",
  batch="core")

A("ColossusPower", 1, ["takes x0.5 (DamageDecrease) from powered attacks whose dealer has Vulnerable", "Decrement at the end of every enemy side turn"],
  notes="Ticks on side == Enemy, not on participants.",
  vd=("cosmetic", "ModifyDamageMultiplicative gained a CardPlay parameter; body identical"),
  batch="core", deps=["VulnerablePower"])

A("ConcoctPower", 1, ["when the owner deals unblocked powered attack damage: apply Amount Poison to the target", "removed at the end of the opposing side's turn"],
  vd=("new", "not in v0.107.1"),
  batch="core", deps=["PoisonPower"])

A("ConfusedPower", 1, ["each owner card drawn with canonical cost >= 0 gets a random cost 0-3 for the rest of combat (SetThisCombat)"],
  req=["CardEnergyCost.SetThisCombat (present)", "TestMode seam TestEnergyCostOverride (drop)"],
  rng=["RunRng.CombatEnergyCosts (NextInt(4) per eligible drawn card)"],
  notes="Applied by Snecko Eye relics. X-cost cards (canonical 0) still consume a roll although the cost has no effect on them; canonical -1 (unplayable) cards do not.",
  batch="core")

A("ConquerorPower", 2, ["Sovereign Blade powered attacks against the owner deal x2", "Decrement at owner side turn end"],
  req=["HookParam.SideTurnParticipants", "SovereignBlade card def (cardSource is SovereignBlade)"],
  vd=("cosmetic", "ModifyDamageMultiplicative gained a CardPlay parameter; body identical"),
  batch="forge")

A("ConstrictPower", 2, ["owner side turn end: owner takes Amount damage (Unpowered, dealer owner)", "removed when the applier dies"],
  req=["HookParam.SideTurnParticipants"],
  batch="core")

A("ConsumingShadowPower", 3, ["owner side turn end: if any orbs, evoke the last orb Amount times"],
  req=["OrbQueue", "OrbCmd.EvokeLast", "HookParam.SideTurnParticipants"],
  vd=("cosmetic", "loop counter hoisted into the state machine; same logic"),
  batch="orbs")

A("CoolantPower", 3, ["owner side turn start: gain (number of distinct orb types held) x Amount block"],
  req=["OrbQueue (orb ids)", "HookParam.SideTurnParticipants"],
  batch="orbs")

A("CoordinatePower", 2, ["temporary Strength (TemporaryStrengthPower subclass; origin card Coordinate)"],
  req=["TemporaryStrengthPower base", "ITemporaryPower"],
  batch="temp", deps=["TemporaryStrengthPower"])

A("CorrosiveWavePower", 2, ["each card the owner draws: apply Amount Poison to all hittable enemies", "removed at owner side turn end"],
  req=["HookParam.SideTurnParticipants"],
  vd=("cosmetic", "VFX loop restructured; same logic"),
  batch="core", deps=["PoisonPower"])
