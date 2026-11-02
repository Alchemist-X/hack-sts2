# cards-7-1 (wave A) — Defect cards index

Common drops for every entry: TriggerAnim / WithHitFx / WithAttackerAnim / Cmd.Wait / hover tips (Part A §1).

- BALL_LIGHTNING — Models.Cards/BallLightning.cs:18 — attack then channel Lightning.
- BARRAGE — Models.Cards/Barrage.cs:18 — hit count = CalculatedHits (orb count).
- CHILL — Models.Cards/Chill.cs:17 — one Frost per hittable enemy (snapshot list, as the game's HittableEnemies ToList).
- COLD_SNAP — Models.Cards/ColdSnap.cs:18 — attack then channel Frost.
- COMPILE_DRIVER — Models.Cards/CompileDriver.cs:18 — draw = distinct orb ids.
- COOLHEADED — Models.Cards/Coolheaded.cs:16 — channel Frost, then draw.
- DEFRAGMENT — Models.Cards/Defragment.cs:16 — FocusPower.
- DUALCAST — Models.Cards/Dualcast.cs:14 — EvokeNext(dequeue:false), EvokeNext(); OrbEvokeType.Front dropped (UI-only: NCardPlay/NOrbManager); CustomScaledWait dropped.
- FOCUSED_STRIKE — Models.Cards/FocusedStrike.cs:18 — FOCUSED_STRIKE_POWER by id (other owner).
- FUSION — Models.Cards/Fusion.cs:15 — channel Plasma.
- GLACIER — Models.Cards/Glacier.cs:17 — block, then 2 Frost.
- GLASSWORK — Models.Cards/Glasswork.cs:17 — block, then Glass.
- HOTFIX — Models.Cards/Hotfix.cs:16 — HOTFIX_POWER by id (other owner).
- HYPERBEAM — Models.Cards/Hyperbeam.cs:24 — all-enemy attack, then HYPERBEAM_FOCUS_DOWN_POWER by id; BeforeDamage lambda is VFX + wait only (dropped).
- ICE_LANCE — Models.Cards/IceLance.cs:18 — attack, then Repeat x Frost.
- METEOR_STRIKE — Models.Cards/MeteorStrike.cs:23 — missile VFX wait dropped; attack then 3 Plasma.
- NULL — Models.Cards/Null.cs:19 — attack, Weak, then Dark.
- RAINBOW — Models.Cards/Rainbow.cs:15 — Lightning, Frost, Dark (that order).
- REFRACT — Models.Cards/Refract.cs:18 — 2 hits (OnlyPlayAnimOnce dropped), then Repeat x Glass.
- SHADOW_SHIELD — Models.Cards/ShadowShield.cs:17 — block, then Dark.
- SYNCHRONIZE — Models.Cards/Synchronize.cs:18 — SYNCHRONIZE_POWER by id, amount = distinct orb ids x CalculationExtra.
- TEMPEST — Models.Cards/Tempest.cs:15 — X cost; X (+1 if upgraded) Lightning; no OnUpgrade in the game.
- ZAP — Models.Cards/Zap.cs:15 — channel Lightning.
