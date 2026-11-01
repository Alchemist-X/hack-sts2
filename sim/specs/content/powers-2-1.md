# powers-2-1 (W3 wave A) — port index

Files: `sim/Sts2Sim.Core/Powers/Defs/<Class>.cs`. Decompile paths are relative to `MegaCrit.Sts2.Core.`.

- ANTICIPATE_POWER — Models.Powers/AnticipatePower.cs:5 — TemporaryDexterityPower subclass; OriginModel Card Anticipate -> OriginModelId "ANTICIPATE"; Title/HoverTips dropped.
- ARTIFACT_POWER — Models.Powers/ArtifactPower.cs:15 — needs PowerModel.IsVisible (kernel edit; single-player: `Target == null || IsMe(Target) || Target.IsEnemy` always true, so IsVisible == IsVisibleInternal); multiplayer scaling ported as override.
- BLUR_POWER — Models.Powers/BlurPower.cs:16 — AfterPreventingBlockClear is Flash-only, dropped.
- BORROWED_TIME_POWER — Models.Powers/BorrowedTimePower.cs:17 — none.
- BURST_POWER — Models.Powers/BurstPower.cs:17 — Flash dropped.
- CLARITY_POWER — Models.Powers/ClarityPower.cs:16 — `Owner.Player` mapped to `Owner as PlayerState`.
- CONSTRICT_POWER — Models.Powers/ConstrictPower.cs:17 — Applier read via GetApplier(cs); deathAnimLength param dropped.
- CORROSIVE_WAVE_POWER — Models.Powers/CorrosiveWavePower.cs:23 — gaseous VFX loop dropped; Poison applied by id (POISON_POWER) per hittable enemy, in Monsters order.
- COUNTDOWN_POWER — Models.Powers/CountdownPower.cs:17 — target = cs.Rng.CombatTargets.NextItem(hittable enemies); Throwing context pushed for ThrowingPlayerChoiceContext.
- CRUSH_UNDER_POWER — Models.Powers/CrushUnderPower.cs:5 — TemporaryStrengthPower, IsPositive false, origin "CRUSH_UNDER".
- DARK_EMBRACE_POWER — Models.Powers/DarkEmbracePower.cs:18 — W1 pilot already ported; untouched.
- DARK_SHACKLES_POWER — Models.Powers/DarkShacklesPower.cs:5 — TemporaryStrengthPower, IsPositive false, origin "DARK_SHACKLES".
- DEBILITATE_POWER — Models.Powers/DebilitatePower.cs:18 — implements IVulnerableMultiplierModifier / IWeakMultiplierModifier (consulted by Vulnerable/Weak through MultiplierModifierIds.DebilitatePower).
- DEMISE_POWER — Models.Powers/DemisePower.cs:17 — Flash dropped.
- DEMON_FORM_POWER — Models.Powers/DemonFormPower.cs:20 — NDemonFormVfx AfterApplied/AfterRemoved (VFX only) dropped.
- DISINTEGRATION_POWER — Models.Powers/DisintegrationPower.cs:17 — VfxCmd dropped; hook is AfterSideTurnEndLate.
- DOOM_POWER — Models.Powers/DoomPower.cs:25 — enemies die at BeforeSideTurnEnd, player side at AfterSideTurnEnd (v0.111.0); PlayVfx/StartDoomAnim (incl. UI-only OnDieToDoom, ShouldDie peek, waits) dropped; DoomKill/GetDoomedCreatures public static.
- DOUBLE_DAMAGE_POWER — Models.Powers/DoubleDamagePower.cs:18 — `Owner.Pets.Contains(dealer)` mapped to PetOwnerOf(dealer) == Owner.
- DRAW_CARDS_NEXT_TURN_POWER — Models.Powers/DrawCardsNextTurnPower.cs:16 — none.
- DUPLICATION_POWER — Models.Powers/DuplicationPower.cs:16 — Flash dropped.
- DYING_STAR_POWER — Models.Powers/DyingStarPower.cs:5 — TemporaryStrengthPower, IsPositive false, origin "DYING_STAR".
- ENFEEBLING_TOUCH_POWER — Models.Powers/EnfeeblingTouchPower.cs:5 — TemporaryStrengthPower, IsPositive false, origin "ENFEEBLING_TOUCH".
- ENVENOM_POWER — Models.Powers/EnvenomPower.cs:16 — Poison applied by id (POISON_POWER).
- FEEDING_FRENZY_POWER — Models.Powers/FeedingFrenzyPower.cs:5 — TemporaryStrengthPower, origin "FEEDING_FRENZY".
- FLEX_POTION_POWER — Models.Powers/FlexPotionPower.cs:5 — TemporaryStrengthPower, origin potion "FLEX_POTION".
- HELICAL_DART_POWER — Models.Powers/HelicalDartPower.cs:5 — TemporaryDexterityPower, origin relic "HELICAL_DART".
- MANGLE_POWER — Models.Powers/ManglePower.cs:5 — TemporaryStrengthPower, IsPositive false, origin "MANGLE".
- MONARCHS_GAZE_POWER — Models.Powers/MonarchsGazePower.cs:14 — none.
- MONARCHS_GAZE_STRENGTH_DOWN_POWER — Models.Powers/MonarchsGazeStrengthDownPower.cs:5 — TemporaryStrengthPower, IsPositive false, origin card "MONARCHS_GAZE".
- MONOLOGUE_POWER — Models.Powers/MonologuePower.cs:19 — DynamicVars Strength (PowerVar 1, set by the Monologue card after Apply: `MonologuePower.Strength`) and StrengthApplied are instance properties; Data dict keyed by CardInstance.InstanceId; DisplayAmount/InvokeDisplayAmountChanged/Flash dropped.
- NEUROSURGE_POWER — Models.Powers/NeurosurgePower.cs:17 — Throwing context pushed.
