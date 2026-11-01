# W3 wave A — cards-1-2 index

Dropped everywhere (Part A §1): WithHitFx / hit VFX nodes, TriggerAnim, Cmd.Wait, ExtraHoverTips, ExtraRunAssetPaths, ShouldGlowGoldInternal.

- DEFILE — Models.Cards/Defile.cs:16 — none
- DEFLECT — Models.Cards/Deflect.cs:15 — none
- DEFY — Models.Cards/Defy.cs:17 — Cast anim dropped
- DISMANTLE — Models.Cards/Dismantle.cs:21 — glow (HittableEnemies with Vulnerable) dropped
- DOMINATE — Models.Cards/Dominate.cs:16 — Cast anim dropped; StrengthPerVulnerable var kept (display only)
- DOUBLE_ENERGY — Models.Cards/DoubleEnergy.cs:14 — PowerUp anim dropped
- DOUBT — Models.Cards/Doubt.cs:16 — none (SkipNextDurationTick only when Weak newly applied)
- DRAMATIC_ENTRANCE — Models.Cards/DramaticEntrance.cs:17 — fullscreen VFX dropped
- ENLIGHTENMENT — Models.Cards/Enlightenment.cs:10 — none
- ENTRENCH — Models.Cards/Entrench.cs:14 — VisualCardPool (UI) dropped
- ERADICATE — Models.Cards/Eradicate.cs:16 — none
- ESCAPE_PLAN — Models.Cards/EscapePlan.cs:16 — Draw via CardPileCmd.DrawCards (returns drawn list)
- EXTERMINATE — Models.Cards/Exterminate.cs:16 — none
- FEAR — Models.Cards/Fear.cs:18 — Cast anim dropped
- FEED — Models.Cards/Feed.cs:19 — none
- FIEND_FIRE — Models.Cards/FiendFire.cs:21 — BeforeDamage ground-fire VFX/SFX dropped
- FIGHT_ME — Models.Cards/FightMe.cs:18 — anims dropped; order attack -> self Strength -> enemy Strength
- FINESSE — Models.Cards/Finesse.cs:15 — none
- FISTICUFFS — Models.Cards/Fisticuffs.cs:18 — none (block = sum TotalDamage+OverkillDamage)
- FLASH_OF_STEEL — Models.Cards/FlashOfSteel.cs:22 — thin-slice VFX and FastMode anim delay dropped
- FOLLY — Models.Cards/Folly.cs:8 — keyword array from IL (Unplayable, Eternal, Innate, Ethereal; version-deltas.json)
- FOOTWORK — Models.Cards/Footwork.cs:16 — none
- FORGOTTEN_RITUAL — Models.Cards/ForgottenRitual.cs:19 — ground-fire VFX dropped
- FUEL — Models.Cards/Fuel.cs:15 — none
- GIANT_ROCK — Models.Cards/GiantRock.cs:16 — none
- GO_FOR_THE_EYES — Models.Cards/GoForTheEyes.cs:20 — Target.Monster.IntendsToAttack -> MonsterLifecycle.IntendsToAttack((MonsterState)target)
- GRAND_FINALE — Models.Cards/GrandFinale.cs:20 — anticipation VFX + Cmd.Wait dropped; IsPlayable = empty draw pile
- GUNK_UP — Models.Cards/GunkUp.cs:18 — PreviewCardPileAdd + Wait dropped
- HEAVENLY_DRILL — Models.Cards/HeavenlyDrill.cs:16 — none
- HEMOKINESIS — Models.Cards/Hemokinesis.cs:17 — none
- IMPATIENCE — Models.Cards/Impatience.cs:15 — none
- IMPERVIOUS — Models.Cards/Impervious.cs:15 — none
- INFECTION — Models.Cards/Infection.cs:20 — impact VFX and HasBuiltInOverlay dropped
- INFLAME — Models.Cards/Inflame.cs:21 — OnEnqueuePlayVfx / power-up VFX dropped
- INJURY — Models.Cards/Injury.cs:6 — none
