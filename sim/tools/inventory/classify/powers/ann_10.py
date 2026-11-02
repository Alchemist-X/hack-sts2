from annot_lib import A, P

A("RetainHandPower", 2, ["the owner's player keeps its hand at end of turn (ShouldFlush false)", "Decrement at owner side turn end"],
  req=["HookParam.SideTurnParticipants", "Hook.ShouldFlush (present)"],
  batch="core")

A("RingingPower", 3, ["Ceremonial Beast debuff on the player: every unafflicted player card (on apply and on entering combat) gets Ringing (1)",
                      "Ringing cards cannot be played once the player started any card play this turn (combat history, ShouldPlay)",
                      "removed at owner side turn end; clears Ringing from all player cards"],
  req=["Afflictions (Ringing: CardCmd.Afflict<T>, CardCmd.ClearAffliction)", "CombatHistory: CardPlayStartedEntry by owner's player this turn",
       "HookParam.ShouldPlay.AutoPlayType", "HookParam.SideTurnParticipants", "PlayerCombatState.AllCards"],
  vd=("cosmetic", "predicate uses CardPlay.Player (equivalent in single player)"),
  batch="afflict")

A("RitualPower", 2, ["owner side turn end: gain Amount Strength, except the first turn end after an enemy received it"],
  req=["HookParam.SideTurnParticipants", "per-instance field WasJustAppliedByEnemy"],
  batch="core", deps=["StrengthPower"])

A("RollingBoulderPower", 4, ["instanced: at the owner's player turn start deal Amount (Unpowered) to every hittable enemy, then Amount += 5 (Damage var)"],
  req=["CreatureCmd.Damage (single-target per hit)", "mutable per-instance amount growth via SetAmount"],
  notes="TestMode path: one multi-target Damage over HittableEnemies. Real game (TestMode off): the boulder VFX emits HitCreature per creature as it rolls left to right (Nodes.Vfx/NRollingBoulderVfx.cs), each starting a separate single-target Damage task, awaited together (Task.WhenAll).",
  oq=["RollingBoulderPower: the real-game damage order and interleaving come from VFX signals (creature node x order, concurrently started tasks); decide whether the sim emulates per-creature hits in screen order or the TestMode multi-target call, and verify against recordings with 2+ enemies."],
  vd=("cosmetic", "signal plumbing spelled out (CallDeferred/AddChild); same logic"),
  batch="crosscut")

A("RoyaltiesPower", 3, ["at combat end: add a GoldReward of Amount to the room's extra rewards"],
  req=["Hook.AfterCombatEnd", "CombatRoom.AddExtraReward + GoldReward (run layer rewards)"],
  batch="run")

A("RupturePower", 1, ["on the owner's own turn, unblocked damage to the owner grants Amount Strength: immediately if not from an owner card being played, else after that card play resolves"],
  req=["per-instance data: card-instance-id -> deferred strength map"],
  batch="core", deps=["StrengthPower"])

A("SandpitPower", 2, ["The Insatiable: instanced countdown on its Target player; Decrement at the end of every enemy side turn start (late)",
                      "when removed while both owner and target live, kills the target player and its Osty (Kill force) — the player is eaten"],
  req=["PowerModel.Target (target creature)", "Creature.Pets / Osty", "CreatureCmd.Kill(force: true) (present)", "Hook.AfterSideTurnStartLate (present)"],
  notes="AfterApplied/AfterPowerAmountChanged/AfterCreatureAddedToCombat/AfterOstyRevived/BeforeSideTurnEnd only move nodes or music (TestMode guarded); the AfterOstyRevived override can be dropped.",
  vd=("cosmetic", "tween API spelled differently; same logic"),
  batch="monster:TheInsatiable")

A("SeekingEdgePower", 1, ["marker read by the SovereignBlade card"],
  batch="forge")

A("SelfFormingClayPower", 1, ["after the owner's block is cleared: gain Amount block, remove itself"],
  batch="core")

A("SentryModePower", 2, ["before the owner's hand draw: add Amount Sweeping Gaze cards to hand"],
  req=["CombatState.CreateCard<SweepingGaze> (fixed-card creation)", "CardPileCmd.AddGeneratedCardToCombat (Hand)"],
  vd=("cosmetic", "loop counter hoisted; same logic"),
  batch="cardops")

A("SerpentFormPower", 1, ["after each owner card play (snapshot at BeforeCardPlayed): deal Amount (Unpowered) to a random hittable enemy"],
  rng=["RunRng.CombatTargets (NextItem over HittableEnemies per card play)"],
  notes="AfterApplied/AfterRemoved only drive a VFX node.",
  vd=("cosmetic", "v0.111.0 adds VFX-only overrides; same rule"),
  batch="core")

A("SetupStrikePower", 2, ["temporary Strength (TemporaryStrengthPower subclass; origin card SetupStrike)"],
  req=["TemporaryStrengthPower base", "ITemporaryPower"],
  batch="temp", deps=["TemporaryStrengthPower"])

A("ShacklingPotionPower", 2, ["temporary Strength loss (TemporaryStrengthPower, IsPositive false; origin potion ShacklingPotion)"],
  req=["TemporaryStrengthPower base", "ITemporaryPower"],
  batch="temp", deps=["TemporaryStrengthPower"])

A("ShadowmeldPower", 2, ["all block the owner gains is multiplied by 2^Amount (no value-prop check)", "removed at owner side turn end"],
  req=["HookParam.SideTurnParticipants"],
  notes="(decimal)Math.Pow(2.0, Amount) goes through double.",
  batch="core")

A("ShadowStepPower", 2, ["owner side turn start: gain Amount DoubleDamage, remove itself"],
  req=["HookParam.SideTurnParticipants"],
  batch="core", deps=["DoubleDamagePower"])

A("ShriekPower", 1, ["Terror Eel: when unblocked damage leaves it at or below Amount HP: Stun with next move TerrorState, remove itself"],
  req=["CreatureCmd.Stun(creature, nextMoveId) (present)", "MonsterDef coupling: TerrorEel.TerrorState"],
  notes="AllowNegative debuff; ShouldScaleInMultiplayer is multiplayer-only.",
  batch="monster:TerrorEel")

A("ShrinkPower", 2, ["owner's powered attacks deal x(100-30)/100; Decrement at owner side turn end unless infinite (Amount < 0, StackType Single)", "removed when the applier dies"],
  req=["HookParam.SideTurnParticipants (sim uses IsTurnParticipant approximation)"],
  notes="Already ported (sim Powers/Defs/ShrinkPower.cs); scaling calls are UI.",
  vd=("cosmetic", "ModifyDamageMultiplicative gained a CardPlay parameter; body identical"),
  batch="core")

A("ShroudPower", 1, ["whenever the owner applies Doom: gain Amount block (Unpowered)"],
  batch="doom", deps=["DoomPower"])

A("SicEmPower", 3, ["debuff on an enemy: when Osty (of the applier) damages it, summon Osty for Amount", "removed at owner side turn end"],
  req=["Osty / pets", "OstyCmd.Summon", "HookParam.SideTurnParticipants"],
  batch="osty")

A("SignalBoostPower", 1, ["owner's next Power card is played one extra time (ModifyCardPlayCount +1); Decrement per use"],
  batch="core")
