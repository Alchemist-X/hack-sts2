from annot_lib import A, P

A("HammerTimePower", 3, ["when the owner's player forges (not from Hammer Time): every other alive player forges the same amount"],
  req=["Hook.AfterForge", "ForgeCmd.Forge"],
  notes="Single player: no other players, so inert.",
  vd=("cosmetic", "closure capture and enumerator state machine; same logic"),
  batch="forge")

A("HangPower", 1, ["damage from the Hang card to the owner is multiplied by Amount (no powered-attack check)"],
  req=["Hang card def (cardSource is Hang)"],
  vd=("cosmetic", "ModifyDamageMultiplicative gained a CardPlay parameter; body identical"),
  batch="core")

A("HardenedShellPower", 2, ["Skulking Colony: caps the owner's HP loss per side turn at Amount (min(amount, Amount - lost this turn), ModifyHpLostBeforeOstyLate)",
                            "tracks unblocked damage received this turn; resets at every side turn start; sets HpDisplay InfiniteWithNumbers once the cap is reached"],
  req=["CreatureState.HpDisplay (rule-relevant: read by HellraiserPower)", "per-instance data damageReceivedThisTurn (decimal)"],
  notes="BeforeSideTurnStart resets without a participants check (both sides' turn starts). ShouldScaleInMultiplayer is multiplayer-only.",
  vd=("cosmetic", "DisplayAmount cast spelled decimal.op_Implicit; same logic"),
  batch="monster:SkulkingColony")

A("HardToKillPower", 1, ["caps each damage instance against the owner at Amount (ModifyDamageCap)"],
  vd=("cosmetic", "ModifyDamageCap gained a CardPlay parameter; decimal.MaxValue written as a literal"),
  batch="monster:Exoskeleton")

A("HatchPower", 2, ["Tough Egg countdown: Decrement at owner side turn end (the monster hatches from it)"],
  req=["HookParam.SideTurnParticipants", "MonsterDef coupling: ToughEgg reads/removes it"],
  batch="monster:ToughEgg")

A("HauntPower", 1, ["whenever the owner plays a Soul card: deal Amount (Unblockable|Unpowered, no dealer) to a random hittable enemy"],
  req=["Soul card def"],
  rng=["RunRng.CombatTargets (NextItem over HittableEnemies)"],
  batch="core")

A("HeistPower", 3, ["instanced loot marker on a thief: when the owner is about to die, add a GoldReward of Amount (stolen back) for the robbed player to the combat room and mark loot returned in map history"],
  req=["PowerModel.Target (robbed creature)", "CombatRoom.AddExtraReward + GoldReward(wasGoldStolenBack) (run layer)", "MapPointHistory MarkLootReturned (run history)",
       "Hook.BeforeDeath (present)"],
  batch="run")

A("HelicalDartPower", 2, ["temporary Dexterity (TemporaryDexterityPower subclass; origin relic HelicalDart)"],
  req=["TemporaryDexterityPower base", "ITemporaryPower"],
  batch="temp", deps=["TemporaryDexterityPower"])

A("HelloWorldPower", 3, ["before the owner's hand draw (if AmountOnTurnStart >= 1): add AmountOnTurnStart distinct random Common cards from the character pool to hand"],
  req=["CardFactory.GetDistinctForCombat", "CardPoolModel.GetUnlockedCards(UnlockState, CardMultiplayerConstraint)", "CardPileCmd.AddGeneratedCardsToCombat (Hand)",
       "PowerModel.AmountOnTurnStart"],
  rng=["RunRng.CombatCardGeneration (GetDistinctForCombat, AmountOnTurnStart cards)"],
  batch="cardgen")

A("HellraiserPower", 2, ["each owner Strike-tagged card drawn is auto-played at once (no target)",
                         "when every hittable enemy shows infinite HP, at most 9 such auto-plays per turn (counter reset at owner side turn end or when the condition breaks)"],
  req=["CardCmd.AutoPlay", "Hook.AfterCardDrawnEarly (present)", "CreatureState.HpDisplay.IsInfinite (set by HardenedShellPower, WaterfallGiant)",
       "HookParam.SideTurnParticipants", "per-instance data: auto-playing card-instance-id set, infinite auto-play counter"],
  rng=["RunRng.CombatTargets (indirect: CardCmd.AutoPlay with no target picks a random hittable enemy for AnyEnemy cards, Commands/CardCmd.cs:411)"],
  notes="BeforeAttack only restyles the attack VFX; ThinkCmd cap message is UI.",
  vd=("cosmetic", "condition polarity inverted by the state machine; same logic"),
  batch="cardops")

A("HexPower", 3, ["Spectral Knight debuff on the player: every player card in combat gets Hexed (Amount) on apply and on entering combat; Hexed cards gain Ethereal",
                  "removed when the applier dies; on removal clears Hexed from all player cards"],
  req=["Afflictions (Hexed: CardCmd.Afflict<T>, CardCmd.ClearAffliction)", "Hook.TryModifyKeywordsInCombat (dispatcher ModifyKeywordsInCombat)", "PlayerCombatState.AllCards",
       "Hook.AfterCardEnteredCombat (present)"],
  vd=("cosmetic", "foreach became an enumerator state machine; same logic"),
  batch="afflict")

A("HibernatePower", 3, ["counter read by FrostOrb; Decrement at the owner's player turn start"],
  req=["OrbQueue / FrostOrb (reads HibernatePower)"],
  vd=("new", "not in v0.107.1"),
  batch="orbs")

A("HighVoltagePower", 2, ["Zapbot: owner side turn end gains Amount Strength"],
  req=["HookParam.SideTurnParticipants"],
  batch="monster:Zapbot", deps=["StrengthPower"])

A("HotfixPower", 3, ["temporary Focus (TemporaryFocusPower subclass; origin card Hotfix)"],
  req=["TemporaryFocusPower base", "ITemporaryPower", "FocusPower (orbs)"],
  batch="orbs", deps=["TemporaryFocusPower"])

A("HyperbeamFocusDownPower", 3, ["temporary Focus loss (TemporaryFocusPower, IsPositive false; origin card Hyperbeam)"],
  req=["TemporaryFocusPower base", "ITemporaryPower", "FocusPower (orbs)"],
  vd=("new", "not in v0.107.1"),
  batch="orbs", deps=["TemporaryFocusPower"])

A("IllusionPower", 2, ["Eye With Teeth / Parafright: applies MinionPower on apply",
                       "on owner death: marks reviving, forces REVIVE_MOVE (heal to full) with the previous move as follow-up, untargetable while reviving, not removed from combat",
                       "global: while it exists, dying creatures keep buffs and temporary debuffs (ShouldPowerBeRemovedOnDeath iterates all listeners)"],
  req=["MonsterModel.SetMoveImmediate + MoveState(FollowUpStateId, MustPerformOnceBeforeTransitioning) + HealIntent", "move StateLog (present)",
       "dead-but-present monster revive flow", "per-instance data isReviving / FollowUpStateId",
       "ITemporaryPower marker (ShouldPowerBeRemovedOnDeath keeps temporary debuffs)"],
  notes="AfterCombatEnd override only plays a death animation (omit). ShouldPowerBeRemovedOnDeath affects every creature's death, not only the owner's.",
  batch="monster:Parafright", deps=["MinionPower"])

A("ImbalancedPower", 1, ["when the owner's attack is fully blocked: Bowlbug Rock becomes off-balance (IsOffBalance), any other owner is stunned"],
  req=["CreatureCmd.Stun(creature) (present)", "MonsterDef coupling: BowlbugRock.IsOffBalance"],
  batch="monster:BowlbugRock")

A("ImitationLearningPower", 2, ["instanced: when the target player plays a Power card (first in series), the owner auto-plays a clone of it; Decrement per copy"],
  req=["CardCmd.AutoPlay", "CardModel.CreateCloneForPlayer", "per-instance data: (card, clone) list"],
  notes="Multiplayer-only (watches another player's cards); throws if applied without PlayerTarget.",
  vd=("new", "not in v0.107.1"),
  batch="cardops")

A("ImprovementPower", 3, ["at combat end: upgrade Amount random upgradable cards of the deck (permanent)"],
  req=["Hook.AfterCombatEnd", "run effects: permanent deck upgrade (CardCmd.Upgrade on deck cards)"],
  rng=["RunRng.CombatCardSelection (NextItem per upgrade over upgradable deck cards)"],
  batch="run")

A("InfernoPower", 1, ["owner's player turn start: owner takes SelfDamage (Unblockable|Unpowered, grows via IncrementSelfDamage)",
                      "whenever the owner loses HP on its own side's turn: deal Amount to all hittable enemies (Unpowered)"],
  req=["mutable per-instance DynamicVar SelfDamage (IncrementSelfDamage called by the Inferno card)"],
  vd=("cosmetic", "conditions inverted by the state machine; same logic"),
  batch="core")
