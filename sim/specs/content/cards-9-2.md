# cards-9-2 (wave A) — model index

Common drops for every model: TriggerAnim / Cmd.Wait / hit VFX+SFX / hover tips / ShouldGlowRed (UI).
Powers not yet in the tree are applied by id (`PowerRegistry.Create` + `PowerCmd.Apply`) and their `PowerVar<T>` is a plain `DynamicVar` with the game's var name.
`ForgeCmd.Forge` / `OstyCmd.Summon` get `source: null` (CardInstance is not an AbstractModel; no v0.111 consumer reads a card source).
`Osty.CheckMissingWithAnim` = `CombatStatus.IsOstyMissing` (shake anim dropped); `Owner.Osty` = `CombatStatus.GetOsty`.

- NEUTRON_AEGIS — Models.Cards/NeutronAegis.cs:16 — PLATING_POWER by id.
- PARRY — Models.Cards/Parry.cs:16 — PARRY_POWER by id.
- PARTICLE_WALL — Models.Cards/ParticleWall.cs:15 — Cmd.Wait(0.25) dropped; result location Discard -> Hand.
- POKE — Models.Cards/Poke.cs:17 — none.
- PROTECTOR — Models.Cards/Protector.cs:18 — calculated damage passed as a per-target closure over CalculatedDamageVar.Calculate.
- PULL_AGGRO — Models.Cards/PullAggro.cs:18 — order Summon -> GainBlock.
- QUASAR — Models.Cards/Quasar.cs:17 — Colorless pool, 3 distinct on CombatCardGeneration; upgrade preview style dropped.
- RADIATE — Models.Cards/Radiate.cs:21 — hits = sum of positive StarsModified this turn by owner (CombatHistory.SumThisTurn).
- RATTLE — Models.Cards/Rattle.cs:22 — hits = 1 + CreatureAttacked this turn by Osty.
- REANIMATE — Models.Cards/Reanimate.cs:17 — none.
- REFINE_BLADE — Models.Cards/RefineBlade.cs:16 — order Forge -> ENERGY_NEXT_TURN_POWER (by id).
- REFLECT — Models.Cards/Reflect.cs:16 — order GainBlock -> REFLECT_POWER (by id).
- RESONANCE — Models.Cards/Resonance.cs:18 — self Strength, then -1 Strength on each hittable enemy (snapshot list).
- RIGHT_HAND_HAND — Models.Cards/RightHandHand.cs:18 — AfterCardPlayedLate via CardListener; owner compared by reference.
- ROYAL_GAMBLE — Models.Cards/RoyalGamble.cs:14 — upgrade adds Retain.
- SACRIFICE — Models.Cards/Sacrifice.cs:17 — block computed before Kill(Osty); no-op when Osty missing.
- SEEKING_EDGE — Models.Cards/SeekingEdge.cs:16 — order SEEKING_EDGE_POWER (by id) -> Forge.
- SENTRY_MODE — Models.Cards/SentryMode.cs:16 — SENTRY_MODE_POWER by id.
- SEVEN_STARS — Models.Cards/SevenStars.cs:16 — none.
- SHINING_STRIKE — Models.Cards/ShiningStrike.cs:16 — result location Discard -> Draw top.
- SIC_EM — Models.Cards/SicEm.cs:19 — SIC_EM_POWER on target applied even when Osty is missing.
- SNAP — Models.Cards/Snap.cs:20 — hand filter uses the card's own keyword mask (game `c.Keywords`).
- SOLAR_STRIKE — Models.Cards/SolarStrike.cs:16 — none.
- SPOILS_OF_BATTLE — Models.Cards/SpoilsOfBattle.cs:15 — order Forge -> Draw.
- SPUR — Models.Cards/Spur.cs:17 — order Summon -> Heal(Osty).
- SQUEEZE — Models.Cards/Squeeze.cs:19 — multiplier counts OstyAttack-tagged cards in all combat piles except itself.
- STARDUST — Models.Cards/Stardust.cs:16 — star-X hits on random enemies.
- SUMMON_FORTH — Models.Cards/SummonForth.cs:17 — blades outside hand moved to hand first, then Forge; blades matched by id.
- SWEEPING_GAZE — Models.Cards/SweepingGaze.cs:17 — game class is non-sealed; sealed here.
- SWORD_SAGE — Models.Cards/SwordSage.cs:16 — SWORD_SAGE_POWER by id.
- THE_SEALED_THRONE — Models.Cards/TheSealedThrone.cs:13 — THE_SEALED_THRONE_POWER by id.
- THE_SMITH — Models.Cards/TheSmith.cs:15 — none.
- UNLEASH — Models.Cards/Unleash.cs:18 — multiplier = alive Osty CurrentHp.
- VENERATE — Models.Cards/Venerate.cs:14 — none.
- VOID_FORM — Models.Cards/VoidForm.cs:15 — GAP: PlayerCmd.EndTurn is a kernel NotImplementedException stub; OnPlay throws after applying VOID_FORM_POWER.
- WROUGHT_IN_WAR — Models.Cards/WroughtInWar.cs:17 — order Attack -> Forge.
- SOVEREIGN_BLADE — skipped: W1 pilot already ported (Cards/Defs/SovereignBlade.cs).
