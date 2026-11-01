# Powers inventory: semantic classification (game v0.111.0)

2026-11-01 · W0 semantic enrichment, kind `powers` · machine-readable companion:
[`sim/specs/inventory/powers.classified.json`](../../sim/specs/inventory/powers.classified.json)
(joins [`powers.json`](../../sim/specs/inventory/powers.json) on `id`, same entry order).

Rule truth: `/Users/Aincrad/dev-proj/hack-sts2/output/server-bootstrap/linux-decompiled/`, cited as
`<ns-dir>/<File>.cs:<line>` without the `MegaCrit.Sts2.Core.` prefix. The v0.107.1 tree
(`artifacts/knowledge/decompiled-v0.107.1/MegaCrit/sts2/Core/Models/Powers/`) was used only for
`version_delta_v107`. Scratch scripts (Python 3 stdlib, git-ignored): `tmp/sim-port/powers/`
(`condense.py` turns the async state machines into readable pseudo-source, `features.py` extracts
hooks, Cmd calls, referrers and reference modes, `vdiff.py` fingerprints both versions,
`ann_*.py` hold the per-power reading notes, `build.py` writes the JSON).

## 0. Summary

- 286 files: 268 game powers (265 concrete, 3 abstract `Temporary{Strength,Dexterity,Focus}Power`
  bases) and 18 mocks. Every file was read in full (async state machines condensed to pseudo-source
  first). The 274 files shared with v0.107.1 were fingerprint-compared against the clean v0.107.1
  decompile and the 120 that differ were read side by side.
- Non-mock tiers: **96 tier 1** (port on today's kernel), **103 tier 2** (need game facades, hooks
  or hook parameters the sim lacks), **68 tier 3** (need a prompt or a new subsystem), **1 tier 4**
  (`RollingBoulderPower`, damage order driven by VFX signals). 8 are already ported
  (`Strength`, `Dexterity`, `Frail`, `Weak`, `Vulnerable`, `Shrink`, `Thorns`, `Ravenous`);
  `Vulnerable`/`Weak` still miss the `PaperPhrog`/`PaperKrane` relic steps and the pet-owner
  `Cruelty` fallback.
- The single biggest prerequisite is restoring the **side-turn `participants` list** on the
  side-turn hooks: 73 non-mock powers read it (`Enumerable.Contains(participants, Owner)`); 48 of
  them need nothing else beyond per-instance fields or monster-specific calls.
- 13 hook virtuals used by game powers are missing in the sim (12 rule-relevant; Sandpit's
  `AfterOstyRevived` only moves nodes), plus the `AfterDiedToDoom` dispatcher DoomPower calls.
  84 distinct hooks are overridden by game powers (88 with mocks); `AfterSideTurnEnd` alone has
  82 overriders (counting inherited overrides).
- Prompts: exactly 5 powers prompt (3 hand selections, 2 draw-pile selections, all exactly `Amount`
  cards, never skippable). v0.111.0 removed the `WellLaidPlans` prompt that v0.107.1 had.
- 19 powers are reachable only through `MultiplayerOnly` cards (out of single-player scope;
  batch 10). 4 powers have no referrer at all (`Gravity`, `Leadership`, `MagicBomb`,
  `NoEnergyGain`). No power reads an `AscensionLevel` (ascension scaling lives in the appliers).
- Version delta vs v0.107.1: 154 same, 90 cosmetic, **14 rule changes**, 10 new game powers
  (+2 new mocks); `DiamondDiademPower` and `OutbreakPower` were removed.
- No power file needs a re-decompile: the only decompiler warning (`DoomPower.cs:607`,
  "Expected O, but got Unknown") is inside the UI-only `StartDoomAnim`.

## 1. Field contract (added per entry)

| Field | Meaning |
|---|---|
| `mechanics` | effect primitives in game order, with owner/target/timing |
| `prompts` | prompts the power triggers itself: `type`, `cmd`, `source`, `min`/`max` (expressions), `site`, `follow-up` |
| `kernel_requirements` | concrete Cmd/Hook/subsystem features the sim must have first. `Hook.X` = virtual or dispatcher missing in the sim; `HookParam.SideTurnParticipants` = participants list; `(present…)` = the sim already has it |
| `rng_streams` | `RunRng.*` streams read, with call and order; `indirect:` = consumed by a kernel call the power makes |
| `ascension_sensitivity` | `none` for all 286 |
| `version_delta_v107` | `same` / `cosmetic` / `rule-change` / `new`, one-line note |
| `tier`, `tier_derived_from_requirements` | manual tier and a keyword-derived cross-check (they agree for every non-mock entry) |
| `batch_label`, `port_batch` | semantic group (`monster:<Name>` = port with that monster) and proposed batch (null for mocks) |
| `hooks`, `non_hook_overrides`, `override_lines`, `cmd_calls` | hook overrides with line, scope and sim presence; other overrides; Cmd calls with sim status |
| `depends_on`, `weak_refs`, `manual_deps` | outgoing references from rule code (UI members masked) with modes; hover-tip/var-only refs; reading-derived deps |
| `referrers`, `referrer_counts`, `dependent_referrers`, `appliers`, `non_rule_referrers`, `core_referrers` | who references the power (by kind, with modes such as `apply`, `read`, `remove`, `is`); `dependent_referrers` drops hover-tip-only refs and non-rule kinds (achievements, badges) |
| `multiplayer_only` | every applier is a `MultiplayerOnly` card (or such a power) |
| `notes`, `open_questions` | porting notes and unresolved questions |

Tier rules: **1** = only existing sim facades/hooks (per-instance fields and monster-specific calls
are normal port work); **2** = needs game surface missing in the sim (hook virtuals or dispatchers,
the `participants` parameter, Cmd methods or overloads, card/creature/power fields such as
`Pets`, `PetOwner`, `HpDisplay`, `IsDupe`, `ITemporaryPower`, `SetMoveImmediate`); **3** = needs
the choice API or a new subsystem (combat history, orbs, Osty/pets, stars, forge, afflictions,
pool card generation, mid-combat summons, relic runtime, potion runtime, run layer); **4** =
unreadable or cross-cutting.

## 2. Tiers and port batches

| tier \ batch | B1 | B2 | B3 | B4 | B5 | B6 | B7 | B8 | B9 | B10 | all |
|---|--:|--:|--:|--:|--:|--:|--:|--:|--:|--:|--:|
| tier 1 | 61 | 4 | 0 | 0 | 0 | 2 | 0 | 0 | 18 | 11 | 96 |
| tier 2 | 0 | 59 | 10 | 0 | 0 | 3 | 0 | 0 | 21 | 10 | 103 |
| tier 3 | 0 | 2 | 0 | 9 | 14 | 29 | 5 | 7 | 1 | 1 | 68 |
| tier 4 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 1 | 1 |
| total | 61 | 65 | 10 | 9 | 14 | 34 | 5 | 7 | 40 | 23 | 268 |

Batches (`port_batch`; the kernel features of each batch come from the W1 kernel wave, see §4):

| Batch | Content |
|---|---|
| 1 | existing kernel: pure numbers/hooks the sim already has |
| 2 | combat-kernel surface: side-turn participants, missing combat hooks/Cmd overloads, temporary-stat family, Doom, relic-query step of Vulnerable/Weak |
| 3 | card-model operations: keywords, upgrade/downgrade, clones, fixed-card creation, auto-play and auto play phases, result-location hook |
| 4 | combat-history readers |
| 5 | pool card generation, afflictions, mid-combat monster summons |
| 6 | character subsystems: orbs (Defect), Osty (Necrobinder), stars and forge (Regent) |
| 7 | player-choice prompts |
| 8 | run-layer coupled (combat-end rewards, gold, deck) |
| 9 | monster-coupled: port together with the named monster (monster wave) |
| 10 | out of single-player scope or deferred: multiplayer-only, unreferenced, `RollingBoulderPower` |

Batch numbers were raised to respect power→power dependencies (`Envenom` → `Poison` in 2,
`Surprise` → `Thievery`/`Heist` in 8). A dependency on an already-ported power counts as met.
`Hibernate` is multiplayer-only as an applied power but stays in batch 6 because `FrostOrb` reads it.

**B1** (61):
core: Accelerant, Accuracy, Afterimage, Ambergris, Arsenal, Automation, Barricade, BlockNextTurn, Buffer, Colossus, Confused, CrimsonMantle, Cruelty, Curious, DanseMacabre, Demesne, Dexterity, EnergyNextTurn, FanOfKnives, Fasten, FeelNoPain, FlameBarrier, Frail, FreeAttack, FreePower, FreeSkill, Friendship, Gigantification, Hang, Haunt, Inferno, Intangible, Juggernaut, MachineLearning, MindRot, Minion, NoBlock, Orbit, Pagestorm, PillarOfCreation, Pyre, Radiance, Rupture, SelfFormingClay, SerpentForm, SignalBoost, Slippery, Smokestack, Speedster, SpiritOfAsh, Strength, Subroutine, Tainted, TheGambit, TheHunt, Thorns, Veilpiercer, Vicious, Vigor, WasteAway, WellLaidPlans

**B2** (65):
core: Artifact, Blur, BorrowedTime, Burst, Clarity, Constrict, CorrosiveWave, DarkEmbrace, Debilitate, Demise, DemonForm, Disintegration, DoubleDamage, DrawCardsNextTurn, Duplication, Envenom, Monologue, NoDraw, NoxiousFumes, OneTwoPunch, Panache, Plating, Poison, PrepTime, Rage, Reflect, Regen, RetainHand, Ritual, ShadowStep, Shadowmeld, Shrink, Sloth, Strangle, TheBomb, ToricToughness, Tracking, Vulnerable, Weak, WraithForm; doom: Countdown, Doom, Neurosurge, Oblivion, ReaperForm, Shroud; temp: Anticipate, CrushUnder, DarkShackles, DyingStar, EnfeeblingTouch, FeedingFrenzy, FlexPotion, HelicalDart, Mangle, MonarchsGaze, MonarchsGazeStrengthDown, PiercingWail, ReptileTrinket, SetupStrike, ShacklingPotion, SleightOfFlesh, SpeedPotion, TemporaryDexterity, TemporaryStrength

**B3** (10):
cardops: Aggression, Corruption, Hellraiser, InfiniteBlades, MasterPlanner, Mayhem, Nightmare, Rebound, SentryMode, Stampede

**B4** (9):
history: EchoForm, Feral, Iteration, Juggling, Lethality, Nostalgia, PaleBlueDot, PhantomBlades, Unmovable

**B5** (14):
afflict: ChainsOfBinding, Galvanic, Hex, Ringing, Smoggy, Tangled, VitalSpark; cardgen: Calamity, CallOfTheVoid, CreativeAi, HelloWorld, SpectrumShift; summon: Infested, Stock

**B6** (34):
forge: Conqueror, Furnace, Parry, SeekingEdge, SwordSage; orbs: BiasedCognition, ConsumingShadow, Coolant, Focus, FocusedStrike, Hailstorm, Hibernate, Hotfix, HyperbeamFocusDown, LightningRod, Loop, Spinner, Storm, Synchronize, TemporaryFocus, Thunder, TrashToTreasure; osty: Calcify, DevourLife, DieForYou, NecroMastery, SicEm, SummonNextTurn; stars: BlackHole, ChildOfTheStars, Genesis, StarNextTurn, TheSealedThrone, VoidForm

**B7** (5):
prompt: Entropy, ForegoneConclusion, Stratagem, ToolsOfTheTrade, Tyranny

**B8** (7):
run: ForbiddenGrimoire, Heist, Improvement, Royalties, Swipe, Thievery; summon: Surprise

**B9** (40):
Aeonglass: WitheringPresence; BattleFriend: BattlewornDummyTimeLimit; BowlbugRock: Imbalanced; BygoneEffigy: Slow; Byrdonis: Territorial; CeremonialBeast: Plow; CorpseSlug: Ravenous; Crusher: BackAttackLeft, CrabRage; CrusherRocket: Surrounded; Decimillipede: Reattach; Entomancer: PersonalHive; Exoskeleton: HardToKill; FossilStalker: Suck; HunterKiller: Tender; LagavulinMatriarch: Asleep; LivingShield: Rampart; LouseProgenitor: CurlUp; MagiKnight: Dampen; OwlMagistrate: Soar; Parafright: Illusion; PhantasmalGardener: Skittish; Rocket: BackAttackRight; ScrollOfBiting: PaperCuts; SkulkingColony: HardenedShell; SlumberingBeetle: Slumber; TerrorEel: Shriek; TestSubject: Adaptable, Enrage, Nemesis, PainfulStabs; TheForgotten: PossessSpeed; TheInsatiable: Sandpit; TheLost: PossessStrength; ThievingHopper: EscapeArtist, Flutter; ToughEgg: Hatch; Tunneler: Burrowed; WaterfallGiant: SteamEruption; Zapbot: HighVoltage

**B10** (23):
cardops: ImitationLearning, Soulbound; core: BeaconOfHope, Cacophony, Concoct, Covered, Flanking, Guarded, Intercept, Knockdown, OneForAll, Sneaky, TagTeam, Tank; crosscut: RollingBoulder; doom: Underworld; forge: HammerTime; temp: Coordinate, Fade; unreferenced: Gravity, Leadership, MagicBomb, NoEnergyGain

## 3. Counts per pool / act by tier

Context = the card pool, monster act, relic pool or potion pool of every model whose port depends
on the power (`dependent_referrers`, propagated through power→power references; a power counts in
every context it reaches). Non-mock powers only.

| context (pool/act of dependent referrers, transitive through powers) | T1 | T2 | T3 | T4 | powers |
|---|--:|--:|--:|--:|--:|
| (no referrer) | 1 | 3 | 0 | 0 | 4 |
| afflictions | 0 | 0 | 1 | 0 | 1 |
| cards:COLORLESS | 13 | 11 | 6 | 1 | 31 |
| cards:CURSE | 1 | 1 | 1 | 0 | 3 |
| cards:DEFECT | 11 | 3 | 22 | 0 | 36 |
| cards:EVENT | 7 | 7 | 4 | 0 | 18 |
| cards:IRONCLAD | 15 | 14 | 4 | 0 | 33 |
| cards:NECROBINDER | 14 | 12 | 10 | 0 | 36 |
| cards:REGENT | 11 | 12 | 15 | 0 | 38 |
| cards:SILENT | 19 | 24 | 4 | 0 | 47 |
| cards:STATUS | 0 | 1 | 0 | 0 | 1 |
| cards:TOKEN | 5 | 2 | 0 | 0 | 7 |
| core:OstyCmd | 0 | 0 | 1 | 0 | 1 |
| enchantments | 0 | 1 | 1 | 0 | 2 |
| modifiers | 1 | 1 | 0 | 0 | 2 |
| monsters:(no act: summoned/event) | 2 | 6 | 1 | 0 | 9 |
| monsters:Glory | 12 | 9 | 6 | 0 | 27 |
| monsters:Hive | 14 | 11 | 5 | 0 | 30 |
| monsters:Overgrowth | 6 | 8 | 5 | 0 | 19 |
| monsters:Underdocks | 12 | 7 | 6 | 0 | 25 |
| orbs | 0 | 0 | 1 | 0 | 1 |
| potions:DEFECT | 0 | 0 | 1 | 0 | 1 |
| potions:EVENT | 1 | 0 | 0 | 0 | 1 |
| potions:NECROBINDER | 0 | 1 | 0 | 0 | 1 |
| potions:SHARED | 8 | 14 | 2 | 0 | 24 |
| potions:SILENT | 2 | 1 | 0 | 0 | 3 |
| relics:DEFECT | 0 | 0 | 1 | 0 | 1 |
| relics:EVENT | 4 | 2 | 1 | 0 | 7 |
| relics:IRONCLAD | 2 | 0 | 0 | 0 | 2 |
| relics:NECROBINDER | 0 | 1 | 0 | 0 | 1 |
| relics:REGENT | 1 | 0 | 0 | 0 | 1 |
| relics:SHARED | 5 | 5 | 2 | 0 | 12 |
| relics:SILENT | 2 | 3 | 0 | 0 | 5 |

## 4. Kernel prerequisites (what must exist before which batch)

Non-mock counts. Full per-power lists: `kernel_requirements` in the JSON.

| Group | Requirement | Powers |
|---|---|--:|
| hook parameters | `HookParam.SideTurnParticipants` (side-turn hooks must pass the creatures taking the turn) | 73 |
| missing hooks | `Hook.ModifyCardPlayResultLocation` + `Hook.AfterModifyingCardPlayResultLocation` (`CardLocation` struct; the after-hook is dispatched from card-play code, not `Hook.cs`): Corruption, Feral, Nostalgia, Rebound | 4 |
| | `Hook.AfterCombatEnd`: ForbiddenGrimoire, Improvement, Royalties (Illusion's override is animation-only) | 3 |
| | `Hook.AfterAutoPrePlayPhaseEntered` (Mayhem), `Hook.AfterAutoPostPlayPhaseEntered` (Stampede) and the auto play phases in the turn loop | 2 |
| | `Hook.AfterStarsGained` (BlackHole), `Hook.AfterStarsSpent` (ChildOfTheStars), `Hook.AfterForge` (HammerTime), `Hook.ModifyOrbValue` (Focus), `Hook.AfterOrbEvoked` (Thunder), `Hook.TryModifyKeywordsInCombat` (Hex), `Hook.BeforePotionUsed` (Surrounded), `Hook.AfterDiedToDoom` dispatcher (Doom) | 8 |
| missing Cmds | `CardCmd.ClearAffliction` 7, `CardCmd.Afflict` 6, `CardCmd.AfflictAndPreview` 1, `CardCmd.ApplyKeyword` 3, `CardCmd.Upgrade` 3, `CardCmd.AutoPlay` 3, `CardCmd.Downgrade` 1, `CardCmd.TransformToRandom` 1, `CardCmd.Discard` (partial) 1 | |
| | `CardPileCmd.AddGeneratedCardsToCombat` 4, `CardPileCmd.Add` (partial) 3, `CardPileCmd.AddToCombatAndPreview<T>` 2, `CardPileCmd.AutoPlayFromDrawPile` 1, `CardPileCmd.RemoveFromDeck` 1 | |
| | `CreatureCmd.Add` 3, `CreatureCmd.Escape` 1, `CreatureCmd.GainBlock(BlockVar)` 2 (CrabRage, ToricToughness), `CreatureCmd.Damage(targets, DamageVar, dealer)` 1 (TheBomb) | |
| | `OrbCmd.Channel` 4, `OrbCmd.EvokeLast` 1, `OrbCmd.Passive` 1, `OstyCmd.Summon` 3, `ForgeCmd.Forge` 2, `PlayerCmd.GainStars` 3, `PlayerCmd.LoseGold` 1, `CardSelectCmd.FromHand` 2, `CardSelectCmd.FromHandForDiscard` 1, `CardSelectCmd.FromCombatPile` 2 | |
| power model | per-instance mutable state (`InitInternalData` data classes, fields, mutated `DynamicVars`); card and creature references must become instance/creature ids | 39 |
| | `ITemporaryPower` (`OriginModel`, `InternallyAppliedPower`) on the 24 temporary-family powers; read by SleightOfFlesh and Illusion (and Misery, Rend, UnsettlingLamp outside powers) | 26 |
| | computed `Type`/`StackType`: temporary family (`IsPositive`), `ShrinkPower` (infinite when negative), `MonologuePower` (`StrengthApplied`) | |
| | `PowerModel.IsVisible`/`IsVisibleInternal` (Artifact skips invisible debuffs, `Models/PowerModel.cs:161-173`) | 1 |
| card model | `PlayerCombatState.AllCards` (10), current pile type of a card (6: FreeAttack/Power/Skill, Veilpiercer, VoidForm, Lethality), keyword query incl. in-combat modifiers (3), `CreateClone`/`CreateCloneForPlayer` (3), fixed-card creation (`Shiv.CreateInHand`, `Soul.Create`, `CreateCard<Dazed/SweepingGaze>`, `AddToCombatAndPreview<Wound/Wither>`), `IsClone`, `DeckVersion` | |
| creature / monster | `Pets`/`PetOwner`/`IsPet` (11; null-safe without Osty), `HpDisplay` (HardenedShell writes, Hellraiser reads), `MonsterModel.SetMoveImmediate` (Illusion, Reattach), `CombatState.PlayersTakingExtraTurn` (Rampart), `EncounterDef` state (BattlewornDummy `RanOutOfTime`, GremlinMercNormal `MarkGoldStolen`), monster-specific calls (17 powers) | |
| subsystems | combat history 12 (B4 9 + ChainsOfBinding, Ringing, Smoggy), afflictions 7, pool card generation 5 (+Entropy transform), summons 3, orbs 17, Osty 6, stars 6, forge 5, relic queries 2 (PaperPhrog, PaperKrane), potions runtime 1, run layer 7 | |
| prompts | choice API with turn-setup deferral (4 at player turn start / hand draw) and inline (Stratagem inside card plays) | 5 |

Unmet requirements per batch (non-present only): B1 none beyond per-instance state, a card's
current pile, keyword queries and card defs (Hang, Soul); B2 participants 39, temporary family 17, Pets/PetOwner 4,
`AfterDiedToDoom`, relic queries 2, the BlockVar/DamageVar overloads; B3 auto-play, the
result-location hook pair, keywords, clones, auto play phases; B4 combat history 9; B5 afflictions
7, card factory 5, summons 2; B6 orbs/Osty/stars/forge; B7 the choice API; B8 run layer; B9
monster coupling 15 plus participants 10.

## 5. Dependency order

Earliest power batch after which each dependent model has all its powers (`dependent_referrers`;
"ported" = depends only on already-ported powers; non-mock models):

| content kind | ported only | B1 | B2 | B3 | B4 | B5 | B6 | B7 | B8 | B9 | B10 | all |
|---|--:|--:|--:|--:|--:|--:|--:|--:|--:|--:|--:|--:|
| cards | 44 | 58 | 69 | 10 | 9 | 5 | 34 | 5 | 3 | 1 | 17 | 255 |
| monsters | 29 | 8 | 14 | 0 | 0 | 8 | 0 | 0 | 2 | 36 | 0 | 97 |
| relics | 21 | 4 | 8 | 0 | 0 | 0 | 1 | 0 | 0 | 0 | 0 | 34 |
| potions | 8 | 6 | 12 | 0 | 0 | 0 | 1 | 0 | 0 | 0 | 0 | 27 |
| modifiers | 1 | 0 | 1 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 2 |
| enchantments | 1 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 1 |
| afflictions | 0 | 0 | 0 | 0 | 0 | 1 | 0 | 0 | 0 | 0 | 0 | 1 |
| orbs | 0 | 0 | 0 | 0 | 0 | 0 | 1 | 0 | 0 | 0 | 0 | 1 |

Most depended-on powers (port these first within their batch):

| power | tier | batch | ported | cards | monsters | relics | potions | powers | all |
|---|--:|--:|---|--:|--:|--:|--:|--:|--:|
| StrengthPower | 1 | 1 | yes | 13 | 50 | 14 | 2 | 15 | 95 |
| VulnerablePower | 3 | 2 | yes | 24 | 9 | 2 | 2 | 2 | 39 |
| WeakPower | 3 | 2 | yes | 20 | 12 | 1 | 2 | 1 | 37 |
| DexterityPower | 1 | 1 | yes | 5 | 2 | 5 | 2 | 4 | 18 |
| DoomPower | 2 | 2 |  | 8 | 0 | 1 | 1 | 6 | 16 |
| FrailPower | 1 | 1 | yes | 1 | 15 | 0 | 0 | 0 | 16 |
| PoisonPower | 2 | 2 |  | 8 | 0 | 2 | 1 | 4 | 15 |
| TemporaryStrengthPower | 2 | 2 |  | 0 | 0 | 0 | 0 | 14 | 14 |
| PlatingPower | 2 | 2 |  | 3 | 5 | 1 | 1 | 1 | 12 |
| ArtifactPower | 2 | 2 |  | 1 | 8 | 1 | 0 | 0 | 10 |
| EnergyNextTurnPower | 1 | 1 |  | 10 | 0 | 0 | 0 | 0 | 10 |
| MinionPower | 1 | 1 |  | 0 | 6 | 0 | 0 | 1 | 7 |
| DrawCardsNextTurnPower | 2 | 2 |  | 5 | 0 | 0 | 0 | 1 | 6 |
| FocusPower | 3 | 6 |  | 2 | 0 | 1 | 1 | 2 | 6 |
| IntangiblePower | 1 | 1 |  | 2 | 2 | 0 | 1 | 1 | 6 |
| ThornsPower | 1 | 1 | yes | 2 | 2 | 1 | 1 | 0 | 6 |
| VigorPower | 1 | 1 |  | 2 | 1 | 1 | 0 | 1 | 5 |
| BlockNextTurnPower | 1 | 1 |  | 3 | 0 | 0 | 1 | 0 | 4 |
| RetainHandPower | 2 | 2 |  | 3 | 0 | 0 | 1 | 0 | 4 |
| RitualPower | 2 | 2 |  | 0 | 3 | 0 | 1 | 0 | 4 |
| TemporaryDexterityPower | 2 | 2 |  | 0 | 0 | 0 | 0 | 4 | 4 |
| TemporaryFocusPower | 3 | 6 |  | 0 | 0 | 0 | 0 | 4 | 4 |

Power→power chains that fix the order inside batches:

- `TemporaryStrengthPower` → 13 subclasses (+1 mock), `TemporaryDexterityPower` → 4,
  `TemporaryFocusPower` → 4 (needs `FocusPower`, so it sits with orbs in B6). The bases apply the
  stat in `BeforeApplied`, re-stacks apply the delta in `AfterPowerAmountChanged`, and the stat is
  reverted after `PowerCmd.Remove` at the owner's side turn end.
- `AccelerantPower` → `PoisonPower` (TriggerCount) → Envenom, CorrosiveWave, NoxiousFumes, Concoct;
  `PoisonPower.Trigger()` is also called by the Outbreak card.
- `DoomPower` → Countdown, Neurosurge, Oblivion, ReaperForm, Shroud, Underworld (and the
  `DoomKill`/`GetDoomedCreatures` statics used by cards such as EndOfDays and TimesUp).
- `CrueltyPower`, `DebilitatePower` → read by `VulnerablePower`/`WeakPower` through the existing
  `IVulnerableMultiplierModifier`/`IWeakMultiplierModifier` seams; `ColossusPower` and `ViciousPower`
  read `VulnerablePower`; `TrackingPower` reads `WeakPower`.
- `DoubleDamagePower` ← ShadowStep; `DrawCardsNextTurnPower` ← PaleBlueDot; `VigorPower` ← PrepTime;
  `IntangiblePower` ← Nemesis; `MinionPower` ← Illusion; `HeistPower`/`ThieveryPower` ← Surprise;
  `BackAttackLeft`/`BackAttackRight` ← Surrounded; `PlatingPower` ← Asleep; `InterceptPower` ←
  Covered; `GuardedPower` ← Tank; `TaintedPower` ← VitalSpark.
- Card defs needed by powers: SovereignBlade (Conqueror, SwordSage, Furnace via Forge; Parry and
  SeekingEdge are its markers), Shiv (InfiniteBlades; FanOfKnives is its marker), Soul (DevourLife,
  Haunt, Soulbound), Hang, Omnislice (Thorns), Dazed, Wound, Wither, SweepingGaze.

## 6. Hook virtuals used by powers

Overrides counted per power file (inherited overrides included, so the 24 temporary-family game
powers and the temporary mock count for `AfterPowerAmountChanged` and `AfterSideTurnEnd`). Scope from `hooks.json`.

| hook virtual | AbstractModel.cs line | scope | in sim | powers | mocks |
|---|--:|---|---|--:|--:|
| AfterSideTurnEnd | 683 | combat | yes | 82 | 2 |
| AfterPowerAmountChanged | 563 | combat | yes | 32 | 1 |
| AfterCardPlayed | 298 | combat | yes | 29 | 1 |
| BeforeCardPlayed | 293 | combat | yes | 22 | 0 |
| ModifyDamageMultiplicative | 748 | both | yes | 20 | 1 |
| AfterDeath | 373 | both | yes | 19 | 1 |
| AfterSideTurnStart | 643 | combat | yes | 19 | 0 |
| AfterDamageReceived | 358 | both | yes | 14 | 0 |
| AfterPlayerTurnStart | 658 | combat | yes | 9 | 2 |
| AfterCardDrawn | 268 | combat | yes | 8 | 1 |
| AfterCardEnteredCombat | 273 | combat | yes | 8 | 0 |
| AfterDamageGiven | 348 | combat | yes | 8 | 0 |
| BeforeHandDraw | 423 | combat | yes | 8 | 1 |
| ModifyDamageAdditive | 738 | both | yes | 8 | 0 |
| ModifyHandDraw | 773 | combat | yes | 7 | 0 |
| AfterEnergyReset | 383 | combat | yes | 6 | 0 |
| AfterModifyingCardPlayCount | 453 | combat | yes | 6 | 0 |
| BeforeSideTurnStart | 638 | combat | yes | 6 | 0 |
| ModifyCardPlayCount | 708 | combat | yes | 6 | 0 |
| ShouldCreatureBeRemovedFromCombatAfterDeath | 1077 | combat | yes | 6 | 1 |
| TryModifyEnergyCostInCombatLate | 914 | combat | yes | 6 | 1 |
| AfterAttack | 203 | combat | yes | 5 | 1 |
| AfterCardGeneratedForCombat | 278 | combat | yes | 5 | 0 |
| BeforeSideTurnEnd | 678 | combat | yes | 5 | 1 |
| ShouldStopCombatFromEnding | 1082 | combat | yes | 5 | 2 |
| AfterCombatEnd | 318 | both | **no** | 4 | 0 |
| AfterModifyingCardPlayResultLocation | 458 | unknown | **no** | 4 | 0 |
| ModifyBlockMultiplicative | 703 | combat | yes | 4 | 0 |
| ModifyCardPlayResultLocation | 713 | combat | **no** | 4 | 0 |
| ModifyMaxEnergy | 803 | combat | yes | 4 | 0 |
| ShouldAllowHitting | 977 | combat | yes | 4 | 2 |
| ShouldPlay | 1042 | combat | yes | 4 | 0 |
| AfterBlockCleared | 228 | combat | yes | 3 | 0 |
| AfterModifyingDamageAmount | 473 | both | yes | 3 | 0 |
| BeforeAttack | 198 | combat | yes | 3 | 0 |
| ShouldClearBlock | 992 | combat | yes | 3 | 0 |
| TryModifyEnergyCostInCombat | 908 | combat | yes | 3 | 1 |
| AfterBlockGained | 238 | combat | yes | 2 | 0 |
| AfterCardExhausted | 283 | combat | yes | 2 | 0 |
| AfterModifyingHpLostAfterOsty | 503 | both | yes | 2 | 0 |
| BeforeCombatStart | 308 | both | yes | 2 | 0 |
| BeforeDeath | 368 | both | yes | 2 | 0 |
| BeforeSideTurnEndEarly | 673 | combat | yes | 2 | 0 |
| ModifyBlockAdditive | 698 | combat | yes | 2 | 0 |
| ModifyDamageCap | 743 | both | yes | 2 | 0 |
| ModifyHpLostAfterOsty | 793 | both | yes | 2 | 0 |
| ShouldFlush | 1022 | combat | yes | 2 | 0 |
| AfterAutoPostPlayPhaseEntered | 208 | combat | **no** | 1 | 1 |
| AfterAutoPrePlayPhaseEntered | 218 | combat | **no** | 1 | 1 |
| AfterBlockBroken | 243 | combat | yes | 1 | 0 |
| AfterCardDrawnEarly | 263 | combat | yes | 1 | 0 |
| AfterCreatureAddedToCombat | 338 | combat | yes | 1 | 0 |
| AfterCurrentHpChanged | 343 | both | yes | 1 | 0 |
| AfterEnergySpent | 393 | combat | yes | 1 | 0 |
| AfterForge | 618 | combat | **no** | 1 | 0 |
| AfterModifyingBlockAmount | 448 | combat | yes | 1 | 0 |
| AfterModifyingEnergyGain | 478 | combat | yes | 1 | 0 |
| AfterModifyingHandDraw | 488 | combat | yes | 1 | 0 |
| AfterModifyingHpLostBeforeOsty | 498 | both | yes | 1 | 0 |
| AfterModifyingPowerAmountReceived | 508 | combat | yes | 1 | 0 |
| AfterOrbEvoked | 528 | combat | **no** | 1 | 0 |
| AfterOstyRevived | 533 | combat | **no** | 1 | 0 |
| AfterPreventingBlockClear | 568 | combat | yes | 1 | 0 |
| AfterShuffle | 603 | combat | yes | 1 | 0 |
| AfterSideTurnEndLate | 688 | combat | yes | 1 | 0 |
| AfterSideTurnStartLate | 648 | combat | yes | 1 | 0 |
| AfterStarsGained | 613 | combat | **no** | 1 | 0 |
| AfterStarsSpent | 608 | combat | **no** | 1 | 0 |
| AfterTakingExtraTurn | 628 | combat | yes | 1 | 1 |
| BeforeDamageReceived | 353 | both | yes | 1 | 0 |
| BeforePotionUsed | 538 | both | **no** | 1 | 0 |
| BeforePowerAmountChanged | 558 | combat | yes | 1 | 0 |
| BeforeSideTurnEndVeryEarly | 668 | combat | yes | 1 | 0 |
| ModifyEnergyGain | 753 | combat | yes | 1 | 0 |
| ModifyHpLostAfterOstyLate | 798 | both | yes | 1 | 0 |
| ModifyHpLostBeforeOstyLate | 788 | both | yes | 1 | 0 |
| ModifyOrbValue | 827 | combat | **no** | 1 | 0 |
| ModifyUnblockedDamageTarget | 856 | combat | yes | 1 | 0 |
| ShouldDraw | 1012 | combat | yes | 1 | 0 |
| ShouldPowerBeRemovedOnDeath | 1062 | combat | yes | 1 | 0 |
| ShouldTakeExtraTurn | 1087 | combat | yes | 1 | 1 |
| TryModifyKeywordsInCombat | 920 | combat | **no** | 1 | 0 |
| TryModifyPowerAmountReceived | 931 | combat | yes | 1 | 1 |
| TryModifyStarCost | 925 | combat | yes | 1 | 1 |
| AfterCardChangedPiles | 248 | both | **no** | 0 | 2 |
| AfterPreventingDeath | 573 | both | yes | 0 | 2 |
| BeforeFlush | 403 | combat | yes | 0 | 1 |
| ShouldDie | 997 | both | yes | 0 | 2 |

Other `PowerModel` members overridden by game powers: `ExtraHoverTips` 100 (UI), `CanonicalVars`
38, `InitInternalData` 34, `InstanceType` 23 (`Instanced` 21, `InstancedPerApplier` 2), `AfterApplied`
23 (12 rule, 11 UI/loc only), `OriginModel` 21, `AfterRemoved` 15 (8 rule), `DisplayAmount` 13 (UI),
`ShouldScaleInMultiplayer` 12 and `GetScaledAmountForMultiplayer` 5 (multiplayer only:
`Commands/PowerCmd.cs:550` scales only when `Players.Count > 1`), `IsPositive` 9,
`ShouldPowerBeRemovedAfterOwnerDeath` 6, `AllowNegative` 5, `BeforeApplied` 4,
`ShouldOwnerDeathTriggerFatal` 2, `IsVisibleInternal` 1 (Ambergris), `OwnerIsSecondaryEnemy` 1 (Minion).

## 7. Prompts

| Power | Type | Cmd | Source | Min | Max | Site | Follow-up |
|---|---|---|---|---|---|---|---|
| EntropyPower | HandSelect | `CardSelectCmd.FromHand` | Hand | Amount | Amount | AfterPlayerTurnStart | `CardCmd.TransformToRandom` per card (RunRng.CombatCardSelection) |
| ToolsOfTheTradePower | HandSelect | `CardSelectCmd.FromHandForDiscard` | Hand | Amount | Amount | AfterPlayerTurnStart | `CardCmd.Discard(selected)` if non-empty |
| TyrannyPower | HandSelect | `CardSelectCmd.FromHand` | Hand | Amount | Amount | AfterPlayerTurnStart | `CardCmd.Exhaust` per card |
| ForegoneConclusionPower | PileSelect | `CardSelectCmd.FromCombatPile` | Draw | Amount | Amount | BeforeHandDraw | `ShuffleIfNecessary` first; `CardPileCmd.Add(selected, Hand)`; remove itself |
| StratagemPower | PileSelect | `CardSelectCmd.FromCombatPile` | Draw | Amount | Amount | AfterShuffle | `CardPileCmd.Add` each to Hand |

Frequencies: HandSelect 3 (`FromHand` 2, `FromHandForDiscard` 1), PileSelect (draw pile) 2; sites
`AfterPlayerTurnStart` 3, `BeforeHandDraw` 1, `AfterShuffle` 1. All use
`CardSelectorPrefs(prompt, Amount)`, i.e. min = max = `Amount`
(`CardSelection/CardSelectorPrefs.cs:99-103`), clipped to the available cards, no filter, not
cancelable. Four run in the turn-setup choice context (deferred to the play phase); Stratagem runs
inline when the shuffle happens inside a card play. Indirect prompt sources: powers that auto-play
cards (Hellraiser, Mayhem, Stampede, ImitationLearning) surface the auto-played card's own prompts
inside their hook context; NightmarePower's choice is made by the Nightmare card. v0.107.1's
`WellLaidPlansPower` prompt (choose up to `Amount` cards to retain at `BeforeFlushLate`) no longer
exists; v0.111.0 retains the whole hand.

## 8. RNG

| power | stream (call, order) |
|---|---|
| AggressionPower | RunRng.CombatCardSelection (UnstableShuffle of the discard-pile Attack list, then Take(Amount)) |
| CacophonyPower | RunRng.CombatTargets (NextItem over HittableEnemies, drawn before the damage) |
| CalamityPower | RunRng.CombatCardGeneration (CardFactory.GetForCombat, Amount cards) |
| CallOfTheVoidPower | RunRng.CombatCardGeneration (one GetDistinctForCombat draw per card, so duplicates across cards are possible) |
| ConfusedPower | RunRng.CombatEnergyCosts (NextInt(4) per eligible drawn card) |
| CountdownPower | RunRng.CombatTargets (NextItem over HittableEnemies) |
| CreativeAiPower | RunRng.CombatCardGeneration (one GetDistinctForCombat(…,1,…) per card) |
| EntropyPower | RunRng.CombatCardSelection (passed to CardCmd.TransformToRandom for each selected card) |
| FlutterPower | RunRng.MonsterAi (GetNextState of the last logged move state when Flutter runs out) |
| ForegoneConclusionPower | RunRng.Shuffle (indirect: ShuffleIfNecessary reshuffles discard into draw when the draw pile is empty) |
| HauntPower | RunRng.CombatTargets (NextItem over HittableEnemies) |
| HelloWorldPower | RunRng.CombatCardGeneration (GetDistinctForCombat, AmountOnTurnStart cards) |
| HellraiserPower | RunRng.CombatTargets (indirect: CardCmd.AutoPlay with no target picks a random hittable enemy for AnyEnemy cards, Commands/CardCmd.cs:411) |
| ImprovementPower | RunRng.CombatCardSelection (NextItem per upgrade over upgradable deck cards) |
| JuggernautPower | RunRng.CombatTargets (NextItem over HittableEnemies) |
| MayhemPower | RunRng.CombatTargets (indirect: each auto-played AnyEnemy/AnyAlly card picks a random target in CardCmd.AutoPlay, Commands/CardCmd.cs:411/431) |
| PersonalHivePower | RunRng.Shuffle (CardPilePosition.Random insertion: NextInt(drawCount + 1) per Dazed, Commands/CardPileCmd.cs:311) |
| SerpentFormPower | RunRng.CombatTargets (NextItem over HittableEnemies per card play) |
| SoulboundPower | RunRng.Shuffle (CardPilePosition.Random insertion per Soul) |
| SpectrumShiftPower | RunRng.CombatCardGeneration (GetDistinctForCombat, Amount cards) |
| StampedePower | RunRng.Shuffle (NextItem over hand Attacks without Unplayable, once per iteration) |
| StampedePower | RunRng.CombatTargets (indirect: CardCmd.AutoPlay random target for AnyEnemy attacks, after each Shuffle pick, Commands/CardCmd.cs:411) |
| TrashToTreasurePower | RunRng.CombatOrbGeneration (OrbModel.GetRandomOrb per channel) |

Not listed: every draw a power triggers can reshuffle the discard pile through the kernel
(`RunRng.Shuffle`, `Commands/CardPileCmd.cs:2759`). `CardPilePosition.Random` insertion always uses
`RunRng.Shuffle.NextInt(count + 1)` (`Commands/CardPileCmd.cs:311`, `:3431`).

## 9. Version delta vs v0.107.1

154 same (identical token fingerprint: identifiers, dotted calls, numbers, strings, comparisons,
overrides, literal properties), 90 cosmetic (decompiler restructuring, hook signatures that gained
an unused `CardPlay` parameter, VFX-only overrides, values moved into DynamicVars), 14 rule changes,
10 new game powers (Ambergris, Cacophony, Concoct, Fade, Hibernate, HyperbeamFocusDown,
ImitationLearning, OneForAll, Soulbound, Underworld) and 2 new mocks. Removed in v0.111.0:
`DiamondDiademPower` (the DiamondDiadem relic now applies `BlurPower`) and `OutbreakPower` (the
Outbreak card calls `PoisonPower.Trigger()`). The card-play result hook was renamed
(`ModifyCardPlayResultPileTypeAndPosition` → `ModifyCardPlayResultLocation` with `CardLocation`);
ports must use the v0.111.0 names.

| power | v0.107.1 -> v0.111.0 |
|---|---|
| DoomPower | v0.107.1 killed at BeforeSideTurnEnd of the owner's side for both sides; v0.111.0 keeps that for enemies but kills player-side creatures at AfterSideTurnEnd(Player) |
| FeralPower | v0.111.0 adds the IsDupe exclusion (dupes no longer return to hand) on top of the CardLocation hook refactor |
| JugglingPower | v0.107.1 counted and cloned at AfterCardPlayed; v0.111.0 at BeforeCardPlayed (clones made before the 3rd attack resolves) |
| PaleBlueDotPower | v0.107.1 raised the hand draw by Amount if 5+ cards were finished last player turn (ModifyHandDraw); v0.111.0 applies DrawCardsNextTurnPower once 5 plays finish this turn |
| RegenPower | v0.107.1 healed at AfterSideTurnEnd; v0.111.0 heals at BeforeSideTurnEndEarly |
| SurprisePower | v0.107.1 added Sneaky then Fat Gremlin and applied Heist afterwards; v0.111.0 creates the Fat Gremlin first, applies Heist before it joins, then adds Sneaky and Fat (creation/apply order changed) |
| TankPower | v0.107.1 multiplied incoming powered damage by 2; v0.111.0 by DamageIncrease 1.5 |
| TemporaryDexterityPower | v0.107.1 had IgnoreNextInstance (Misery copies skipped the internal stat once); v0.111.0 removed it and Misery now skips the InternallyAppliedPower itself; standalone behaviour unchanged |
| TemporaryFocusPower | v0.107.1 had IgnoreNextInstance (Misery copies skipped the internal stat once); v0.111.0 removed it and Misery now skips the InternallyAppliedPower itself; standalone behaviour unchanged |
| TemporaryStrengthPower | v0.107.1 had IgnoreNextInstance (Misery copies skipped the internal stat once); v0.111.0 removed it and Misery now skips the InternallyAppliedPower itself; standalone behaviour unchanged |
| TrackingPower | v0.107.1 multiplied by Amount (Tracking applied 2, then +1); v0.111.0 multiplies by 1 + Amount/100 (Tracking applies 50) |
| UnmovablePower | v0.107.1 counted every card/move block entry whose actor was the target; v0.111.0 only entries from the owner's card plays (CardPlay != null) |
| VulnerablePower | v0.111.0 adds the pet-owner fallback for CrueltyPower (Osty attacks use the owner's Cruelty); ModifyDamageMultiplicative also gained a CardPlay parameter |
| WellLaidPlansPower | v0.107.1 (Counter) prompted at BeforeFlushLate to choose up to Amount cards to retain (CardSelectCmd.FromHand, single-turn retain); v0.111.0 (Single) retains the whole hand via ShouldFlush, no prompt |

New in v0.111.0: AmbergrisPower, CacophonyPower, ConcoctPower, FadePower, HibernatePower, HyperbeamFocusDownPower, ImitationLearningPower, MockGatedTurnStartPower, MockRecordCardChangedPilesPower, OneForAllPower, SoulboundPower, UnderworldPower

## 10. Single-player scope

Multiplayer-only (19; every applier is a `MultiplayerOnly` card, filtered out of single-player pools
by `CardPoolModel.GetUnlockedCards(…, CardMultiplayerConstraint)`): BeaconOfHope, Cacophony,
Concoct, Coordinate, Covered, Fade, Flanking, Guarded, HammerTime, Hibernate (kept in B6 for
FrostOrb), ImitationLearning, Intercept, Knockdown, OneForAll, Sneaky, Soulbound, TagTeam, Tank,
Underworld. Several are also inert even if reached in single player (BeaconOfHope, Guarded,
HammerTime, Sneaky, TagTeam, Underworld only affect other players).

Unreferenced (4): GravityPower, LeadershipPower, MagicBombPower (no referrer in either version),
NoEnergyGainPower (its only v0.107.1 applier, ExpectAFight, no longer applies it).

## 11. Decompile readability

No power needs a re-decompile with reference assemblies. 208 of the 286 v0.111.0 files carry
"Unknown result type (might be due to invalid IL or missing references)" comments on awaiter
locals, which do not affect the logic. All async bodies decompiled as state machines and were read
after condensing (`tmp/sim-port/powers/condense.py`); 274 files were also checked against the
v0.107.1 decompile, which has async/await reconstructed. The only other warning,
`DoomPower.cs:607` ("Expected O, but got Unknown"), and the `InlineArray`/`PrivateImplementationDetails`
code next to it are in `StartDoomAnim`, which is UI-only (the only `OnDieToDoom` override,
TorchHeadAmalgam, toggles visuals). A reference-assembly decompile of v0.111.0 would still speed up
review of the 12 new files.

## 12. Porting traps found while reading

- `participants` is not the same as `side`: many powers tick on `side == CombatSide.Enemy`
  (Vulnerable, Weak, Frail, Colossus, Intangible, Covered, Intercept, NoBlock, Tainted, Underworld)
  or on `Owner.Side != side` (FlameBarrier, Concoct, Skittish), not on participants.
- DoomPower kills enemy-side creatures at `BeforeSideTurnEnd(Enemy)` but player-side creatures
  (e.g. self-Doom from Neurosurge) at `AfterSideTurnEnd(Player)` (`Models.Powers/DoomPower.cs:524`,
  `:539`); the first doomed creature's power kills every doomed creature on that side at once.
- `IllusionPower.ShouldPowerBeRemovedOnDeath` (`Models.Powers/IllusionPower.cs:296`) is consulted for
  every dying creature (`Hooks/Hook.cs:11741` iterates all listeners): while an Illusion monster is
  alive, nobody loses buffs or temporary debuffs on death.
- `CreatureState.HpDisplay` is rule state: HardenedShell (`HardenedShellPower.cs:66`, `:75`) and
  WaterfallGiant (`Models.Monsters/WaterfallGiant.cs:918`) write it; Hellraiser caps auto-plays at 9
  per turn when every hittable enemy shows infinite HP (`HellraiserPower.cs:73`).
- `VigorPower.AfterAttack` never clears `commandToModify`; Vigor that survives an attack (gained
  mid-attack) gives no bonus to later card attacks. `GigantificationPower` does clear it.
- `SuckPower` mutates the AttackCommand's per-hit result lists (`RemoveAll`), so later `AfterAttack`
  listeners see trimmed results.
- `VoidFormPower.HideTemporaryZeroCostVisual` is named like UI but sets the rule counter to
  999999999 on apply/re-stack, so Void Form only starts at the owner's next turn.
- Snapshot-at-`BeforeCardPlayed` pattern (Afterimage, Calamity, Gravity, Monologue, Oblivion,
  Rupture, SerpentForm, Storm, Strangle, Subroutine): keyed by the card, one entry per play
  iteration; replays and powers applied mid-play behave differently from a naive `AfterCardPlayed`.
- History ordering: `ModifyCardPlayResultLocation` and `GeneratePlayCount` run before the play's
  `CardPlayStarted` entry (`Models/CardModel.cs:936`, `:543`, `:665`), `CardPlayFinished` is recorded
  before `AfterCardPlayed`, and `CardDrawn` before `AfterCardDrawn` (EchoForm, Nostalgia,
  PaleBlueDot, Iteration rely on this).
- No power uses `ShouldPlay`'s `AutoPlayType` argument (ChainsOfBinding, Ringing, Sloth, Smoggy
  ignore it).
- `PoisonPower.Trigger` keeps its precomputed iteration count after the owner dies; ConfusedPower
  consumes a `CombatEnergyCosts` roll for every drawn card with canonical cost >= 0, X-cost cards
  included; `TheBomb`,
  `CrabRage` and `ToricToughness` use the DamageVar/BlockVar Cmd overloads the sim lacks.
- `RollingBoulderPower` (`RollingBoulderPower.cs:199`): TestMode does one multi-target Damage; the
  real game starts one single-target Damage per creature as the boulder VFX reaches it (left to
  right) and awaits them together.

## 13. Open questions

- Single-player scope assumption: `MultiplayerOnly` cards are filtered out by `CardPoolModel.GetUnlockedCards(UnlockState, CardMultiplayerConstraint)`; confirm no single-player path (event, relic, transform, `CreateCard<T>`) can still produce one, otherwise the 19 multiplayer-only powers move back to their natural batches.
- Auto-play inside hook contexts: Mayhem (AfterAutoPrePlayPhaseEntered), Stampede (AfterAutoPostPlayPhaseEntered) and Hellraiser (AfterCardDrawnEarly) auto-play cards whose own prompts would open inside the power's hook context; the choice API design must say which context (turn-setup deferral, end-of-turn, inline) those prompts use.
- The history-ordering claims (ModifyCardPlayResultLocation and GeneratePlayCount before CardPlayStarted; CardPlayFinished before AfterCardPlayed; CardDrawn before AfterCardDrawn) were read in the v0.107.1 CardModel/CardPileCmd and matched to v0.111.0 line positions (`Models/CardModel.cs:543`, `:665`, `:735`, `:739`, `:936`); the kernel port should re-confirm them from the v0.111.0 state machine.
- Misery (card) and the temporary-power family changed together in v0.111.0 (IgnoreNextInstance removed): the cards classifier should confirm Misery's v0.111.0 copy rule so the temporary powers are ported without the old seam.
- PlatingPower.AfterApplied sets the enemy Decrement var to `RunState.Players.Count`; single player gives 1, but confirm `RunState.Players` (not `CombatState.Players`) is what the sim's run context will expose.
- GravityPower has no referrer in either decompile; confirm it is unreachable content before spending port effort.
- LeadershipPower has no referrer in either decompile; confirm it is unreachable before porting.
- MagicBombPower has no referrer in either decompile; confirm it is unreachable before porting.
- NoEnergyGainPower lost its only applier (ExpectAFight) in v0.111.0; confirm nothing else reaches it.
- RollingBoulderPower: the real-game damage order and interleaving come from VFX signals (creature node x order, concurrently started tasks); decide whether the sim emulates per-creature hits in screen order or the TestMode multi-target call, and verify against recordings with 2+ enemies.
- WitheringPresencePower: confirm the Wither added by AddToCombatAndPreview<Wither> is not upgraded to match Aeonglass elsewhere (only the hover-tip path calls MatchWitherToUpgradeCount).
