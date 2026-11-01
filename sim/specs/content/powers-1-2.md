# powers-1-2 (W3 wave A) — index

Common drops: Flash(), VFX/SFX, hover tips, DisplayAmount/InvokeDisplayAmountChanged, ShouldPlayVfx. Card-keyed internal Data dictionaries are keyed by CardInstance.InstanceId (clone-safe) and written to the canonical form sorted by id.

- INTANGIBLE_POWER — Models.Powers/IntangiblePower.cs:17 — AfterModifyingHpLostAfterOsty / AfterModifyingDamageAmount are Flash-only, dropped; CombatManager.IsInProgress read as rs.Combat.IsInProgress.
- JUGGERNAUT_POWER — Models.Powers/JuggernautPower.cs:15 — Owner.Player.RunState.Rng.CombatTargets = cs.Rng.CombatTargets.
- MACHINE_LEARNING_POWER — Models.Powers/MachineLearningPower.cs:6 — none.
- MIND_ROT_POWER — Models.Powers/MindRotPower.cs:8 — AfterModifyingHandDraw Flash-only, dropped.
- MINION_POWER — Models.Powers/MinionPower.cs:5 — ShouldPlayVfx dropped.
- NO_BLOCK_POWER — Models.Powers/NoBlockPower.cs:18 — none.
- ORBIT_POWER — Models.Powers/OrbitPower.cs:14 — EnergyVar(4) -> const EnergyIncrement; Data -> EnergySpent/TriggerCount fields; DisplayAmount dropped.
- PAGESTORM_POWER — Models.Powers/PagestormPower.cs:15 — Keywords (all sources) via CardPileCmd.HasKeywordInCombat.
- PILLAR_OF_CREATION_POWER — Models.Powers/PillarOfCreationPower.cs:15 — none.
- PYRE_POWER — Models.Powers/PyrePower.cs:8 — none.
- RADIANCE_POWER — Models.Powers/RadiancePower.cs:15 — EnergyVar(1) -> const Energy.
- RUPTURE_POWER — Models.Powers/RupturePower.cs:17 — playedCards keyed by InstanceId; Dictionary.Add throw-on-duplicate kept.
- SELF_FORMING_CLAY_POWER — Models.Powers/SelfFormingClayPower.cs:15 — none.
- SERPENT_FORM_POWER — Models.Powers/SerpentFormPower.cs:19 — NSerpentFormVfx (AfterApplied/AfterRemoved bodies are VFX-only) and Cmd.CustomScaledWait dropped; NextItem still drawn on an empty hittable list (Rng returns default without a draw).
- SIGNAL_BOOST_POWER — Models.Powers/SignalBoostPower.cs:13 — none.
- SLIPPERY_POWER — Models.Powers/SlipperyPower.cs:17 — GetScaledAmountForMultiplayer ported (amount x player count), reached only in multiplayer.
- SMOKESTACK_POWER — Models.Powers/SmokestackPower.cs:17 — none.
- SPEEDSTER_POWER — Models.Powers/SpeedsterPower.cs:15 — VfxCmd/SfxCmd dropped.
- SPIRIT_OF_ASH_POWER — Models.Powers/SpiritOfAshPower.cs:15 — none.
- SUBROUTINE_POWER — Models.Powers/SubroutinePower.cs:14 — energy granted as N separate GainEnergy(1) calls, as in the game.
- TAINTED_POWER — Models.Powers/TaintedPower.cs:17 — none.
- THE_GAMBIT_POWER — Models.Powers/TheGambitPower.cs:14 — Remove then Kill(owner), owner captured before Remove.
- THE_HUNT_POWER — Models.Powers/TheHuntPower.cs:5 — marker power, no hooks.
- VEILPIERCER_POWER — Models.Powers/VeilpiercerPower.cs:14 — card.Pile?.Type read via GetCombatPile (null pile -> false).
- VICIOUS_POWER — Models.Powers/ViciousPower.cs:15 — none.
- VIGOR_POWER — Models.Powers/VigorPower.cs:16 — `ModelSource is not CardModel` = AttackCommand.IsFromMonster (the sim's only non-card source); commandToModify never reset, as in the game; canonical writes presence + AmountWhenAttackStarted.
- WASTE_AWAY_POWER — Models.Powers/WasteAwayPower.cs:8 — none.
- WELL_LAID_PLANS_POWER — Models.Powers/WellLaidPlansPower.cs:6 — v0.111.0 Single/ShouldFlush form (v0.107 Counter prompt not ported).
