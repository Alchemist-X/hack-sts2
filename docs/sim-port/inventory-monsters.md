# Monster inventory: semantic classification (game v0.111.0)

2026-11-01 · W0 classification for the Route C full port · machine-readable companion: [`sim/specs/inventory/monsters.classified.json`](../../sim/specs/inventory/monsters.classified.json) (same entry order and ids as [`monsters.json`](../../sim/specs/inventory/monsters.json)). Rule truth: decompile tree v0.111.0 (`/Users/Aincrad/dev-proj/hack-sts2/output/server-bootstrap/linux-decompiled/`), cited as `<ns-dir>/<File>.cs:<line>` with the `MegaCrit.Sts2.Core.` prefix dropped; the v0.107.1 tree was used only to read async bodies in source order and for the version delta. Encounters and acts: [`inventory-encounters.md`](inventory-encounters.md).

## 0. Summary

- 127 manifest entries: 6 mocks, 121 content-or-support models. Of those, 108 are encounter/spawn content (11 ported in P0), 3 player-side pets (Osty, Byrdpip, PaelsLegion), 5 test monsters, 3 unused (TheAdversaryMk1–3), 1 deprecated and 1 abstract base (DecimillipedeSegment).
- Own-code tier over the 111 real monsters (pets included): tier 1 = 76, tier 2 = 16, tier 3 = 19, tier 4 = 0. Effective tier (including the power, affliction and enchantment dependencies): 1 = 49, 2 = 35, 3 = 27, 4 = 0.
- Port batches (all 127 entries): B0 = 11, B1 = 61, B2 = 8, B3 = 6, B4 = 8, B5 = 8, B6 = 6, B7 = 1, B8 = 3, B9 = 15.
- Player prompts: exactly one site, KnowledgeDemon's CURSE_OF_KNOWLEDGE (`ChooseCard`, pick 1 of 2 generated cards, no skip).
- Decompile-unreadable: one rule-relevant blob, `KnowledgeDemon._disintegrationDamageValues` (`int[3]`, LdMemberToken), recovered as {6, 7, 8} from the IL-identical macOS v0.111.0 decompile (section 9).
- v0.107.1 → v0.111.0: 95 same, 22 cosmetic, 10 rule-change (section 7).
- Ascension: monster and encounter files use only `ToughEnemies` (A8: HP, some block/plating/Strength) and `DeadlyEnemies` (A9: damage, debuff and buff amounts). There are no A18+ branches: maximum ascension is 10 (`Entities.Ascension/AscensionManager.cs:8`, enum `Entities.Ascension/AscensionLevel.cs:3-16`).

## 1. Tiers and batches

| Tier | Meaning |
| --- | --- |
| 1 | pure numbers through facades the sim already has (DamageCmd.Attack, CreatureCmd.GainBlock/Heal/Kill/Stun, PowerCmd.Apply/Remove, move state machine) |
| 2 | needs facades or hooks that exist in the game but not in the sim |
| 3 | needs a player prompt or a new subsystem (summons, revive, pets, afflictions, run-layer effects) |
| 4 | decompile-unreadable data or cross-cutting |

`tier` grades the monster file itself. `effective_tier` = max(tier, tier of every unported power it applies or consults, taken from [`powers.classified.json`](../../sim/specs/inventory/powers.classified.json) (`deps.powers[].powers_classified`); a mechanical scan of each power file is kept as `tier_hint`/`features`). Any affliction or enchantment dependency makes it ≥ 3. Already-ported powers count as satisfied. `effective_tier_from` names the dependencies that raise it. Rocket is the only monster whose effective tier (3, from SurroundedPower: per-instance Facing plus `Hook.BeforePotionUsed`) exceeds what its batch (B1) implies; KaiserCrabBoss inherits that.

| Batch | Contents |
| --- | --- |
| B0 | ported in P0 (sim/Sts2Sim.Core/Monsters/Defs, Monsters/Encounters) |
| B1 | plain: own code uses ported facades only; power deps need at most W1 hook work (side-turn participants, power internal data) |
| B2 | kernel facades: monster-as-listener hooks, generic CardPileCmd.AddToCombatAndPreview<T>, CreatureCmd.Escape, raw HP writes |
| B3 | afflictions: depends on affliction-applying powers (Galvanized, Tainted, Smog, Hexed, Entangled, Ringing, Bound) |
| B4 | summons: mid-combat CreatureCmd.Add into encounter slots (own move or a death power) |
| B5 | revive / dead-but-present creatures and multi-phase bosses (Reattach, Illusion, Adaptable, SteamEruption) |
| B6 | run-layer coupled: gold theft, deck theft, extra combat rewards, event custom state |
| B7 | player prompt during the enemy turn and/or decompile-broken data (KnowledgeDemon) |
| B8 | player-side pets (Osty, relic pets): ship with the Osty/pet subsystem and their cards/relics |
| B9 | not content: test monsters, mocks, deprecated or unused models (port only as test seams) |

Batches are cumulative: batch N assumes the kernel features of batches < N. A monster's batch is the highest category it needs (prompt > run-layer > revive > summon > afflictions > facades > plain). Summoned monsters keep their own batch (they can be unit-tested through the spawn seam), while an encounter's batch is the max over everything it spawns (encounters doc).

## 2. Counts by act and pool

Distinct monsters reachable from each act pool (initial composition, random picks and mid-combat spawns), by effective tier. A monster in several pools is counted in each.

| Act | Pool | Monsters | T1 | T2 | T3 | T4 |
| --- | --- | --- | --- | --- | --- | --- |
| Overgrowth | weak | 7 | 7 | 0 | 0 | 0 |
| Overgrowth | normal | 21 | 16 | 3 | 2 | 0 |
| Overgrowth | elite | 4 | 0 | 3 | 1 | 0 |
| Overgrowth | boss | 4 | 2 | 1 | 1 | 0 |
| Underdocks | weak | 4 | 4 | 0 | 0 | 0 |
| Underdocks | normal | 14 | 4 | 6 | 4 | 0 |
| Underdocks | elite | 3 | 2 | 1 | 0 | 0 |
| Underdocks | boss | 3 | 1 | 1 | 1 | 0 |
| Hive | weak | 6 | 5 | 0 | 1 | 0 |
| Hive | normal | 15 | 7 | 6 | 2 | 0 |
| Hive | elite | 5 | 0 | 1 | 4 | 0 |
| Hive | boss | 4 | 0 | 2 | 2 | 0 |
| Glory | weak | 4 | 2 | 2 | 0 | 0 |
| Glory | normal | 15 | 8 | 4 | 3 | 0 |
| Glory | elite | 5 | 2 | 2 | 1 | 0 |
| Glory | boss | 4 | 1 | 1 | 2 | 0 |

Not in any act pool: event-only combats (Architect, BattleFriendV1–3, FakeMerchantMonster, MysteriousKnight), pets (3), test/unused/deprecated models. TunnelerNormal (Chomper + Tunneler) is registered but listed in no act in either version.

## 3. Port batches and dependency order

| Batch | # | Monsters |
| --- | --- | --- |
| B0 | 11 | CorpseSlug, FuzzyWurmCrawler, LeafSlimeM, LeafSlimeS, Nibbit, Seapunk, ShrinkerBeetle, SludgeSpinner, Toadpole, TwigSlimeM, TwigSlimeS |
| B1 | 61 | Architect, AssassinRubyRaider, AxeRubyRaider, BowlbugEgg, BowlbugNectar, BowlbugRock, BowlbugSilk, BruteRubyRaider, BygoneEffigy, Byrdonis, CalcifiedCultist, CrossbowRubyRaider, Crusher, CubexConstruct, DampCultist, DevotedSculptor, Entomancer, Exoskeleton, FakeMerchantMonster, FlailKnight, Flyconid, FossilStalker, FrogKnight, GasBomb, Guardbot, HunterKiller, Inklet, KinFollower, KinPriest, LagavulinMatriarch, LivingShield, LouseProgenitor, MagiKnight, Mawler, MysteriousKnight, Noisebot, OwlMagistrate, PhantasmalGardener, PunchConstruct, Rocket, ScrollOfBiting, SewerClam, SkulkingColony, SlimedBerserker, SlitheringStrangler, SlumberingBeetle, SnappingJaxfruit, SneakyGremlin, SoulFysh, SoulNexus, SpinyToad, Stabbot, TerrorEel, TheForgotten, TheInsatiable, TheLost, TorchHeadAmalgam, TrackerRubyRaider, Tunneler, TurretOperator, Zapbot |
| B2 | 8 | Aeonglass, Chomper, HauntedShip, MechaKnight, Myte, ToughEgg, Vantom, Wriggler |
| B3 | 6 | CeremonialBeast, GlobeHead, InfestedPrism, Queen, SpectralKnight, VineShambler |
| B4 | 8 | Axebot, Fabricator, Fogmog, LivingFog, Ovicopter, PhrogParasite, TheObscura, TwoTailedRat |
| B5 | 8 | DecimillipedeSegment, DecimillipedeSegmentBack, DecimillipedeSegmentFront, DecimillipedeSegmentMiddle, EyeWithTeeth, Parafright, TestSubject, WaterfallGiant |
| B6 | 6 | BattleFriendV1, BattleFriendV2, BattleFriendV3, FatGremlin, GremlinMerc, ThievingHopper |
| B7 | 1 | KnowledgeDemon |
| B8 | 3 | Byrdpip, Osty, PaelsLegion |
| B9 | 15 | BigDummy, DeprecatedMonster, MockArtifactMonster, MockAttackAndSummonMinionMonster, MockAttackMonster, MockIntangibleMonster, MockPlatingMonster, MockReattachMonster, MultiAttackMoveMonster, OneHpMonster, SingleAttackMoveMonster, TenHpMonster, TheAdversaryMkOne, TheAdversaryMkThree, TheAdversaryMkTwo |

### 3.1 What must exist before each batch

Kernel (W1) and content (W3 powers/cards) prerequisites, from `kernel_features`, `kernel_features_via_powers` and `deps`:

| Batch | Prerequisites |
| --- | --- |
| B1 | W1: side-turn `participants` restored (Ritual, Plating, Territorial, HighVoltage, Hatch, Constrict, Slow, Tender, Intangible, Nemesis, …); power internal data cloned with the power (CurlUp, Skittish, PossessSpeed/Strength, Vigor, HardenedShell, Dampen); `CreatureCmd.Stun(stunMove, nextMoveId)` called from powers (Plow, Imbalanced, Asleep, Slumber, Burrowed, Shriek, Flutter); `ShouldClearBlock`/`AfterBlockBroken` (Burrowed); `AfterAttack` results (Suck, Skittish, PainfulStabs); `BeforeCardPlayed`/`BeforePotionUsed` (Surrounded). W3: every power in `deps.powers` (69 distinct, 8 ported). |
| B2 | monster as hook listener (Aeonglass `AfterCardGeneratedForCombat`, Queen `AfterDeath`); generic `CardPileCmd.AddToCombatAndPreview<T>` (generalise `Slimed.AddToCombatAndPreview`) including hand overflow; `CreatureCmd.Escape` + `CombatState.EscapedCreatures`; raw HP writes (`SetCurrentHpInternal`, `SetMaxAndCurrentHp`); status/curse card defs: Dazed, Burn, Wound, Toxic, Infection, Beckon, Wither, FranticEscape. |
| B3 | afflictions subsystem (`CardCmd.Afflict`/`AfflictAndPreview`/`ClearAffliction`, `CardModel.Affliction`, `ShouldPlay`, `TryModifyKeywordsInCombat`, `TryModifyEnergyCostInCombat`) and the affliction defs Galvanized, Tainted, Smog, Hexed, Entangled, Ringing, Bound; combat history `CardAfflictedEntry` / `CardPlayStartedEntry` (ChainsOfBinding, Ringing, Smoggy). |
| B4 | mid-combat summon path `CreatureCmd.Add` (`Commands/CreatureCmd.cs:3554-3586`: CreateCreature with the Niche HP roll → AddCreature → SetUpForCombat + slot sort → AfterCreatureAdded → PrepareForNextTurn(rollNewMove: false) off the enemy side → `Hook.AfterCreatureAddedToCombat`), `EncounterModel.GetNextSlot`, `CombatState.IsLiveCombat`, MinionPower semantics (secondary enemies die with the last primary, already in `CreatureCmd.Kill`), `ShouldStopCombatFromEnding` for death spawners (Stock, Infested, Surprise). |
| B5 | dead-but-present creatures (`ShouldCreatureBeRemovedFromCombatAfterDeath` false, `ShouldAllowHitting` false while reviving, `ShouldPowerBeRemovedAfterOwnerDeath` false), `SetMoveImmediate(force)` from power hooks, runtime-created `MoveState` (IllusionPower REVIVE_MOVE with `FollowUpStateId` = last log entry), `CreatureCmd.SetMaxHp` + `Heal` revive, `ShouldOwnerDeathTriggerFatal` (Reattach). |
| B6 | run-layer effects raised inside combat: `PlayerCmd.LoseGold` (Thievery), extra combat rewards (`CombatRoom.AddExtraReward`: Heist gold, Swipe `SpecialCardReward`), deck mutation (`CardPileCmd.RemoveFromDeck`, `RunState.AddCard`) with `CardModel.DeckVersion`, encounter custom state (`RanOutOfTime`, `GoldWasStolen`) read by the event / `CalculateGoldProportion`; the design note's `RunEffects` log (docs/sim-port/design-combat-kernel.md §5). |
| B7 | choice API: `CardSelectCmd.FromChooseACardScreen` during the enemy turn with `BlockingPlayerChoiceContext`; card `IChoosable.OnChosen` (Disintegration, MindRot, Sloth, WasteAway); v0.111.0 values of `_disintegrationDamageValues`. |
| B8 | Osty/pet subsystem (`PlayerCmd.AddPet`, `Creature.PetOwner`, ally-side monsters) with the Necrobinder cards and the Byrdpip/PaelsLegion relics. |

### 3.2 Power dependencies

Powers applied or consulted by monsters (`deps.powers`): tier and batch from the powers classification (its batch 9 = 'port together with the named monster'), plus the mechanical features of the power file:

| Power | Powers tier/batch | Features | # | Monsters |
| --- | --- | --- | --- | --- |
| AdaptablePower | T1/B9 monster:TestSubject | revive, dead_present_flags, internal_data | 1 | TestSubject |
| ArtifactPower | T2/B2 core | - | 8 | Aeonglass, Chomper, CubexConstruct, MechaKnight, PunchConstruct, TheAdversaryMkOne, TheAdversaryMkThree, TheAdversaryMkTwo |
| AsleepPower | T2/B9 monster:LagavulinMatriarch | side_turn_participants, stun_from_power | 1 | LagavulinMatriarch |
| BackAttackLeftPower | T1/B9 monster:Crusher | - | 1 | Crusher |
| BackAttackRightPower | T1/B9 monster:Rocket | - | 1 | Rocket |
| BattlewornDummyTimeLimitPower | T2/B9 monster:BattleFriend | side_turn_participants | 3 | BattleFriendV1, BattleFriendV2, BattleFriendV3 |
| BurrowedPower | T1/B9 monster:Tunneler | stun_from_power, block_rules | 1 | Tunneler |
| ChainsOfBindingPower | T3/B5 afflict | afflictions, side_turn_participants, combat_history, internal_data, should_play | 1 | Queen |
| ConstrictPower | T2/B2 core | side_turn_participants | 1 | SlitheringStrangler |
| CrabRagePower | T2/B9 monster:Crusher | - | 2 | Crusher, Rocket |
| CurlUpPower | T1/B9 monster:LouseProgenitor | internal_data | 1 | LouseProgenitor |
| DampenPower | T2/B9 monster:MagiKnight | internal_data, card_upgrade_downgrade | 1 | MagiKnight |
| DexterityPower | ported T1/B1 core | - | 2 | LagavulinMatriarch, TheForgotten |
| EnragePower | T1/B9 monster:TestSubject | - | 1 | TestSubject |
| EscapeArtistPower | T2/B9 monster:ThievingHopper | side_turn_participants | 1 | ThievingHopper |
| FlutterPower | T1/B9 monster:ThievingHopper | stun_from_power | 1 | ThievingHopper |
| FrailPower | ported T1/B1 core | side_turn_participants | 15 | Axebot, CorpseSlug, Crusher, FakeMerchantMonster, Flyconid, FossilStalker, FrogKnight, GlobeHead, KinPriest, LouseProgenitor, PunchConstruct, Queen, Stabbot, TrackerRubyRaider, TwoTailedRat |
| GalvanicPower | T3/B5 afflict | afflictions | 1 | GlobeHead |
| HardToKillPower | T1/B9 monster:Exoskeleton | - | 1 | Exoskeleton |
| HardenedShellPower | T2/B9 monster:SkulkingColony | side_turn_participants, internal_data | 1 | SkulkingColony |
| HatchPower | T2/B9 monster:ToughEgg | side_turn_participants | 1 | ToughEgg |
| HeistPower | T3/B8 run | run_layer, instanced_target | 2 | FatGremlin, GremlinMerc |
| HexPower | T3/B5 afflict | afflictions, keyword_mod | 1 | SpectralKnight |
| HighVoltagePower | T2/B9 monster:Zapbot | side_turn_participants | 1 | Zapbot |
| IllusionPower | T2/B9 monster:Parafright | revive, dead_present_flags, internal_data | 2 | EyeWithTeeth, Parafright |
| ImbalancedPower | T1/B9 monster:BowlbugRock | stun_from_power | 1 | BowlbugRock |
| InfestedPower | T3/B5 summon | summon, dead_present_flags | 1 | PhrogParasite |
| IntangiblePower | T1/B1 core | side_turn_participants | 2 | SoulFysh, TestSubject |
| MinionPower | T1/B1 core | - | 8 | EyeWithTeeth, Fabricator, GasBomb, KinFollower, Ovicopter, Parafright, TorchHeadAmalgam, ToughEgg |
| NemesisPower | T2/B9 monster:TestSubject | side_turn_participants | 1 | TestSubject |
| PainfulStabsPower | T2/B9 monster:TestSubject | dead_present_flags, card_generation | 1 | TestSubject |
| PaperCutsPower | T1/B9 monster:ScrollOfBiting | - | 1 | ScrollOfBiting |
| PersonalHivePower | T2/B9 monster:Entomancer | card_generation, osty_pets | 1 | Entomancer |
| PlatingPower | T2/B2 core | side_turn_participants | 5 | FrogKnight, LagavulinMatriarch, MysteriousKnight, SewerClam, SlumberingBeetle |
| PlowPower | T1/B9 monster:CeremonialBeast | stun_from_power | 1 | CeremonialBeast |
| PossessSpeedPower | T1/B9 monster:TheForgotten | internal_data | 1 | TheForgotten |
| PossessStrengthPower | T1/B9 monster:TheLost | internal_data | 1 | TheLost |
| RampartPower | T2/B9 monster:LivingShield | side_turn_participants | 1 | LivingShield |
| RavenousPower | ported T1/B9 monster:CorpseSlug | stun_from_power | 1 | CorpseSlug |
| ReattachPower | T2/B9 monster:Decimillipede | revive, dead_present_flags, internal_data | 4 | DecimillipedeSegment, DecimillipedeSegmentBack, DecimillipedeSegmentFront, DecimillipedeSegmentMiddle |
| RingingPower | T3/B5 afflict | afflictions, side_turn_participants, combat_history, should_play | 1 | CeremonialBeast |
| RitualPower | T2/B2 core | side_turn_participants | 3 | CalcifiedCultist, DampCultist, DevotedSculptor |
| SandpitPower | T2/B9 monster:TheInsatiable | side_turn_participants, instanced_target, osty_pets, kill_player | 1 | TheInsatiable |
| ShriekPower | T1/B9 monster:TerrorEel | stun_from_power | 1 | TerrorEel |
| ShrinkPower | ported T2/B2 core | side_turn_participants | 1 | ShrinkerBeetle |
| SkittishPower | T1/B9 monster:PhantasmalGardener | side_turn_participants, internal_data | 1 | PhantasmalGardener |
| SlipperyPower | T1/B1 core | - | 2 | Inklet, Vantom |
| SlowPower | T2/B9 monster:BygoneEffigy | side_turn_participants | 1 | BygoneEffigy |
| SlumberPower | T2/B9 monster:SlumberingBeetle | side_turn_participants, stun_from_power | 1 | SlumberingBeetle |
| SmoggyPower | T3/B5 afflict | afflictions, side_turn_participants, combat_history, should_play | 1 | LivingFog |
| SoarPower | T1/B9 monster:OwlMagistrate | - | 1 | OwlMagistrate |
| SteamEruptionPower | T1/B9 monster:WaterfallGiant | revive, dead_present_flags | 1 | WaterfallGiant |
| StockPower | T3/B5 summon | summon, dead_present_flags | 1 | Axebot |
| StrengthPower | ported T1/B1 core | - | 53 | Aeonglass, Axebot, BowlbugNectar, BruteRubyRaider, BygoneEffigy, CeremonialBeast, Crusher, CubexConstruct, DecimillipedeSegment, DecimillipedeSegmentBack, DecimillipedeSegmentFront, DecimillipedeSegmentMiddle, Entomancer, Exoskeleton, FakeMerchantMonster, FlailKnight, Fogmog, FrogKnight, FuzzyWurmCrawler, GlobeHead, GremlinMerc, KinFollower, KinPriest, KnowledgeDemon, LagavulinMatriarch, LivingShield, LouseProgenitor, MechaKnight, MysteriousKnight, Myte, Nibbit, Ovicopter, PhantasmalGardener, Queen, Rocket, ScrollOfBiting, Seapunk, SewerClam, SkulkingColony, SlimedBerserker, SludgeSpinner, SlumberingBeetle, SnappingJaxfruit, TestSubject, TheAdversaryMkOne, TheAdversaryMkThree, TheAdversaryMkTwo, TheInsatiable, TheLost, TheObscura, TurretOperator, Vantom, Wriggler |
| SuckPower | T2/B9 monster:FossilStalker | osty_pets | 1 | FossilStalker |
| SurprisePower | T3/B8 summon | summon, dead_present_flags, instanced_target | 1 | GremlinMerc |
| SurroundedPower | T3/B9 monster:CrusherRocket | osty_pets | 1 | Rocket |
| SwipePower | T3/B8 run | run_layer, instanced_target | 1 | ThievingHopper |
| TaintedPower | T1/B1 core | side_turn_participants | 1 | InfestedPrism |
| TangledPower | T3/B5 afflict | afflictions, side_turn_participants, energy_cost_mod | 1 | VineShambler |
| TenderPower | T2/B9 monster:HunterKiller | side_turn_participants | 1 | HunterKiller |
| TerritorialPower | T2/B9 monster:Byrdonis | side_turn_participants | 1 | Byrdonis |
| ThieveryPower | T3/B8 run | run_layer | 1 | GremlinMerc |
| ThornsPower | ported T1/B1 core | - | 2 | SpinyToad, Toadpole |
| VigorPower | T1/B1 core | internal_data | 1 | TerrorEel |
| VitalSparkPower | T3/B5 afflict | afflictions | 1 | InfestedPrism |
| VulnerablePower | ported T3/B2 core | side_turn_participants, osty_pets | 9 | Flyconid, Mawler, Ovicopter, OwlMagistrate, Queen, SoulFysh, SoulNexus, TerrorEel, TestSubject |
| WeakPower | ported T3/B2 core | side_turn_participants | 15 | Axebot, BowlbugSilk, Crusher, DecimillipedeSegment, DecimillipedeSegmentBack, DecimillipedeSegmentFront, DecimillipedeSegmentMiddle, GremlinMerc, HauntedShip, KinPriest, Queen, SlimedBerserker, SludgeSpinner, SoulNexus, WaterfallGiant |
| WitheringPresencePower | T2/B9 monster:Aeonglass | card_generation | 1 | Aeonglass |

### 3.3 Card, affliction and enchantment dependencies

| Dependency | Monsters |
| --- | --- |
| affliction Bound | Queen |
| affliction Entangled | VineShambler |
| affliction Galvanized | GlobeHead |
| affliction Hexed | SpectralKnight |
| affliction Ringing | CeremonialBeast |
| affliction Smog | LivingFog |
| affliction Tainted | InfestedPrism |
| card Beckon | SoulFysh |
| card Burn | MechaKnight, TestSubject |
| card Dazed | Chomper, Entomancer, EyeWithTeeth, HauntedShip, Noisebot |
| card Disintegration | KnowledgeDemon |
| card FranticEscape | TheInsatiable |
| card Infection | PhrogParasite, Wriggler |
| card MindRot | KnowledgeDemon |
| card Slimed | LeafSlimeM, LeafSlimeS, SlimedBerserker, TwigSlimeM |
| card Sloth | KnowledgeDemon |
| card Toxic | Myte |
| card WasteAway | KnowledgeDemon |
| card Wither | Aeonglass |
| card Wound | TestSubject, Vantom |
| enchantment Imbued | ThievingHopper |

### 3.4 Monster-to-monster dependencies

| Spawned monster | Spawned by |
| --- | --- |
| Axebot | StockPower (Axebot respawn on death) |
| BigDummy | MockAttackAndSummonMinionMonster |
| Byrdpip | relic Byrdpip SummonPet (PlayerCmd.AddPet) |
| EyeWithTeeth | Fogmog ILLUSION_MOVE |
| FatGremlin | SurprisePower (GremlinMerc death) |
| GasBomb | LivingFog BLOAT_MOVE |
| Guardbot | Fabricator (defense spawn) |
| Noisebot | Fabricator (defense spawn) |
| Osty | OstyCmd.Summon / PlayerCmd.AddPet<Osty> (Necrobinder) |
| PaelsLegion | relic PaelsLegion SummonPet (PlayerCmd.AddPet) |
| Parafright | TheObscura ILLUSION_MOVE |
| SneakyGremlin | SurprisePower (GremlinMerc death) |
| Stabbot | Fabricator (aggro spawn) |
| ToughEgg | Ovicopter LAY_EGGS_MOVE |
| TwoTailedRat | TwoTailedRat CALL_FOR_BACKUP_MOVE |
| Wriggler | InfestedPower (PhrogParasite death) |
| Zapbot | Fabricator (aggro spawn) |

Multi-creature fights that must be ported together: KaiserCrabBoss (Crusher + Rocket, SurroundedPower facing), QueenBoss (Queen reads the TorchHeadAmalgam's death), TheKinBoss (KinPriest + 2 KinFollower minions), DecimillipedeElite (3 segments revive each other), KnightsElite (Flail + Spectral + Magi), TheLostAndForgottenNormal, TurretOperatorWeak (LivingShield's Rampart shields TurretOperator), GremlinMercNormal (+ Sneaky/Fat gremlins), FabricatorNormal (+ 4 bot types; Guardbot shields the Fabricator).

## 4. Prompt types

| Prompt type | Count | Sites |
| --- | --- | --- |
| ChooseCard (`CardSelectCmd.FromChooseACardScreen`, min 1, max 1, no skip) | 1 | KnowledgeDemon CURSE_OF_KNOWLEDGE: `Models.Monsters/KnowledgeDemon.cs:124` (`Commands/CardSelectCmd.cs:3169`); one prompt per living target, during the enemy turn, `BlockingPlayerChoiceContext` |

No other monster, encounter or act file opens a prompt. Indirect choices: none (the curse cards' `OnChosen` effects live in the cards slice).

## 5. Monster table

HP is `MinInitialHp–MaxInitialHp` at A0 (A8 values in brackets). Graph notation: `init` initial state(s); `A→B` follow-up; `*` MustPerformOnceBeforeTransitioning; `RAND{X(nr,w0.4,cd3)}` random branch (nr = CannotRepeat, xN = CanRepeatXTimes N, once = UseOnlyOnce, any = CanRepeatForever, w weight, cd cooldown); `IF{X if cond}` conditional branch. `_MOVE` suffixes dropped. Full graphs, intents with values, move-body citations and per-move mechanics: JSON `move_graph`, `move_bodies`, `mechanics`.

| Monster | Role | Acts | HP | Move graph | Special intents | T/E/B | v107 |
| --- | --- | --- | --- | --- | --- | --- | --- |
| Aeonglass | boss | Glory | 512 (A8 535) | init EBB · EBB→EYE_LASERS, EYE_LASERS→INCREASING_INTENSITY, INCREASING_INTENSITY→EBB | Status | 2/2/B2 | rule-change |
| Architect | event | - | 9999 | init NOTHING · NOTHING→NOTHING | Hidden | 1/1/B1 | same |
| AssassinRubyRaider | normal | Overg | 18–23 (A8 19–24) | init KILLSHOT · KILLSHOT→KILLSHOT | - | 1/1/B1 | same |
| Axebot | normal | Glory | 70–78 (A8 76–86) +bonus | init BOOT_UP[_stockOverrideAmount.HasValue] / HAMMER_UPPERCUT[else] · BOOT_UP→HAMMER_UPPERCUT, ONE_TWO→HAMMER_UPPERCUT, HAMMER_UPPERCUT→ONE_TWO | - | 3/3/B4 | rule-change |
| AxeRubyRaider | normal | Overg | 20–22 (A8 21–23) | init SWING_1 · SWING_1→SWING_2, SWING_2→BIG_SWING, BIG_SWING→SWING_1 | - | 1/1/B1 | same |
| BattleFriendV1 | event | - | 75 | init NOTHING · NOTHING→NOTHING | - | 2/2/B6 | same |
| BattleFriendV2 | event | - | 150 | init NOTHING · NOTHING→NOTHING | - | 2/2/B6 | same |
| BattleFriendV3 | event | - | 300 | init NOTHING · NOTHING→NOTHING | - | 2/2/B6 | same |
| BigDummy | test | - | 9999 | init NOTHING · NOTHING→NOTHING | Hidden | 1/1/B9 | same |
| BowlbugEgg | normal/weak | Hive | 21–22 (A8 23–24) | init BITE · BITE→BITE | - | 1/1/B1 | same |
| BowlbugNectar | normal/weak | Hive | 35–38 (A8 36–39) | init THRASH · THRASH→BUFF, BUFF→THRASH2, THRASH2→THRASH2 | - | 1/1/B1 | same |
| BowlbugRock | normal/weak | Hive | 45–48 (A8 46–49) | init HEADBUTT · HEADBUTT→POST_HEADBUTT, DIZZY→HEADBUTT, POST_HEADBUTT=IF{DIZZY if IsOffBalance; HEADBUTT if !IsOffBalance} | Stun | 1/1/B1 | same |
| BowlbugSilk | normal | Hive | 40–43 (A8 41–44) | init TOXIC_SPIT · THRASH→TOXIC_SPIT, TOXIC_SPIT→THRASH | - | 1/1/B1 | same |
| BruteRubyRaider | normal | Overg | 30–33 (A8 31–34) | init BEAT · BEAT→ROAR, ROAR→BEAT | - | 1/1/B1 | same |
| BygoneEffigy | elite | Overg | 127 (A8 132) | init SLEEP · SLEEP→WAKE, WAKE→SLASHES, SLEEP_MOVE_2→SLASHES, SLASHES→SLASHES | Sleep | 1/2/B1 | same |
| Byrdonis | elite | Overg | 81–84 (A8 90) | init SWOOP · PECK→SWOOP, SWOOP→PECK | - | 1/2/B1 | same |
| Byrdpip | pet | - | 9999 | init NOTHING · NOTHING→NOTHING | - | 3/3/B8 | same |
| CalcifiedCultist | normal | Under | 38–41 (A8 39–42) | init INCANTATION · INCANTATION→DARK_STRIKE, DARK_STRIKE→DARK_STRIKE | - | 1/2/B1 | same |
| CeremonialBeast | boss | Overg | 252 (A8 262) | init STAMP · STAMP→PLOW, PLOW→PLOW, STUN*→BEAST_CRY, BEAST_CRY→STOMP, STOMP→CRUSH, CRUSH→BEAST_CRY | Stun | 2/3/B3 | cosmetic |
| Chomper | normal | Hive | 60–64 (A8 63–67) | init SCREECH[_screamFirst] / CLAMP[!(_screamFirst)] · CLAMP→SCREECH, SCREECH→CLAMP | Status | 2/2/B2 | same |
| CorpseSlug | normal/weak | Under | 25–27 (A8 27–29) | init WHIP_SLAP[(StarterMoveIdx % 3) == 0] / GLOMP[(StarterMoveIdx % 3) == 1] / GOOP[(StarterMoveIdx % 3) otherwise] · WHIP_SLAP→GLOMP, GLOMP→GOOP, GOOP→WHIP_SLAP | - | 1/1/B0 | same |
| CrossbowRubyRaider | normal | Overg | 18–21 (A8 19–22) | init RELOAD · FIRE→RELOAD, RELOAD→FIRE | - | 1/1/B1 | same |
| Crusher | boss | Hive | 209 (A8 219) | init THRASH · THRASH→ENLARGING_STRIKE, ENLARGING_STRIKE→BUG_STING, BUG_STING→ADAPT, ADAPT→GUARDED_STRIKE, GUARDED_STRIKE→THRASH | - | 1/2/B1 | same |
| CubexConstruct | normal | Glory,Overg | 65 (A8 70) | init CHARGE_UP · CHARGE_UP→REPEATER_BLAST, REPEATER_BLAST→REPEATER_BLAST_MOVE_2, REPEATER_BLAST_MOVE_2→EXPEL, EXPEL→REPEATER_BLAST | - | 1/2/B1 | same |
| DampCultist | normal | Under | 51–53 (A8 52–54) | init INCANTATION · INCANTATION→DARK_STRIKE, DARK_STRIKE→DARK_STRIKE | - | 1/2/B1 | same |
| DecimillipedeSegment | abstract base | - | 40–46 (A8 46–52) | init WRITHE[(StarterMoveIdx % 3) == 0] / BULK[(StarterMoveIdx % 3) == 1] / CONSTRICT[(StarterMoveIdx % 3) otherwise] · WRITHE→CONSTRICT, BULK→WRITHE, CONSTRICT→BULK, DEAD→REATTACH, REATTACH*→RAND, RAND=RAND{WRITHE(nr) BULK(nr) CONSTRICT(nr)} | Heal | 3/3/B5 | same |
| DecimillipedeSegmentBack | elite | Hive | 40–46 (A8 46–52) | = DecimillipedeSegment | - | 3/3/B5 | same |
| DecimillipedeSegmentFront | elite | Hive | 40–46 (A8 46–52) | = DecimillipedeSegment | - | 3/3/B5 | same |
| DecimillipedeSegmentMiddle | elite | Hive | 40–46 (A8 46–52) | = DecimillipedeSegment | - | 3/3/B5 | same |
| DeprecatedMonster | deprecated | - | 0 | init STUB · STUB→STUB | Hidden | 1/1/B9 | same |
| DevotedSculptor | weak | Glory | 162 (A8 172) | init FORBIDDEN_INCANTATION · FORBIDDEN_INCANTATION→SAVAGE, SAVAGE→SAVAGE | - | 1/2/B1 | same |
| Entomancer | elite | Hive | 145 (A8 165) | init BEES · PHEROMONE_SPIT→BEES, BEES→SPEAR, SPEAR→PHEROMONE_SPIT | - | 1/2/B1 | rule-change |
| Exoskeleton | normal/weak | Hive | 24–28 (A8 26–30) | init INIT · SKITTER→RAND, MANDIBLES→ENRAGE, ENRAGE→RAND, RAND=RAND{SKITTER(nr) MANDIBLES(nr)}, INIT=IF{SKITTER if SlotName == "first"; MANDIBLES if SlotName == "second"; ENRAGE if SlotName == "third"; RAND if SlotName == "fourth"} | - | 1/1/B1 | rule-change |
| EyeWithTeeth | normal (summoned) | Overg | 6 | init DISTRACT · DISTRACT→DISTRACT | Status | 2/2/B5 | same |
| Fabricator | normal | Glory | 150 (A8 155) | init fabricateBranch · FABRICATE→fabricateBranch, FABRICATING_STRIKE→fabricateBranch, DISINTEGRATE→fabricateBranch, RAND=RAND{FABRICATE(any) FABRICATING_STRIKE(any)}, fabricateBranch=IF{RAND if CanFabricate; DISINTEGRATE if !CanFabricate} | Summon | 3/3/B4 | same |
| FakeMerchantMonster | event | - | 165 (A8 175) | init SWIPE · SWIPE→RAND, SPEW_COINS→RAND, THROW_RELIC→RAND_ATTACK, ENRAGE→RAND, RAND=RAND{SWIPE(nr) SPEW_COINS(nr) THROW_RELIC(nr) ENRAGE(nr,cd3)}, RAND_ATTACK=RAND{SWIPE(nr) SPEW_COINS(nr) THROW_RELIC(nr)} | - | 1/1/B1 | same |
| FatGremlin | normal (summoned) | Under | 13–17 (A8 14–18) | init SPAWNED · SPAWNED→FLEE, FLEE→FLEE | Escape,Stun | 2/3/B6 | cosmetic |
| FlailKnight | elite | Glory | 101 (A8 108) | init RAM · WAR_CHANT→RAND, FLAIL→RAND, RAM→RAND, RAND=RAND{WAR_CHANT(nr) FLAIL(x2) RAM(x2)} | - | 1/1/B1 | same |
| Flyconid | normal | Overg | 47–49 (A8 51–53) | init INITIAL · VULNERABLE_SPORES→RAND, FRAIL_SPORES→RAND, SMASH→RAND, RAND=RAND{VULNERABLE_SPORES(nr,cd3) FRAIL_SPORES(nr,cd2) SMASH(nr)}, INITIAL=RAND{FRAIL_SPORES(nr,cd2) SMASH(nr)} | - | 1/1/B1 | same |
| Fogmog | normal | Overg | 74 (A8 78) | init ILLUSION · ILLUSION→SWIPE, SWIPE→BRANCH, SWIPE_RANDOM→HEADBUTT, HEADBUTT→SWIPE, BRANCH=RAND{SWIPE_RANDOM(nr,w0.4) HEADBUTT(nr,w0.6)} | Summon | 3/3/B4 | same |
| FossilStalker | normal | Under | 51–53 (A8 54–56) | init LATCH · TACKLE→RAND, LATCH→RAND, LASH→RAND, RAND=RAND{LATCH(x2) TACKLE(x2) LASH(x2)} | - | 1/2/B1 | same |
| FrogKnight | normal | Glory | 191 (A8 199) | init TONGUE_LASH · FOR_THE_QUEEN→HALF_HEALTH, STRIKE_DOWN_EVIL→FOR_THE_QUEEN, TONGUE_LASH→STRIKE_DOWN_EVIL, BEETLE_CHARGE→TONGUE_LASH, HALF_HEALTH=IF{TONGUE_LASH if HasBeetleCharged \|\| CurrentHp >= MaxHp / 2; BEETLE_CHARGE if !HasBeetleCharged && CurrentHp < MaxHp / 2} | - | 1/2/B1 | same |
| FuzzyWurmCrawler | normal/weak | Overg | 55–57 (A8 58–59) | init FIRST_ACID_GOOP · FIRST_ACID_GOOP→INHALE, ACID_GOOP→FIRST_ACID_GOOP, INHALE→ACID_GOOP | - | 1/1/B0 | same |
| GasBomb | normal (summoned) | Under | 7 (A8 8) | init EXPLODE · EXPLODE→∅ | DeathBlow | 1/1/B1 | cosmetic |
| GlobeHead | normal | Glory | 148 (A8 158) | init SHOCKING_SLAP · THUNDER_STRIKE→GALVANIC_BURST, SHOCKING_SLAP→THUNDER_STRIKE, GALVANIC_BURST→SHOCKING_SLAP | - | 1/3/B3 | rule-change |
| GremlinMerc | normal | Under | 47–49 (A8 51–53) | init GIMME · GIMME→DOUBLE_SMASH, DOUBLE_SMASH→HEHE, HEHE→GIMME | - | 3/3/B6 | same |
| Guardbot | normal (summoned) | Glory | 16–20 (A8 17–21) | init GUARD · GUARD→GUARD | - | 1/1/B1 | same |
| HauntedShip | normal | Under | 63 (A8 67) | init HAUNT · SWIPE→STOMP, STOMP→SWIPE, HAUNT→SWIPE | Status | 2/2/B2 | same |
| HunterKiller | normal | Hive | 121 (A8 126) | init TENDERIZING_GOOP · TENDERIZING_GOOP→RAND, BITE→RAND, PUNCTURE→RAND, RAND=RAND{BITE(nr) PUNCTURE(x2)} | - | 1/2/B1 | cosmetic |
| InfestedPrism | elite | Hive | 161 (A8 171) | init JAB · JAB→RADIATE, RADIATE→WHIRLWIND, WHIRLWIND→PULSATE, PULSATE→JAB | - | 1/3/B3 | same |
| Inklet | normal | Overg | 11–17 (A8 12–18) | init WHIRLWIND[_middleInklet] / JAB[!(_middleInklet)] · JAB→RAND, WHIRLWIND→JAB, PIERCING_GAZE→JAB, INIT_RAND=RAND{JAB(x2) WHIRLWIND(nr)}, RAND=RAND{PIERCING_GAZE(nr) WHIRLWIND(nr)} · unreachable: INIT_RAND | - | 1/1/B1 | cosmetic |
| KinFollower | boss | Overg | 58–59 (A8 62–63) | init POWER_DANCE[StartsWithDance] / QUICK_SLASH[!(StartsWithDance)] · QUICK_SLASH→BOOMERANG, BOOMERANG→POWER_DANCE, POWER_DANCE→QUICK_SLASH | - | 1/1/B1 | same |
| KinPriest | boss | Overg | 190 (A8 199) | init ORB_OF_FRAILTY · ORB_OF_FRAILTY→ORB_OF_WEAKNESS, ORB_OF_WEAKNESS→BEAM, BEAM→RITUAL, RITUAL→ORB_OF_FRAILTY | - | 1/1/B1 | cosmetic |
| KnowledgeDemon | boss | Hive | 379 (A8 399) | init CURSE_OF_KNOWLEDGE · CURSE_OF_KNOWLEDGE→SLAP, SLAP→KNOWLEDGE_OVERWHELMING, KNOWLEDGE_OVERWHELMING→PONDER, PONDER→CurseOfKnowledgeBranch, CurseOfKnowledgeBranch=IF{CURSE_OF_KNOWLEDGE if _curseOfKnowledgeCounter < 3; SLAP if _curseOfKnowledgeCounter >= 3} | Heal | 3/3/B7 | cosmetic |
| LagavulinMatriarch | boss | Under | 222 (A8 233) | init SLEEP · SLEEP→SLEEP_BRANCH, SLASH→DISEMBOWEL, SLASH2→SOUL_SIPHON, DISEMBOWEL→SLASH2, SOUL_SIPHON→SLASH, SLEEP_BRANCH=IF{SLEEP if HasPower<AsleepPower>(); SLASH if !HasPower<AsleepPower>()} | Sleep | 1/2/B1 | same |
| LeafSlimeM | normal/weak | Overg | 32–35 (A8 33–36) | init STICKY_SHOT · CLUMP_SHOT→STICKY_SHOT, STICKY_SHOT→CLUMP_SHOT | Status | 1/1/B0 | same |
| LeafSlimeS | normal/weak | Overg | 11–15 (A8 12–16) | init RAND · TACKLE→RAND, GOOP→RAND, RAND=RAND{TACKLE(nr) GOOP(nr)} | Status | 1/1/B0 | same |
| LivingFog | normal | Under | 80 (A8 82) | init ADVANCED_GAS · ADVANCED_GAS→BLOAT, BLOAT→SUPER_GAS_BLAST, SUPER_GAS_BLAST→BLOAT | CardDebuff,Summon | 3/3/B4 | cosmetic |
| LivingShield | weak | Glory | 55 (A8 65) | init SHIELD_SLAM · SHIELD_SLAM→SHIELD_SLAM_BRANCH, SHIELD_SLAM_BRANCH=IF{SHIELD_SLAM if GetAllyCount() > 0; SMASH if GetAllyCount() == 0}, SMASH→SMASH | - | 1/2/B1 | same |
| LouseProgenitor | normal | Hive | 134–136 (A8 138–141) | init WEB_CANNON · WEB_CANNON→CURL_AND_GROW, POUNCE→WEB_CANNON, CURL_AND_GROW→POUNCE | - | 1/1/B1 | rule-change |
| MagiKnight | elite | Glory | 82 (A8 89) | init POWER_SHIELD · POWER_SHIELD→DAMPEN, DAMPEN→RAM, PREP→MAGIC_BOMB, MAGIC_BOMB→RAM, RAM→PREP | - | 1/2/B1 | same |
| Mawler | normal | Overg | 72 (A8 76) | init CLAW · RIP_AND_TEAR→RAND, ROAR→RAND, CLAW→RAND, RAND=RAND{RIP_AND_TEAR(nr) ROAR(once) CLAW(nr)} | - | 1/1/B1 | same |
| MechaKnight | elite | Glory | 300 (A8 320) | init CHARGE · CHARGE→FLAMETHROWER, FLAMETHROWER→WINDUP, WINDUP→HEAVY_CLEAVE, HEAVY_CLEAVE→FLAMETHROWER | Status | 2/2/B2 | rule-change |
| MultiAttackMoveMonster | test | - | 999 | init POKE · POKE→POKE | - | 1/1/B9 | same |
| MysteriousKnight | event | - | 101 (A8 108) | = FlailKnight | - | 1/2/B1 | same |
| Myte | normal | Hive | 61–67 (A8 64–69) | init INIT · TOXIC→BITE, BITE→SUCK, SUCK→TOXIC, INIT=IF{TOXIC if SlotName == "first"; SUCK if SlotName == "second"} | Status | 2/2/B2 | same |
| Nibbit | normal/weak | Overg | 42–46 (A8 44–48) | init INIT · BUTT→SLICE, SLICE→HISS, HISS→BUTT, INIT=IF{BUTT if IsAlone; HISS if !IsFront; SLICE if IsFront} | - | 1/1/B0 | same |
| Noisebot | normal (summoned) | Glory | 18–23 (A8 19–24) | init NOISE · NOISE→NOISE | Status | 1/1/B1 | cosmetic |
| OneHpMonster | test | - | 1 | init NOTHING · NOTHING→NOTHING | Hidden | 1/1/B9 | same |
| Osty | pet | - | 1 | init NOTHING · NOTHING→NOTHING | - | 3/3/B8 | same |
| Ovicopter | normal | Hive | 124–130 (A8 126–132) | init LAY_EGGS · LAY_EGGS→SMASH, SMASH→TENDERIZER, TENDERIZER→SUMMON_BRANCH_STATE, NUTRITIONAL_PASTE→SMASH, SUMMON_BRANCH_STATE=IF{LAY_EGGS if CanLay; NUTRITIONAL_PASTE if !CanLay} | Summon | 3/3/B4 | cosmetic |
| OwlMagistrate | normal | Glory | 231 (A8 247) | init MAGISTRATE_SCRUTINY · MAGISTRATE_SCRUTINY→PECK_ASSAULT, PECK_ASSAULT→JUDICIAL_FLIGHT, JUDICIAL_FLIGHT→VERDICT, VERDICT→MAGISTRATE_SCRUTINY | - | 1/1/B1 | same |
| PaelsLegion | pet | - | 9999 | init NOTHING · NOTHING→NOTHING | - | 3/3/B8 | same |
| Parafright | normal (summoned) | Hive | 21 | init SLAM · SLAM→SLAM | - | 2/2/B5 | same |
| PhantasmalGardener | elite | Under | 26–31 (A8 27–32) | init INIT · BITE→LASH, LASH→FLAIL, FLAIL→ENLARGE, ENLARGE→BITE, INIT=IF{FLAIL if SlotName == "first"; BITE if SlotName == "second"; LASH if SlotName == "third"; ENLARGE if SlotName == "fourth"} | - | 1/1/B1 | cosmetic |
| PhrogParasite | elite | Overg | 61–64 (A8 66–68) | init INFECT · INFECT→LASH, LASH→INFECT, RAND=RAND{INFECT(nr) LASH(nr)} · unreachable: RAND | Status | 3/3/B4 | same |
| PunchConstruct | event/normal | Glory,Under | 55 (A8 60) | init FAST_PUNCH[StartsWithFastPunch] / READY[!(StartsWithFastPunch)] · READY→FAST_PUNCH, STRONG_PUNCH→READY, FAST_PUNCH→STRONG_PUNCH | - | 1/2/B1 | same |
| Queen | boss | Glory | 400 (A8 419) | init PUPPET_STRINGS · PUPPET_STRINGS→YOU_ARE_MINE, YOU_ARE_MINE→YOURE_MINE_NOW_BRANCH, YOURE_MINE_NOW_BRANCH=IF{BURN_BRIGHT_FOR_ME if !HasAmalgamDied; OFF_WITH_YOUR_HEAD if HasAmalgamDied}, BURN_BRIGHT_FOR_ME→BURN_BRIGHT_FOR_ME_BRANCH, BURN_BRIGHT_FOR_ME_BRANCH=IF{BURN_BRIGHT_FOR_ME if !HasAmalgamDied; OFF_WITH_YOUR_HEAD if HasAmalgamDied}, OFF_WITH_YOUR_HEAD→EXECUTION, EXECUTION→ENRAGE, ENRAGE→OFF_WITH_YOUR_HEAD | CardDebuff | 2/3/B3 | cosmetic |
| Rocket | boss | Hive | 199 (A8 209) | init TARGETING_RETICLE · TARGETING_RETICLE→PRECISION_BEAM, PRECISION_BEAM→CHARGE_UP, CHARGE_UP→LASER, LASER→RECHARGE, RECHARGE→TARGETING_RETICLE | Sleep | 1/3/B1 | same |
| ScrollOfBiting | normal/weak | Glory | 30–37 (A8 33–39) | init CHOMP[(StarterMoveIdx % 3) == 0] / CHEW[(StarterMoveIdx % 3) == 1] / MORE_TEETH[(StarterMoveIdx % 3) otherwise] · CHOMP→MORE_TEETH, CHEW→rand, MORE_TEETH→CHEW, rand=RAND{CHOMP(nr) CHEW(x2)} | - | 1/1/B1 | same |
| Seapunk | normal/weak | Under | 44–46 (A8 47–49) | init SEA_KICK · SEA_KICK→SPINNING_KICK, SPINNING_KICK→BUBBLE_BURP, BUBBLE_BURP→SEA_KICK | - | 1/1/B0 | same |
| SewerClam | normal | Under | 56 (A8 58) | init JET · PRESSURIZE→JET, JET→PRESSURIZE | - | 1/2/B1 | same |
| ShrinkerBeetle | normal/weak | Overg | 38–40 (A8 40–42) | init SHRINKER · SHRINKER→CHOMP, CHOMP→STOMP, STOMP→CHOMP | - | 1/1/B0 | same |
| SingleAttackMoveMonster | test | - | 999 | init POKE · POKE→POKE | - | 1/1/B9 | same |
| SkulkingColony | elite | Under | 75 (A8 80) | init ZOOM · ZOOM→ZOOM_MOVE_2, ZOOM_MOVE_2→INERTIA, INERTIA→PIERCING_STABS, PIERCING_STABS→ZOOM | - | 1/2/B1 | cosmetic |
| SlimedBerserker | normal | Glory | 261 (A8 281) | init VOMIT_ICHOR · VOMIT_ICHOR→FURIOUS_PUMMELING, LEECHING_HUG→SMOTHER, SMOTHER→VOMIT_ICHOR, FURIOUS_PUMMELING→LEECHING_HUG | Status | 1/1/B1 | cosmetic |
| SlitheringStrangler | normal | Overg | 53–55 (A8 54–56) | init CONSTRICT · CONSTRICT→rand, THWACK→CONSTRICT, LASH→CONSTRICT, rand=RAND{THWACK(any) LASH(any)} | - | 1/2/B1 | same |
| SludgeSpinner | weak | Under | 37–39 (A8 41–42) | init OIL_SPRAY · OIL_SPRAY→RAND, SLAM→RAND, RAGE→RAND, RAND=RAND{OIL_SPRAY(nr) SLAM(nr) RAGE(nr)} | - | 1/1/B0 | same |
| SlumberingBeetle | normal | Hive | 86 (A8 89) | init SNORE · SNORE→SNORE_NEXT, ROLL_OUT→ROLL_OUT, SNORE_NEXT=IF{SNORE if HasPower<SlumberPower>(); ROLL_OUT if !HasPower<SlumberPower>()} | Sleep | 1/2/B1 | same |
| SnappingJaxfruit | normal | Overg | 31–33 (A8 34–36) | init ENERGY_ORB · ENERGY_ORB→ENERGY_ORB | - | 1/1/B1 | same |
| SneakyGremlin | normal (summoned) | Under | 10–14 (A8 11–15) | init SPAWNED · SPAWNED→TACKLE, TACKLE→TACKLE | Stun | 1/1/B1 | same |
| SoulFysh | boss | Under | 211 (A8 221) | init BECKON · BECKON→DE_GAS, DE_GAS→GAZE, GAZE→FADE, FADE→SCREAM, SCREAM→BECKON | Status | 1/1/B1 | rule-change |
| SoulNexus | elite | Glory | 234 (A8 254) | init SOUL_BURN · SOUL_BURN→RAND, MAELSTROM→RAND, DRAIN_LIFE→RAND, RAND=RAND{SOUL_BURN(nr) MAELSTROM(nr) DRAIN_LIFE(nr)} | - | 1/1/B1 | same |
| SpectralKnight | elite | Glory | 93 (A8 97) | init HEX · HEX→SOUL_SLASH, SOUL_SLASH→RAND, SOUL_FLAME→RAND, RAND=RAND{SOUL_SLASH(x2) SOUL_FLAME(nr)} | - | 1/3/B3 | same |
| SpinyToad | normal | Hive | 116–119 (A8 121–124) | init PROTRUDING_SPIKES · PROTRUDING_SPIKES→SPIKE_EXPLOSION, SPIKE_EXPLOSION→TONGUE_LASH, TONGUE_LASH→PROTRUDING_SPIKES | - | 1/1/B1 | cosmetic |
| Stabbot | normal (summoned) | Glory | 18–23 (A8 19–24) | init STAB · STAB→STAB | - | 1/1/B1 | same |
| TenHpMonster | test | - | 10 | init NOTHING · NOTHING→NOTHING | Hidden | 1/1/B9 | same |
| TerrorEel | elite | Under | 140 (A8 150) | init CRASH · CRASH→THRASH, THRASH→CRASH, STUN→TERROR, TERROR→CRASH | Stun | 1/1/B1 | same |
| TestSubject | boss | Glory | 100 (A8 111) | init BITE · RESPAWN*→REVIVE_BRANCH, BITE→SKULL_BASH, SKULL_BASH→BITE, MULTI_CLAW→MULTI_CLAW, PHASE3_LACERATE→BIG_POUNCE, BIG_POUNCE→BURNING_GROWL, BURNING_GROWL→PHASE3_LACERATE, REVIVE_BRANCH=IF{MULTI_CLAW if Respawns < 2; PHASE3_LACERATE if Respawns >= 2} | Heal,Status | 3/3/B5 | same |
| TheAdversaryMkOne | unused | - | 100 | init SMASH · SMASH→BEAM, BEAM→BARRAGE, BARRAGE→SMASH | - | 1/2/B9 | same |
| TheAdversaryMkThree | unused | - | 300 (A8 300) | init CRASH · CRASH→FLAME_BEAM, FLAME_BEAM→BARRAGE, BARRAGE→CRASH | - | 1/2/B9 | same |
| TheAdversaryMkTwo | unused | - | 200 (A8 200) | init BASH · BASH→FLAME_BEAM, FLAME_BEAM→BARRAGE, BARRAGE→BASH | - | 1/2/B9 | same |
| TheForgotten | normal | Glory | 106 (A8 111) | init MIASMA · MIASMA→DREAD, DREAD→MIASMA | - | 1/1/B1 | same |
| TheInsatiable | boss | Hive | 321 (A8 341) | init LIQUIFY_GROUND · LIQUIFY_GROUND→THRASH, THRASH→LUNGING_BITE, THRASH_MOVE_2→THRASH, LUNGING_BITE→SALIVATE, SALIVATE→THRASH_MOVE_2 | Status | 1/2/B1 | cosmetic |
| TheLost | normal | Glory | 93 (A8 99) | init DEBILITATING_SMOG · DEBILITATING_SMOG→EYE_LASERS, EYE_LASERS→DEBILITATING_SMOG | - | 1/1/B1 | same |
| TheObscura | normal | Hive | 123 (A8 129) | init ILLUSION · ILLUSION→RAND, PIERCING_GAZE→RAND, SAIL→RAND, HARDENING_STRIKE→RAND, RAND=RAND{PIERCING_GAZE(nr) SAIL(nr) HARDENING_STRIKE(nr)} | Summon | 3/3/B4 | cosmetic |
| ThievingHopper | weak | Hive | 79 (A8 84) | init THIEVERY · THIEVERY→FLUTTER, NAB→ESCAPE, HAT_TRICK→NAB, FLUTTER→HAT_TRICK, ESCAPE→ESCAPE | CardDebuff,Escape | 3/3/B6 | cosmetic |
| Toadpole | weak | Under | 21–25 (A8 22–26) | init INIT · SPIKE_SPIT→WHIRL, WHIRL→SPIKEN, SPIKEN→SPIKE_SPIT, INIT=IF{WHIRL if !IsFront; SPIKEN if IsFront} | - | 1/1/B0 | same |
| TorchHeadAmalgam | boss | Glory | 199 (A8 211) | init STRONG_TACKLE · STRONG_TACKLE→TACKLE_2, TACKLE_2→BEAM, BEAM→TACKLE_3, TACKLE_3→TACKLE_4, TACKLE_4→BEAM | - | 1/1/B1 | rule-change |
| ToughEgg | normal (summoned) | Hive | 14–18 (A8 15–19) | init HATCH · HATCH→NIBBLE, NIBBLE→NIBBLE | Summon | 2/2/B2 | rule-change |
| TrackerRubyRaider | normal | Overg | 21–25 (A8 22–26) | init TRACK · TRACK→HOUNDS, HOUNDS→HOUNDS | - | 1/1/B1 | same |
| Tunneler | weak | Hive | 87 (A8 92) | init BITE · BITE→BURROW, BURROW→BELOW, BELOW→BELOW, DIZZY→BITE | Stun | 1/1/B1 | cosmetic |
| TurretOperator | weak | Glory | 41 (A8 51) | init UNLOAD · UNLOAD→UNLOAD_MOVE_2, UNLOAD_MOVE_2→RELOAD, RELOAD→UNLOAD | - | 1/1/B1 | same |
| TwigSlimeM | normal/weak | Overg | 26–28 (A8 27–29) | init STICKY_SHOT · POKEY_POUNCE→RAND, STICKY_SHOT→RAND, RAND=RAND{POKEY_POUNCE(x2) STICKY_SHOT(nr)} | Status | 1/1/B0 | same |
| TwigSlimeS | normal/weak | Overg | 7–11 (A8 8–12) | init TACKLE · TACKLE→TACKLE | - | 1/1/B0 | same |
| TwoTailedRat | normal | Under | 17–21 (A8 18–22) | init RAND[StarterMoveIndex == -1] / SCRATCH[(StarterMoveIndex % 3) == 0] / DISEASE_BITE[(StarterMoveIndex % 3) == 1] / SCREECH[(StarterMoveIndex % 3) otherwise] · SCRATCH→RAND, DISEASE_BITE→RAND, SCREECH→RAND, CALL_FOR_BACKUP→RAND, RAND=RAND{SCRATCH(nr,w=!CanSummon()?1:1/12) DISEASE_BITE(nr,w=!CanSummon()?1:1/12) SCREECH(nr,w=!CanSummon()?1:1/12,cd3) CALL_FOR_BACKUP(once,w=!CanSummon()?0:0.75)} | Summon | 3/3/B4 | cosmetic |
| Vantom | boss | Overg | 173 (A8 183) | init INK_BLOT · INK_BLOT→INKY_LANCE, INKY_LANCE→DISMEMBER, DISMEMBER→PREPARE, PREPARE→INK_BLOT | Status | 2/2/B2 | same |
| VineShambler | normal | Overg | 61 (A8 64) | init SWIPE · GRASPING_VINES→CHOMP, SWIPE→GRASPING_VINES, CHOMP→SWIPE | CardDebuff | 1/3/B3 | same |
| WaterfallGiant | boss | Under | 240 (A8 250) | init PRESSURIZE · PRESSURIZE→STOMP, STOMP→RAM, RAM→SIPHON, SIPHON→PRESSURE_GUN, PRESSURE_GUN→PRESSURE_UP, PRESSURE_UP→STOMP, ABOUT_TO_BLOW*→EXPLODE, EXPLODE→EXPLODE | DeathBlow,Heal,Stun | 3/3/B5 | cosmetic |
| Wriggler | elite/event (summoned) | Overg | 17–21 (A8 18–22) | init SPAWNED[StartStunned] / INIT[!(StartStunned)] · NASTY_BITE→WRIGGLE, WRIGGLE→NASTY_BITE, SPAWNED→INIT, INIT=IF{NASTY_BITE if SlotName == "wriggler1"; WRIGGLE if SlotName == "wriggler2"; NASTY_BITE if SlotName == "wriggler3"; WRIGGLE if SlotName == "wriggler4"} | Status,Stun | 2/2/B2 | same |
| Zapbot | normal (summoned) | Glory | 18–23 (A8 19–24) | init ZAP · ZAP→ZAP | - | 1/2/B1 | same |

Mocks (6, batch B9): MockArtifactMonster, MockAttackAndSummonMinionMonster, MockAttackMonster, MockIntangibleMonster, MockPlatingMonster, MockReattachMonster — the game's own test seams (`IsMock`).

## 6. Mechanics catalogues

### 6.1 Summons and spawns

| Source | What |
| --- | --- |
| Fabricator | Guardbot/Noisebot/Zapbot/Stabbot into the next free slot |
| Fogmog | EyeWithTeeth (slot 'illusion') |
| GremlinMerc | SneakyGremlin + FatGremlin on death |
| LivingFog | GasBomb per BLOAT into the next free encounter slot |
| Ovicopter | ToughEgg x up to 3 per LAY_EGGS |
| PhrogParasite | 4 Wrigglers on death |
| TheObscura | Parafright (slot 'illusion') |
| TwoTailedRat | TwoTailedRat (itself) up to 3 calls in total |
| StockPower (Axebot) | on death with stock > 0: a new Axebot with StockAmount − 1 in the same slot (`CreatureCmd.Add(monster, side, slot)`) |
| InfestedPower (PhrogParasite) | on death: 4 Wrigglers (StartStunned) into wriggler1..4 |
| SurprisePower (GremlinMerc) | on death: SneakyGremlin ('sneaky') and FatGremlin ('fat'); stolen gold moves to HeistPower on the FatGremlin |

Slot rules that fix turn order: summons pick `GetNextSlot` (first free slot in `Slots` order: Fabricator, LivingFog, TwoTailedRat's CanSummon) or the last free slot (`Slots.LastOrDefault`: Ovicopter eggs, TwoTailedRat reinforcements), or a fixed slot name (Fogmog/TheObscura 'illusion', gremlins, Wrigglers, Axebot's own slot). Every slotted AddCreature re-sorts the enemy list by slot index (`Combat/CombatState.cs:958-964`, `Combat/CombatManager.cs:6990-7005`).

### 6.2 Multi-part, phases, revive and death rules

| Monster | Rule |
| --- | --- |
| BattleFriendV1 | escape on time-out; escapes (CreatureCmd.Escape) |
| BattleFriendV2 | escape on time-out; escapes (CreatureCmd.Escape) |
| BattleFriendV3 | escape on time-out; escapes (CreatureCmd.Escape) |
| CeremonialBeast | phase 2 after Plow breaks (IsInSecondPhase) |
| Crusher | with Rocket; BackAttackLeft/Right mark sides for Rocket's SurroundedPower |
| DecimillipedeSegment | 3 segments (Front/Middle/Back) revive each other; revive/dead-but-present |
| DecimillipedeSegmentBack | one of 3 segments; revive/dead-but-present |
| DecimillipedeSegmentFront | one of 3 segments; revive/dead-but-present |
| DecimillipedeSegmentMiddle | one of 3 segments; revive/dead-but-present |
| EyeWithTeeth | revive/dead-but-present |
| FatGremlin | escapes on its 2nd turn; escapes (CreatureCmd.Escape) |
| GasBomb | kills itself after exploding |
| Parafright | revive/dead-but-present |
| Queen | Queen + TorchHeadAmalgam (minion) |
| Rocket | with Crusher |
| TestSubject | 3 forms (100/200/300 HP at A0), revives twice; revive/dead-but-present |
| ThievingHopper | escapes on its 5th move if not killed; escapes (CreatureCmd.Escape) |
| WaterfallGiant | explodes one turn after its first death; revive/dead-but-present |

Visual-only overrides that a port drops: `ShouldDisappearFromDoom` and `OnDieToDoom` are read only by `DoomPower` VFX (`Models.Powers/DoomPower.cs:377`, `:612`); every `BeforeRemovedFromRoom`, `ShouldFadeAfterDeath`, `DeathAnimLengthOverride`, `IsHealthBarVisible` override is audio/visual; the AbstractModel hook overrides of Crusher, DecimillipedeSegment, DecimillipedeSegmentBack, DecimillipedeSegmentFront, DecimillipedeSegmentMiddle, KinPriest, LagavulinMatriarch, Rocket, SoulFysh, TestSubject, TheInsatiable, Vantom, WaterfallGiant only drive visuals or music (`listener_hooks.visual_only`); the only rule-relevant monster listeners are Aeonglass `AfterCardGeneratedForCombat` and Queen `AfterDeath`.

### 6.3 RNG

| Stream use | Monsters |
| --- | --- |
| RunRng.Niche | 117 |
| RunRng.MonsterAi | 24 |
| RunRng.Shuffle | 3 |
| RunRng.Shuffle via PersonalHivePower | 1 |
| RunRng.CombatCardGeneration | 1 |

| Monster | RNG beyond the spawn HP roll |
| --- | --- |
| DecimillipedeSegment | RunRng.MonsterAi: NextFloat per evaluated random branch (RAND) |
| DecimillipedeSegmentBack | RunRng.MonsterAi: NextFloat per evaluated random branch (RAND) |
| DecimillipedeSegmentFront | RunRng.MonsterAi: NextFloat per evaluated random branch (RAND) |
| DecimillipedeSegmentMiddle | RunRng.MonsterAi: NextFloat per evaluated random branch (RAND) |
| Entomancer | RunRng.Shuffle via PersonalHivePower (Dazed at random draw-pile positions when hit) |
| Exoskeleton | RunRng.MonsterAi: NextFloat per evaluated random branch (RAND) |
| Fabricator | RunRng.MonsterAi: NextFloat per evaluated random branch (RAND); RunRng.MonsterAi: NextItem for each bot pick (SpawnBot) |
| FakeMerchantMonster | RunRng.MonsterAi: NextFloat per evaluated random branch (RAND_MOVE, RAND_ATTACK_MOVE) |
| FlailKnight | RunRng.MonsterAi: NextFloat per evaluated random branch (RAND) |
| Flyconid | RunRng.MonsterAi: NextFloat per evaluated random branch (RAND, INITIAL) |
| Fogmog | RunRng.MonsterAi: NextFloat per evaluated random branch (BRANCH) |
| FossilStalker | RunRng.MonsterAi: NextFloat per evaluated random branch (RAND) |
| HunterKiller | RunRng.MonsterAi: NextFloat per evaluated random branch (RAND) |
| Inklet | RunRng.MonsterAi: NextFloat per evaluated random branch (RAND) |
| LeafSlimeS | RunRng.MonsterAi: NextFloat per evaluated random branch (RAND) |
| Mawler | RunRng.MonsterAi: NextFloat per evaluated random branch (RAND) |
| MysteriousKnight | RunRng.MonsterAi: NextFloat per evaluated random branch (RAND) |
| Noisebot | RunRng.Shuffle (random draw-pile position) |
| ScrollOfBiting | RunRng.MonsterAi: NextFloat per evaluated random branch (rand) |
| SlitheringStrangler | RunRng.MonsterAi: NextFloat per evaluated random branch (rand) |
| SludgeSpinner | RunRng.MonsterAi: NextFloat per evaluated random branch (RAND) |
| SoulFysh | RunRng.Shuffle (random draw-pile position) |
| SoulNexus | RunRng.MonsterAi: NextFloat per evaluated random branch (RAND) |
| SpectralKnight | RunRng.MonsterAi: NextFloat per evaluated random branch (RAND) |
| TheInsatiable | RunRng.Shuffle (random pile positions, 6 per target) |
| TheObscura | RunRng.MonsterAi: NextFloat per evaluated random branch (RAND) |
| ThievingHopper | RunRng.CombatCardGeneration (card to steal) |
| ToughEgg | RunRng.Niche (hatchling HP roll) |
| TwigSlimeM | RunRng.MonsterAi: NextFloat per evaluated random branch (RAND) |
| TwoTailedRat | RunRng.MonsterAi: NextFloat per evaluated random branch (RAND) |

Private `MonsterModel.Rng` (`Models/MonsterModel.cs:396`): read only by ToughEgg in `SetupSkins` (visual), so no monster needs the per-monster stream for rules. The spawn HP roll is one `RunRng.Niche` draw per created creature (sim/specs/monsters.md §4).

### 6.4 Special intents

| Intent | # | Monsters |
| --- | --- | --- |
| Status | 17 | Aeonglass, Chomper, EyeWithTeeth, HauntedShip, LeafSlimeM, LeafSlimeS, MechaKnight, Myte, Noisebot, PhrogParasite, SlimedBerserker, SoulFysh, TestSubject, TheInsatiable, TwigSlimeM, Vantom, Wriggler |
| Stun | 8 | BowlbugRock, CeremonialBeast, FatGremlin, SneakyGremlin, TerrorEel, Tunneler, WaterfallGiant, Wriggler |
| Summon | 7 | Fabricator, Fogmog, LivingFog, Ovicopter, TheObscura, ToughEgg, TwoTailedRat |
| Hidden | 5 | Architect, BigDummy, DeprecatedMonster, OneHpMonster, TenHpMonster |
| Sleep | 4 | BygoneEffigy, LagavulinMatriarch, Rocket, SlumberingBeetle |
| Heal | 4 | DecimillipedeSegment, KnowledgeDemon, TestSubject, WaterfallGiant |
| CardDebuff | 4 | LivingFog, Queen, ThievingHopper, VineShambler |
| Escape | 2 | FatGremlin, ThievingHopper |
| DeathBlow | 2 | GasBomb, WaterfallGiant |

Intents drive rules only through `IntendsToAttack` (Attack/DeathBlow) and through intent damage, which is recomputed with `Hook.ModifyDamage` (sim/specs/monsters.md §6). Lambda-valued intents: TheForgotten DREAD (damage + own Dexterity), WaterfallGiant PRESSURE_GUN / EXPLODE, GasBomb EXPLODE, TestSubject MULTI_CLAW (hit count).

### 6.5 Ascension

99 monsters change values at A8 (ToughEnemies) and 96 at A9 (DeadlyEnemies); per-value lists in JSON `ascension_sensitivity`. A8 is HP plus these non-HP values: FrogKnight (PlatingAmount); GremlinMerc (GimmeDamage, DoubleSmashDamage, HeheDamage); InfestedPrism (PulsateBlock); LagavulinMatriarch (Slash2Block); LouseProgenitor (CurlBlock); MagiKnight (PowerShieldBlock); Nibbit (SliceBlock); PhantasmalGardener (SkittishAmount); Seapunk (BubbleBlock); SewerClam (inline@85); SlumberingBeetle (PlatingAmount); TerrorEel (ShriekAmount); TestSubject (FirstFormHp, SecondFormHp, ThirdFormHp); ToughEgg (HatchlingMinHp, HatchlingMaxHp); Tunneler (BlockGain); Vantom (SlipperyAmt); WaterfallGiant (SiphonHeal). A9 never changes HP. No-op branches (same value on both sides): Byrdonis PeckRepeat (DeadlyEnemies 3=3); Crusher EnlargingStrikeDamage (DeadlyEnemies 4=4); Entomancer BeesDamage (DeadlyEnemies 3=3); KinFollower QuickSlashDamage (DeadlyEnemies 5=5); KinFollower BoomerangDamage (DeadlyEnemies 2=2); KinPriest BeamDamage (DeadlyEnemies 3=3); OwlMagistrate PeckAssaultDamage (DeadlyEnemies 4=4); PhantasmalGardener BiteDamage (DeadlyEnemies 5=5); PhantasmalGardener LashDamage (DeadlyEnemies 7=7); PhantasmalGardener FlailRepeat (DeadlyEnemies 3=3); Queen inline@217 (DeadlyEnemies 1=1); SoulNexus MaelstromRepeat (DeadlyEnemies 4=4); TheAdversaryMkThree MinInitialHp (ToughEnemies 300=300); TheAdversaryMkTwo MinInitialHp (ToughEnemies 200=200); TheForgotten DebilitatingSmogDexStealAmount (DeadlyEnemies 2=2); TheLost DebilitatingSmogStrengthStealAmount (DeadlyEnemies 2=2); TorchHeadAmalgam SoulBeamDamage (DeadlyEnemies 8=8); TrackerRubyRaider HoundsDamage (DeadlyEnemies 1=1).

### 6.6 Kernel feature frequencies

| Feature (own code) | Monsters |
| --- | --- |
| power_monster_coupling | 16 |
| add_to_combat_and_preview | 10 |
| mid_combat_summon | 9 |
| set_move_immediate_from_hook | 9 |
| dead_but_present | 8 |
| set_hp_direct | 7 |
| stun_with_move | 6 |
| escape | 5 |
| starter_move_index | 5 |
| power_internal_data | 5 |
| generated_cards | 5 |
| combat_end_blockers | 4 |
| side_turn_participants | 4 |
| encounter_custom_state | 4 |
| slot_name_branch | 4 |
| encounter_slots | 4 |
| instanced_power_target | 3 |
| player_side_pets | 3 |
| monster_ai_rng_extra | 3 |
| run_layer_effects | 3 |
| random_pile_position | 3 |
| monster_hook_listener | 2 |
| card_upgrade_ops | 2 |
| multipart_boss | 2 |
| runtime_move_state | 2 |
| live_combat_check | 2 |
| hand_overflow | 2 |
| player_prompt | 1 |

| Feature (through tier ≥ 2 powers) | Monsters |
| --- | --- |
| side_turn_participants | 29 |
| internal_data | 15 |
| dead_present_flags | 11 |
| revive | 8 |
| stun_from_power | 7 |
| afflictions | 7 |
| osty_pets | 4 |
| instanced_target | 4 |
| card_generation | 3 |
| summon | 3 |
| combat_history | 3 |
| should_play | 3 |
| run_layer | 3 |
| card_upgrade_downgrade | 1 |
| keyword_mod | 1 |
| kill_player | 1 |
| block_rules | 1 |
| energy_cost_mod | 1 |

## 7. Version delta v0.107.1 → v0.111.0

Method: per file, compare (a) numeric literals, rule enums, Cmd/model calls and move-id strings outside presentation code, (b) the extracted move graph (states, intents, follow-ups, branches, initial state, registration order), (c) every evaluated property value, (d) each move's Cmd sequence in execution order, (e) comparison operators, ±constants and RNG call arguments; then read every flagged file. The two trees were decompiled with different settings, so raw text diffs are meaningless.

| Rule change | What changed |
| --- | --- |
| Aeonglass | EbbDamage 26/32 -> 22/26 (A9) |
| Axebot | HammerUppercut 12/14 -> 14/18, OneTwo 9/10 -> 10/11, HP gains +10 per respawn (new), BootUp Str uses RespawnCount |
| Entomancer | HP A8 155 -> 165 |
| Exoskeleton | HP A8 25-29 -> 26-30 |
| GlobeHead | GalvanicPower amount 6 now has an A9 value 8 |
| LouseProgenitor | CurlAndGrow Strength 5 -> GrowStrength 5 (A9 7) |
| MechaKnight | FLAMETHROWER gains a SingleAttackIntent and an attack (FlamethrowerDamage 8, A9 12) |
| SoulFysh | DeGasDamage A9 17 -> 18 |
| TorchHeadAmalgam | initial TACKLE_MOVE (TackleDamage) replaced by STRONG_TACKLE_MOVE (26, A9 32); TackleDamage A9 19 -> 22; WeakTackleDamage A9 15 -> 16 |
| ToughEgg | hatchling HP roll NextInt(HatchlingMinHp, HatchlingMaxHp) -> NextInt(HatchlingMinHp, HatchlingMaxHp + 1) (max now reachable) |

Cosmetic only (22): CeremonialBeast, FatGremlin, GasBomb, HunterKiller, Inklet, KinPriest, KnowledgeDemon, LivingFog, MockAttackAndSummonMinionMonster, Noisebot, Ovicopter, PhantasmalGardener, Queen, SkulkingColony, SlimedBerserker, SpinyToad, TheInsatiable, TheObscura, ThievingHopper, Tunneler, TwoTailedRat, WaterfallGiant.

## 8. Porting caveats

- **Execution order inside v0.111.0 state machines is not source-line order.** `switch (num)` state machines place awaited blocks out of order; follow the `IL_xxxx` labels (IL offsets rise in original source order) or read the v0.107.1 async body. Example: Queen YOU_ARE_MINE applies Frail, Weak, Vulnerable in that order although Weak appears first in the v0.111.0 text (`Models.Monsters/Queen.cs:659-681`).
- Monster `AfterAddedToRoom` at encounter setup runs in window B: block gains and pile adds are refused, `PowerCmd.Apply` works (sim/specs/loop.md §2). CubexConstruct's 13 block therefore never appears at combat start; it does for a mid-combat spawn.
- Unregistered states may still be initial (Myte and PhantasmalGardener `INIT_MOVE`), and some built states are never entered (no incoming edge and no code path; JSON `move_graph.orphan_states`): BygoneEffigy SLEEP_MOVE_2; CeremonialBeast STUN_MOVE; Inklet INIT_RAND; PhrogParasite RAND; TerrorEel STUN_MOVE; Tunneler DIZZY_MOVE. `CreatureCmd.Stun` always builds its own STUNNED state (sim/specs/monsters.md §5), which is why the registered STUN/DIZZY states are dead. States entered only by code are listed in `move_graph.entered_by_code`. A random branch draws `MonsterAi` only when it is evaluated.
- Iteration orders that feed RNG: Fabricator bot pools are `HashSet<MonsterModel>` (insertion order), Bowlbugs/RubyRaiders worker tables are `Dictionary` key order; `NextItem` over them must use the same order.
- Powers reach into monster classes (PlowPower → CeremonialBeast, ImbalancedPower → BowlbugRock, AsleepPower → LagavulinMatriarch, SlumberPower → SlumberingBeetle, BurrowedPower → Tunneler, ShriekPower → TerrorEel, FlutterPower → ThievingHopper, AdaptablePower → TestSubject, SteamEruptionPower → WaterfallGiant, ReattachPower → DecimillipedeSegment, CurlUpPower → LouseProgenitor): the sim needs typed access from a power to its owner's `MonsterDef` and extra state.
- Instance-specific HP: Axebot (+10 per respawn), DecimillipedeSegment (even, unique), TestSubject forms, ToughEgg hatchling, WaterfallGiant re-arm, PunchConstruct `StartingHpReduction` (raw write without hooks).
- `GetValueIfAscension(level, a, b)` is `cs.GetValueIfAscension` (CONTRACTS §1); A8/A9 only.

## 9. Decompile-unreadable files

| File | What is lost | Cross-reference | Needed |
| --- | --- | --- | --- |
| `Models.Monsters/KnowledgeDemon.cs:771-774` | `_disintegrationDamageValues` (`int[3]`) is filled by `RuntimeHelpers.InitializeArray` from a field token the decompiler cannot print (`LdMemberToken`); the values set the DisintegrationPower var of the curse card offered by CURSE_OF_KNOWLEDGE (`:114`) | macOS v0.111.0 decompile `artifacts/knowledge/decompiled-9cb4f1ad/MegaCrit.Sts2.Core.Models.Monsters/KnowledgeDemon.cs:45` gives {6, 7, 8} (v0.107.1 `Models/Monsters/KnowledgeDemon.cs:45` agrees); its IL matches the Linux build method for method (`output/server-bootstrap/assembly-comparison.json`: 51602/51602, ldtoken operands included), and the Linux `sts2.dll` contains the `<PrivateImplementationDetails>` field named by SHA-256 of those 12 bytes, which occur once (`tmp/sim-port/monsters/blobcheck.py`) | resolved: use {6, 7, 8}; the rule-truth tree itself stays unreadable here |

No other monster, encounter or act file in v0.111.0 has an unreadable construct that affects rules (scan for `LdMemberToken`, `OpCode not supported`, `Expected O, but got Unknown`).

## 10. Open questions

1. Power tiers come from powers.classified.json, whose batch scheme (1-10) differs from this file's monster batches (0-9); the orchestrator should map powers with batch_label 'monster:<Name>' into that monster's batch.
2. IllusionPower REVIVE_MOVE is created at death time with `FollowUpStateId = power.FollowUpStateId ?? StateLog.Last().Id` (`Models.Powers/IllusionPower.cs:146-148`; the power property at `:276` is never set by any model, so the fallback applies): the sim's shared immutable graph needs a deterministic per-instance off-graph state (like STUNNED); decide its canonical-hash encoding.
3. FlutterPower computes the post-stun state with `StateLog.Last().GetNextState(owner, MonsterAi)`; for ThievingHopper the last state is always a MoveState (no draw), but the sim should port the general call in case a random branch is ever last.
4. KnowledgeDemon runs one `FromChooseACardScreen` per target concurrently (`Task.WhenAll`); single player has one target, but the choice API must define the order of prompts if more than one player creature (or Osty) is a target.
