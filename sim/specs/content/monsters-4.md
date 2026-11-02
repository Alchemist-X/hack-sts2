# monsters-4 (W3 wave B) — index

Dropped everywhere: anim/VFX/SFX/music/TalkCmd, `Cmd.Wait`, phobia textures, bestiary overrides, `ShouldDisappearFromDoom` / `ShouldFadeAfterDeath`. `IsLiveCombat()` → `cs.IsInProgress`. `Players.Count` / `ScaleHpForMultiplayer` taken as single player. Extra-state tags 0x4DA0–0x4DA9 in `MonsterCanonicalTags.cs`.

- CEREMONIAL_BEAST — Models.Monsters/CeremonialBeast.cs:31 — state in extra (IsStunnedByPlowRemoval, IsInSecondPhase, InMidCharge); SetStunned/StunnedMove static, called by PlowPower
- GLOBE_HEAD — Models.Monsters/GlobeHead.cs:20 — none
- INFESTED_PRISM — Models.Monsters/InfestedPrism.cs:23 — none
- QUEEN — Models.Monsters/Queen.cs:28 — Amalgam stored as combat id; AfterAddedToRoom throws if no TORCH_HEAD_AMALGAM (First semantics); TorchHeadAmalgam referenced by id (not ported here)
- SPECTRAL_KNIGHT — Models.Monsters/SpectralKnight.cs:24 — none
- VINE_SHAMBLER — Models.Monsters/VineShambler.cs:21 — none
- AXEBOT — Models.Monsters/Axebot.cs:23 — StockOverrideAmount in extra, HP bonus via MinInitialHp(cs, self); ShouldPlaySpawnAnimation dropped (visual)
- FABRICATOR — Models.Monsters/Fabricator.cs:24 — HashSet spawn lists as ordered id lists (insertion order); _lastSpawned as id; bots (ZAPBOT/STABBOT/GUARDBOT/NOISEBOT) by id, not ported here
- FOGMOG — Models.Monsters/Fogmog.cs:23 — none
- LIVING_FOG — Models.Monsters/LivingFog.cs:25 — BloatAmount is constant 1 (never written); GAS_BOMB by id
- OVICOPTER — Models.Monsters/Ovicopter.cs:23 — TOUGH_EGG by id; BeforeRemovedFromRoom SFX only
- PHROG_PARASITE — Models.Monsters/PhrogParasite.cs:26 — death spawn lives in InfestedPower (W3 gap there: Wriggler.StartStunned seam, not mine)
- THE_OBSCURA — Models.Monsters/TheObscura.cs:23 — HasSummoned kept in extra (only read by animator)
- TWO_TAILED_RAT — Models.Monsters/TwoTailedRat.cs:26 — StarterMoveIndex/TurnsUntilSummonable/CallForBackupCount in extra; encounter sets StarterMoveIndex via Configure
- DECIMILLIPEDE_SEGMENT_BACK — Models.Monsters/DecimillipedeSegmentBack.cs:5 — SegmentAttack visual only
- DECIMILLIPEDE_SEGMENT_FRONT — Models.Monsters/DecimillipedeSegmentFront.cs:5 — SegmentAttack visual only
- DECIMILLIPEDE_SEGMENT_MIDDLE — Models.Monsters/DecimillipedeSegmentMiddle.cs:6 — SegmentAttack visual only
- DECIMILLIPEDE_SEGMENT (abstract base) — Models.Monsters/DecimillipedeSegment.cs:31 — ported as abstract DecimillipedeSegment; not registrable; AfterDeath visual only
- EYE_WITH_TEETH — Models.Monsters/EyeWithTeeth.cs:22 — IllusionPower applied by id
- PARAFRIGHT — Models.Monsters/Parafright.cs:22 — IllusionPower applied by id
- TEST_SUBJECT — Models.Monsters/TestSubject.cs:32 — Respawns/ExtraMultiClawCount in extra; PowerApplied/PowerRemoved color handlers dropped (visual); TestSubjectKills on run ExtraFields
- WATERFALL_GIANT — Models.Monsters/WaterfallGiant.cs:27 — PressureBuildupIdx dropped (anim/music only); HpDisplay dropped
- KNOWLEDGE_DEMON — Models.Monsters/KnowledgeDemon.cs:28 — _disintegrationDamageValues {6,7,8} from inventory (LdMemberToken loss); IChoosable.OnChosen dispatched by card type; prompt site KNOWLEDGE_DEMON:ChooseCurse, players handled serially

Coupled powers:
- REATTACH_POWER — Models.Powers/ReattachPower.cs:22 — fade-out VFX dropped; ShouldOwnerDeathTriggerFatal via new cs overload
- PLOW_POWER — Models.Powers/PlowPower.cs:18 — none
- STEAM_ERUPTION_POWER — Models.Powers/SteamEruptionPower.cs:13 — none
- ADAPTABLE_POWER — Models.Powers/AdaptablePower.cs:13 — none
- ENRAGE_POWER — Models.Powers/EnragePower.cs:13 — none
- NEMESIS_POWER — Models.Powers/NemesisPower.cs:16 — none
- PAINFUL_STABS_POWER — Models.Powers/PainfulStabsPower.cs:20 — flat Results list grouped by receiver in insertion order
- ILLUSION_POWER — Models.Powers/IllusionPower.cs:19 — AfterCombatEnd anim only
- STOCK_POWER (gap closed) — Models.Powers/StockPower.cs:15 — respawn through MonsterSpawner.SpawnForAdd configure
