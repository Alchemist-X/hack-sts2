# monsters-2 (W3 wave B) — index

Decompile root `MegaCrit.Sts2.Core.Models.Monsters/` (monsters) and `MegaCrit.Sts2.Core.Models.Powers/` (powers). Dropped everywhere: anims, SFX, VFX, TalkCmd, Cmd.Wait, music, skins, bestiary.

## Monsters

- GAS_BOMB — Models.Monsters/GasBomb.cs:25 — HasExploded (animator only) dropped.
- GUARDBOT — Models.Monsters/Guardbot.cs:23 — AfterAddedToRoom is visual only (bot fall position); Fabricator matched by id "FABRICATOR".
- HUNTER_KILLER — Models.Monsters/HunterKiller.cs:22 — none.
- INKLET — Models.Monsters/Inklet.cs:23 — INIT_RAND is built but not registered or used, as in the game; MiddleInklet lives in extra state (set by the encounter).
- KIN_FOLLOWER — Models.Monsters/KinFollower.cs:28 — boomerang target positioning and hair skin dropped; StartsWithDance lives in extra state.
- KIN_PRIEST — Models.Monsters/KinPriest.cs:29 — AfterDeath is speech + music only, so no override; SpeechUsed dropped.
- LAGAVULIN_MATRIARCH — Models.Monsters/LagavulinMatriarch.cs:26 — IsAwake / IsShellAwake / SleepingVfx affect visuals only, so dropped; WakeUpMove has no effect in the sim (anim + IsAwake); AfterDamageReceived / AfterDeath are visual only.
- LIVING_SHIELD — Models.Monsters/LivingShield.cs:21 — none.
- LOUSE_PROGENITOR — Models.Monsters/LouseProgenitor.cs:23 — Curled (Uncurl anim gate, also set by CurlUpPower) is visual only, so dropped.
- MAGI_KNIGHT — Models.Monsters/MagiKnight.cs:29 — dampen banter + wait dropped; DampenPower is built with `new` (game ModelDb.ToMutable).
- MAWLER — Models.Monsters/Mawler.cs:22 — none.
- MYSTERIOUS_KNIGHT — Models.Monsters/MysteriousKnight.cs:12 — derives from FlailKnight in the game (Models.Monsters/FlailKnight.cs:23); the FlailKnight HP / values / graph are inlined here, because FlailKnight belongs to another owner.
- NOISEBOT — Models.Monsters/Noisebot.cs:26 — PreviewCardPileAdd / wait dropped; Player ?? PetOwner resolved through PetOwnerId.
- OWL_MAGISTRATE — Models.Monsters/OwlMagistrate.cs:22 — IsFlying is SFX only, so dropped; BeforeRemovedFromRoom is SFX only.
- PHANTASMAL_GARDENER — Models.Monsters/PhantasmalGardener.cs:25 — INIT_MOVE is the initial state but not registered, as in the game; EnlargeTriggers / CurrentScale are visual only, so dropped.
- PUNCH_CONSTRUCT — Models.Monsters/PunchConstruct.cs:23 — StartsWithFastPunch / StartingHpReduction live in extra state (set by the event); HP write goes through CreatureInternals.SetCurrentHpInternal (raw, no hooks), as in the game.
- ROCKET — Models.Monsters/Rocket.cs:25 — AfterCurrentHpChanged / BeforeDeath / BeforeRemovedFromRoom are visual / music only, so no overrides.
- SCROLL_OF_BITING — Models.Monsters/ScrollOfBiting.cs:24 — StarterMoveIdx lives in extra state; the encounter that sets it is not in this list.
- SEWER_CLAM — Models.Monsters/SewerClam.cs:22 — none.
- SKULKING_COLONY — Models.Monsters/SkulkingColony.cs:22 — none.
- SLIMED_BERSERKER — Models.Monsters/SlimedBerserker.cs:24 — Leeching Hug applies Weak with a null applier, as in the game.
- SLITHERING_STRANGLER — Models.Monsters/SlitheringStrangler.cs:24 — none.
- SLUMBERING_BEETLE — Models.Monsters/SlumberingBeetle.cs:26 — IsAwake (animator only) and Died -> vfx handler dropped.

## Coupled powers (batch powers:9)

- TENDER_POWER — Models.Powers/TenderPower.cs:18 — restore order is Str, Dex, then reset (decompile listing is shuffled; read by await state).
- ASLEEP_POWER — Models.Powers/AsleepPower.cs:18 — wake: remove Plating, Stun(WakeUpMove, SLASH_MOVE), remove self.
- SLUMBER_POWER — Models.Powers/SlumberPower.cs:18 — AfterRemoved is SFX only.
- RAMPART_POWER — Models.Powers/RampartPower.cs:19 — Turret Operator matched by id "TURRET_OPERATOR".
- CURL_UP_POWER — Models.Powers/CurlUpPower.cs:18 — remembered card is stored as its InstanceId; LouseProgenitor.Curled write dropped (visual).
- SOAR_POWER — Models.Powers/SoarPower.cs:10 — DynamicVar DamageDecrease 50 kept as a const.
- PAPER_CUTS_POWER — Models.Powers/PaperCutsPower.cs:14 — none.
- CRAB_RAGE_POWER — Models.Powers/CrabRagePower.cs:17 — PowerVar 6 / BlockVar 99 (Unpowered) kept as consts.
- BACK_ATTACK_RIGHT_POWER — Models.Powers/BackAttackRightPower.cs:5 — marker.
- BACK_ATTACK_LEFT_POWER — Models.Powers/BackAttackLeftPower.cs:5 — marker.
- HARDENED_SHELL_POWER — Models.Powers/HardenedShellPower.cs:12 — HpDisplay writes dropped (UI).
- SKITTISH_POWER — Models.Powers/SkittishPower.cs:20 — the decompile drops the guard's goto; ported as "block only when the guard passes". Command.Results is read flat (same first match as the game's SelectMany).
- SURROUNDED_POWER — Models.Powers/SurroundedPower.cs:22 — FlipScale / music dropped; Facing only.
- DAMPEN_POWER — Models.Powers/DampenPower.cs:16 — casters stored as combat ids, downgraded cards as (InstanceId, level); the cards are read from the player's AllPiles (Hand, Draw, Discard, Exhaust, Play), and the game's PlayerCombatState.AllCards order may differ (gap: order of downgrade hooks only).
