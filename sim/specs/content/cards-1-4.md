# W3 cards-1-4 (wave A) index

Paths are relative to the decompile root (`MegaCrit.Sts2.Core.` prefix omitted). Dropped everywhere: hit VFX/SFX, attacker anims, `CreatureCmd.TriggerAnim`, hover tips.

- SKEWER — Models.Cards/Skewer.cs:19 — X-cost hits via `ResolveEnergyXValue`; stab VFX dropped
- SKIM — Models.Cards/Skim.cs:14 — none
- SLICE — Models.Cards/Slice.cs:22 — thin-slice VFX and FastMode anim delay dropped
- SOOT — Models.Cards/Soot.cs:6 — none (no hooks)
- SOW — Models.Cards/Sow.cs:16 — none
- SPORE_MIND — Models.Cards/SporeMind.cs:6 — none (no hooks)
- SQUASH — Models.Cards/Squash.cs:18 — none
- STRIKE_DEFECT — Models.Cards/StrikeDefect.cs:16 — none
- STRIKE_NECROBINDER — Models.Cards/StrikeNecrobinder.cs:16 — none
- STRIKE_REGENT — Models.Cards/StrikeRegent.cs:16 — none
- STRIKE_SILENT — Models.Cards/StrikeSilent.cs:16 — none
- SUCKER_PUNCH — Models.Cards/SuckerPunch.cs:18 — none
- SUNDER — Models.Cards/Sunder.cs:19 — energy refund on any `WasTargetKilled` in `AttackCommand.Results`
- SUPERCRITICAL — Models.Cards/Supercritical.cs:15 — PowerUp anim dropped
- SUPPRESS — Models.Cards/Suppress.cs:24 — thin-slice VFX and anim delay dropped
- SWEEPING_BEAM — Models.Cards/SweepingBeam.cs:22 — `BeforeDamage` lambda is VFX + `Cmd.Wait` only, dropped
- SWORD_BOOMERANG — Models.Cards/SwordBoomerang.cs:16 — random targets draw CombatTargets inside AttackCommand
- TAUNT — Models.Cards/Taunt.cs:17 — Cast anim dropped
- THUNDERCLAP — Models.Cards/Thunderclap.cs:19 — Vulnerable applied to `HittableEnemies` re-read after the attack
- TOXIC — Models.Cards/Toxic.cs:16 — `OnTurnEndInHand` damages owner with the Unpowered|Move DamageVar
- TREMBLE — Models.Cards/Tremble.cs:16 — Cast anim dropped
- TWIN_STRIKE — Models.Cards/TwinStrike.cs:16 — none
- ULTIMATE_DEFEND — Models.Cards/UltimateDefend.cs:15 — none
- ULTIMATE_STRIKE — Models.Cards/UltimateStrike.cs:16 — none
- UPPERCUT — Models.Cards/Uppercut.cs:19 — "Power" var read once after the attack, Weak then Vulnerable
- VOLLEY — Models.Cards/Volley.cs:16 — X-cost random-target hits
- WHIRLWIND — Models.Cards/Whirlwind.cs:23 — X-cost; the `num2 > 0` branch is VFX/SFX only, attack runs for any X
- WHISTLE — Models.Cards/Whistle.cs:18 — Stun on a non-monster throws "Can't stun a player." (Creature.cs:1105); stun move effect per kernel `CreatureCmd.Stun` (open question in inventory)
- WISP — Models.Cards/Wisp.cs:15 — upgrade adds Retain
- WITHER — Models.Cards/Wither.cs:18 — `FakeUpgrade` (called by Aeonglass) is `Wither.FakeUpgrade(CardInstance)`; FakeUpgradeLevel kept in CardExtraState; title/portrait overrides dropped
- WOUND — Models.Cards/Wound.cs:6 — none
- WRITHE — Models.Cards/Writhe.cs:6 — none
