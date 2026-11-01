# Inventory: Necrobinder and Regent cards (semantic classification)

2026-11-01 · W0 semantic pass · game v0.111.0 · machine-readable companion:
[`sim/specs/inventory/cards-necrobinder-regent.classified.json`](../../sim/specs/inventory/cards-necrobinder-regent.classified.json)
(the 182 `cards.json` entries of the two pools, in manifest order, with the classification fields added;
`requirement_catalog`, `power_dependencies`, `token_dependencies` and `batch_labels` live in the envelope).

Rule truth: `/Users/Aincrad/dev-proj/hack-sts2/output/server-bootstrap/linux-decompiled/`, cited as
`<ns-dir>/<File>.cs:<line>` with the `MegaCrit.Sts2.Core.` prefix dropped. The v0.107.1 tree
(`artifacts/knowledge/decompiled-v0.107.1/`) was used only for the version delta and, where the two
files are semantically identical, as a readable cross-check of await order.

## 0. Summary

- 182 cards: 91 Necrobinder + 91 Regent. 10 are multiplayer-only (5 per pool) and unreachable in single player.
- Own-code tiers: tier 1 72, tier 2 20, tier 3 89, tier 4 1. With dependencies (powers applied, tokens created): tier 1 35, tier 2 36, tier 3 110, tier 4 1.
- Prompts: 17 cards open a CardSelectCmd prompt in their own play, 2 more through the power they apply (ForegoneConclusion, Tyranny), 2 auto-play other cards whose prompts nest inside the play.
- Batches: 1: 19, 2: 31, 3: 20, 4: 9, 5: 14, 6: 4, 7: 71, 8: 4, 9: 10 (section 4).
- Starter decks gate on batch 7: Necrobinder's Bodyguard and Unleash need Osty (with starter relic BoundPhylactery,
  which summons), Regent's FallingStar and Venerate need Stars (with DivineRight, 3 stars per combat room); no
  combat of either character can be diffed before those subsystems land.
- Subsystems: Osty touches 26 cards, Stars 36, Forge 14, Doom 14 (own code or dependency).
- Version delta v0.107.1 -> v0.111.0: 127 same, 27 cosmetic, 22 rule-change, 6 new in v0.111.0.
- Ascension: no card in the slice, no power it applies, and neither OstyCmd, ForgeCmd nor the Osty monster reads an AscensionLevel.
- Decompile: every file is readable; none needs a re-decompile (section 11).

## 1. Method

1. Every OnPlay and hook override was read from the v0.111.0 state machines. Effect order is the await order:
   the C# compiler numbers await points in source order, so sorting the `num = (<>1__state = k)` sites by `k`
   recovers the source sequence of Cmd calls (UI calls such as `TriggerAnim`, `Cmd.Wait`, VFX dropped).
   Control flow around `Osty.CheckMissingWithAnim` was read from the IL labels (two patterns, section 6.1).
2. Powers the cards apply and token cards they create were read in full to fix the dependency order;
   their tiers are provisional (the powers slice owns them).
3. Kernel requirements list only what the sim lacks today (checked against `sim/Sts2Sim.Core`: hook virtuals
   in `Combat/Pipeline/AbstractModel.cs`, facades in `Combat/Loop` and `Combat/Pipeline`, `CardInstance`,
   `CardEnergyCost`, `PowerModel`) and the facade status table of `sim/specs/inventory/kernel-gaps.json`.
4. A token/fingerprint cross-check (script in scratch) confirmed for all 182 cards that every Cmd, history type,
   prompt and power application in the source maps to a requirement key or dependency in the JSON.
5. Version delta: a normalized fingerprint (ctor arguments, CanonicalVars signature, keywords, tags, upgrade
   statements, property overrides, overridden members, Cmd/Hook calls, logic identifiers and literals) plus a Cmd
   call-order comparison, both versions; every flagged file was then read by hand and labelled.

Tier (same scale as the other W0 slices): 1 = only facades the sim already has; 2 = facades/hooks/vars that
exist in the game but not in the sim; 3 = a player prompt or a new subsystem (combat history, pool generation,
Stars, Osty, Forge, run-state effects); 4 = cross-cutting or unreadable. `tier` is the card's own code;
`tier_with_deps` adds the powers and tokens it needs (provisional; `powers.classified.json` owns power tiers).
Stars counts as a subsystem although P0 already holds the star state and costs: the gain/spend facades, hooks
and history are what is missing.

## 2. Counts

By pool and own tier (and tier with dependencies):

| Pool | Tier 1 | Tier 2 | Tier 3 | Tier 4 | Cards | MP-only |
|---|---|---|---|---|---|---|
| NECROBINDER | 44 (22) | 12 (26) | 34 (42) | 1 (1) | 91 | 5 |
| REGENT | 28 (13) | 8 (10) | 55 (68) | 0 (0) | 91 | 5 |

By pool and rarity (own tier 1/2/3/4):

| Pool | Rarity | Cards | Tiers 1/2/3/4 |
|---|---|---|---|
| NECROBINDER | Basic | 4 | 2/0/2/0 |
| NECROBINDER | Common | 20 | 10/3/7/0 |
| NECROBINDER | Uncommon | 38 | 19/4/15/0 |
| NECROBINDER | Rare | 27 | 12/5/9/1 |
| NECROBINDER | Ancient | 2 | 1/0/1/0 |
| REGENT | Basic | 4 | 2/0/2/0 |
| REGENT | Common | 20 | 5/1/14/0 |
| REGENT | Uncommon | 38 | 12/3/23/0 |
| REGENT | Rare | 27 | 9/4/14/0 |
| REGENT | Ancient | 2 | 0/0/2/0 |

By proposed port batch:

| Batch | Gate | Necrobinder | Regent | Total |
|---|---|---|---|---|
| 1 | tier 1 on the current sim: existing facades, every dependency already ported (Strength, Vulnerable, Weak) or none | 13 | 6 | 19 |
| 2 | tier-1 card logic that needs unported powers or tokens; port those in the same batch (their tier-2 needs: side-turn participants, temporary powers, Doom kill) | 22 | 9 | 31 |
| 3 | tier 2: card-kernel facades missing from the sim (card hook listeners, calculated vars, card ops and creation, auto-play, EndTurn, ResourceInfo) | 12 | 8 | 20 |
| 4 | tier 3, combat history readers | 5 | 4 | 9 |
| 5 | tier 3, player prompts (choice API) | 5 | 9 | 14 |
| 6 | tier 3, card generation from pools (CardFactory + UnlockState) | 1 | 3 | 4 |
| 7 | tier 3, character subsystems: Stars, Osty, Forge (SovereignBlade) | 25 | 46 | 71 |
| 8 | run-state effects and cross-cutting (deck copy, combat-end rewards, generic debuff copy) | 3 | 1 | 4 |
| 9 | multiplayer-only: unreachable in single-player pools; port last or never | 5 | 5 | 10 |

## 3. Kernel requirements

Requirement keys (`kernel_requirements` in the JSON) with how many cards need them in their own code and
including dependencies. Tier and batch are the key's contribution to the card's tier and port batch.

| Requirement | Tier | Batch | Subsystem | Cards (own) | Cards (with deps) | Citation |
|---|---|---|---|---|---|---|
| `CardCmd.AutoPlay` | 2 | 3 | Auto-play | 3 | 3 | Commands/CardCmd.cs:1779, :377-531 |
| `CardPileCmd.AutoPlayFromDrawPile` | 2 | 3 | Auto-play | 1 | 1 | Commands/CardPileCmd.cs:4074, :1612 |
| `CombatState.CreateCard<T>` | 2 | 3 | Card creation | 10 | 12 | Combat/CombatState.cs:671-677 |
| `CardModel.AfterDowngraded` | 2 | 3 | Card model | 2 | 2 | Models.Cards/KinglyPunch.cs:150-155; Models.Cards/TheScythe.cs:162-176 |
| `CardModel.CreateClone` | 2 | 3 | Card model | 2 | 2 | Models/CardModel.cs:2073, :3571 |
| `Keyword sources` | 2 | 3 | Card model | 1 | 1 | Models/CardModel.cs:1940, :2775-2828 |
| `CardCmd.ApplyKeyword` | 2 | 3 | Card ops | 2 | 3 | Commands/CardCmd.cs:2278 |
| `CardCmd.Transform` | 2 | 3 | Card ops | 2 | 2 | Commands/CardCmd.cs:1966 |
| `CardCmd.TransformTo<T>` | 2 | 3 | Card ops | 2 | 2 | Commands/CardCmd.cs:1957 |
| `CardCmd.Upgrade` | 2 | 3 | Card ops | 9 | 9 | Commands/CardCmd.cs:1882-1890 |
| `CardPlay.Resources` | 2 | 3 | Card play | 1 | 2 | Entities.Cards/ResourceInfo.cs:10-40; Commands/CardCmd.cs:516-522 |
| `CardPileCmd.AddGeneratedCardsToCombat` | 2 | 3 | Cmd facades | 6 | 9 | Commands/CardPileCmd.cs:3204, :3431 |
| `CreatureCmd.Damage(DamageVar)` | 2 | 3 | Cmd facades | 1 | 1 | Commands/CreatureCmd.cs:3588 |
| `DamageCmd.Attack(CalculatedDamageVar)` | 2 | 3 | Cmd facades | 8 | 8 | Commands/DamageCmd.cs:13; Commands.Builders/AttackCommand.cs:433, :732-736, :1131 |
| `DoomPower.DoomKill from card` | 2 | 3 | Doom | 1 | 14 | Models.Powers/DoomPower.cs:506-521 |
| `CalculatedVar family` | 2 | 3 | DynamicVars | 15 | 15 | Localization.DynamicVars/CalculatedVar.cs:40-50 |
| `Side-turn participants` | 2 | 3 | Hook contract | 0 | 32 | Combat/CombatManager.cs:2055-2088, :2457, :2967, :4914, :5080; sim/specs/inventory/kernel-gaps.json (Side-turn participants) |
| `Card hook listeners` | 2 | 3 | Listeners | 9 | 9 | Combat/CombatState.cs:893-910; sim/CONTRACTS.md section 9 |
| `Hook.AfterCardChangedPiles` | 2 | 3 | Listeners | 3 | 16 | Commands/CardPileCmd.cs:444, :980, :2421; Models/AbstractModel.cs:248 |
| `PowerModel.DynamicVars` | 2 | 3 | Powers | 1 | 2 | Models.Cards/Monologue.cs:91-95; Models.Powers/MonologuePower.cs:222-226 |
| `Temporary powers` | 2 | 3 | Powers | 0 | 5 | Models.Powers/TemporaryStrengthPower.cs:18-406; Models/ITemporaryPower.cs |
| `Hook.AfterAutoPostPlayPhaseEntered` | 2 | 3 | Turn flow | 1 | 1 | Combat/CombatManager.cs:2389; Models/AbstractModel.cs:208 |
| `Hook.AfterAutoPrePlayPhaseEntered` | 2 | 3 | Turn flow | 1 | 1 | Combat/CombatManager.cs:3891; Models/AbstractModel.cs:213 |
| `PlayerCmd.EndTurn` | 2 | 3 | Turn flow | 1 | 1 | Commands/PlayerCmd.cs:984-994 |
| `History.CardDrawn` | 3 | 4 | Combat history | 1 | 1 | Combat.History/CombatHistory.cs:94 |
| `History.CardGenerated` | 3 | 4 | Combat history | 1 | 1 | Combat.History/CombatHistory.cs:104 |
| `History.CardPlaysFinished` | 3 | 4 | Combat history | 5 | 6 | Combat.History/CombatHistory.cs:28, :79; Combat.History.Entries/CardPlayFinishedEntry.cs:19-49 |
| `History.CardPlaysStarted` | 3 | 4 | Combat history | 0 | 1 | Combat.History/CombatHistory.cs:26, :74 |
| `History.CreatureAttacked` | 3 | 4 | Combat history | 2 | 2 | Combat.History/CombatHistory.cs:109 |
| `History.DamageReceived` | 3 | 4 | Combat history | 1 | 1 | Combat.History/CombatHistory.cs:114 |
| `History.PowerReceived` | 3 | 4 | Combat history | 1 | 1 | Combat.History/CombatHistory.cs:144 |
| `History.StarsModified` | 3 | 4 | Combat history | 1 | 1 | Combat.History/CombatHistory.cs:149 |
| `CardSelectCmd.FromChooseACardScreen` | 3 | 5 | Prompts | 1 | 1 | Commands/CardSelectCmd.cs:3169-3234 |
| `CardSelectCmd.FromCombatPile` | 3 | 5 | Prompts | 7 | 8 | Commands/CardSelectCmd.cs:3353-3439 |
| `CardSelectCmd.FromHand` | 3 | 5 | Prompts | 9 | 10 | Commands/CardSelectCmd.cs:3668 |
| `CardFactory.GetDistinctForCombat` | 3 | 6 | Card generation | 4 | 6 | Factories/CardFactory.cs:93-102, :118-121; Models/CardPoolModel.cs:68-81; Extensions/IEnumerableExtensions.cs:9-12 |
| `ForgeCmd.Forge` | 3 | 7 | Forge | 10 | 14 | Commands/ForgeCmd.cs:153-209 |
| `SovereignBlade token` | 3 | 7 | Forge | 10 | 14 | Models.Cards/SovereignBlade.cs:223-406 |
| `AttackCommand.FromOsty` | 3 | 7 | Osty | 12 | 14 | Commands.Builders/AttackCommand.cs:765-779 |
| `CreatureCmd.Heal(Osty)` | 3 | 7 | Osty | 1 | 1 | Models.Cards/Spur.cs:96 |
| `CreatureCmd.Kill(Osty)` | 3 | 7 | Osty | 2 | 2 | Models.Powers/DieForYouPower.cs:37-48 |
| `Osty pet creature` | 3 | 7 | Osty | 22 | 26 | Entities.Players/Player.cs:247-251; Models.Monsters/Osty.cs; Combat/CombatState.cs:860-910 |
| `Osty-missing gate` | 3 | 7 | Osty | 13 | 14 | Models.Monsters/Osty.cs:74-78 |
| `OstyCmd.Summon` | 3 | 7 | Osty | 9 | 12 | Commands/OstyCmd.cs:336-394 |
| `PlayerCmd.GainStars` | 3 | 7 | Stars | 10 | 14 | Commands/PlayerCmd.cs:844, :390-393; Entities.Players/PlayerCombatState.cs:138-154 |
| `Star spend hooks` | 3 | 7 | Stars | 23 | 25 | Models/CardModel.cs:1435-1436, :3436; Entities.Players/PlayerCombatState.cs:138-154 |
| `Generic power copy` | 4 | 8 | Cross-cutting | 1 | 1 | Models.Cards/Misery.cs:107-245; Models/AbstractModel.cs:155; Commands/PowerCmd.cs:1260 |
| `DeckVersion mutation` | 3 | 8 | Run-state effects | 1 | 1 | Models.Cards/TheScythe.cs:71-73, :100-126 |
| `Extra combat reward` | 3 | 8 | Run-state effects | 0 | 2 | Rooms/CombatRoom.cs:806; Models.Powers/RoyaltiesPower.cs:14-17; Models.Powers/ForbiddenGrimoirePower.cs:14-20 |
| `Ally targeting (multiplayer)` | 3 | 9 | Multiplayer | 7 | 9 | Commands/CardCmd.cs:426-431; Runs/IRunState.cs:74-84 |

Not listed because the sim already has them: `DamageCmd.Attack` with `Targeting`/`TargetingAllOpponents`/
`TargetingRandomOpponents`/`WithHitCount`, `CreatureCmd.GainBlock`/`Heal`/`Kill`, `PowerCmd.Apply` (single and
multi-target)/`ModifyAmount`/`FindExistingInstanceForStacking`, `PlayerCmd.GainEnergy`, `CardPileCmd.Draw`/
`AddGeneratedCardToCombat`/`Exhaust`, `CardPilePosition.Random` (Shuffle stream), `CardEnergyCost.AddThisCombat`/
`SetThisTurn`, energy X (`ResolveEnergyXValue`, `Hook.ModifyXValue`), star cost affordability and star X
(`LastStarsSpent`, `ResolveStarXValue`), `Hook.ModifyBlock`, `GetResultLocationForCardPlay` and `IsPlayable`
overrides, `BaseReplayCount`, keywords Exhaust/Ethereal/Retain/Innate/Eternal. The `BlockVar`/`DamageVar`
overloads of `GainBlock`/`Damage` used by most cards are thin adapters and are not counted.

## 4. Dependency order

A card's batch is the highest batch among its own requirements and those of the powers and tokens it
needs. Each power and token is listed at the first batch that needs it; port it together with (or before)
that batch's cards. Parentheses: provisional power tier / tier with its own dependencies.

**Batch 1 — tier 1 on the current sim: existing facades, every dependency already ported (Strength, Vulnerable, Weak) or none.**

- Necrobinder (13): Bury, DefendNecrobinder, Defile, Defy, Eradicate, Fear, Parse, Putrefy, Reap, SharedFate, Sow, StrikeNecrobinder, Wisp
- Regent (6): CelestialMight, DefendRegent, HeavenlyDrill, KnowThyPlace, Prophesize, StrikeRegent

**Batch 2 — tier-1 card logic that needs unported powers or tokens; port those in the same batch (their tier-2 needs: side-turn participants, temporary powers, Doom kill).**

- Powers: ArsenalPower (t1/1), BlockNextTurnPower (t1/1), BorrowedTimePower (t2/2), CountdownPower (t2/2), CrushUnderPower (t2/2), DanseMacabrePower (t1/1), DebilitatePower (t2/2), DemesnePower (t1/1), DoomPower (t2/2), EnergyNextTurnPower (t1/1), EnfeeblingTouchPower (t2/2), FriendshipPower (t1/1), HangPower (t1/1), HauntPower (t1/1), MonarchsGazePower (t1/2), MonarchsGazeStrengthDownPower (t2/2), NeurosurgePower (t2/2), OblivionPower (t1/2), OrbitPower (t1/1), PagestormPower (t1/1), PillarOfCreationPower (t1/1), ReaperFormPower (t1/2), ShroudPower (t1/2), SleightOfFleshPower (t2/2), SpiritOfAshPower (t1/1), VeilpiercerPower (t1/1), VigorPower (t1/1)
- Tokens: Soul
- Necrobinder (22): BlightStrike, BorrowedTime, Countdown, DanseMacabre, Deathbringer, Debilitate, Delay, Demesne, EnfeeblingTouch, Friendship, Hang, Haunt, NegativePulse, Neurosurge, Oblivion, Pagestorm, ReaperForm, Scourge, Shroud, SleightOfFlesh, SpiritOfAsh, Veilpiercer
- Regent (9): Arsenal, CrushUnder, Glitterstream, Hegemony, MonarchsGaze, Orbit, Patter, PillarOfCreation, Terraforming

**Batch 3 — tier 2: card-kernel facades missing from the sim (card hook listeners, calculated vars, card ops and creation, auto-play, EndTurn, ResourceInfo).**

- Powers: MonologuePower (t2/2), VoidFormPower (t2/2)
- Tokens: Debris
- Necrobinder (12): CaptureSpirit, DrainPower, Eidolon, EndOfDays, GraveWarden, Melancholy, NoEscape, Reave, Severance, SoulStorm, TimesUp, Undeath
- Regent (8): Bombardment, CollisionCourse, CrashLanding, IAmInvincible, KinglyKick, KinglyPunch, Monologue, VoidForm

**Batch 4 — tier 3, combat history readers.**

- Powers: DrawCardsNextTurnPower (t2/2), LethalityPower (t3/3), PaleBlueDotPower (t3/3)
- Necrobinder (5): BansheesCry, DeathsDoor, DeathMarch, Lethality, PullFromBelow
- Regent (4): LunarBlast, MakeItSo, PaleBlueDot, Supermassive

**Batch 5 — tier 3, player prompts (choice API).**

- Powers: ForegoneConclusionPower (t3/3), TyrannyPower (t3/3)
- Tokens: MinionDiveBomb, MinionSacrifice, MinionStrike
- Necrobinder (5): Dredge, Graveblast, SculptingStrike, Seance, Transfigure
- Regent (9): Begone, Charge, CosmicIndifference, ForegoneConclusion, Glimmer, Guards, HeirloomHammer, PhotonCut, Tyranny

**Batch 6 — tier 3, card generation from pools (CardFactory + UnlockState).**

- Powers: CallOfTheVoidPower (t3/3), SpectrumShiftPower (t3/3)
- Necrobinder (1): CallOfTheVoid
- Regent (3): BundleOfJoy, ManifestAuthority, SpectrumShift

**Batch 7 — tier 3, character subsystems: Stars, Osty, Forge (SovereignBlade).**

- Powers: BlackHolePower (t3/3), CalcifyPower (t3/3), ChildOfTheStarsPower (t3/3), ConquerorPower (t3/3), DevourLifePower (t3/3), DieForYouPower (t3/3), DyingStarPower (t2/2), FurnacePower (t3/3), GenesisPower (t3/3), NecroMasteryPower (t3/3), ParryPower (t3/3), PlatingPower (t2/2), ReflectPower (t2/2), RetainHandPower (t2/2), SeekingEdgePower (t3/3), SentryModePower (t2/3), SicEmPower (t3/3), StarNextTurnPower (t3/3), SummonNextTurnPower (t3/3), SwordSagePower (t3/3), TheSealedThronePower (t3/3)
- Tokens: SovereignBlade, SweepingGaze
- Necrobinder (25): Afterlife, Bodyguard, BoneShards, Calcify, Cleanse, DevourLife, Dirge, Fetch, Flatten, HighFive, Invoke, NecroMastery, Poke, Protector, PullAggro, Rattle, Reanimate, RightHandHand, Sacrifice, SentryMode, SicEm, Snap, Spur, Squeeze, Unleash
- Regent (46): Alignment, AstralPulse, BeatIntoShape, BigBang, BlackHole, Bulwark, ChildOfTheStars, CloakOfStars, Comet, Conqueror, Convergence, CrescentSpear, DecisionsDecisions, Devastate, DyingStar, FallingStar, Furnace, GammaBlast, GatherLight, Genesis, Glow, GuidingStar, HiddenCache, KnockoutBlow, MeteorShower, NeutronAegis, Parry, ParticleWall, Quasar, Radiate, RefineBlade, Reflect, Resonance, RoyalGamble, SeekingEdge, SevenStars, ShiningStrike, SolarStrike, SpoilsOfBattle, Stardust, SummonForth, SwordSage, TheSealedThrone, TheSmith, Venerate, WroughtInWar

**Batch 8 — run-state effects and cross-cutting (deck copy, combat-end rewards, generic debuff copy).**

- Powers: ForbiddenGrimoirePower (t3/3), RoyaltiesPower (t3/3)
- Necrobinder (3): ForbiddenGrimoire, Misery, TheScythe
- Regent (1): Royalties

**Batch 9 — multiplayer-only: unreachable in single-player pools; port last or never.**

- Powers: CacophonyPower (t2/2), HammerTimePower (t3/3), SoulboundPower (t3/3), UnderworldPower (t3/3)
- Necrobinder (5): Cacophony, GlimpseBeyond, LegionOfBone, Soulbound, Underworld
- Regent (5): Constellation, HammerTime, Largesse, Plot, Tutor

Batch prerequisites (`batch_prerequisites`): 1 after -; 2 after 1; 3 after 1, 2; 4 after 3; 5 after 3; 6 after 3; 7 after 3, 4, 5; 8 after 3, 5, 6, 7; 9 after -.
Batches 1-5 use the same numbering as `cards-ironclad-colorless.classified.json`; 6-9 are this slice's subsystem
gates. `port_gates` names the subsystem that fixes each card's batch:

| Batch | Gate | Cards |
|---|---|---|
| 1 | none | 19 |
| 2 | unported powers/tokens | 31 |
| 3 | Auto-play | 1 |
| 3 | Auto-play, Listeners, Turn flow | 2 |
| 3 | Card creation | 2 |
| 3 | Card creation, Card ops, Cmd facades | 1 |
| 3 | Card creation, Cmd facades | 3 |
| 3 | Card model | 1 |
| 3 | Card model, Listeners | 1 |
| 3 | Card ops | 1 |
| 3 | Cmd facades, DynamicVars | 2 |
| 3 | Doom | 1 |
| 3 | DynamicVars | 1 |
| 3 | Listeners | 2 |
| 3 | Powers | 1 |
| 3 | Turn flow | 1 |
| 4 | Combat history | 9 |
| 5 | Prompts | 14 |
| 6 | Card generation | 4 |
| 7 | Forge | 11 |
| 7 | Forge, Stars | 2 |
| 7 | Osty | 25 |
| 7 | Stars | 33 |
| 8 | Cross-cutting | 1 |
| 8 | Run-state effects | 3 |
| 9 | Multiplayer | 10 |

Ordering constraints inside the slice:

- The side-turn participants contract change (W1; the sim's side-turn hooks pass only the side) is needed by
  19 of the powers below, including DoomPower and the TemporaryStrengthPower family; it gates no card batch
  but makes those cards tier 2 with dependencies.
- DoomPower (tier 2: DoomKill ordering, `Hook.AfterDiedToDoom`) is ported in batch 2 and precedes the Doom
  cards (BlightStrike, Deathbringer, NegativePulse, Scourge, Countdown, Neurosurge, Oblivion, ReaperForm, Shroud
  in batch 2; EndOfDays, NoEscape, TimesUp in batch 3; DeathsDoor in batch 4).
- TemporaryStrengthPower and its subclasses CrushUnder, EnfeeblingTouch, MonarchsGazeStrengthDown (batch 2) and
  DyingStar (batch 7, star cost) share one base class; SleightOfFleshPower and Misery read `ITemporaryPower`.
- Soul (token, tier 1) arrives in batch 2 with Haunt; Soul creators need card creation (batch 3) and Seance a
  prompt (batch 5); DevourLife waits for Osty (batch 7).
- The Osty subsystem (pet creature, `OstyCmd.Summon`, `AttackCommand.FromOsty`, DieForYouPower, the
  Osty-missing gate) gates 25 single-player Necrobinder cards in batch 7, including Cleanse and Snap (prompts,
  batch 5) and SentryMode (SweepingGaze token) and the Osty-dependent powers Calcify, DevourLife, NecroMastery,
  SicEm, SummonNextTurn.
- Stars (`PlayerCmd.GainStars`, star spend hooks) gate 35 Regent cards in batch 7 (state and affordability are
  already in the sim); Forge (`ForgeCmd.Forge`, SovereignBlade with `Hook.AfterCardChangedPiles`) gates 13
  (BigBang and TheSmith need both).
- Batch 8: TheScythe (deck copy), ForbiddenGrimoire and Royalties (combat-end rewards), Misery (generic debuff
  copy, tier 4).

Powers these cards depend on (provisional tiers; `power_dependencies` in the JSON):

| Power | Tier/with deps | Batch | Behaviour | Requirements | Depends on | Cards |
|---|---|---|---|---|---|---|
| StrengthPower | ported | 1 | ported | — | — | 9 |
| VulnerablePower | ported | 1 | ported | — | — | 9 |
| WeakPower | ported | 1 | ported | — | — | 9 |
| ArsenalPower | t1/1 | 2 | AfterCardGeneratedForCombat with creator == owner: +Amount Strength | — | StrengthPower | 1 |
| BlockNextTurnPower | t1/1 | 2 | AfterBlockCleared on owner: gain Amount unpowered block, then remove | — | — | 1 |
| BorrowedTimePower | t2/2 | 2 | owner's cards cost +Amount energy (TryModifyEnergyCostInCombat); removed at owner's side turn end | Side-turn participants | — | 1 |
| CacophonyPower | t2/2 | 2 | Instanced; counter var Cards 33 decremented per card drawn; at 0: Amount unpowered damage to a random hittable enemy (CombatTargets), reset to 33 | PowerModel.DynamicVars | — | 1 |
| CountdownPower | t2/2 | 2 | AfterSideTurnStart (owner participates): Amount Doom to a random hittable enemy (CombatTargets) | Side-turn participants | DoomPower | 1 |
| CrushUnderPower | t2/2 | 2 | TemporaryStrengthPower (negative): Strength -Amount until the target's side turn ends | Temporary powers, Side-turn participants | StrengthPower | 1 |
| DanseMacabrePower | t1/1 | 2 | BeforeCardPlayed: owner plays a card with resolved energy cost >= 2 -> Amount unpowered block | — | — | 1 |
| DebilitatePower | t2/2 | 2 | consulted by VulnerablePower/WeakPower: doubles their effect on the owner (sim has IVulnerableMultiplierModifier/IWeakMultiplierModifier); decrements at owner's side turn end | Side-turn participants | VulnerablePower, WeakPower | 1 |
| DemesnePower | t1/1 | 2 | +Amount hand draw and +Amount max energy | — | — | 1 |
| DoomPower | t2/2 | 2 | enemy side: BeforeSideTurnEnd of the enemy turn; player side: AfterSideTurnEnd of the player turn; the first doomed participant (CurrentHp <= Doom) triggers DoomKill of every doomed creature on that side, then Hook.AfterDiedToDoom | DoomPower.DoomKill from card, Side-turn participants | — | 14 |
| DrawCardsNextTurnPower | t2/2 | 2 | +Amount hand draw on the next turn (AmountOnTurnStart != 0), removed after that turn starts | Side-turn participants | — | 4 |
| DyingStarPower | t2/2 | 2 | TemporaryStrengthPower (negative) | Temporary powers, Side-turn participants | StrengthPower | 1 |
| EnergyNextTurnPower | t1/1 | 2 | AfterEnergyReset: gain Amount energy, then remove | — | — | 5 |
| EnfeeblingTouchPower | t2/2 | 2 | TemporaryStrengthPower (negative) | Temporary powers, Side-turn participants | StrengthPower | 1 |
| FriendshipPower | t1/1 | 2 | +Amount max energy | — | — | 1 |
| HangPower | t1/1 | 2 | damage from Hang cards to the owner x Amount | — | — | 1 |
| HauntPower | t1/1 | 2 | AfterCardPlayed: owner plays a Soul -> Amount unblockable unpowered damage to a random hittable enemy (CombatTargets) | — | Soul | 1 |
| MonarchsGazePower | t1/2 | 2 | AfterDamageGiven by owner (powered attack): MonarchsGazeStrengthDownPower Amount on the target | — | MonarchsGazeStrengthDownPower | 1 |
| MonarchsGazeStrengthDownPower | t2/2 | 2 | TemporaryStrengthPower (negative) | Temporary powers, Side-turn participants | StrengthPower | 1 |
| MonologuePower | t2/2 | 2 | Instanced; each card the owner plays this turn grants Strength (var Strength), all reverted at side turn end | PowerModel.DynamicVars, Side-turn participants | StrengthPower | 1 |
| NeurosurgePower | t2/2 | 2 | AfterSideTurnStart: Amount Doom on the owner | Side-turn participants | DoomPower | 1 |
| OblivionPower | t1/2 | 2 | InstancedPerApplier on an enemy: each card the applier plays applies Amount Doom to the owner; removed at player side turn end | — | DoomPower | 1 |
| OrbitPower | t1/1 | 2 | Instanced; each 4 energy the owner spends (cumulative) -> gain Amount energy | — | — | 1 |
| PagestormPower | t1/1 | 2 | AfterCardDrawn: owner draws an Ethereal card -> draw Amount | — | — | 1 |
| PillarOfCreationPower | t1/1 | 2 | AfterCardGeneratedForCombat by owner: Amount unpowered block | — | — | 1 |
| PlatingPower | t2/2 | 2 | block Amount at turn end (player) / start (enemy), decrements | Side-turn participants | — | 1 |
| ReaperFormPower | t1/2 | 2 | AfterDamageGiven by owner or owner's pet (powered, TotalDamage > 0): TotalDamage x Amount Doom on the target | — | DoomPower | 1 |
| ReflectPower | t2/2 | 2 | AfterDamageReceived (powered, blocked > 0): blocked amount back to the dealer (unpowered); decrements at owner's turn start | Side-turn participants | — | 1 |
| RetainHandPower | t2/2 | 2 | ShouldFlush false for owner (hand retained); decrements at side turn end | Side-turn participants | — | 1 |
| ShroudPower | t1/2 | 2 | AfterPowerAmountChanged: owner applies Doom -> Amount unpowered block | — | DoomPower | 1 |
| SleightOfFleshPower | t2/2 | 2 | owner applies a non-temporary debuff to an enemy -> Amount unpowered damage to it | Temporary powers | — | 1 |
| SpiritOfAshPower | t1/1 | 2 | BeforeCardPlayed: owner plays an Ethereal card -> Amount unpowered block | — | — | 1 |
| VeilpiercerPower | t1/1 | 2 | owner's Ethereal cards in hand/play cost 0 (Late); decrements when one is played | — | — | 1 |
| VigorPower | t1/1 | 2 | +Amount damage on the owner's next powered card attack command, consumed after it | — | — | 2 |
| VoidFormPower | t2/2 | 2 | first Amount non-auto-play cards each turn cost 0 energy and 0 stars | Side-turn participants | — | 1 |
| LethalityPower | t3/3 | 4 | owner's first Attack started this turn (not a replay): x(1 + Amount/100) damage | History.CardPlaysStarted | — | 1 |
| PaleBlueDotPower | t3/3 | 4 | once per turn when the owner has finished >= 5 card plays this turn: DrawCardsNextTurnPower Amount | History.CardPlaysFinished, Side-turn participants | DrawCardsNextTurnPower | 1 |
| ForegoneConclusionPower | t3/3 | 5 | BeforeHandDraw: ShuffleIfNecessary, choose Amount cards from the draw pile into hand, remove power | CardSelectCmd.FromCombatPile | — | 1 |
| TyrannyPower | t3/3 | 5 | +Amount hand draw; AfterPlayerTurnStart: choose Amount hand cards to exhaust | CardSelectCmd.FromHand | — | 1 |
| CallOfTheVoidPower | t3/3 | 6 | BeforeHandDraw: Amount cards from the character pool (unlocked, not Basic/Ancient), each First(GetDistinctForCombat(...,1, CombatCardGeneration)) made Ethereal, into hand | CardFactory.GetDistinctForCombat, CardCmd.ApplyKeyword, CardPileCmd.AddGeneratedCardsToCombat | — | 1 |
| SpectrumShiftPower | t3/3 | 6 | BeforeHandDraw: Amount distinct colorless cards into hand (CombatCardGeneration) | CardFactory.GetDistinctForCombat, CardPileCmd.AddGeneratedCardsToCombat | — | 1 |
| BlackHolePower | t3/3 | 7 | after a star-spending card play (last in series) or any star gain: Amount unpowered damage to all hittable enemies | PlayerCmd.GainStars, Star spend hooks, CardPlay.Resources | — | 1 |
| CalcifyPower | t3/3 | 7 | ModifyDamageAdditive +Amount on powered attacks dealt by the owner's Osty | Osty pet creature, AttackCommand.FromOsty | — | 1 |
| ChildOfTheStarsPower | t3/3 | 7 | AfterStarsSpent by owner: gain Amount x stars spent unpowered block | Star spend hooks | — | 1 |
| ConquerorPower | t3/3 | 7 | SovereignBlade powered attacks against the owner deal x2; decrements at owner's side turn end | SovereignBlade token, Side-turn participants | SovereignBlade | 1 |
| DevourLifePower | t3/3 | 7 | AfterCardPlayed: owner plays a Soul -> Summon Amount | OstyCmd.Summon | Soul | 1 |
| DieForYouPower | t3/3 | 7 | on Osty: powered attacks aimed at the pet owner hit Osty while alive (ModifyUnblockedDamageTarget); dead Osty stays in combat; power survives owner death | Osty pet creature | — | 12 |
| FurnacePower | t3/3 | 7 | AfterSideTurnStart (owner participates): Forge Amount | ForgeCmd.Forge, Side-turn participants | SovereignBlade | 1 |
| GenesisPower | t3/3 | 7 | AfterEnergyReset: gain Amount stars | PlayerCmd.GainStars | — | 1 |
| NecroMasteryPower | t3/3 | 7 | AfterCurrentHpChanged: owner's Osty loses HP -> that loss x Amount unblockable unpowered damage to all hittable enemies | Osty pet creature | — | 1 |
| ParryPower | t3/3 | 7 | read by SovereignBlade: block = Amount when a blade is played | SovereignBlade token | SovereignBlade | 1 |
| SeekingEdgePower | t3/3 | 7 | read by SovereignBlade: blade targets all enemies | SovereignBlade token | SovereignBlade | 1 |
| SentryModePower | t2/3 | 7 | BeforeHandDraw: Amount SweepingGaze into hand | CombatState.CreateCard<T> | SweepingGaze | 1 |
| SicEmPower | t3/3 | 7 | AfterDamageGiven by the applier's Osty to the owner -> Summon Amount; removed at the owner's side turn end | OstyCmd.Summon, AttackCommand.FromOsty, Side-turn participants | — | 1 |
| StarNextTurnPower | t3/3 | 7 | AfterEnergyReset: gain Amount stars, then remove | PlayerCmd.GainStars | — | 2 |
| SummonNextTurnPower | t3/3 | 7 | AfterPlayerTurnStart (AmountOnTurnStart != 0): Summon Amount, then remove | OstyCmd.Summon | — | 1 |
| SwordSagePower | t3/3 | 7 | every owner SovereignBlade BaseReplayCount += Amount (also on entering combat); reverted on removal | SovereignBlade token | SovereignBlade | 1 |
| TheSealedThronePower | t3/3 | 7 | BeforeCardPlayed: owner plays a card -> gain Amount stars | PlayerCmd.GainStars | — | 1 |
| ForbiddenGrimoirePower | t3/3 | 8 | AfterCombatEnd: Amount extra CardRemovalReward | Extra combat reward | — | 1 |
| RoyaltiesPower | t3/3 | 8 | AfterCombatEnd: extra GoldReward Amount | Extra combat reward | — | 1 |
| HammerTimePower | t3/3 | 9 | AfterForge by owner: every other living player forges the same amount | ForgeCmd.Forge, Ally targeting (multiplayer) | SovereignBlade | 1 |
| SoulboundPower | t3/3 | 9 | the applier generates a Soul -> owner (another player) gets Amount Souls in draw pile | CardPileCmd.AddGeneratedCardsToCombat, CombatState.CreateCard<T>, Ally targeting (multiplayer) | Soul | 1 |
| UnderworldPower | t3/3 | 9 | ally (not owner, not owner's pet) powered damage -> TotalDamage x Amount Doom on the target; removed at enemy side turn end | Ally targeting (multiplayer) | DoomPower | 1 |

Reconciliation with `sim/specs/inventory/powers.classified.json` (the powers slice owns these tiers): 11 of the
66 provisional tiers differ: CacophonyPower 2 vs 1, ConquerorPower 3 vs 2, ParryPower 3 vs 1, ReaperFormPower 1 vs 2, SeekingEdgePower 3 vs 1, SoulboundPower 3 vs 2, SwordSagePower 3 vs 2, UnderworldPower 3 vs 2, VoidFormPower 2 vs 3, VulnerablePower 1 vs 3, WeakPower 1 vs 3. The differences are scale choices, not readings: the powers slice rates
SovereignBlade-reading powers by their own code (this table adds the Forge token), Vulnerable/Weak by their relic
queries (PaperPhrog, PaperKrane), VoidFormPower under Stars, and ReaperFormPower by `PetOwner` (this table by
its Doom dependency). Card tiers here do not depend on these power tiers; only `tier_with_deps` does.

Token cards (`token_dependencies`):

| Token | Pool | Tier | Batch | Behaviour | Used by |
|---|---|---|---|---|---|
| Debris | STATUS | t1 | 2 | 1-cost Status, Exhaust, no effect, not upgradable | CollisionCourse, CrashLanding |
| MinionDiveBomb | TOKEN | t1 | 2 | 0-cost Attack (Minion), Exhaust: 13 (+3) damage | Charge |
| MinionSacrifice | TOKEN | t1 | 2 | 0-cost Skill (Minion), Exhaust: 7 (+3) block | Guards |
| MinionStrike | TOKEN | t1 | 2 | 0-cost Attack (Strike, Minion), Exhaust: 6 (+3) damage, draw 1 | Begone |
| Soul | TOKEN | t1 | 2 | 0-cost Skill, Exhaust: draw 2 (+1) | CaptureSpirit, DevourLife, Dirge, GlimpseBeyond, GraveWarden, Haunt, Reave, Seance, Severance, SoulStorm, Soulbound |
| SovereignBlade | TOKEN | t3 | 7 | 2-cost Attack, Retain: 10 damage x Repeat; forged damage accumulates | BeatIntoShape, BigBang, Bulwark, Conqueror, Furnace, HammerTime, Parry, RefineBlade, SeekingEdge, SpoilsOfBattle, SummonForth, SwordSage, TheSmith, WroughtInWar |
| SweepingGaze | TOKEN | t3 | 7 | 0-cost Attack (OstyAttack), Ethereal+Exhaust: Osty hits a random enemy 10 (+5) (CombatTargets) | SentryMode |

## 5. Prompt types

| Kind | Entry | Pile | Origin | Sites |
|---|---|---|---|---|
| ChooseCard | `CardSelectCmd.FromChooseACardScreen` | generated | direct | 1 |
| HandSelect | `CardSelectCmd.FromHand` | Hand | direct | 9 |
| HandSelect | `CardSelectCmd.FromHand` | Hand | indirect | 1 |
| Nested | `CardCmd.AutoPlay of each exhausted Ethereal card` | - | nested | 1 |
| Nested | `CardCmd.AutoPlay of the chosen Skill (3 times)` | - | nested | 1 |
| PileSelect | `CardSelectCmd.FromCombatPile` | Discard | direct | 3 |
| PileSelect | `CardSelectCmd.FromCombatPile` | Draw | direct | 3 |
| PileSelect | `CardSelectCmd.FromCombatPile` | Draw | indirect | 1 |
| PileSelect | `CardSelectCmd.FromCombatPile` | Draw (target player) | direct | 1 |

All direct and indirect prompt sites:

| Card | Site | Kind | Pile | Min..max | Filter | Skip | Confirm | Then | Citation |
|---|---|---|---|---|---|---|---|---|---|
| Begone | `BEGONE:OnPlay` | HandSelect | Hand | 1..1 | — |  |  | CardCmd.Transform(pick, new MinionStrike) | Models.Cards/Begone.cs:65 |
| Charge | `CHARGE:OnPlay` | PileSelect | Draw | 2..2 | — |  |  | CardCmd.TransformTo<MinionDiveBomb> per pick, in pick order | Models.Cards/Charge.cs:77 |
| Cleanse | `CLEANSE:OnPlay` | PileSelect | Draw | 1..1 | — |  |  | CardCmd.Exhaust the pick | Models.Cards/Cleanse.cs:118 |
| CosmicIndifference | `COSMIC_INDIFFERENCE:OnPlay` | PileSelect | Discard | 1..1 | — |  |  | CardPileCmd.Add(pick, Draw, Top) | Models.Cards/CosmicIndifference.cs:143 |
| DecisionsDecisions | `DECISIONS_DECISIONS:OnPlay` | HandSelect | Hand | 1..1 | Type == Skill && !Keywords.Contains(Unplayable) |  |  | CardCmd.AutoPlay(pick, null) x Repeat(3) | Models.Cards/DecisionsDecisions.cs:154 |
| Dredge | `DREDGE:OnPlay` | PileSelect | Discard | n..n | — |  |  | CardPileCmd.Add(picks, Hand) | Models.Cards/Dredge.cs:114 |
| ForegoneConclusion | `FOREGONE_CONCLUSION_POWER:BeforeHandDraw` | PileSelect | Draw | Amount..Amount | — |  |  | CardPileCmd.Add(picks, Hand), remove power | Models.Powers/ForegoneConclusionPower.cs:177 |
| Glimmer | `GLIMMER:OnPlay` | HandSelect | Hand | 1..1 | — |  |  | CardPileCmd.Add(picks, Draw, Top) | Models.Cards/Glimmer.cs:115 |
| Graveblast | `GRAVEBLAST:OnPlay` | PileSelect | Discard | 1..1 | — |  |  | CardPileCmd.Add(pick, Hand) | Models.Cards/Graveblast.cs:125 |
| Guards | `GUARDS:OnPlay` | HandSelect | Hand | 0..999999999 | — |  | yes | CardCmd.Transform(pick, new MinionSacrifice) per pick | Models.Cards/Guards.cs:105 |
| HeirloomHammer | `HEIRLOOM_HAMMER:OnPlay` | HandSelect | Hand | 1..1 | VisualCardPool.IsColorless |  |  | Repeat(1) x AddGeneratedCardToCombat(pick.CreateClone(), Hand) | Models.Cards/HeirloomHammer.cs:126 |
| PhotonCut | `PHOTON_CUT:OnPlay` | HandSelect | Hand | 1..1 | — |  |  | CardPileCmd.Add(picks, Draw, Top) | Models.Cards/PhotonCut.cs:119 |
| Quasar | `QUASAR:OnPlay` | ChooseCard | generated | 1..1 | — | yes |  | CardPileCmd.AddGeneratedCardToCombat(pick, Hand) | Models.Cards/Quasar.cs:71 |
| SculptingStrike | `SCULPTING_STRIKE:OnPlay` | HandSelect | Hand | 1..1 | !GetKeywordsWithSources(Local).Contains(Ethereal) |  |  | CardCmd.ApplyKeyword(pick, Ethereal) | Models.Cards/SculptingStrike.cs:90 |
| Seance | `SEANCE:OnPlay` | PileSelect | Draw | 1..1 | — |  |  | CardCmd.TransformTo<Soul>(pick) | Models.Cards/Seance.cs:77 |
| Snap | `SNAP:OnPlay` | HandSelect | Hand | 1..1 | !Keywords.Contains(Retain) |  |  | CardCmd.ApplyKeyword(pick, Retain) | Models.Cards/Snap.cs:106 |
| Transfigure | `TRANSFIGURE:OnPlay` | HandSelect | Hand | 1..1 | — |  |  | EnergyCost.AddThisCombat(1); BaseReplayCount++ | Models.Cards/Transfigure.cs:68 |
| Tutor | `TUTOR:OnPlay` | PileSelect | Draw (target player) | 1..1 | — |  |  | CardPileCmd.Add(pick, Hand) | Models.Cards/Tutor.cs:69 |
| Tyranny | `TYRANNY_POWER:AfterPlayerTurnStart` | HandSelect | Hand | Amount..Amount | — |  |  | CardCmd.Exhaust per pick | Models.Powers/TyrannyPower.cs:163 |

Notes for the choice API (docs/sim-port/design-combat-kernel.md section 4):

- `CardSelectorPrefs(prompt, n)` sets MinSelect = MaxSelect = n, so RequireManualConfirmation is false and
  a pile/hand with at most n eligible cards resolves without a decision (`CardSelection/CardSelectorPrefs.cs:99-115`).
  Only Guards (`0..999999999`) needs a confirm action; Quasar is the only skippable ChooseCard.
- Draw-pile prompts (Cleanse, Seance, Charge, Tutor, ForegoneConclusionPower) present options sorted by
  (rarity, id) on the selector path; discard prompts (Dredge, Graveblast, CosmicIndifference) keep pile order.
- Multi-pick order matters for Charge (transform order fixes new card ids) and Guards; the put-back prompts
  (Glimmer, PhotonCut) take one card in v0.111.0, but PutBack is a var, so keep them ordered.
- Filters: SculptingStrike reads Local keywords only, Snap all keywords (incl. Hook.ModifyKeywordsInCombat),
  DecisionsDecisions only Skills that are not Unplayable (`PretendCardsCanBePlayed` is UI-only),
  HeirloomHammer `VisualCardPool.IsColorless`.
- Indirect prompts open in the turn-setup context: ForegoneConclusionPower at BeforeHandDraw (after
  `CardPileCmd.ShuffleIfNecessary`), TyrannyPower at AfterPlayerTurnStart; both are deferral sites in the
  kernel design's prompt catalogue.
- Nested prompts: Eidolon (every Ethereal card in the exhaust pile) and DecisionsDecisions (the chosen Skill,
  3 times) auto-play other cards; any prompt of the auto-played card opens inside the play. Bombardment and
  IAmInvincible auto-play only themselves (no prompt). AnyEnemy targets of auto-plays are random, not prompts.
- No Necrobinder or Regent card uses HandUpgrade, SimpleGrid, ChooseABundle, discard prompts, or a run-layer
  deck prompt.

## 6. Character subsystems

### 6.1 Osty (Necrobinder)

- **Summon** (`Commands/OstyCmd.cs:336-394`): `amount = Hook.ModifySummonAmount(...)`; 0 returns. Osty alive →
  `CreatureCmd.GainMaxHp(Osty, amount)` only (no heal). Otherwise the dead Osty in `CombatState.Allies` is
  re-added (`PlayerCombatState.AddPetInternal`) or a new one is created (`PlayerCmd.AddPet<Osty>` →
  `CombatState.CreateCreature` on the player's side + `CreatureCmd.Add`, then `DieForYouPower` 1), then
  `SetMaxHp(amount)` and `Heal(amount)`, and `Hook.AfterOstyRevived` on a revive. Always
  `History.Summoned(amount)` and `Hook.AfterSummon`. Summon sources here: 9 cards plus DevourLife, SicEm,
  SummonNextTurn powers and the starter relic BoundPhylactery (1 at combat start, 1 after each energy reset
  from turn 2, `Models.Relics/BoundPhylactery.cs:221-248`).
- **Osty** (`Models.Monsters/Osty.cs`): 1 HP monster, `NOTHING_MOVE`; found as `Player.Osty`
  (`PlayerCombatState.GetPet<Osty>()`), `IsOstyAlive`, `IsOstyMissing = !IsOstyAlive`
  (`Entities.Players/Player.cs:247-251`). As an ally creature after the player in `Allies`, its powers
  (DieForYouPower) and its monster model are hook listeners after all of the player's listeners (powers,
  relics, potions, orbs, cards) and before the enemies (`Combat/CombatState.cs:860-910`).
- **Damage to the player** (`Commands/CreatureCmd.cs:3697-3719`): a pet target spends its owner's block
  (`originalTarget.PetOwner?.Creature ?? originalTarget`); DieForYouPower redirects the unblocked part of a
  powered attack on the owner to a living Osty (`ModifyUnblockedDamageTarget`,
  `Models.Powers/DieForYouPower.cs:15-30`); Osty's overkill returns to the owner after the AfterOsty HP-loss
  phase. The sim's `CreatureCmd.Damage` has the phases and the redirect but uses the target itself as blocker.
- **Osty attacks**: `DamageCmd.Attack(v).FromOsty(owner.Osty, card, cardPlay)` sets Osty as attacker and the card
  as source (`Commands.Builders/AttackCommand.cs:765-779`); `OstyDamageVar` and `.FromOsty()` on vars only
  change previews. History records the attack as `CreatureAttacked` by Osty (Flatten, Rattle read it).
- **Missing-Osty patterns**: (a) whole effect skipped — BoneShards, Fetch (no draw either), Flatten, HighFive
  (also unplayable), Poke, Protector, Rattle, RightHandHand, Sacrifice, Squeeze, Unleash, SweepingGaze;
  (b) only the attack skipped — SicEm still applies SicEmPower, Snap still prompts for Retain.
- **Killing Osty**: BoneShards (after its block) and Sacrifice (block = 3 x max HP computed before the kill).
  The dead Osty stays in combat (DieForYouPower `ShouldCreatureBeRemovedFromCombatAfterDeath`), so the next
  summon revives it with max HP = the summon amount.
- **Osty-scaled values**: Unleash (+current HP), Protector (+max HP), Sacrifice (3 x max HP), Squeeze (+5 per
  other OstyAttack card), NecroMasteryPower (Osty HP loss x Amount to all enemies), CalcifyPower (+Amount on
  Osty attacks).

### 6.2 Stars (Regent)

- State already in the sim: `PlayerState.Stars`, card star costs, affordability, `LastStarsSpent`, star X.
- Missing: `PlayerCmd.GainStars` (combat-ending guard, `Hook.ShouldGainStars`, `Hook.AfterStarsGained`;
  `Commands/PlayerCmd.cs:844`, `:390-393`), the spend hook (`CardModel.SpendStars` → `LoseStars` +
  `Hook.AfterStarsSpent`, `Models/CardModel.cs:1435-1436`) and `History.StarsModified` from the Stars setter
  (`Entities.Players/PlayerCombatState.cs:138-154`).
- Star-cost cards in the slice (23 incl. Stardust X and multiplayer Constellation): Alignment 2, AstralPulse 3,
  CloakOfStars 1, Comet 5, Constellation 2, CrescentSpear 1, DecisionsDecisions 6, Devastate 4, DyingStar 3,
  FallingStar 2, GammaBlast 3, GuidingStar 1, MeteorShower 2, NeutronAegis 5, ParticleWall 2, Quasar 2,
  Reflect 3, Resonance 2, RoyalGamble 5, SevenStars 7, TheSealedThrone 3, TheSmith 4, Stardust X.
- Star gain: BigBang, GatherLight, Glow, HiddenCache, KnockoutBlow (on kill), RoyalGamble, ShiningStrike,
  SolarStrike, Venerate; powers Genesis, StarNextTurn, TheSealedThrone; starter relic DivineRight (3 stars
  on entering a combat room, `Models.Relics/DivineRight.cs:90`).
- Readers: Radiate (stars gained this turn), CrescentSpear (star-cost cards owned), BlackHolePower and
  ChildOfTheStarsPower (gain/spend hooks), VoidFormPower (`TryModifyStarCost` → 0).

### 6.3 Forge and SovereignBlade (Regent)

- `ForgeCmd.Forge(amount, player, source)` (`Commands/ForgeCmd.cs:153-209`): nothing once the combat is
  over/ending; with no non-dupe SovereignBlade outside the exhaust pile a new blade (CreatedThroughForge) is
  generated into hand; then every non-dupe blade, exhausted ones included, gets `AddDamage(amount)` +
  `AfterForged`; then `Hook.AfterForge` (HammerTimePower, multiplayer). A new blade is a generated card
  (`AddGeneratedCardToCombat` = `AddGeneratedCardsToCombat([card])`, `Commands/CardPileCmd.cs:3195-3241`):
  `History.CardGenerated` (Supermassive counts it), `AfterCardEnteredCombat`, `AfterCardGeneratedForCombat`
  (ArsenalPower and PillarOfCreationPower trigger on it).
- SovereignBlade (`Models.Cards/SovereignBlade.cs`): 2 energy (upg 1), Retain, 10 damage x Repeat; damage
  persists through `CurrentDamage`; SeekingEdgePower → AllEnemies; ParryPower → block on play; SwordSagePower
  → BaseReplayCount; ConquerorPower → x2 against its owner; overrides `AfterCardChangedPiles`.
- Forge sources: BeatIntoShape, BigBang, Bulwark, Conqueror, RefineBlade, SeekingEdge, SpoilsOfBattle,
  SummonForth (also pulls every blade to hand), TheSmith, WroughtInWar; FurnacePower each turn; relic
  FencingManual.

### 6.4 Doom (Necrobinder)

- DoomPower (`Models.Powers/DoomPower.cs:29-160`, `:506-600`): Debuff counter. Enemy side: in BeforeSideTurnEnd
  of the enemy turn (`side != Player`, called from `Combat/CombatManager.cs:2056`); player side (Neurosurge's
  self-Doom): in AfterSideTurnEnd of the player turn (`side != Enemy`). The first doomed participant
  (CurrentHp <= Doom) triggers DoomKill of every doomed creature on its side, then `Hook.AfterDiedToDoom`.
  EndOfDays calls `DoomPower.DoomKill` directly on the hittable enemies right after applying Doom.
- Appliers: BlightStrike (damage dealt), Deathbringer, EndOfDays, NegativePulse, NoEscape (scales with Doom),
  Scourge, powers Countdown (random enemy), Neurosurge (self), Oblivion (per card played), ReaperForm (damage
  dealt), Underworld (multiplayer). Readers: DeathsDoor (applied this turn), TimesUp, ShroudPower.

### 6.5 Souls and other tokens

- Soul (0-cost Skill, Exhaust, draw 2, upg 3) is created by CaptureSpirit, Dirge, GraveWarden, Reave,
  Severance (3 piles), Seance (transform), GlimpseBeyond/Soulbound (multiplayer); read by SoulStorm (in
  exhaust), HauntPower and DevourLifePower (on play).
- Regent transforms: Begone → MinionStrike, Guards → MinionSacrifice, Charge → MinionDiveBomb; Debris
  (Status) from CollisionCourse and CrashLanding (fills the hand to 10).

### 6.6 Play-path facts these cards rely on

Per play of a series (`Models/CardModel.cs` OnPlayWrapper, await order): `Hook.BeforeCardPlayed` →
`History.CardPlayStarted` (`:665`) → `OnPlay` → enchantment/affliction `OnPlay` → `History.CardPlayFinished`
(`:735`) → `Hook.AfterCardPlayed`/`AfterCardPlayedLate` (`:739`); the result-pile move (exhaust, discard,
`GetResultLocationForCardPlay`) follows the whole series. Consequences:

- MakeItSo's count includes the Skill just played (returns on every 3rd Skill); BansheesCry's AfterCardPlayed
  sees the played card's current keywords.
- Fetch draws on its first play of a turn only; a replay in the same series (e.g. after Transfigure) already
  sees the earlier finished entry.
- PullFromBelow, LunarBlast, DeathMarch, Rattle evaluate history at hit time, so their own play is not counted
  (CalculatedVar multipliers are evaluated per hit, `Commands.Builders/AttackCommand.cs:433`).
- RightHandHand's AfterCardPlayedLate fires while the triggering card is still in the play pile;
  RightHandHand itself must already be in the discard pile.
- `WasEthereal` is captured from the card's keywords when the play finishes (`Combat.History.Entries/CardPlayFinishedEntry.cs:49`).
- `AfterCardEnteredCombat` (BansheesCry, Flatten, SwordSagePower) fires from `CardPileCmd.Add` when a card
  without a pile enters a combat pile (`Commands/CardPileCmd.cs:3434-3437`, generated cards included) and for
  transform replacements (`Commands/CardCmd.cs:1472`, `:2070`). Generated cards run, per card,
  `History.CardGenerated` → `Add` → `Hook.AfterCardGeneratedForCombat` (`Commands/CardPileCmd.cs:3231-3241`).

## 7. RNG streams

| Stream | Consumer | Cards | Which |
|---|---|---|---|
| `RunRngType.CombatCardGeneration` | card code | 4 | BundleOfJoy, Largesse, ManifestAuthority, Quasar |
| `RunRngType.CombatCardGeneration` | via applied power | 2 | CallOfTheVoid, SpectrumShift |
| `RunRngType.CombatCardSelection` | card code | 1 | DrainPower |
| `RunRngType.CombatTargets` | auto-play target | 3 | Bombardment, DecisionsDecisions, Eidolon |
| `RunRngType.CombatTargets` | card code | 1 | Stardust |
| `RunRngType.CombatTargets` | via applied power | 4 | Cacophony, Countdown, Haunt, SentryMode |
| `RunRngType.Shuffle` | draw reshuffle (conditional) | 12 | BigBang, Constellation, DecisionsDecisions, Fetch, Glimmer, Glow, Neurosurge, Parse, PhotonCut, Prophesize, Scourge, SpoilsOfBattle |
| `RunRngType.Shuffle` | random insert position | 6 | CaptureSpirit, Dirge, GlimpseBeyond, GraveWarden, Reave, Severance |
| `RunRngType.Shuffle` | via applied power | 1 | Pagestorm |

No card or applied power in the slice touches PlayerRngType streams, MonsterAi, Niche or the encounter/model-private RNGs.

Order facts the port must keep:

- `CardPilePosition.Random` draws `RunRngType.Shuffle.NextInt(pileCount + 1)` once per inserted card, the pile
  growing between draws (`Commands/CardPileCmd.cs:3431`).
- Colorless generation shuffles the whole filtered pool once (`TakeRandom` = `UnstableShuffle` + `Take`) and
  creates cards lazily; BundleOfJoy interleaves create and add; CallOfTheVoidPower calls `GetDistinctForCombat`
  with count 1 per card, so its cards can repeat.
- DrainPower shuffles the upgradable discard cards with `CombatCardSelection` after the attack.
- Auto-play with a null target draws `CombatTargets` per AnyEnemy card at play time.
- Draw can reshuffle (Shuffle stream) whenever the draw pile empties; 12 cards draw directly.

## 8. Ascension

`ascension_sensitivity` is `none` for all 182 cards: no `AscensionLevel`/`HasAscension` read in any card file of
the slice, in the 62 powers they apply, in `Commands/OstyCmd.cs`, `Commands/ForgeCmd.cs` or
`Models.Monsters/Osty.cs`. Ascension reaches these cards only through the run layer (rewards, starting state).

## 9. Version delta v0.107.1 -> v0.111.0

Pool changes: NecrobinderCardPool and RegentCardPool grew from 88 to 91 entries; the 6 additions are all
multiplayer-only (Cacophony, Soulbound, Underworld; Constellation, Plot, Tutor). Rule changes:

| Card | Pool | Change |
|---|---|---|
| Alignment | Regent | star cost 3 -> 2 |
| CollisionCourse | Regent | damage 11 -> 10 |
| CrushUnder | Regent | damage 7 -> 8 |
| Devastate | Regent | damage 30 -> 35 |
| Eidolon | Necrobinder | rework: v0.107.1 exhausted the whole hand and applied IntangiblePower 1 when >= 9 cards were exhausted; v0.111.0 auto-plays every Ethereal, non-Unplayable card in the exhaust pile, adds the Exhaust keyword and CanBeGeneratedInCombat = false |
| GuidingStar | Regent | star cost 2 -> 1; the immediate Draw 2 became DrawCardsNextTurnPower 2 applied after the attack |
| Haunt | Necrobinder | HauntPower amount (HpLoss) 6 -> 7 |
| LegionOfBone | Necrobinder | Exhaust keyword removed (multiplayer-only card) |
| Misery | Necrobinder | temporary debuffs: v0.107.1 called ITemporaryPower.IgnoreNextInstance on each copy; v0.111.0 snapshots (clone, amount) pairs, folds a temporary power's amount into the entry of its InternallyAppliedPower and skips zero amounts |
| PillarOfCreation | Regent | block per generated card 3 -> 2 |
| Reave | Necrobinder | damage 9 -> 10; upgrade +2 -> +3 |
| RefineBlade | Regent | Forge 9 -> 8 |
| Resonance | Regent | star cost 3 -> 2 |
| Sacrifice | Necrobinder | block = Osty max HP x 2 -> x 3 |
| ShiningStrike | Regent | return-to-draw-top moved from an explicit CardPileCmd.Add(Draw, Top) at the end of OnPlay (skipped when Exhaust/ExhaustOnNextPlay) to GetResultLocationForCardPlay (Discard -> Draw Top); timing relative to after-play hooks changes |
| Shroud | Necrobinder | block per Doom application 2 -> 3 |
| SoulStorm | Necrobinder | damage per exhausted Soul 2 -> 4; upgrade +1 -> +2 |
| SpoilsOfBattle | Regent | Forge 5 -> 6 |
| Terraforming | Regent | Vigor 6 -> 7; upgrade +2 -> +3 |
| TheScythe | Necrobinder | growth per play 4 -> 5; upgrade +1 -> +2 |
| TimesUp | Necrobinder | Exhaust keyword removed |
| Transfigure | Necrobinder | CanBeGeneratedInCombat = false added (no longer a random in-combat generation candidate) |

Cosmetic (27): BeatIntoShape, BundleOfJoy, CaptureSpirit, CosmicIndifference, DeathsDoor, DecisionsDecisions, Dirge, DrainPower, Dredge, EndOfDays, Fetch, Flatten, GlimpseBeyond, Glitterstream, GraveWarden, Hang, IAmInvincible, KinglyPunch, KnockoutBlow, LunarBlast, NegativePulse, ParticleWall, PullFromBelow, Putrefy, Radiate, Severance, Undeath.

Diff-gate implication: a v0.107.1 recording that plays one of these 22 cards (or relies on the 6 new ones being
absent from the pool) cannot validate the v0.111.0 port without a version switch; for the other 154 cards the
two versions behave identically.

## 10. Multiplayer-only cards

`CardMultiplayerConstraint.MultiplayerOnly` cards are removed from single-player pools
(`Runs/IRunState.cs:74-84`, `Models/CardPoolModel.cs:68-81`, `Factories/CardFactory.cs:25-31`); the only
relic that offers them, MassiveScroll, requires more than one player (`Models.Relics/MassiveScroll.cs:158-160`).
Batch 9, deferred: Cacophony (Necrobinder), Constellation (Regent), GlimpseBeyond (Necrobinder), HammerTime (Regent), Largesse (Regent), LegionOfBone (Necrobinder), Plot (Regent), Soulbound (Necrobinder), Tutor (Regent), Underworld (Necrobinder).

## 11. Decompile readability

No file needs a re-decompile with reference assemblies. All 182 card files, the 62 powers, OstyCmd, ForgeCmd,
Osty and the token cards were scanned for decompiler failure markers (none) and read. The `//IL_xxxx: Unknown
result type` comments (up to 39 per file: Comet, Constellation, BigBang, Misery, GuidingStar) are noise on
awaiter structs. Files whose control flow must be read via the IL labels rather than line order:
Fetch, HighFive, SicEm, Snap, Sacrifice, BoneShards (Osty gate branches), Spur, Cleanse, Dredge, EndOfDays,
CosmicIndifference (await order differs from textual order), Misery (nested dictionary loops).

## 12. Open questions

- Misery: Misery overwrites values of its snapshot Dictionary while enumerating it (Models.Cards/Misery.cs:108-127). The build uses C# 12 inline arrays (.NET 8+), where overwriting an existing key does not invalidate the enumerator, so the sim (.NET 9) can mirror the two-phase loop; pin the merge result (temporary debuff + its Strength entry) with a unit test against a recorded or MCP-driven combat.
- DivineRight grants stars in AfterRoomEntered; Stars live on the per-combat PlayerCombatState (Entities.Players/Player.cs:1211). Confirm the order (room entered vs combat state creation) so the starting 3 stars survive (relics slice).
- Pets spend their owner's block (Commands/CreatureCmd.cs:3697); the sim blocker is the target itself. Decide whether this lands with the Osty subsystem (W1) or the damage pipeline owner.
- Recorder: Osty-attack card plays and summons need the pet creature in the snapshot/canonical state (HP, max HP, alive) to diff Necrobinder combats; check sts2rec exports pets.

## 13. Per-card table

Cost = energy, `S<n>` stars, `X` energy X, `SX` star X. Req = own requirement keys (abbreviated subsystems).

### Necrobinder

| Card | Cost | Type | Rarity | T | Td | B | Req | Prompt | v107 | Mechanics |
|---|---|---|---|---|---|---|---|---|---|---|
| Afterlife | 1 | Skill | Common | 3 | 3 | 7 | Osty |  | = | summon Osty 6 (upg 9) |
| BansheesCry | 9 | Attack | Rare | 3 | 3 | 4 | Hist Listener |  | = | damage 33 to all enemies; listener AfterCardPlayed: each Ethereal card the owner plays -> this card costs -2 this combat; listener AfterCardEnteredCombat (self, |
| BlightStrike | 1 | Attack | Common | 1 | 2 | 2 | — |  | = | damage 8 (upg 10) to target; apply Doom = total damage dealt by that attack (sum of DamageResult.TotalDamage) to target |
| Bodyguard | 1 | Skill | Basic | 3 | 3 | 7 | Osty |  | = | summon Osty 5 (upg 7) |
| BoneShards | 1 | Attack | Uncommon | 3 | 3 | 7 | Osty |  | = | if Osty missing: nothing; Osty attacks all enemies 9 (upg 12); gain 9 (upg 12) block; kill Osty (if still alive) |
| BorrowedTime | 1 | Skill | Uncommon | 1 | 2 | 2 | — |  | = | gain 4 (upg 6) energy; apply BorrowedTimePower 1 to self (owner's cards cost +1 until owner's side turn ends) |
| Bury | 4 | Attack | Uncommon | 1 | 1 | 1 | — |  | = | damage 52 (upg 63) to target |
| Cacophony | 2 | Power | Rare | 1 | 2 | 9 | — |  | new | apply CacophonyPower 66 (upg 99) to self (every 33 cards drawn: that much unpowered damage to a random enemy) |
| Calcify | 1 | Power | Uncommon | 1 | 3 | 7 | — |  | = | apply CalcifyPower 4 (upg 6): +Amount damage on Osty's powered attacks |
| CallOfTheVoid | 1 | Power | Rare | 1 | 3 | 6 | — |  | = | apply CallOfTheVoidPower 1: each turn before hand draw, 1 random Ethereal card from the character pool into hand |
| CaptureSpirit | 1 | Skill | Uncommon | 2 | 2 | 3 | Cmd Create |  | ~ | deal 3 (upg 4) unblockable unpowered damage to target (CreatureCmd.Damage, not an attack); add 3 (upg 4) Souls to the draw pile at random positions |
| Cleanse | 1 | Skill | Uncommon | 3 | 3 | 7 | Osty Prompt | PileSelect | = | summon Osty 3 (upg 5); choose 1 card in the draw pile and exhaust it |
| Countdown | 1 | Power | Uncommon | 1 | 2 | 2 | — |  | = | apply CountdownPower 6 (upg 9): each turn start, that much Doom on a random enemy |
| DanseMacabre | 1 | Power | Uncommon | 1 | 1 | 2 | — |  | = | apply DanseMacabrePower 4 (upg 6): playing a card with resolved energy cost >= 2 grants that much block |
| Deathbringer | 2 | Skill | Uncommon | 1 | 2 | 2 | — |  | = | apply Doom 21 (upg 26) to all hittable enemies; apply Weak 1 to all hittable enemies |
| DeathsDoor | 1 | Skill | Uncommon | 3 | 3 | 4 | Hist |  | ~ | gain 6 (upg 7) block; if the owner applied Doom this turn: gain the same block 2 more times (3 total) |
| DeathMarch | 1 | Attack | Uncommon | 3 | 3 | 4 | CalcVar Cmd Hist |  | = | damage (8 + 4 x cards drawn this turn outside the hand draw) to target (upg 9 + 6 x) |
| Debilitate | 1 | Attack | Uncommon | 1 | 2 | 2 | — |  | = | damage 10 (upg 12) to target; apply DebilitatePower 2 (upg 3): Vulnerable on the target and Weak on its attacks have double effect |
| DefendNecrobinder | 1 | Skill | Basic | 1 | 1 | 1 | — |  | = | gain 5 (upg 8) block |
| Defile | 1 | Attack | Common | 1 | 1 | 1 | — |  | = | damage 13 (upg 17) to target |
| Defy | 1 | Skill | Common | 1 | 1 | 1 | — |  | = | gain 6 (upg 9) block; apply Weak 1 to target |
| Delay | 2 | Skill | Uncommon | 1 | 1 | 2 | — |  | = | gain 11 (upg 13) block; apply EnergyNextTurnPower 1 (upg 2) |
| Demesne | 3 | Power | Rare | 1 | 1 | 2 | — |  | = | apply DemesnePower 1: +1 hand draw and +1 max energy each turn |
| DevourLife | 1 | Power | Rare | 1 | 3 | 7 | — |  | = | apply DevourLifePower 1 (upg 2): each Soul the owner plays summons that much |
| Dirge | X | Skill | Uncommon | 3 | 3 | 7 | CardOps Cmd Create Osty |  | ~ | X = ResolveEnergyXValue; summon 3 (upg 4) X times (separate summons); add X Souls (upgraded if Dirge is upgraded) to the draw pile at random positions |
| DrainPower | 1 | Attack | Common | 2 | 2 | 3 | CardOps |  | ~ | damage 10 (upg 12) to target; upgrade 2 (upg 3) random upgradable cards in the discard pile |
| Dredge | 1 | Skill | Uncommon | 3 | 3 | 5 | Prompt | PileSelect | ~ | n = min(3, 10 - hand size); if n > 0: choose n cards from the discard pile into hand |
| Eidolon | 2 | Skill | Rare | 2 | 2 | 3 | AutoPlay | Nested | Δ | auto-play every card in the exhaust pile that is Ethereal and not Unplayable (snapshot list, in pile order) |
| EndOfDays | 3 | Skill | Rare | 2 | 2 | 3 | Doom |  | ~ | apply Doom 29 (upg 37) to each hittable enemy; DoomKill every hittable enemy whose HP <= its Doom |
| EnfeeblingTouch | 1 | Skill | Uncommon | 1 | 2 | 2 | — |  | = | apply EnfeeblingTouchPower 8 (upg 11) to target: -8 Strength until its side turn ends |
| Eradicate | X | Attack | Rare | 1 | 1 | 1 | — |  | = | damage 11 (upg 14) to target, X hits (ResolveEnergyXValue) |
| Fear | 1 | Attack | Common | 1 | 1 | 1 | — |  | = | damage 7 (upg 8) to target; apply Vulnerable 1 (upg 2) to target |
| Fetch | 0 | Attack | Uncommon | 3 | 3 | 7 | Hist Osty |  | ~ | if Osty missing: nothing; Osty attacks target 3 (upg 6); if this card has not finished a play earlier this turn: draw 1 |
| Flatten | 2 | Attack | Common | 3 | 3 | 7 | Hist Listener Osty |  | ~ | if Osty missing: nothing; Osty attacks target 12 (upg 16); listener AfterAttack: an attack by the owner's Osty -> this card costs 0 this turn; listener AfterCar |
| ForbiddenGrimoire | 2 | Power | Ancient | 1 | 3 | 8 | — |  | = | apply ForbiddenGrimoirePower 1: after combat, 1 extra card-removal reward |
| Friendship | 1 | Power | Uncommon | 1 | 1 | 2 | — |  | = | apply Strength -2 (upg -1) to self; apply FriendshipPower 1: +1 max energy |
| GlimpseBeyond | 1 | Skill | Rare | 3 | 3 | 9 | Cmd Create MP |  | ~ | for each living player teammate (self included): add 3 (upg 4) Souls to that player's draw pile at random positions |
| Graveblast | 1 | Attack | Common | 3 | 3 | 5 | Prompt | PileSelect | = | damage 4 (upg 6) to target; choose 1 card from the discard pile into hand |
| GraveWarden | 1 | Skill | Common | 2 | 2 | 3 | Cmd Create |  | ~ | gain 8 (upg 11) block; add 1 Soul to the draw pile at a random position |
| Hang | 1 | Attack | Rare | 1 | 1 | 2 | — |  | ~ | damage 10 (upg 13) to target; apply HangPower max(2, current HangPower) to target (capped at 999999999 total): doubles Hang's multiplier |
| Haunt | 1 | Power | Uncommon | 1 | 1 | 2 | — |  | Δ | apply HauntPower 7 (upg 9): each Soul the owner plays deals that much unblockable damage to a random enemy |
| HighFive | 2 | Attack | Uncommon | 3 | 3 | 7 | Osty |  | = | playable only while Osty is alive; Osty attacks all enemies 11 (upg 13); apply Vulnerable 2 (upg 3) to all hittable enemies |
| Invoke | 1 | Skill | Common | 1 | 3 | 7 | — |  | = | apply SummonNextTurnPower 2 (upg 3); apply EnergyNextTurnPower 2 (upg 3) |
| LegionOfBone | 2 | Skill | Uncommon | 3 | 3 | 9 | MP Osty |  | Δ | for each living player creature: summon 6 (upg 8) for that player |
| Lethality | 1 | Power | Uncommon | 1 | 3 | 4 | — |  | = | apply LethalityPower 50 (upg 75): the owner's first Attack each turn deals +50% |
| Melancholy | 3 | Skill | Uncommon | 2 | 2 | 3 | Listener |  | = | gain 13 (upg 17) block; listener AfterDeath: any creature dies (removal not prevented) while this card is in a combat pile -> cost -1 this combat |
| Misery | 0 | Attack | Rare | 4 | 4 | 8 | Cross |  | Δ | snapshot the target's debuffs (TypeForCurrentAmount == Debuff) as (clone, amount); fold temporary powers' amounts into their InternallyAppliedPower entry; damag |
| NecroMastery | 2 | Power | Rare | 3 | 3 | 7 | Osty |  | = | summon 5 (upg 8); apply NecroMasteryPower 1: whenever Osty loses HP, that loss is dealt as unblockable damage to all enemies |
| NegativePulse | 1 | Skill | Common | 1 | 2 | 2 | — |  | ~ | gain 5 (upg 6) block; apply Doom 7 (upg 11) to each hittable enemy |
| Neurosurge | 0 | Power | Rare | 1 | 2 | 2 | — |  | = | gain 3 (upg 4) energy; draw 2; apply NeurosurgePower 3 to self: 3 Doom on yourself at each turn start |
| NoEscape | 1 | Skill | Uncommon | 2 | 2 | 3 | CalcVar |  | = | apply Doom 10 (upg 15) + 5 x floor(target Doom / 10) to target |
| Oblivion | 0 | Skill | Rare | 1 | 2 | 2 | — |  | = | apply OblivionPower 3 (upg 4) to target: every card the owner plays this turn applies that much Doom to it |
| Pagestorm | 1 | Power | Uncommon | 1 | 1 | 2 | — |  | = | apply PagestormPower 1: drawing an Ethereal card draws 1 more |
| Parse | 1 | Skill | Uncommon | 1 | 1 | 1 | — |  | = | draw 3 (upg 4) |
| Poke | 0 | Attack | Common | 3 | 3 | 7 | Osty |  | = | if Osty missing: nothing; Osty attacks target 6 (upg 9) |
| Protector | 1 | Attack | Ancient | 3 | 3 | 7 | CalcVar Cmd Osty |  | = | if Osty missing: nothing; Osty attacks target for 10 (upg 15) + Osty max HP |
| PullAggro | 2 | Skill | Common | 3 | 3 | 7 | Osty |  | = | summon 4 (upg 5); gain 7 (upg 9) block |
| PullFromBelow | 1 | Attack | Uncommon | 3 | 3 | 4 | CalcVar Hist |  | ~ | damage 5 (upg 7) to target, hits = Ethereal card plays finished by the owner this combat (0 hits possible) |
| Putrefy | 1 | Skill | Uncommon | 1 | 1 | 1 | — |  | ~ | apply Weak 2 (upg 3) to target; apply Vulnerable 2 (upg 3) to target |
| Rattle | 1 | Attack | Uncommon | 3 | 3 | 7 | CalcVar Hist Osty |  | = | if Osty missing: nothing; Osty attacks target 7 (upg 9), hits = 1 + Osty attacks earlier this turn |
| Reanimate | 3 | Skill | Rare | 3 | 3 | 7 | Osty |  | = | summon 20 (upg 25) |
| Reap | 3 | Attack | Common | 1 | 1 | 1 | — |  | = | damage 27 (upg 33) to target |
| ReaperForm | 3 | Power | Rare | 1 | 2 | 2 | — |  | = | apply ReaperFormPower 1: powered attack damage by the owner or Osty applies that much Doom to the target |
| Reave | 1 | Attack | Common | 2 | 2 | 3 | CardOps Cmd Create |  | Δ | damage 10 (upg 13) to target; add 1 Soul (upgraded if Reave is upgraded) to the draw pile at a random position |
| RightHandHand | 0 | Attack | Uncommon | 3 | 3 | 7 | Listener Osty Play |  | = | if Osty missing: nothing; Osty attacks target 4 (upg 6); listener AfterCardPlayedLate: owner plays a card with EnergyValue >= 2 while this card is in the discar |
| Sacrifice | 1 | Skill | Rare | 3 | 3 | 7 | CalcVar Osty |  | Δ | if Osty missing: nothing; block = 3 x Osty max HP (computed before the kill); kill Osty; gain that block |
| Scourge | 1 | Skill | Common | 1 | 2 | 2 | — |  | = | apply Doom 13 (upg 16) to target; draw 1 (upg 2) |
| SculptingStrike | 1 | Attack | Common | 3 | 3 | 5 | CardModel CardOps Prompt | HandSelect | = | damage 9 (upg 12) to target; choose 1 hand card without a Local Ethereal keyword and make it Ethereal |
| Seance | 1 | Skill | Rare | 3 | 3 | 5 | CardOps Prompt | PileSelect | = | choose 1 card in the draw pile and transform it into a Soul |
| SentryMode | 2 | Power | Rare | 1 | 3 | 7 | — |  | = | apply SentryModePower 1: each turn before hand draw, add 1 SweepingGaze to hand |
| Severance | 2 | Attack | Uncommon | 2 | 2 | 3 | Create |  | ~ | damage 13 (upg 18) to target; create 3 Souls: one into the draw pile at a random position, one into the discard pile, one into hand (separate adds) |
| SharedFate | 0 | Skill | Rare | 1 | 1 | 1 | — |  | = | apply Strength -2 to self; apply Strength -2 (upg -3) to target |
| Shroud | 1 | Power | Uncommon | 1 | 2 | 2 | — |  | Δ | apply ShroudPower 3 (upg 4): each Doom the owner applies grants that much block |
| SicEm | 1 | Attack | Uncommon | 3 | 3 | 7 | Osty |  | = | Osty attacks target 5 (upg 6) (skipped if Osty missing); apply SicEmPower 3 (upg 4) to target (even if Osty is missing): Osty hits on it summon that much |
| SleightOfFlesh | 2 | Power | Uncommon | 1 | 2 | 2 | — |  | = | apply SleightOfFleshPower 9 (upg 13): each non-temporary debuff the owner applies to an enemy deals that much damage to it |
| Snap | 1 | Attack | Common | 3 | 3 | 7 | CardOps Osty Prompt | HandSelect | = | Osty attacks target 7 (upg 10) (skipped if Osty missing); choose 1 hand card without Retain and give it Retain (even if Osty is missing) |
| Soulbound | 1 | Power | Uncommon | 3 | 3 | 9 | MP |  | new | apply SoulboundPower 1 to an ally player: whenever the owner generates a Soul, that ally gets 1 Soul in its draw pile |
| SoulStorm | 1 | Attack | Rare | 2 | 2 | 3 | CalcVar Cmd |  | Δ | damage 9 + 4 (upg 6) x Souls in the owner's exhaust pile to target |
| Sow | 1 | Attack | Common | 1 | 1 | 1 | — |  | = | damage 8 (upg 11) to all enemies |
| SpiritOfAsh | 1 | Power | Rare | 1 | 1 | 2 | — |  | = | apply SpiritOfAshPower 4 (upg 5): playing an Ethereal card grants that much block |
| Spur | 1 | Skill | Uncommon | 3 | 3 | 7 | Osty |  | = | summon 3 (upg 5); heal Osty 5 (upg 7) |
| Squeeze | 3 | Attack | Rare | 3 | 3 | 7 | CalcVar Cmd Osty |  | = | if Osty missing: nothing; Osty attacks target for 25 (upg 30) + 5 (upg 6) x other OstyAttack cards in the owner's combat piles |
| StrikeNecrobinder | 1 | Attack | Basic | 1 | 1 | 1 | — |  | = | damage 6 (upg 9) to target |
| TheScythe | 2 | Attack | Rare | 3 | 3 | 8 | CardModel RunState |  | Δ | damage CurrentDamage (13 + accumulated) to target; +5 (upg +7) CurrentDamage on this card and on its DeckVersion |
| TimesUp | 2 | Attack | Rare | 2 | 2 | 3 | CalcVar Cmd |  | Δ | damage = target's Doom amount (0 + 1 x Doom) |
| Transfigure | 1 | Skill | Rare | 3 | 3 | 5 | Prompt | HandSelect | Δ | choose 1 hand card: if not X-cost and cost >= 0 -> +1 energy cost this combat; BaseReplayCount +1 (plays an extra time) |
| Undeath | 0 | Skill | Rare | 2 | 2 | 3 | CardModel |  | ~ | gain 7 (upg 9) block; add a clone of this card to the discard pile |
| Underworld | 2 | Skill | Uncommon | 1 | 3 | 9 | — |  | new | apply UnderworldPower 1: allies' powered attack damage applies that much Doom |
| Unleash | 1 | Attack | Basic | 3 | 3 | 7 | CalcVar Cmd Osty |  | = | if Osty missing: nothing; Osty attacks target for 6 (upg 9) + Osty current HP |
| Veilpiercer | 1 | Attack | Uncommon | 1 | 1 | 2 | — |  | = | damage 10 (upg 13) to target; apply VeilpiercerPower 1: the next Ethereal card played from hand costs 0 |
| Wisp | 0 | Skill | Common | 1 | 1 | 1 | — |  | = | gain 1 energy |

### Regent

| Card | Cost | Type | Rarity | T | Td | B | Req | Prompt | v107 | Mechanics |
|---|---|---|---|---|---|---|---|---|---|---|
| Alignment | 0 S2 | Skill | Uncommon | 3 | 3 | 7 | Stars |  | Δ | pay 2 stars; gain 2 (upg 3) energy |
| Arsenal | 1 | Power | Rare | 1 | 1 | 2 | — |  | = | apply ArsenalPower 1: each card the owner generates for combat grants 1 Strength |
| AstralPulse | 0 S3 | Attack | Common | 3 | 3 | 7 | Stars |  | = | pay 3 stars; damage 6 (upg 8) to all enemies, 2 hits |
| BeatIntoShape | 1 | Attack | Rare | 3 | 3 | 7 | CalcVar Forge Hist |  | ~ | damage 5 (upg 7) to target; Forge 5 (upg 7) + 5 (upg 7) x powered hits the owner dealt to the target earlier this turn (history count after the attack minus thi |
| Begone | 1 | Skill | Common | 3 | 3 | 5 | CardOps Create Prompt | HandSelect | = | choose 1 hand card and transform it into MinionStrike (upgraded if Begone is upgraded) |
| BigBang | 0 | Skill | Rare | 3 | 3 | 7 | Forge Stars |  | = | draw 1; gain 1 star; gain 1 energy; Forge 5 |
| BlackHole | 1 | Power | Uncommon | 1 | 3 | 7 | — |  | = | apply BlackHolePower 3 (upg 4): each star gain and each star-spending card play deals that much damage to all enemies |
| Bombardment | 3 | Attack | Rare | 2 | 2 | 3 | AutoPlay Listener Turn |  | = | damage 18 (upg 24) to target; listener AfterAutoPrePlayPhaseEnteredEarly (owner's turn start): while in the exhaust pile, auto-play itself (random target) |
| Bulwark | 2 | Skill | Uncommon | 3 | 3 | 7 | Forge |  | = | gain 12 (upg 15) block; Forge 10 (upg 13) |
| BundleOfJoy | 1 | Skill | Rare | 3 | 3 | 6 | Gen |  | ~ | add 3 (upg 4) distinct random colorless cards to hand (one AddGeneratedCardToCombat each) |
| CelestialMight | 2 | Attack | Common | 1 | 1 | 1 | — |  | = | damage 6 to target, 3 (upg 4) hits |
| Charge | 1 | Skill | Uncommon | 3 | 3 | 5 | CardOps Prompt | PileSelect | = | choose 2 cards in the draw pile and transform each into MinionDiveBomb (upgrade each result if Charge is upgraded) |
| ChildOfTheStars | 1 | Power | Uncommon | 1 | 3 | 7 | — |  | = | apply ChildOfTheStarsPower 2 (upg 3): spending stars grants that much block per star |
| CloakOfStars | 0 S1 | Skill | Common | 3 | 3 | 7 | Stars |  | = | pay 1 star; gain 7 (upg 10) block |
| CollisionCourse | 0 | Attack | Common | 2 | 2 | 3 | Create |  | Δ | damage 10 (upg 14) to target; add 1 Debris to hand |
| Comet | 0 S5 | Attack | Rare | 3 | 3 | 7 | Stars |  | = | pay 5 stars; damage 33 (upg 44) to target; apply Weak 3; apply Vulnerable 3 |
| Conqueror | 1 | Skill | Uncommon | 3 | 3 | 7 | Forge |  | = | Forge 3 (upg 5); apply ConquerorPower 1 to target: SovereignBlade attacks deal double damage to it this turn |
| Constellation | 0 S2 | Skill | Uncommon | 3 | 3 | 9 | MP Stars |  | new | pay 2 stars; target ally player: gain 9 (upg 12) block, gain 1 energy, draw 1 |
| Convergence | 1 | Skill | Uncommon | 1 | 3 | 7 | — |  | = | apply RetainHandPower 1 (keep the hand this turn); apply EnergyNextTurnPower 1; apply StarNextTurnPower 1 (upg 2) |
| CosmicIndifference | 1 | Skill | Common | 3 | 3 | 5 | Prompt | PileSelect | ~ | gain 6 (upg 9) block; choose 1 card in the discard pile and put it on top of the draw pile (if still in draw/discard) |
| CrashLanding | 1 | Attack | Rare | 2 | 2 | 3 | Cmd Create |  | = | damage 21 (upg 26) to all enemies; fill the hand with Debris (10 - hand size) |
| CrescentSpear | 1 S1 | Attack | Common | 3 | 3 | 7 | CalcVar Cmd Stars |  | = | pay 1 star; damage 8 + 2 (upg 3) x owner's combat cards with a star cost (CanonicalStarCost >= 0 or star X) |
| CrushUnder | 1 | Attack | Common | 1 | 2 | 2 | — |  | Δ | capture hittable enemies; damage 8 (upg 9) to all enemies; apply CrushUnderPower 1 (upg 2) to the captured enemies: Strength down until their turn ends |
| DecisionsDecisions | 0 S6 | Skill | Rare | 3 | 3 | 7 | AutoPlay Prompt Stars | HandSelect,Nested | ~ | pay 6 stars; draw 3 (upg 5); choose 1 Skill (not Unplayable) in hand; auto-play it 3 times |
| DefendRegent | 1 | Skill | Basic | 1 | 1 | 1 | — |  | = | gain 5 (upg 8) block |
| Devastate | 1 S4 | Attack | Uncommon | 3 | 3 | 7 | Stars |  | Δ | pay 4 stars; damage 35 (upg 45) to target |
| DyingStar | 1 S3 | Attack | Rare | 3 | 3 | 7 | Stars |  | = | pay 3 stars; capture hittable enemies; damage 9 (upg 11) to all enemies; apply DyingStarPower 9 (upg 11) to each captured enemy: Strength down until their turn  |
| FallingStar | 0 S2 | Attack | Basic | 3 | 3 | 7 | Stars |  | = | pay 2 stars; damage 8 (upg 12) to target; apply Weak 1; apply Vulnerable 1 |
| ForegoneConclusion | 1 | Skill | Rare | 1 | 3 | 5 | — | PileSelect* | = | apply ForegoneConclusionPower 2 (upg 3): next turn, before the hand draw, choose that many draw-pile cards into hand |
| Furnace | 1 | Power | Uncommon | 1 | 3 | 7 | — |  | = | apply FurnacePower 5 (upg 7): Forge that much at each turn start |
| GammaBlast | 0 S3 | Attack | Uncommon | 3 | 3 | 7 | Stars |  | = | pay 3 stars; damage 13 (upg 18) to target; apply Weak 2; apply Vulnerable 2 |
| GatherLight | 1 | Skill | Common | 3 | 3 | 7 | Stars |  | = | gain 8 (upg 11) block; gain 1 star |
| Genesis | 2 | Power | Rare | 1 | 3 | 7 | — |  | = | apply GenesisPower 2 (upg 3): gain that many stars after each energy reset |
| Glimmer | 1 | Skill | Uncommon | 3 | 3 | 5 | Prompt | HandSelect | = | draw 3 (upg 4); choose 1 hand card and put it on top of the draw pile |
| Glitterstream | 2 | Skill | Common | 1 | 1 | 2 | — |  | ~ | blockNextTurn = Hook.ModifyBlock(5 (upg 7), Move) computed at play; gain 11 (upg 13) block; apply BlockNextTurnPower blockNextTurn |
| Glow | 1 | Skill | Common | 3 | 3 | 7 | Stars |  | = | gain 1 (upg 2) star; draw 1; apply DrawCardsNextTurnPower 1 |
| Guards | 2 | Skill | Rare | 3 | 3 | 5 | CardOps Create Prompt | HandSelect | = | choose any number of hand cards; transform each into MinionSacrifice (upgraded if Guards is upgraded) |
| GuidingStar | 1 S1 | Attack | Common | 3 | 3 | 7 | Stars |  | Δ | pay 1 star; damage 12 (upg 13) to target; apply DrawCardsNextTurnPower 2 (upg 3) |
| HammerTime | 2 | Power | Rare | 1 | 3 | 9 | — |  | = | apply HammerTimePower: when the owner forges, every other living player forges the same amount |
| HeavenlyDrill | X | Attack | Rare | 1 | 1 | 1 | — |  | = | hits = X (ResolveEnergyXValue); if hits >= 4 then hits x 2; damage 8 (upg 10) to target per hit |
| Hegemony | 2 | Attack | Uncommon | 1 | 1 | 2 | — |  | = | damage 15 (upg 18) to target; apply EnergyNextTurnPower 2 (upg 3) |
| HeirloomHammer | 2 | Attack | Rare | 3 | 3 | 5 | CardModel Prompt | HandSelect | = | damage 20 (upg 25) to target; choose 1 colorless hand card (VisualCardPool.IsColorless); add 1 clone of it to hand |
| HiddenCache | 1 | Skill | Common | 3 | 3 | 7 | Stars |  | = | gain 1 star; apply StarNextTurnPower 3 (upg 4) |
| IAmInvincible | 1 | Skill | Rare | 2 | 2 | 3 | AutoPlay Listener Turn |  | ~ | gain 10 (upg 13) block; listener AfterAutoPostPlayPhaseEntered (owner's end of turn): if this card is the top of the draw pile, auto-play it from there |
| KinglyKick | 4 | Attack | Uncommon | 2 | 2 | 3 | Listener |  | = | damage 27 (upg 35) to target; listener AfterCardDrawn (self): cost -1 this combat |
| KinglyPunch | 1 | Attack | Uncommon | 2 | 2 | 3 | CardModel Listener |  | ~ | damage 8 (upg 10) to target; listener AfterCardDrawn (self): damage +4 (upg +6) for this combat (re-applied after downgrade) |
| KnockoutBlow | 3 | Attack | Uncommon | 3 | 3 | 7 | Stars |  | ~ | damage 30 (upg 38) to target; if any hit killed its target: gain 5 stars |
| KnowThyPlace | 0 | Skill | Common | 1 | 1 | 1 | — |  | = | apply Weak 1 to target; apply Vulnerable 1 to target |
| Largesse | 0 | Skill | Uncommon | 3 | 3 | 9 | CardOps Gen MP |  | = | give the target ally player 1 random colorless card (upgraded if Largesse is upgraded) in hand |
| LunarBlast | 0 | Attack | Uncommon | 3 | 3 | 4 | CalcVar Hist |  | ~ | damage 4 (upg 5) to target, hits = Skills the owner finished playing this turn |
| MakeItSo | 0 | Attack | Rare | 3 | 3 | 4 | Hist Listener |  | = | damage 6 (upg 9) to target; listener AfterCardPlayedLate: owner plays a Skill while this card is not in hand -> if Skills finished this turn % 3 == 0, move it t |
| ManifestAuthority | 1 | Skill | Uncommon | 3 | 3 | 6 | CardOps Gen |  | = | gain 7 (upg 8) block; add 1 random colorless card (upgraded if this is upgraded) to hand |
| MeteorShower | 0 S2 | Attack | Ancient | 3 | 3 | 7 | Stars |  | = | pay 2 stars; damage 14 (upg 21) to all enemies; apply Weak 2 to all hittable enemies; apply Vulnerable 2 to all hittable enemies |
| MonarchsGaze | 2 | Power | Rare | 1 | 2 | 2 | — |  | = | apply MonarchsGazePower 1: the owner's powered attack damage applies 1 temporary Strength down to the target |
| Monologue | 0 | Skill | Uncommon | 2 | 2 | 3 | Powers |  | = | apply MonologuePower 1; set the power's Strength var to Power (1): each card played this turn grants +1 Strength until end of turn |
| NeutronAegis | 1 S5 | Power | Rare | 3 | 3 | 7 | Stars |  | = | pay 5 stars; apply PlatingPower 8 (upg 11) |
| Orbit | 2 | Power | Uncommon | 1 | 1 | 2 | — |  | = | apply OrbitPower 1: every 4 energy the owner spends grants 1 energy |
| PaleBlueDot | 1 | Power | Uncommon | 1 | 3 | 4 | — |  | = | apply PaleBlueDotPower 1 (upg 2): once per turn, after 5 card plays, gain that much DrawCardsNextTurn |
| Parry | 1 | Power | Uncommon | 1 | 3 | 7 | — |  | = | apply ParryPower 10 (upg 14): playing a SovereignBlade grants that much block |
| ParticleWall | 0 S2 | Skill | Uncommon | 3 | 3 | 7 | Stars |  | ~ | pay 2 stars; gain 9 (upg 12) block; after the play it returns to hand instead of the discard pile |
| Patter | 1 | Skill | Common | 1 | 1 | 2 | — |  | = | gain 8 (upg 10) block; apply Vigor 2 (upg 3) |
| PhotonCut | 1 | Attack | Common | 3 | 3 | 5 | Prompt | HandSelect | = | damage 10 (upg 13) to target; draw 1 (upg 2); choose 1 hand card and put it on top of the draw pile |
| PillarOfCreation | 1 | Power | Uncommon | 1 | 1 | 2 | — |  | Δ | apply PillarOfCreationPower 2 (upg 3): each card the owner generates for combat grants that much block |
| Plot | 1 | Skill | Uncommon | 3 | 3 | 9 | MP |  | new | apply DrawCardsNextTurnPower 2 (upg 3) to each living player teammate |
| Prophesize | 2 | Skill | Uncommon | 1 | 1 | 1 | — |  | = | draw 6 (upg 9) |
| Quasar | 0 S2 | Skill | Uncommon | 3 | 3 | 7 | CardOps Gen Prompt Stars | ChooseCard | = | pay 2 stars; generate 3 distinct colorless cards (all upgraded if Quasar is upgraded); choose 1 (skippable) into hand |
| Radiate | 0 | Attack | Uncommon | 3 | 3 | 7 | CalcVar Hist Stars |  | ~ | damage 3 (upg 4) to all enemies, hits = stars gained by the owner this turn (sum of positive StarsModified) |
| RefineBlade | 1 | Skill | Common | 3 | 3 | 7 | Forge |  | Δ | Forge 8 (upg 12); apply EnergyNextTurnPower 1 |
| Reflect | 1 S3 | Skill | Uncommon | 3 | 3 | 7 | Stars |  | = | pay 3 stars; gain 15 (upg 20) block; apply ReflectPower 1: blocked powered damage is reflected to the attacker until next turn |
| Resonance | 1 S2 | Skill | Uncommon | 3 | 3 | 7 | Stars |  | Δ | pay 2 stars; gain 1 (upg 2) Strength; each hittable enemy loses 1 Strength |
| Royalties | 1 | Power | Rare | 1 | 3 | 8 | — |  | = | apply RoyaltiesPower 30 (upg 40): after combat, an extra gold reward of that size |
| RoyalGamble | 0 S5 | Skill | Uncommon | 3 | 3 | 7 | Stars |  | = | pay 5 stars; gain 9 stars |
| SeekingEdge | 1 | Power | Rare | 3 | 3 | 7 | Forge |  | = | apply SeekingEdgePower 1: SovereignBlades hit all enemies; Forge 7 (upg 11) |
| SevenStars | 2 S7 | Attack | Rare | 3 | 3 | 7 | Stars |  | = | pay 7 stars; damage 7 to all enemies, 7 hits |
| ShiningStrike | 1 | Attack | Uncommon | 3 | 3 | 7 | Stars |  | Δ | damage 8 (upg 11) to target; gain 2 stars; after the play it goes to the top of the draw pile instead of the discard pile |
| SolarStrike | 1 | Attack | Common | 3 | 3 | 7 | Stars |  | = | damage 9 (upg 10) to target; gain 1 (upg 2) star |
| SpectrumShift | 2 | Power | Uncommon | 1 | 3 | 6 | — |  | = | apply SpectrumShiftPower 1: each turn before hand draw, add 1 random colorless card to hand |
| SpoilsOfBattle | 1 | Skill | Common | 3 | 3 | 7 | Forge |  | Δ | Forge 6 (upg 9); draw 2 |
| Stardust | 0 SX | Attack | Uncommon | 3 | 3 | 7 | Stars |  | = | pay all stars (X = ResolveStarXValue = ModifyXValue(LastStarsSpent)); damage 5 (upg 7) X hits, each to a random enemy |
| StrikeRegent | 1 | Attack | Basic | 1 | 1 | 1 | — |  | = | damage 6 (upg 9) to target |
| SummonForth | 1 | Skill | Uncommon | 3 | 3 | 7 | Forge Listener |  | = | move every owner SovereignBlade not in hand (draw, discard, exhaust, no pile) to hand; Forge 8 (upg 11) |
| Supermassive | 1 | Attack | Uncommon | 3 | 3 | 4 | CalcVar Cmd Hist |  | = | damage 5 + 3 (upg 4) x cards the owner generated this combat to target |
| SwordSage | 2 | Power | Rare | 1 | 3 | 7 | — |  | = | apply SwordSagePower 1: every SovereignBlade plays 1 extra time |
| Terraforming | 1 | Skill | Uncommon | 1 | 1 | 2 | — |  | Δ | apply Vigor 7 (upg 10) |
| TheSealedThrone | 1 S3 | Power | Ancient | 3 | 3 | 7 | Stars |  | = | pay 3 stars; apply TheSealedThronePower 1: each card the owner plays grants 1 star |
| TheSmith | 1 S4 | Skill | Rare | 3 | 3 | 7 | Forge Stars |  | = | pay 4 stars; Forge 30 (upg 40) |
| Tutor | 1 | Skill | Rare | 3 | 3 | 9 | MP Prompt | PileSelect | new | target ally player chooses 1 card from its own draw pile into its hand |
| Tyranny | 1 | Power | Rare | 1 | 3 | 5 | — | HandSelect* | = | apply TyrannyPower 1: +1 hand draw; after each turn start choose 1 hand card to exhaust |
| Venerate | 1 | Skill | Basic | 3 | 3 | 7 | Stars |  | = | gain 2 (upg 3) stars |
| VoidForm | 3 | Power | Rare | 2 | 2 | 3 | Turn |  | = | apply VoidFormPower 2: the first 2 non-auto-play cards each turn cost 0 energy and 0 stars; end the turn (PlayerCmd.EndTurn, canBackOut false) |
| WroughtInWar | 1 | Attack | Common | 3 | 3 | 7 | Forge |  | = | damage 7 (upg 9) to target; Forge 7 (upg 9) |

`*` = prompt opened by the applied power. v107: `=` same, `~` cosmetic, `Δ` rule change, `new` added in v0.111.0.
