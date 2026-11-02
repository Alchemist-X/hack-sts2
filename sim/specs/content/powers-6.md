# powers-6 (W3 wave A) — index

Decompile paths are relative to `MegaCrit.Sts2.Core.` (e.g. `Models.Powers/X.cs`). Common drops for every entry: `Flash()`, hover tips, `Cmd.Wait`, SFX/VFX/anim; async state machines rewritten synchronously in game order. `Owner.Player` maps to `Owner as PlayerState`.

- BIASED_COGNITION_POWER — Models.Powers/BiasedCognitionPower.cs:17 — Focus -Amount applied under a Throwing choice context.
- BLACK_HOLE_POWER — Models.Powers/BlackHolePower.cs:17 — BlockingPlayerChoiceContext -> Inline context; HittableEnemies built from cs.Monsters + CombatStatus.IsHittable.
- CALCIFY_POWER — Models.Powers/CalcifyPower.cs:9 — Osty test via MonsterId == OSTY; pet owner via PetOwnerId.
- CHILD_OF_THE_STARS_POWER — Models.Powers/ChildOfTheStarsPower.cs:13 — none.
- CONQUEROR_POWER — Models.Powers/ConquerorPower.cs:20 — `cardSource is SovereignBlade` -> card id SOVEREIGN_BLADE (dupes included, as the type test).
- CONSUMING_SHADOW_POWER — Models.Powers/ConsumingShadowPower.cs:16 — Wait(0.25) between evokes dropped; orb count checked once, as in the game.
- COOLANT_POWER — Models.Powers/CoolantPower.cs:17 — GroupBy(orb.Id).Count -> distinct orb id set.
- DEVOUR_LIFE_POWER — Models.Powers/DevourLifePower.cs:17 — `Card is Soul` -> card id SOUL.
- FOCUSED_STRIKE_POWER — Models.Powers/FocusedStrikePower.cs:5 — TemporaryFocusPower subclass; origin FOCUSED_STRIKE.
- FURNACE_POWER — Models.Powers/FurnacePower.cs:17 — none.
- GENESIS_POWER — Models.Powers/GenesisPower.cs:12 — none.
- HAILSTORM_POWER — Models.Powers/HailstormPower.cs:20 — DynamicVar FrostOrbs=1 kept as const FrostOrbCount (no power DynamicVars in the kernel).
- HIBERNATE_POWER — Models.Powers/HibernatePower.cs:16 — counter read by FrostOrb (HIBERNATE_POWER id already referenced there).
- HOTFIX_POWER — Models.Powers/HotfixPower.cs:5 — TemporaryFocusPower subclass; origin HOTFIX.
- HYPERBEAM_FOCUS_DOWN_POWER — Models.Powers/HyperbeamFocusDownPower.cs:5 — IsPositive false; origin HYPERBEAM.
- LIGHTNING_ROD_POWER — Models.Powers/LightningRodPower.cs:16 — Channel under Throwing context, then Decrement.
- LOOP_POWER — Models.Powers/LoopPower.cs:14 — Wait(0.25) dropped; Orbs[0] re-read each iteration.
- NECRO_MASTERY_POWER — Models.Powers/NecroMasteryPower.cs:16 — `creature.CombatState` -> rs.Combat.
- PARRY_POWER — Models.Powers/ParryPower.cs:5 — marker counter, no hooks.
- SEEKING_EDGE_POWER — Models.Powers/SeekingEdgePower.cs:7 — marker, no hooks.
- SIC_EM_POWER — Models.Powers/SicEmPower.cs:19 — Applier via GetApplier(cs).
- SPINNER_POWER — Models.Powers/SpinnerPower.cs:16 — Amount re-read each iteration.
- STAR_NEXT_TURN_POWER — Models.Powers/StarNextTurnPower.cs:12 — none.
- STORM_POWER — Models.Powers/StormPower.cs:16 — internal Data dict keyed by card InstanceId (cloned in CopyMutableReferenceState, written to canonical); Dictionary.Add throw semantics kept.
- SUMMON_NEXT_TURN_POWER — Models.Powers/SummonNextTurnPower.cs:16 — none.
- SWORD_SAGE_POWER — Models.Powers/SwordSagePower.cs:13 — needs CardInstance.IsClone (kernel edit added; nobody sets it yet: whoever ports CardModel.CreateClone must set it).
- SYNCHRONIZE_POWER — Models.Powers/SynchronizePower.cs:5 — TemporaryFocusPower subclass; origin SYNCHRONIZE.
- THE_SEALED_THRONE_POWER — Models.Powers/TheSealedThronePower.cs:12 — none.
- THUNDER_POWER — Models.Powers/ThunderPower.cs:18 — TriggerAnim/SFX/VFX dropped; living targets snapshotted before damage.
- TRASH_TO_TREASURE_POWER — Models.Powers/TrashToTreasurePower.cs:14 — rng CombatOrbGeneration via OrbModel.GetRandomOrb.
- VOID_FORM_POWER — Models.Powers/VoidFormPower.cs:15 — NVoidFormVfx (AfterApplied/AfterRemoved bodies) dropped; Data.cardsPlayedThisTurn -> CardsPlayedThisTurn (canonical).
- Skipped (pre-existing W1 pilot files, not stubs): DIE_FOR_YOU_POWER, FOCUS_POWER, TEMPORARY_FOCUS_POWER.
