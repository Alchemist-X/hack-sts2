# monsters-3 (wave B) — index

Dropped everywhere: SFX/VFX/anim/talk, `Cmd.Wait`, `CardCmd.PreviewCardPileAdd`, bestiary, music, skins, visual-only flags.
Move bodies follow the async state numbers (await order), not decompile line order.

## Monsters

- SNAPPING_JAXFRUIT — Models.Monsters/SnappingJaxfruit.cs:26 — AfterAddedToRoom/BeforeRemovedFromRoom visual only.
- SNEAKY_GREMLIN — Models.Monsters/SneakyGremlin.cs:20 — IsAwake (anim flag) dropped.
- SOUL_FYSH — Models.Monsters/SoulFysh.cs:30 — IsInvisible, AfterDeath, AfterCardChangedPilesLate visual only.
- SOUL_NEXUS — Models.Monsters/SoulNexus.cs:23 — Died handler / BeforeRemovedFromRoom visual only.
- SPINY_TOAD — Models.Monsters/SpinyToad.cs:22 — IsSpiny (anim) dropped.
- STABBOT — Models.Monsters/Stabbot.cs:24 — AfterAddedToRoom visual only.
- TERROR_EEL — Models.Monsters/TerrorEel.cs:22 — STUN_MOVE registered but unreachable (as in game).
- THE_FORGOTTEN — Models.Monsters/TheForgotten.cs:23 — DREAD intent damage is a lambda over own Dexterity.
- THE_INSATIABLE — Models.Monsters/TheInsatiable.cs:30 — HasLiquified (anim) dropped.
- THE_LOST — Models.Monsters/TheLost.cs:22 — none.
- TORCH_HEAD_AMALGAM — Models.Monsters/TorchHeadAmalgam.cs:25 — OnDieToDoom visual only.
- TRACKER_RUBY_RAIDER — Models.Monsters/TrackerRubyRaider.cs:20 — none.
- TUNNELER — Models.Monsters/Tunneler.cs:25 — GetStunned / StillDizzyMove are anim-only (kept as no-op seams).
- TURRET_OPERATOR — Models.Monsters/TurretOperator.cs:22 — none.
- ZAPBOT — Models.Monsters/Zapbot.cs:24 — none.
- AEONGLASS — Models.Monsters/Aeonglass.cs:28 — AfterDeath music only; AllCards = player AllPiles.
- CHOMPER — Models.Monsters/Chomper.cs:23 — ScreamFirst lives in ChomperExtraState (set by the encounter's Configure).
- HAUNTED_SHIP — Models.Monsters/HauntedShip.cs:24 — none.
- MECHA_KNIGHT — Models.Monsters/MechaKnight.cs:31 — IsWoundUp (anim) dropped.
- MYTE — Models.Monsters/Myte.cs:27 — INIT_MOVE conditional is the initial state but not registered (as in game).
- TOUGH_EGG — Models.Monsters/ToughEgg.cs:30 — IsHatched in ToughEggExtraState; HatchPos/skins dropped; ScaleHpForMultiplayer treated as identity (1 player).
- VANTOM — Models.Monsters/Vantom.cs:33 — AfterDeath/SetupSkins visual only.
- WRIGGLER — Models.Monsters/Wriggler.cs:25 — StartStunned in WrigglerExtraState (set by the spawner's Configure).

## Coupled powers (batch powers:9)

- SHRIEK_POWER — Models.Powers/ShriekPower.cs:17 — multiplayer scaling not ported (single player).
- POSSESS_SPEED_POWER — Models.Powers/PossessSpeedPower.cs:15 — stolen map keyed by creature id, insertion-ordered list.
- POSSESS_STRENGTH_POWER — Models.Powers/PossessStrengthPower.cs:15 — as above.
- SANDPIT_POWER — Models.Powers/SandpitPower.cs:25 — AfterApplied/AfterPowerAmountChanged/AfterCreatureAddedToCombat/AfterOstyRevived/BeforeSideTurnEnd are position tweens only.
- BURROWED_POWER — Models.Powers/BurrowedPower.cs:14 — none.
- HIGH_VOLTAGE_POWER — Models.Powers/HighVoltagePower.cs:17 — none.
- WITHERING_PRESENCE_POWER — Models.Powers/WitheringPresencePower.cs:18 — DynamicVar CardsLeft stored as a decimal field; hover-tip Wither matching dropped.
- HATCH_POWER — Models.Powers/HatchPower.cs:16 — none.

Kernel edit: `Monsters/MonsterCanonicalTags.cs` +4 tags (0x4DA1–0x4DA4) for the extra states.
