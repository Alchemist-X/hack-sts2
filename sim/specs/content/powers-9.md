# powers-9 (wave B, monster-coupled powers) — index

Monster coupling: monsters owned by monsters-1..4 / cards-11-monsters-6-8 are not in this tree, so powers reach them through
`Powers/Defs/MonsterPowerCoupling.cs` (runtime lookup by the game's member name on the monster def: a static or instance
method `Name(CombatState, MonsterState[, IReadOnlyList<CreatureState>])`, or a bool property `Name` on the monster's
`MonsterExtraState`). A missing member throws `NotImplementedException("W3 gap: ...")`. Integrator may replace these with
direct calls. Machine states are read by id (`Ai.Machine.Get`). Dropped everywhere: Flash, SFX, TriggerAnim, Cmd.Wait,
InvokeDisplayAmountChanged, music parameters, NCombatRoom/creature-node visuals.

- ADAPTABLE_POWER — Models.Powers/AdaptablePower.cs:13 — TestSubject.TriggerDeadState via coupling; DoRevive() public for the monster.
- ASLEEP_POWER — Models.Powers/AsleepPower.cs:18 — LagavulinMatriarch.IsAwake (extra flag) + WakeUpMove via coupling; stun follow-up SLASH_MOVE.
- BACK_ATTACK_LEFT_POWER — Models.Powers/BackAttackLeftPower.cs:5 — marker.
- BACK_ATTACK_RIGHT_POWER — Models.Powers/BackAttackRightPower.cs:5 — marker.
- BATTLEWORN_DUMMY_TIME_LIMIT_POWER — Models.Powers/BattlewornDummyTimeLimitPower.cs:17 — gap: encounter `RanOutOfTime` flag has no kernel encounter state; recorded as Fidelity ApproximatedRule, escape ported.
- BURROWED_POWER — Models.Powers/BurrowedPower.cs:14 — Tunneler.GetStunned + StillDizzyMove via coupling; follow-up BITE_MOVE; AfterRemoved LoseBlock 999999999.
- CRAB_RAGE_POWER — Models.Powers/CrabRagePower.cs:17 — vars Strength 6, Block 99 Unpowered as consts.
- CURL_UP_POWER — Models.Powers/CurlUpPower.cs:18 — played card stored by InstanceId; LouseProgenitor.Curled via coupling.
- DAMPEN_POWER — Models.Powers/DampenPower.cs:16 — cards stored as (InstanceId, level) in insertion order, casters by CombatId; on removal a remembered card no longer in any combat pile is skipped (game upgrades the object).
- ENRAGE_POWER — Models.Powers/EnragePower.cs:13 — none.
- ESCAPE_ARTIST_POWER — Models.Powers/EscapeArtistPower.cs:16 — StartPulsing dropped.
- FLUTTER_POWER — Models.Powers/FlutterPower.cs:20 — DamageDecrease 50 const; next state = last logged state's GetNextState on cs.Rng.MonsterAi; ThievingHopper.IsHovering via coupling.
- HARDENED_SHELL_POWER — Models.Powers/HardenedShellPower.cs:12 — HpDisplay writes dropped (UI only).
- HARD_TO_KILL_POWER — Models.Powers/HardToKillPower.cs:9 — none.
- HATCH_POWER — Models.Powers/HatchPower.cs:16 — none.
- HIGH_VOLTAGE_POWER — Models.Powers/HighVoltagePower.cs:17 — none.
- ILLUSION_POWER — Models.Powers/IllusionPower.cs:19 — REVIVE_MOVE built at death (static perform reads the power off self); AfterCombatEnd is anim only, dropped.
- IMBALANCED_POWER — Models.Powers/ImbalancedPower.cs:15 — BowlbugRock.IsOffBalance via coupling.
- NEMESIS_POWER — Models.Powers/NemesisPower.cs:16 — none.
- PAINFUL_STABS_POWER — Models.Powers/PainfulStabsPower.cs:20 — results read from AttackCommand.HitResults; per-receiver insertion order kept.
- PAPER_CUTS_POWER — Models.Powers/PaperCutsPower.cs:14 — none.
- PERSONAL_HIVE_POWER — Models.Powers/PersonalHivePower.cs:19 — Dazed by id; PreviewCardPileAdd dropped.
- PLOW_POWER — Models.Powers/PlowPower.cs:18 — CeremonialBeast.SetStunned + StunnedMove via coupling, follow-up BEAST_CRY_MOVE.
- POSSESS_SPEED_POWER — Models.Powers/PossessSpeedPower.cs:15 — dictionary as insertion-ordered (CombatId, amount) list.
- POSSESS_STRENGTH_POWER — Models.Powers/PossessStrengthPower.cs:15 — same as Possess Speed.
- RAMPART_POWER — Models.Powers/RampartPower.cs:19 — TurretOperator matched by monster id.
- REATTACH_POWER — Models.Powers/ReattachPower.cs:22 — DoReattach(cs) public for the segment; DEAD_MOVE read from the machine; ShouldOwnerDeathTriggerFatal() has no cs in the kernel, so it returns a cache refreshed on AfterApplied/AfterPowerAmountChanged/AfterCreatureAddedToCombat/AfterCurrentHpChanged/AfterDeath; DoFadeOutOnAllSegments dropped.
- SANDPIT_POWER — Models.Powers/SandpitPower.cs:25 — UpdateCreaturePositions / music / _initialAmount dropped (visual only); kill loop ported.
- SHRIEK_POWER — Models.Powers/ShriekPower.cs:17 — stun follow-up TERROR_MOVE by id.
- SKITTISH_POWER — Models.Powers/SkittishPower.cs:20 — GetScaledAmountForMultiplayer ported.
- SLOW_POWER — Models.Powers/SlowPower.cs:14 — SlowAmount/DisplayAmount vars as fields.
- SLUMBER_POWER — Models.Powers/SlumberPower.cs:18 — SlumberingBeetle.WakeUpMove via coupling; follow-up ROLL_OUT_MOVE; AfterRemoved is SFX only.
- SOAR_POWER — Models.Powers/SoarPower.cs:10 — none.
- STEAM_ERUPTION_POWER — Models.Powers/SteamEruptionPower.cs:13 — WaterfallGiant.TriggerAboutToBlowState via coupling.
- SUCK_POWER — Models.Powers/SuckPower.cs:18 — mutates HitResults inner lists like the game and resyncs the flat Results list.
- SURROUNDED_POWER — Models.Powers/SurroundedPower.cs:22 — FaceDirection reduced to the Facing write (rest is visual/music).
- TENDER_POWER — Models.Powers/TenderPower.cs:18 — none.
- TERRITORIAL_POWER — Models.Powers/TerritorialPower.cs:17 — none.
- WITHERING_PRESENCE_POWER — Models.Powers/WitheringPresencePower.cs:18 — CardsLeft var as field; hover-tip Wither upgrade matching not ported (display only).
