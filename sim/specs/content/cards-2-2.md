# cards-2-2 (wave A) — port index

Decompile root: `MegaCrit.Sts2.Core.Models.Cards/`. Dropped everywhere: TriggerAnim, VfxCmd/Node VFX, Cmd.Wait / CustomScaledWait, WithHitFx / WithAttackerAnim, CardCmd.PreviewCardPileAdd, ExtraHoverTips, VisualCardPool. Unported powers/cards are reached by id (`PowerRegistry.Create` + `PowerCmd.Apply`, `cs.CreateCard(id)`).

- EQUILIBRIUM — Models.Cards/Equilibrium.cs:17 — GainBlock then RETAIN_HAND_POWER by id.
- ETERNAL_ARMOR — Models.Cards/EternalArmor.cs:16 — PLATING_POWER by id, amount IntValue.
- EXPOSE — Models.Cards/Expose.cs:16 — amount captured before LoseBlock; LoseBlock, Remove ARTIFACT_POWER (if present), Vulnerable.
- FASTEN — Models.Cards/Fasten.cs:17 — FASTEN_POWER by id; hover-tip Defend lookup dropped.
- FEEDING_FRENZY — Models.Cards/FeedingFrenzy.cs:16 — FEEDING_FRENZY_POWER by id from PowerVar<StrengthPower>.
- FEEL_NO_PAIN — Models.Cards/FeelNoPain.cs:16 — FEEL_NO_PAIN_POWER by id.
- FIGHT_THROUGH — Models.Cards/FightThrough.cs:16 — 2x WOUND generated to Discard by id.
- FLAME_BARRIER — Models.Cards/FlameBarrier.cs:20 — GainBlock then FLAME_BARRIER_POWER by id.
- FRANTIC_ESCAPE — Models.Cards/FranticEscape.cs:19 — SANDPIT_POWER matched by Id + TargetId == owner (wave-B power); EnergyCost.AddThisCombat(1).
- FRIENDSHIP — Models.Cards/Friendship.cs:16 — Strength -N first, then FRIENDSHIP_POWER by id.
- GLITTERSTREAM — Models.Cards/Glitterstream.cs:17 — Hook.ModifyBlock on BlockNextTurn computed before GainBlock; BLOCK_NEXT_TURN_POWER by id.
- HANG — Models.Cards/Hang.cs:17 — HANG_POWER amount max(2, current), capped at 999999999 total.
- HAZE — Models.Cards/Haze.cs:22 — POISON_POWER by id per hittable enemy, then Weak over a fresh hittable list.
- HEGEMONY — Models.Cards/Hegemony.cs:18 — ENERGY_NEXT_TURN_POWER by id, Energy IntValue.
- INFERNO — Models.Cards/Inferno.cs:15 — Apply<T> return semantics mirrored locally; IncrementSelfDamage() invoked by reflection on the returned INFERNO_POWER (throws W3 gap if the method is missing).
- JUGGERNAUT — Models.Cards/Juggernaut.cs:16 — JUGGERNAUT_POWER by id.
- MACHINE_LEARNING — Models.Cards/MachineLearning.cs:15 — MACHINE_LEARNING_POWER by id; upgrade adds Innate.
- MANGLE — Models.Cards/Mangle.cs:19 — attack then MANGLE_POWER by id on target.
- MONARCHS_GAZE — Models.Cards/MonarchsGaze.cs:16 — MONARCHS_GAZE_POWER by id; upgrade cost -1.
- NEGATIVE_PULSE — Models.Cards/NegativePulse.cs:19 — GainBlock, then hittable snapshot, DOOM_POWER by id each.
- NEUROSURGE — Models.Cards/Neurosurge.cs:16 — GainEnergy, Draw, NEUROSURGE_POWER by id.
- NOXIOUS_FUMES — Models.Cards/NoxiousFumes.cs:16 — NOXIOUS_FUMES_POWER by id.
- OBLIVION — Models.Cards/Oblivion.cs:16 — OBLIVION_POWER by id on target (Doom var).
- ONE_TWO_PUNCH — Models.Cards/OneTwoPunch.cs:15 — ONE_TWO_PUNCH_POWER by id.
- ORBIT — Models.Cards/Orbit.cs:16 — ORBIT_POWER by id; upgrade cost -1.
- OUTBREAK — Models.Cards/Outbreak.cs:22 — POISON_POWER by id per hittable, then fresh hittable list triggers each Poison via reflection on Trigger(CombatState) (gap until POISON_POWER lands with that signature).
- OUTMANEUVER — Models.Cards/Outmaneuver.cs:17 — ENERGY_NEXT_TURN_POWER by id.
- OVERCLOCK — Models.Cards/Overclock.cs:19 — Draw, then BURN generated to Discard by id.
- PAGESTORM — Models.Cards/Pagestorm.cs:16 — PAGESTORM_POWER by id; upgrade cost -1.
- PANACHE — Models.Cards/Panache.cs:15 — PANACHE_POWER by id.
- PANIC_BUTTON — Models.Cards/PanicButton.cs:16 — GainBlock then NO_BLOCK_POWER by id.
- PATTER — Models.Cards/Patter.cs:17 — GainBlock then VIGOR_POWER by id (IntValue).
- PIERCING_WAIL — Models.Cards/PiercingWail.cs:18 — PIERCING_WAIL_POWER by id per hittable enemy.
- PILLAR_OF_CREATION — Models.Cards/PillarOfCreation.cs:17 — PILLAR_OF_CREATION_POWER by id (BlockVar Unpowered).
- POISONED_STAB — Models.Cards/PoisonedStab.cs:18 — attack then POISON_POWER by id.
- POUNCE — Models.Cards/Pounce.cs:17 — attack then FREE_SKILL_POWER 1 by id.
