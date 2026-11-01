# cards-2-1 (W3 wave A) — index

Common drops for every entry: TriggerAnim / VFX / SFX / hover tips / ShouldGlowGold / Cmd.Wait. Powers that are not in this tree yet are applied by id through `PowerRegistry.Create` and the non-generic `PowerCmd.Apply`; PowerVar<T> for an unported T is a plain `DynamicVar` with the same name.

- ACCELERANT — Models.Cards/Accelerant.cs:16 — var "Accelerant" 1 (+1); applies ACCELERANT_POWER.
- AFTERIMAGE — Models.Cards/Afterimage.cs:16 — AfterimagePower 1; upgrade adds Innate.
- ANTICIPATE — Models.Cards/Anticipate.cs:16 — var DexterityPower 2 (+2) feeds ANTICIPATE_POWER.
- APPARITION — Models.Cards/Apparition.cs:16 — Ethereal+Exhaust; INTANGIBLE_POWER 1; upgrade removes Ethereal.
- ARSENAL — Models.Cards/Arsenal.cs:16 — ArsenalPower 1; upgrade adds Innate.
- AUTOMATION — Models.Cards/Automation.cs:16 — EnergyVar 1 (IntValue) to AUTOMATION_POWER; upgrade cost -1.
- BARRICADE — Models.Cards/Barricade.cs:15 — BARRICADE_POWER 1; upgrade cost -1.
- BATTLE_TRANCE — Models.Cards/BattleTrance.cs:15 — draw Cards 3 (+1) then NO_DRAW_POWER 1.
- BLIGHT_STRIKE — Models.Cards/BlightStrike.cs:20 — Strike tag; DOOM_POWER = sum of TotalDamage over AttackCommand.Results.
- BLUR — Models.Cards/Blur.cs:16 — block 5 (+3) then BLUR_POWER "Blur" 1.
- BOOST_AWAY — Models.Cards/BoostAway.cs:16 — block 6 (+3), generated DAZED to discard (by id); PreviewCardPileAdd dropped.
- BORROWED_TIME — Models.Cards/BorrowedTime.cs:16 — gain Energy 4 (+2) then BORROWED_TIME_POWER "ExtraCost" 1.
- BOUNCING_FLASK — Models.Cards/BouncingFlask.cs:24 — Repeat 3 (+1) x CombatTargets.NextItem(hittable) then POISON_POWER 3; hittable re-read per iteration.
- BUBBLE_BUBBLE — Models.Cards/BubbleBubble.cs:24 — POISON_POWER 9 (+3) only if target already has Poison.
- BUFFER — Models.Cards/Buffer.cs:15 — BufferPower 1 (+1).
- BULLET_TIME — Models.Cards/BulletTime.cs:15 — non-X hand cards SetToFreeThisTurn, then NO_DRAW_POWER 1; upgrade cost -1.
- BURST — Models.Cards/Burst.cs:15 — var "Skills" 1 (+1) to BURST_POWER.
- CHARGE_BATTERY — Models.Cards/ChargeBattery.cs:17 — block 7 (+3) then ENERGY_NEXT_TURN_POWER Energy 1.
- COLOSSUS — Models.Cards/Colossus.cs:17 — block 4 (+3) then COLOSSUS_POWER "Colossus" 1.
- CORROSIVE_WAVE — Models.Cards/CorrosiveWave.cs:16 — "CorrosiveWave" 2 (+1).
- COUNTDOWN — Models.Cards/Countdown.cs:16 — CountdownPower 6 (+3).
- CRIMSON_MANTLE — Models.Cards/CrimsonMantle.cs:17 — CrimsonMantlePower 7 (+3); generic Apply<T> return path replicated locally, IncrementSelfDamage called by reflection until CrimsonMantlePower exists (throws "W3 gap" if the method is missing).
- CRUELTY — Models.Cards/Cruelty.cs:16 — CrueltyPower 25 (+25).
- CRUSH_UNDER — Models.Cards/CrushUnder.cs:24 — hittable captured before the all-enemy attack 8 (+1); CRUSH_UNDER_POWER "StrengthLoss" 1 (+1) on the captured list.
- DANSE_MACABRE — Models.Cards/DanseMacabre.cs:16 — DanseMacabrePower 4 (+2); EnergyVar 2 kept (text/power threshold).
- DARK_EMBRACE — Models.Cards/DarkEmbrace.cs:15 — DarkEmbracePower 1 (typed, exists); upgrade cost -1.
- DARK_SHACKLES — Models.Cards/DarkShackles.cs:16 — Exhaust, cost 0; DARK_SHACKLES_POWER "StrengthLoss" 9 (+6) on target.
- DEADLY_POISON — Models.Cards/DeadlyPoison.cs:20 — POISON_POWER 5 (+2) on target.
- DEATHBRINGER — Models.Cards/Deathbringer.cs:17 — DOOM_POWER 21 (+5) on hittable, then WeakPower 1 on hittable re-read.
- DEBILITATE — Models.Cards/Debilitate.cs:18 — damage 10 (+2) then DEBILITATE_POWER 2 (+1).
- DELAY — Models.Cards/Delay.cs:17 — block 11 (+2) then ENERGY_NEXT_TURN_POWER Energy.IntValue 1 (+1).
- DEMESNE — Models.Cards/Demesne.cs:16 — Ethereal; DEMESNE_POWER = Cards 1; upgrade cost -1.
- DEMON_FORM — Models.Cards/DemonForm.cs:16 — var StrengthPower 3 (+1) to DEMON_FORM_POWER.
- DODGE_AND_ROLL — Models.Cards/DodgeAndRoll.cs:16 — block 4 (+2); BLOCK_NEXT_TURN_POWER = block actually gained.
- ENFEEBLING_TOUCH — Models.Cards/EnfeeblingTouch.cs:16 — Ethereal; ENFEEBLING_TOUCH_POWER "StrengthLoss" 8 (+3).
- ENVENOM — Models.Cards/Envenom.cs:16 — EnvenomPower 1 (+1).
