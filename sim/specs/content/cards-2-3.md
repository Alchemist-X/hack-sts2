# cards-2-3 (W3 wave A) index

Common to all: TriggerAnim / VFX / SFX / hit-fx / hover tips dropped (no side-effect reorder); powers not in this tree are applied by id through `PowerCmd.Apply(cs, string powerId, ...)` (kernel edit, mirrors `Apply<T>` incl. return value); `PowerVar<T>` for unported T is a `DynamicVar` with the same name `typeof(T).Name`.

- PREDATOR — Models.Cards/Predator.cs:17 — attack, then DRAW_CARDS_NEXT_TURN_POWER 2 (literal, no var)
- PREP_TIME — Models.Cards/PrepTime.cs:16 — PREP_TIME_POWER var 4 (+2)
- PROLONG — Models.Cards/Prolong.cs:16 — BLOCK_NEXT_TURN_POWER = current block; upgrade removes Exhaust
- PYRE — Models.Cards/Pyre.cs:21 — PYRE_POWER = Energy 1 (+1); OnEnqueuePlayVfx dropped (VFX only)
- RAGE — Models.Cards/Rage.cs:16 — RAGE_POWER var "Power" 3 (+2)
- REAPER_FORM — Models.Cards/ReaperForm.cs:15 — REAPER_FORM_POWER 1; upgrade adds Retain
- RELAX — Models.Cards/Relax.cs:17 — order GainBlock, DRAW_CARDS_NEXT_TURN_POWER, ENERGY_NEXT_TURN_POWER (await states 1,2,3)
- ROLLING_BOULDER — Models.Cards/RollingBoulder.cs:15 — ROLLING_BOULDER_POWER = IntValue 5 (+5); IncrementAmount var 5 kept (read by the power)
- RUPTURE — Models.Cards/Rupture.cs:16 — RUPTURE_POWER = Strength var 1 (+1)
- SALVO — Models.Cards/Salvo.cs:18 — attack 12 (+4), RETAIN_HAND_POWER 1
- SCOURGE — Models.Cards/Scourge.cs:16 — DOOM_POWER on target then draw (await states 1,2)
- SERPENT_FORM — Models.Cards/SerpentForm.cs:15 — SERPENT_FORM_POWER 4 (+2)
- SETUP_STRIKE — Models.Cards/SetupStrike.cs:18 — Strike tag; attack 7 (+2), SETUP_STRIKE_POWER = Strength 3 (+1)
- SHADOWMELD — Models.Cards/Shadowmeld.cs:16 — SHADOWMELD_POWER var "Power" 1; upgrade cost -1
- SHROUD — Models.Cards/Shroud.cs:17 — SHROUD_POWER = BlockVar 3 Unpowered (+1)
- SIDESTEP — Models.Cards/Sidestep.cs:16 — ENERGY_NEXT_TURN_POWER = Energy IntValue 1 (+1)
- SIGNAL_BOOST — Models.Cards/SignalBoost.cs:15 — Exhaust; SIGNAL_BOOST_POWER 1; upgrade cost -1
- SLEIGHT_OF_FLESH — Models.Cards/SleightOfFlesh.cs:15 — SLEIGHT_OF_FLESH_POWER 9 (+4)
- SMOKESTACK — Models.Cards/Smokestack.cs:15 — SMOKESTACK_POWER 5 (+2)
- SNAKEBITE — Models.Cards/Snakebite.cs:16 — Retain; POISON_POWER 7 (+3) on target
- SPEEDSTER — Models.Cards/Speedster.cs:15 — SPEEDSTER_POWER = IntValue 2; upgrade adds Innate
- SPIRIT_OF_ASH — Models.Cards/SpiritOfAsh.cs:16 — SPIRIT_OF_ASH_POWER var "BlockOnExhaust" 4 (+1)
- STONE_ARMOR — Models.Cards/StoneArmor.cs:16 — PLATING_POWER 4 (+2)
- STRANGLE — Models.Cards/Strangle.cs:17 — attack 8 (+2), STRANGLE_POWER 2 (+1) on target
- SUBROUTINE — Models.Cards/Subroutine.cs:13 — SUBROUTINE_POWER 1; upgrade cost -1
- SYNTHESIS — Models.Cards/Synthesis.cs:17 — attack 14 (+6), FREE_POWER_POWER 1
- TERRAFORMING — Models.Cards/Terraforming.cs:16 — VIGOR_POWER = IntValue 7 (+3)
- THE_BOMB — Models.Cards/TheBomb.cs:15 — THE_BOMB_POWER = Turns 3, then SetDamage(BombDamage 40 (+10)) on the returned power; called by reflection on the name "SetDamage(decimal)" until the power is ported (non-null result assumed as in the game)
- THE_GAMBIT — Models.Cards/TheGambit.cs:16 — GainBlock 50 (+25), THE_GAMBIT_POWER 1
- TORIC_TOUGHNESS — Models.Cards/ToricToughness.cs:17 — GainBlock result stored, TORIC_TOUGHNESS_POWER = Turns 2, then `?.SetBlock(gained)` by reflection on "SetBlock(decimal)"
- TRACKING — Models.Cards/Tracking.cs:15 — TRACKING_POWER 50; upgrade cost -1
- UNRELENTING — Models.Cards/Unrelenting.cs:18 — attack 14 (+6), FREE_ATTACK_POWER 1; heavy-anim attacker anim dropped
- VEILPIERCER — Models.Cards/Veilpiercer.cs:18 — attack 10 (+3), VEILPIERCER_POWER 1
- VICIOUS — Models.Cards/Vicious.cs:16 — VICIOUS_POWER = Cards 1 (+1)
- WELL_LAID_PLANS — Models.Cards/WellLaidPlans.cs:13 — WELL_LAID_PLANS_POWER 1; upgrade cost -1
- WRAITH_FORM — Models.Cards/WraithForm.cs:16 — INTANGIBLE_POWER 2 (+1) then WRAITH_FORM_POWER 1 (await states 1,2)
