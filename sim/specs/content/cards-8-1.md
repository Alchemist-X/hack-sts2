# cards-8-1 (wave A) index

All: TriggerAnim / hover tips / VFX / Cmd.Wait dropped; `OrbEvokeType` override dropped (hover-tip only, CardModel.cs:2314). Powers that other owners port are applied by id via `PowerRegistry.Create`, and their DynamicVar is a plain `DynamicVar` with the game's name (`PowerVar<T>` would need the type).

- BULK_UP — Models.Cards/BulkUp.cs:16 — RemoveSlots, then Strength, then Dexterity
- CAPACITOR — Models.Cards/Capacitor.cs:14 — AddSlots(Repeat)
- CHAOS — Models.Cards/Chaos.cs:15 — GetRandomOrb(CombatOrbGeneration) per Repeat; ToMutable implicit (registry creates a fresh orb)
- CONSUMING_SHADOW — Models.Cards/ConsumingShadow.cs:17 — Channel Dark xRepeat, then CONSUMING_SHADOW_POWER by id
- COOLANT — Models.Cards/Coolant.cs:16 — COOLANT_POWER by id
- DARKNESS — Models.Cards/Darkness.cs:17 — live Where over the orb queue after channel; Passive 1x/2x (upgraded) per Dark orb
- HAILSTORM — Models.Cards/Hailstorm.cs:17 — HAILSTORM_POWER by id
- LIGHTNING_ROD — Models.Cards/LightningRod.cs:18 — GainBlock(BlockVar) then LIGHTNING_ROD_POWER by id
- LOOP — Models.Cards/Loop.cs:15 — LOOP_POWER by id, var name "Loop"
- MODDED — Models.Cards/Modded.cs:14 — AddSlots, Draw, then EnergyCost.AddThisCombat(1)
- MULTI_CAST — Models.Cards/MultiCast.cs:12 — X cost (+1 upgraded); EvokeNext dequeues only on the last one
- QUADCAST — Models.Cards/Quadcast.cs:14 — returns early with no orbs; same evoke pattern with Repeat 4; upgrade cost -1
- SHATTER — Models.Cards/Shatter.cs:17 — AoE 7 (+4); for each orb counted up front: EvokeNext(dequeue:false), then EvokeNext(); Exhaust (v107 rule change)
- SPINNER — Models.Cards/Spinner.cs:17 — upgraded channels Glass inside a Throwing choice context (ThrowingPlayerChoiceContext); SPINNER_POWER by id; no OnUpgrade
- STORM — Models.Cards/Storm.cs:17 — STORM_POWER by id
- TESLA_COIL — Models.Cards/TeslaCoil.cs:20 — attack, then a ToList snapshot of Lightning orbs, Passive(target) once (twice upgraded)
- THUNDER — Models.Cards/Thunder.cs:17 — THUNDER_POWER 8 (+3) by id
- TRASH_TO_TREASURE — Models.Cards/TrashToTreasure.cs:15 — TRASH_TO_TREASURE_POWER 1 by id; upgrade cost -1
- VOLTAIC — Models.Cards/Voltaic.cs:21 — CalculatedVar over history OrbChanneledEntries (actor id == owner CombatId, orb id LIGHTNING_ORB); upgrade removes Exhaust
