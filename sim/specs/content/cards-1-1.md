# W3 cards-1-1 (wave A) index

Decompile paths are relative to `MegaCrit.Sts2.Core.Models.Cards/`. Dropped everywhere: VFX/SFX/anim/hover tips/asset paths.

- ADRENALINE — Adrenaline.cs:16 — dropped fullscreen VFX (LocalContext.IsMe)
- ALL_FOR_ONE — AllForOne.cs:18 — Filter uses GetWithModifiers(cs, card, CostModifiers.All); type in Attack/Skill/Power
- ANOINTED — Anointed.cs:15 — TakeRandom over Rare draw-pile cards on CombatCardSelection; AfterCardChangedPiles dispatch is a kernel concern
- ASSASSINATE — Assassinate.cs:18 — none
- BACKFLIP — Backflip.cs:15 — none
- BACKSTAB — Backstab.cs:16 — none
- BAD_LUCK — BadLuck.cs:16 — dropped Cmd.Wait(0.25)
- BEAM_CELL — BeamCell.cs:18 — none
- BECKON — Beckon.cs:16 — no OnPlay in game (playable no-op)
- BLOODLETTING — Bloodletting.cs:17 — dropped TriggerAnim; order self-damage then energy
- BLOOD_WALL — BloodWall.cs:17 — dropped TriggerAnim; order self-damage then block
- BLUDGEON — Bludgeon.cs:17 — dropped heavy attacker anim
- BOOT_SEQUENCE — BootSequence.cs:15 — none
- BREAK — Break.cs:19 — none
- BREAKTHROUGH — Breakthrough.cs:17 — order self-damage then all-enemy attack
- BRIGHTEST_FLAME — BrightestFlame.cs:15 — order energy, draw, LoseMaxHp(isFromCard); run-layer max HP persistence is the run layer's job
- BURN — Burn.cs:20 — dropped ground-fire VFX
- BURY — Bury.cs:16 — none
- BYRD_SWOOP — ByrdSwoop.cs:17 — dropped Byrdpip attacker anim (pet lookup is visual only)
- CALTROPS — Caltrops.cs:17 — dropped TriggerAnim, VisualCardPool
- CELESTIAL_MIGHT — CelestialMight.cs:16 — none
- CINDER — Cinder.cs:21 — random hand card via CombatCardSelection.NextItem after the attack
- CLASH — Clash.cs:18 — IsPlayable ported; ShouldGlowGold and VisualCardPool dropped
- CLUMSY — Clumsy.cs:6 — none
- CONFLAGRATION — Conflagration.cs:22 — dropped per-enemy VFX loop
- CURSE_OF_THE_BELL — CurseOfTheBell.cs:6 — none
- DAGGER_SPRAY — DaggerSpray.cs:22 — dropped WithAttackerFx and VFX-only BeforeDamage
- DASH — Dash.cs:16 — order block then attack
- DAZED — Dazed.cs:6 — none
- DEBRIS — Debris.cs:8 — none
- DECAY — Decay.cs:16 — none
- DEFEND_DEFECT — DefendDefect.cs:15 — none
- DEFEND_NECROBINDER — DefendNecrobinder.cs:15 — none
- DEFEND_REGENT — DefendRegent.cs:15 — none
- DEFEND_SILENT — DefendSilent.cs:15 — none
