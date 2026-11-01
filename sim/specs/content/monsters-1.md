# monsters-1 (W3 wave B) — index

Monsters (`sim/Sts2Sim.Core/Monsters/Defs/`). Dropped everywhere: SFX/VFX/anim triggers, TalkCmd/ThinkCmd banter, `Cmd.Wait`, skins, bestiary lists, `Rng.Chaotic` cosmetic draws.

- ARCHITECT — Models.Monsters/Architect.cs:11 — no-op NOTHING (Hidden) loop; display-only event creature.
- ASSASSIN_RUBY_RAIDER — Models.Monsters/AssassinRubyRaider.cs:18 — none.
- AXE_RUBY_RAIDER — Models.Monsters/AxeRubyRaider.cs:19 — none.
- BOWLBUG_EGG — Models.Monsters/BowlbugEgg.cs:22 — AfterAddedToRoom only calls base (no-op).
- BOWLBUG_NECTAR — Models.Monsters/BowlbugNectar.cs:22 — none.
- BOWLBUG_ROCK — Models.Monsters/BowlbugRock.cs:23 — IsOffBalance in extra state (tag 0x4DA0); Stun() = StunWithMove(DizzyMove).
- BOWLBUG_SILK — Models.Monsters/BowlbugSilk.cs:25 — spine-bone repositioning dropped.
- BRUTE_RUBY_RAIDER — Models.Monsters/BruteRubyRaider.cs:20 — none.
- BYGONE_EFFIGY — Models.Monsters/BygoneEffigy.cs:30 — SLEEP_MOVE_2 registered but unreachable (as in game); music phase trigger dropped.
- BYRDONIS — Models.Monsters/Byrdonis.cs:21 — none.
- CALCIFIED_CULTIST — Models.Monsters/CalcifiedCultist.cs:25 — AttackSfxStrength (BeforeDamage SFX field) dropped.
- CROSSBOW_RUBY_RAIDER — Models.Monsters/CrossbowRubyRaider.cs:21 — IsCrossbowReloaded is anim-only, dropped.
- CRUSHER — Models.Monsters/Crusher.cs:27 — AfterCurrentHpChanged/BeforeDeath are background-anim/music only, dropped; Rocket half is another owner.
- CUBEX_CONSTRUCT — Models.Monsters/CubexConstruct.cs:25 — IsBurrowed/IsCharging and CurrentHpChanged SFX handler are visual only, dropped.
- DAMP_CULTIST — Models.Monsters/DampCultist.cs:25 — as CalcifiedCultist.
- DEVOTED_SCULPTOR — Models.Monsters/DevotedSculptor.cs:22 — Ritual applied with null applier (as in game).
- ENTOMANCER — Models.Monsters/Entomancer.cs:23 — none.
- EXOSKELETON — Models.Monsters/Exoskeleton.cs:23 — INIT_MOVE branches on SlotName first/second/third/fourth.
- FAKE_MERCHANT_MONSTER — Models.Monsters/FakeMerchantMonster.cs:26 — dialogue lines (Rng.Chaotic) dropped.
- FLAIL_KNIGHT — Models.Monsters/FlailKnight.cs:23 — not sealed (game: MysteriousKnight derives); protected ctor(id) for it.
- FLYCONID — Models.Monsters/Flyconid.cs:24 — spore VFX toggle dropped.
- FOSSIL_STALKER — Models.Monsters/FossilStalker.cs:22 — none.
- FROG_KNIGHT — Models.Monsters/FrogKnight.cs:22 — HasBeetleCharged in extra state (tag 0x4DA1); HALF_HEALTH uses int MaxHp/2.

Coupled powers (`sim/Sts2Sim.Core/Powers/Defs/`):

- IMBALANCED_POWER — Models.Powers/ImbalancedPower.cs:15 — non-monster owner: no stun (game would call CreatureCmd.Stun on a player; unreachable).
- SLOW_POWER — Models.Powers/SlowPower.cs:14 — DynamicVar SlowAmount stored as int field (canonical); DisplayAmount derived, dropped.
- TERRITORIAL_POWER — Models.Powers/TerritorialPower.cs:17 — none.
- PERSONAL_HIVE_POWER — Models.Powers/PersonalHivePower.cs:19 — CardCmd.PreviewCardPileAdd dropped; non-player dealer ignored.
- HARD_TO_KILL_POWER — Models.Powers/HardToKillPower.cs:9 — none.
- SUCK_POWER — Models.Powers/SuckPower.cs:18 — mutates the command's per-hit result lists like the game (removes pet-owner hits when a pet was hit).
- BACK_ATTACK_LEFT_POWER — Models.Powers/BackAttackLeftPower.cs:5 — marker only; BACK_ATTACK_RIGHT_POWER left to the Rocket owner.
- CRAB_RAGE_POWER — Models.Powers/CrabRagePower.cs:17 — DynamicVars Strength 6 / Block 99 Unpowered as consts.

Encounters for these monsters are not in this list (owned elsewhere); covered by `Content_monsters_1Tests.Test_Content_monsters_1_RandomPlaySoak` (single-monster combats, A0 and A10).
