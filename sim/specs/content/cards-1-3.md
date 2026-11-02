# W3 cards-1-3 (wave A) index

All files under `sim/Sts2Sim.Core/Cards/Defs/`. Rule truth: `Models.Cards/<Class>.cs` (v0.111.0). Dropped everywhere: TriggerAnim/VFX/SFX/hover tips, PlayerChoiceContext.

- IRON_WAVE — Models.Cards/IronWave.cs:16 — block then attack (game order); hit fx dropped
- KNOW_THY_PLACE — Models.Cards/KnowThyPlace.cs:16 — Weak then Vulnerable; upgrade removes Exhaust
- LEAP — Models.Cards/Leap.cs:15 — none
- LEG_SWEEP — Models.Cards/LegSweep.cs:17 — block then Weak
- LUMINESCE — Models.Cards/Luminesce.cs:15 — none
- MALAISE — Models.Cards/Malaise.cs:15 — X cost via ResolveEnergyXValue, +1 if upgraded (no OnUpgrade in game); redundant TargetType override folded into ctor
- MASTER_OF_STRATEGY — Models.Cards/MasterOfStrategy.cs:14 — none
- MINION_DIVE_BOMB — Models.Cards/MinionDiveBomb.cs:18 — attacker anim/fx dropped
- MINION_SACRIFICE — Models.Cards/MinionSacrifice.cs:15 — none
- MINION_STRIKE — Models.Cards/MinionStrike.cs:16 — tags Strike+Minion
- MOLTEN_FIST — Models.Cards/MoltenFist.cs:19 — re-applies target's current Vulnerable if alive and > 0
- MOMENTUM_STRIKE — Models.Cards/MomentumStrike.cs:16 — EnergyCost.SetThisCombat(0) after the hit
- NEUTRALIZE — Models.Cards/Neutralize.cs:24 — FastMode anim-delay branch and thin-slice vfx dropped
- NOT_YET — Models.Cards/NotYet.cs:14 — CanBeGeneratedInCombat false
- OFFERING — Models.Cards/Offering.cs:17 — self-damage Unblockable|Unpowered|Move with card source, then energy, then draw
- PACTS_END — Models.Cards/PactsEnd.cs:18 — CanDealDamage = exhaust pile count >= Cards (3); ShouldGlowGold dropped
- PARSE — Models.Cards/Parse.cs:14 — draws Cards.IntValue (game uses IntValue here)
- PECK — Models.Cards/Peck.cs:16 — hit count = Repeat
- PILLAGE — Models.Cards/Pillage.cs:17 — draw-one loop while drawn is Attack and hand < MaxCardsInHand
- POMMEL_STRIKE — Models.Cards/PommelStrike.cs:16 — none
- POOR_SLEEP — Models.Cards/PoorSleep.cs:6 — curse, Unplayable+Retain, no OnPlay in game (empty override)
- PRODUCTION — Models.Cards/Production.cs:15 — none
- PROPHESIZE — Models.Cards/Prophesize.cs:14 — none
- PROWESS — Models.Cards/Prowess.cs:16 — Strength then Dexterity on owner
- PUTREFY — Models.Cards/Putrefy.cs:16 — single "Power" var for Weak and Vulnerable
- REAP — Models.Cards/Reap.cs:16 — Retain
- REBOOT — Models.Cards/Reboot.cs:15 — hand snapshot to Draw bottom, Shuffle, Draw
- RESTLESSNESS — Models.Cards/Restlessness.cs:16 — only-card-in-hand check by reference; draw-one loop then energy
- RIP_AND_TEAR — Models.Cards/RipAndTear.cs:20 — 2 random-target hits (duplicates allowed); VisualCardPool dropped
- SCRAWL — Models.Cards/Scrawl.cs:13 — draws MaxCardsInHand - hand count; upgrade adds Retain
- SECOND_WIND — Models.Cards/SecondWind.cs:17 — per non-Attack hand card: exhaust then block (alternating)
- SHAME — Models.Cards/Shame.cs:16 — OnTurnEndInHand: Frail 1 (applier null), SkipNextDurationTick if newly applied
- SHARED_FATE — Models.Cards/SharedFate.cs:16 — player Strength loss first, then enemy
- SHOCKWAVE — Models.Cards/Shockwave.cs:18 — hittable-enemy snapshot; Weak then Vulnerable per enemy
- SHRUG_IT_OFF — Models.Cards/ShrugItOff.cs:15 — none

Test fixups: `CardKernelTests` FactoryDrops/Discovery tests now derive the ported Ironclad set instead of hardcoding {Armaments, Headbutt}.
