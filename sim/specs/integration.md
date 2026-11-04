# Integration spec: build, cross-area fixes, end-to-end tests, open-question triage

Owner: integrate step (P0 walking skeleton, 2026-10-28). Citations are
`<namespace dir without MegaCrit.Sts2.Core.>/<File>.cs:<line>` in
`output/server-bootstrap/linux-decompiled/` (game v0.111.0), as in `CONTRACTS.md`.

## 1. Status

| Gate | Result |
| --- | --- |
| `dotnet build sim/Sts2Sim.sln` (SDK 9.0.117) | 0 errors, 0 warnings. The six-way port already compiled together on the first build. |
| `dotnet run --project sim/Sts2Sim.Tests` | 201 passed, 0 failed at integration (Rng 35, Pipeline 50, Cards 32, Monsters 31, Loop 18, State 24, EndToEnd 8, Smoke 3). After the 2026-10-30 fidelity fixes (§6): 205 passed, 0 failed (Cards 34, Loop 20, others unchanged). |
| `dotnet run -c Release --project sim/Sts2Sim.Bench` (random policy, 1 thread, `NIBBITS_WEAK`) | about 570 000 decisions/s (gate ≥ 1 000); `CombatSim.Clone()` about 2.2 µs (gate ≤ 1 ms). The first run, on a heavily loaded machine, gave 31 000/s and 19 µs; both are far inside the gates. |
| `dotnet run -c Release --project sim/Sts2Sim.Tests` | 201 passed (same as Debug) |

The first test run had 6 failures, all in `LoopTests` and all from one test-double bug (§2, first
row). No test was weakened or skipped.

## 2. Integration changes

| Change | Why | Citation |
| --- | --- | --- |
| `LoopTests.LoopDummy` bite: `DamageCmd.Attack(c, n).FromMonster(s).Execute()` (was `…FromMonster(s).Targeting(targets[0])…`) | The double made a call that also throws in the game: `FromMonster` already runs `TargetingAllOpponents`, and `Targeting` then throws "Already set to target opponents of attacker". The pipeline port is right; the double was wrong. Assertions unchanged. | `Commands.Builders/AttackCommand.cs:781-792`, `:794-809`; monster usage `Models.Monsters/Nibbit.cs:53` |
| `CardInstance.GetStarCostWithModifiers` applies `Hook.ModifyStarCost` when the card is in a combat pile | The cards port predates the pipeline dispatcher. Overriders exist in the game (`VoidFormPower`, `BrilliantScarf`). | `Models/CardModel.cs:3204-3216`, `Hooks/Hook.cs:11071-11092` |
| `CardInstance.HasEnoughResources` ports the "pay excess energy with stars" branch | Keeps `CanPlay` (legal actions) in line with `ActionExecutor.SpendResources`, which already had it. No v0.111.0 model overrides the gate, so behaviour is unchanged today. | `Entities.Players/PlayerCombatState.cs:334-353`; `Models/CardModel.cs:1345-1350`; default `Models/AbstractModel.cs:1037` |
| `CardInstance.ResolveEnergyXValue` / `ResolveStarXValue` call `Hook.ModifyXValue` | Replaces a local listener walk that allocated a list per call; same guarded listener order. | `Models/CardModel.cs:2750-2773`, `Hooks/Hook.cs:11151-11168` |
| `StateHasher.XxHash64` delegates to `DeterministicHash.XxHash64` | Two identical hand-written XXH64 bodies (state and rng areas); one implementation remains, still covered by both areas' vector tests. | XXH64 specification |
| `CombatSim.StateHash()` reuses a per-instance `CanonicalWriter` | The no-scratch overload allocated 4 KB per call (state spec §7). A clone gets its own writer. | — |

New tests: `CardTests.Test_Cards_GlobalStarCostAppliesOnlyToCardsInCombatPiles`,
`CardTests.Test_Cards_ExcessEnergyIsPaidWithStarsOnlyWhenAHookAllowsIt`, and `EndToEndTests` (§3).

## 3. End-to-end tests (`Sts2Sim.Tests/EndToEndTests.cs`, filter `EndToEnd`)

All tests use the real `StartingDecks.Ironclad` deck and the registered `NIBBITS_WEAK` encounter
through `CombatSim.Create`. Since the 2026-10-30 fidelity fixes (§6) every run models the first
combat of a run at its ascension: `CombatSim.Create(seed, StartingDecks.Ironclad, "NIBBITS_WEAK",
ascension, currentHp: StartingDecks.IroncladHpAfterNeowArrival(ascension))`. At A5+ the
starter-deck rule appends Ascender's Bane (11 cards), and at A2+ the player enters with 64/80 HP
(Neow's arrival heal, before any Neow option). The reference derives both from its own constants.

### 3.1 Independent reference model

`NibbitsWeakReference` is a second, minimal implementation of the Ironclad-starter-vs-one-Nibbit
combat, written from the decompile and sharing no rule code with Core (it uses only `RunRngSet`
for the stream seeds, which `RngTests` checks against a real game save). `RunLockstep` steps the sim
and the reference with the same action and, before every action, requires the sim's legal-action
list to equal the reference's; after every action it compares 20 observed fields (round, turn,
side, outcome, player HP/max HP/block/energy, hand/draw/discard/play/exhaust piles as ordered id
strings, monster count/HP/max HP/block/Strength/Vulnerable, monster intent). The exhaust pile is
compared by ids since the fidelity fix (it was a count).

| Rule in the reference | Value | Citation |
| --- | --- | --- |
| Deck, deck order | 5× Strike, 4× Defend, 1× Bash; at A5+ plus Ascender's Bane last | `Models.Characters/Ironclad.cs:40-52`; `Entities.Ascension/AscensionManager.cs:33-38` |
| Player HP | max 80; current 80 at A0–A1, 64 = `(int)(80 × 0.8)` at A2+ | `Models.Characters/Ironclad.cs:30`; `Models/AncientEventModel.cs:76-88` |
| Ascender's Bane | never playable; exhausted at the player's turn end (before the hand is flushed) | `Models.Cards/AscendersBane.cs` (keywords Eternal, Unplayable, Ethereal; cards spec §3); `Combat/CombatManager.cs:1189-1312` (loop spec §3.6) |
| Energy at each player turn start | reset to max energy 3 | `Models/CharacterModel.cs:67`; `Entities.Players/PlayerCombatState.cs:304-307` |
| Hand draw | 5 per turn, from index 0 (top), stop at 10 cards or when draw + discard are empty; reshuffle only when the draw pile is empty and the discard is not | `Combat/CombatManager.cs:4206`; `Commands/CardPileCmd.cs:4024-4055`, `:3075-3078`; `Entities.Cards/CardPile.cs:35` |
| Opening shuffle | deck order into the draw pile, then `UnstableShuffle` on the `Shuffle` stream (`n--; k = NextInt(n + 1); swap`) | `Entities.Players/Player.cs:1214-1233`; `Entities.Cards/CardPile.cs:241-246`; `Extensions/ListExtensions.cs:21-36` |
| Reshuffle | discard then draw cards, sorted by ordinal id, then `UnstableShuffle`; result becomes the draw pile | `Commands/CardPileCmd.cs:2754-2771`; `Extensions/ListExtensions.cs:10-19`; `Models/CardModel.cs:3649-3673` |
| Strike / Defend / Bash | 1 energy, 6 damage / 1 energy, 5 block / 2 energy, 8 damage then Vulnerable 2 (not applied when the hit killed) | `Models.Cards/StrikeIronclad.cs:98-103`; `Models.Cards/DefendIronclad.cs:93-98`; `Models.Cards/Bash.cs:70-89`, `:120-129`; `Commands/PowerCmd.cs` apply skipped while ending (pipeline spec §6) |
| Played card | to the discard; the card that kills the last enemy stays in the play pile | `Models/CardModel.cs:942-975`; `Commands/CardPileCmd.cs:3291` |
| Damage | Strength added, then ×1.5 if the target is Vulnerable; block absorbs `Min(block, dmg)` and loses `(int)` of it; HP loses `(int)` of the rest | `Models.Powers/StrengthPower.cs:16-27`; `Models.Powers/VulnerablePower.cs:96-128`; `Entities.Creatures/Creature.cs:1026-1045` |
| Block clear | at the start of the owner's side turn; the player keeps block on turn 1 | `Entities.Creatures/Creature.cs:136-155` |
| Vulnerable duration | −1 at each enemy turn end; no first-tick skip on a monster | `Models.Powers/VulnerablePower.cs:56-58`; `Commands/PowerCmd.cs:476-479` |
| Nibbit HP | `42 + Niche.NextInt(0, 5)` (A8+: `44 + …`) | `Models.Monsters/Nibbit.cs:281-283`; `Entities.Creatures/Creature.cs:972-984`; `Random/Rng.cs:213-223` |
| Nibbit moves | alone: BUTT, SLICE, HISS, repeat; BUTT 12 (A9: 13); SLICE 6 (A9: 7), then block 5 (A8: 6) unless the attack ended the combat; HISS Strength +2 (A9: +3) | `Models.Monsters/Nibbit.cs:285-291`, `:323-347`, `:53-56`, `:226-246`, `:136-152`; `Models.Encounters/NibbitsWeak.cs:19-24`; `Commands/CreatureCmd.cs:3950-3985` |
| Intent timing | rolled at combat start; each later player turn start advances to the follow-up once a move has been performed | `MonsterMoves.MonsterMoveStateMachine/MonsterMoveStateMachine.cs:76-104`; `Combat/CombatManager.cs:5315-5323` |
| Round and turn | +1 when the side switches back to the player | `Combat/CombatManager.cs:7472`, `:7480` |
| Defeat | the enemy turn stops at the win check; the side stays Enemy | `Combat/CombatManager.cs:3219`, `:7196-7211` |

### 3.2 Tests

| Test | What it pins |
| --- | --- |
| `UsesTheRealStarterDeckAndEncounter` | `StartingDecks.Ironclad` equals the reference deck; for A0–A10 `IroncladAtRunStart`, `CombatLoop.DeckForCombat` (from the bare starter and from a run-start deck) and `IroncladHpAfterNeowArrival` equal the reference's deck and HP; the registry resolves `NIBBITS_WEAK`; one `NIBBIT`. `Create` at A4 keeps 10 cards, at A5 and A10 has 11 with one curse; without an override HP is 80/80 at A10; with `currentHp: 64` it is 64/80 and the state equals the one from `IroncladAtRunStart(10)` (no second curse); any other deck is used as given with the given HP; invalid HP throws. |
| `GreedyPlayMatchesIndependentReferenceUntilVictory` | Lockstep for seeds 1–40 at A0 and 101–110 at A10 with a greedy policy (Bash when the target is not Vulnerable, else Strike, Bash, Defend, end turn); every run ends in Victory. |
| `GreedySeedOneEndsInAPinnedVictory` | Seed 1 greedy: Victory in round 3 after 10 decisions with the full final snapshot (§3.3). |
| `RandomPlayMatchesIndependentReference` | Lockstep for 150 seeds at A0 and 50 at A10 under a uniform random policy; both Victory and Defeat occur. |
| `PassivePlayerLosesOnTheHandComputedRound` | End turn only, in lockstep with the reference; the per-round values come from the reference's trace and must also equal the hand computation below. A0 (seed 5, 80 HP): HP after each enemy turn 68, 62, 62, 48, 40, 40, 24, 14, 14, then 14 − 18 → Defeat in round 10, Nibbit Strength 6, nothing exhausted. A10 (seed 6, 64 HP, 11 cards): 51, 44, 44, 28, 18, 18, then 18 − 19 → Defeat in round 7, Strength 6, exhaust pile = the one Ascender's Bane. Also the intent cycle each round, SLICE block (5 / 6) visible only during the following player turn, untouched Nibbit HP, the side left on Enemy, no legal actions after the end and `Step` throwing. |
| `IdenticalRunsProduceIdenticalHashSequences` | For 3 seeds, two independent runs give identical action and `StateHash` sequences; a replay of the recorded actions reproduces every hash through both `StateHash()` and `StateHash(scratch)`; different seeds give different opening hashes. |
| `CloneAtEveryDecisionReplaysTheSameHashes` | Seed 7: a clone taken at every decision replays the rest of the recorded actions with the recorded hashes, reaches the end, and never changes its source. |
| `CanonicalHashesArePinnedAcrossProcesses` | Seed 1 greedy opening hash `0xA22C1E9190E84745` and final hash `0xF16FB5A5465E6826`. A within-process comparison cannot catch per-process hash randomisation; a pinned value can. The values at integration were `0xE470569300F24DE2` / `0x6BDB996A8B916C48`; they were refreshed once, for the intended layout change that added `IsInProgress` and `IsStarting` to the `CombatState` record (state spec §2.2). |

Hand computation of the A10 passive run (Nibbit at A10: BUTT 13, SLICE 7 then block 6, HISS
Strength +3; monsters spec §2): 64 − 13 = 51; 51 − 7 = 44 (block 6); HISS → Strength 3, 44;
44 − (13 + 3) = 28; 28 − (7 + 3) = 18 (block 6); HISS → Strength 6, 18; 18 − (13 + 6) < 0 →
Defeat in round 7. The curse is drawn by round 3 at the latest (5 + 5 + 1 cards of 11 before the
first reshuffle) and exhausted at that turn's end.

Refreshing the pinned hashes: they change only when the canonical byte layout changes (a field
added to any `WriteCanonical`, a new tag, a changed order). After such an intended change, run
`dotnet run --project sim/Sts2Sim.Tests -- EndToEnd_Canonical`, check that every other test still
passes, and copy the reported values into the two constants. A failure without a layout change
means non-determinism and is a bug.

### 3.3 Hand check of the pinned seed-1 trajectory

Nibbit 44 HP (`Niche` draw 2). Opening hand D D S S D, draw pile S D S B S.
Round 1: Strike 44 → 38, Strike → 32, Defend (block 5); BUTT 12 − 5 → player 73.
Round 2: draw S D S B S; Bash 32 → 24, Vulnerable 2; Strike 6 × 1.5 = 9 → 15; SLICE 6 → player 67,
Nibbit block 5; enemy turn end: Vulnerable 1.
Round 3: reshuffle of the 10 discarded cards (sorted B D D D D S S S S S, then shuffled) gives
hand S S S D S and draw B S D D D; Strike 9 vs block 5 → 11; Strike 9 → 2; Strike 9 → dead.
Victory in round 3: hand D S, draw B S D D D, discard S S, the killing Strike in the play pile,
player 67 HP, 0 energy; 10 decisions.

## 4. Cross-area wiring checked against the decompile

- Combat start: players, then monsters created and added one at a time (HP roll sees earlier
  monsters; no turn state yet), then the setup window (`IsStarting`): deck copied and shuffled,
  `SetUpForCombat` per monster (move graph), `AfterCreatureAdded` per creature (first `RollMove`);
  then `IsInProgress = true`, `IsStarting = false` and `Hook.BeforeCombatStart`
  (`Rooms/CombatRoom.cs:381-394`, `:417`; `Combat/CombatManager.cs:6575-6627`, `:4466-4491`,
  `:509-543`; loop spec §2.2).
- Turn start order: `BeforeTurnStart` per creature → `Hook.BeforeSideTurnStart` → (player side:
  phase Start, `PrepareForNextTurn` per enemy unless an extra turn) → `AfterTurnStart` per creature
  (block clear) → `Hook.AfterBlockCleared` per creature → player setup (energy, draw) →
  `Hook.AfterSideTurnStart` → (player: auto-pre-play phase, win check; enemy: win check, enemy
  turn) (`Combat/CombatManager.cs:4865-5406`, labels `IL_024d`, `IL_0433`, cases 2–7).
- Enemy turn end: `Hook.BeforeSideTurnEnd(Enemy)` → `PlayerCombatState.EndOfTurnCleanup` →
  `Hook.AfterSideTurnEnd(Enemy)` (debuff ticks) → win check → switch sides
  (`Combat/CombatManager.cs:2041-2088`).
- Deaths: `Kill` deactivates player hooks (`Commands/CreatureCmd.cs:2850`); listener liveness reads
  the flag (`Combat/CombatState.cs:1061`); a monster that dies during its own move is removed after
  the move (`Commands/CreatureCmd.cs:2815-2822`, `Models/MonsterModel.cs:287-291`).
- Card play: `PlayCardAction` → `SpendResources` → `OnPlayWrapper` → win check after the action
  (`GameActions/PlayCardAction.cs:137-182`; `Models/CardModel.cs:257-1056`;
  `GameActions/ActionExecutor.cs:129-148`).

## 5. Open-question triage (reports of the six port agents)

Status: **resolved** (fixed or verified, citation given), **P0-safe** (a real gap, no P0 model
reaches it; documented in the owning spec), **decision** (needs the orchestrator or the user).

| Area | Question | Status | Resolution |
| --- | --- | --- | --- |
| rng | Extension-method ambiguity for `UnstableShuffle`/`StableShuffle`/`TakeRandom` | resolved | Only `RngCollectionExtensions` defines them (rng spec §13a). Listed in CONTRACTS §4.3 (2026-10-30). |
| rng | Reshuffle comparer must equal `CardModel.CompareTo`; introsort tie order | resolved | `CardPileCmd.CompareForShuffle` = same reference → 0, ordinal id, upgrade level (`Models/CardModel.cs:3649-3673`); `LoopTests` checks the reshuffle; E2E reshuffles match the reference by id. The shipped runtime version cannot be read from the decompile (decision/low risk). |
| rng | Live-save seed and stream states embedded in `RngTests` | resolved | Orchestrator decision (2026-10-30): the oracle test for seed `Q3X6PF8S37GA` stays exactly as is. |
| rng | `RunRngSetSnapshot.HashedSeed` (sim-only) | resolved | Orchestrator decision: kept, documented as sim-only in CONTRACTS §4.3 and rng spec §11, with its round-trip consequences. |
| rng | `StartsWith("old")` culture vs ordinal | P0-safe | Same result for game-generated, canonicalized and V18-migrated seeds (rng spec §6.1). Hand-built strings with ignorable code points before `old` take the old-hash path in the game under ICU cultures and the XXH64 path in the sim. Not fixed: the projects run with `InvariantGlobalization`, under which a culture-sensitive `StartsWith` is ordinal anyway, and reproducing ICU behaviour would make the sim depend on the host's culture data. |
| rng | `Rng.Chaotic`, `TestRngInjector`, grab bags, map/daily streams not ported | P0-safe | Cosmetic, test-only or run layer (rng spec §14). |
| pipeline | Not built or run | resolved | Builds; 50/50 pipeline tests pass. |
| pipeline | Non-contract members `IsActiveForHooks`, `IsPerformingMove`, `StunInternal` | resolved | Wired and checked (pipeline spec §9). Frozen in CONTRACTS §4.4/§4.7 (2026-10-30). |
| pipeline | `CombatHistory` rule-relevant (Spite, Stomp, GangUp, IterationPower) | P0-safe | No P0 model reads history; needs a cloned, hashed history on `CombatState` before such cards are ported. |
| pipeline | Only powers are listeners; `MonsterModel` is one in the game | P0-safe | `Nibbit` overrides no hook (only HP, move graph, UI members: `Models.Monsters/Nibbit.cs:281-295`, `:323`, `:388`, `:406`). |
| pipeline | `BeforeRemovedFromRoom` / `ResetStateMachine` on removal | P0-safe | `Nibbit` does not override `BeforeRemovedFromRoom` (`Models/MonsterModel.cs:609-611` default). `ResetStateMachine` (`:693-696`) nulls the machine; for a monster killed during its own move this also skips `OnMovePerformed` in the game (monsters spec §8). Not observable in P0. |
| pipeline | Dropped hook parameters (participants, death-anim length, auto-play type) | P0-safe | No P0 power reads them. |
| pipeline | Turn sequencing for debuff ticks | resolved | `CombatLoop.EndEnemyTurn` matches `Combat/CombatManager.cs:2041-2088`; E2E Vulnerable timing matches the reference. |
| pipeline | `AttackCommand.Results` flat vs per-hit | resolved | Both exposed (`Results`, `HitResults`). |
| cards | Not built or run | resolved | 32/32 card tests pass. |
| cards | `Bash` depends on `VulnerablePower` by class name | resolved | Exists in `Powers/Defs`; `PowerVar<VulnerablePower>` name is `"VulnerablePower"`. |
| cards | In-combat cost path must use the `(cs, card)` overloads | resolved | The loop uses `GetAmountToSpend(cs, card)` and `CanPlay(cs, …)`. The `(cs, card)` overloads are listed in CONTRACTS §4.5 (2026-10-30). |
| cards | `ShouldAllowTargeting` filter in legal actions | resolved | Applied by `LegalActionGenerator.IsLegalEnemyTarget` (`Nodes.Combat/NTargetManager.cs:655-671`). |
| cards | Missing `ModifyXValue`, `ModifyStarCost`, `ShouldPayExcessEnergyCostWithStars`, `ModifyKeywordsInCombat` dispatchers | resolved / P0-safe | The first three exist now and are wired into `CardInstance` (§2). Global keywords stay deferred (`HexPower` only). |
| cards | Test file name `CardTests.cs` vs `CardsTests.cs` | resolved | Kept; filter `Cards` selects them. CONTRACTS §8 now names the real files. |
| cards | Deck ids carry no upgrade marker | decision | Needs an id syntax (e.g. `STRIKE_IRONCLAD+1`) before replays start with upgraded cards. |
| monsters | Loop wiring of spawn, first roll, per-turn roll, enemy turn, side switch | resolved | Monsters spec §10. |
| monsters | `Kill` must respect `IsPerformingMove` | resolved | `CreatureCmd.cs` checks it; `Test_Monsters_PerformMoveRemovesMonsterThatDiedDuringItsMove` passes. |
| monsters | `CreatureCmd.Stun` threw | resolved / open | Delegates to `MonsterLifecycle.StunInternal` with a no-op default move (`Commands/CreatureCmd.cs:3409`). The body of the game's stun `Wrapper` is missing from the decompile (pipeline spec §9), so any extra effect of the `STUNNED` move is unknown. |
| monsters | `SpawnedThisTurn` on `MonsterState` | resolved | Single field, used by `SetUpForCombat`, `TakeTurn` and `OnSideSwitch`. |
| monsters | Additive and changed monster contract members | resolved | CONTRACTS §4.7 synced (2026-10-30): `MonsterLifecycle`, `MonsterSpawner.SpawnEncounter`, `IsPerformingMove`, writable `FollowUpState`, init-only `FollowUpStateId` (now as in the game), nullable conditions, unsealed `SingleAttackIntent`, `NextMove == null` as `UNSET_MOVE`, duplicate state ids throw. |
| monsters | `EncounterDef.GenerateMonsters` has no encounter `Rng`; per-monster `Rng` not modelled | P0-safe | `NibbitsWeak` uses neither. Needed before `SlimesWeak` and similar (`Models/EncounterModel.cs:211-215`). |
| monsters | Test file name `MonsterTests.cs` | resolved | Kept; filter `Monsters`. |
| monsters | Fresh RNG streams in `CombatSim.Create` vs real runs | resolved | `CombatSim.Create(RunRngSet rng, ...)` starts from a (deep-cloned) restored set (loop spec §2.1, rng spec §6.2). |
| loop | `PlayerCmd` vs `EnergyCmd` duplication | resolved | One implementation (`EnergyCmd`); `PlayerCmd` is the contract facade named after `Commands/PlayerCmd.cs` and only delegates. |
| loop | `HasEnoughResources` star branch, `GetStarCostWithModifiers` hook | resolved | Fixed in `CardInstance` (§2). |
| loop | Dynamic keywords (`TryModifyKeywordsInCombat`) | P0-safe | Only `HexPower` uses it. |
| loop | Non-contract members the loop calls | resolved | List in loop spec §8; frozen in CONTRACTS §4 (2026-10-30). |
| loop | `PendingLoss` and extra-turn set are step-local | resolved / P0-safe | The loss state is now persisted on `CombatState` (`IsInProgress`, loop spec §3.10). The extra-turn set is still step-local; no P0 power reads it (loop spec §9). |
| loop | Hooks not dispatched (`AfterAutoPrePlayPhaseEntered`, `AfterCardChangedPiles`, …) | P0-safe | The sim `AbstractModel` has no such virtuals, so no P0 listener can miss them. |
| loop | No encounter slots, no `SortEnemiesBySlotName` | P0-safe | `NibbitsWeak` spawns one monster with slot `null` (`Models.Encounters/NibbitsWeak.cs:22-24`). |
| loop | History, effect depth, Sly, enchantments, post-victory run steps | P0-safe | No P0 card or monster reaches them (loop spec §9). |
| loop | Turn-end-in-hand cards resolved card by card (non-interactive semantics) | decision | Matches `NonInteractiveMode`; whether interactive human replays can differ must be checked when the first such card is ported (`Combat/CombatManager.cs:1324-1429`). |
| loop | Not built or run | resolved | 18/18 loop tests pass after the test-double fix. |
| state | Not built or run | resolved | 24/24 state tests pass. |
| state | `MonsterState.IsHittable` cannot match the game | resolved | Had no caller; deleted from the code and CONTRACTS (2026-10-30). `CombatStatus.IsHittable` / `GetHittableEnemies` are used instead. |
| state | `IsActiveForHooks` wiring | resolved | Pipeline spec §9. |
| state | `RemoveInternal` throws when absent | resolved | Every caller locates the pile first. |
| state | Start values in constructors | resolved | The loop re-assigns the same values; no double increment. |
| state | `CombatSim.StateHash()` allocation | resolved | Per-instance writer (§2). |
| state | `GetCreatures`/`GetHittableEnemies` clear the buffer | resolved | Same convention as `CombatSim.LegalActions(buffer)`; `LegalActionGenerator.Fill` alone appends. |
| state | Combat history not in the state | P0-safe | Same as the pipeline item. |
| state | `CombatState` accepts a null `Rng` | resolved | Orchestrator decision (2026-10-30): kept for hand-built test states, documented in CONTRACTS §4.4/§9. |
| state | Hash includes instance and creature ids | decision | Transposition detection modulo ids needs a separate canonical form. |

## 6. Fidelity fixes after the adversarial audit (2026-10-30)

An adversarial audit re-read the decompile against `sim/` and reported three major and about
thirty minor discrepancies. The orchestrator's decisions: make the starting deck ascension-aware,
give `Create` an explicit HP override whose default is the character's max HP, model the setup
window, sync CONTRACTS, keep the live-save RNG oracle test, keep `RunRngSetSnapshot.HashedSeed`
(sim-only) and the nullable `CombatState.Rng`.

### 6.1 Major findings

| Finding | Decompile | Fix | Tests |
| --- | --- | --- | --- |
| Every A5+ combat started from a 10-card deck: Ascender's Bane was missing and not registered | `AscensionManager.ApplyEffectsTo` appends one `AscendersBane` at A5+ (`Entities.Ascension/AscensionManager.cs:33-38`, `AscensionLevel.cs:10`), called at run creation (`Runs/RunState.cs:980`, `Runs/RunManager.cs:3862`, `:4827-4830`); the card: cost −1, Curse, `TargetType.None`, keywords Eternal/Unplayable/Ethereal, `MaxUpgradeLevel` 0 (`Models.Cards/AscendersBane.cs:10-29`); the deck is copied in order and shuffled (`Entities.Players/Player.cs:1214-1233`) | `ASCENDERS_BANE` registered (`Cards/Defs/AscendersBane.cs`; keyword data read from the build's IL, cards spec §3); `StartingDecks.IroncladAtRunStart` / `ApplyAscensionEffectsToDeck`; `Create` applies the effect when it is given the bare starter deck (`CombatLoop.DeckForCombat`, loop spec §2.1) | `CardTests.AscendersBaneDefinition`, `RunStartDeckAndNeowHpFollowAscension`; `EndToEndTests.UsesTheRealStarterDeckAndEncounter`; every A10 lockstep run now shuffles 11 cards |
| Every combat started at 80/80 and `Create` could not express another HP | Neow sets HP to 0, then heals `MaxHp − CurrentHp`, × 0.8 at A2+ (`Models/AncientEventModel.cs:76-88`; Neow is act 1's only ancient, `Models.Acts/Overgrowth.cs:26`) | `Create(..., int? currentHp = null, int? maxHp = null)`, default = the character's max HP at every ascension; run-layer HP is an explicit input (`StartingDecks.IroncladHpAfterNeowArrival` = 64 at A2+) | A10 E2E runs pass 64 and the independent reference derives 64 itself; the passive A10 expectations come from the reference trace (§3.2) |
| Commands and hooks ran as if the combat were in progress while it was being set up | `CombatTurnState`: `IsInProgress` false, `IsStarting` true (`Combat/CombatTurnState.cs:99-115`); `IsInProgress = true` only after the `AfterCreatureAdded` loop (`Combat/CombatManager.cs:4466-4490`); `IsOverOrEnding` gates closed, guarded hooks open (`Hooks/Hook.cs:8794`) | `CombatState.IsInProgress` / `IsStarting` (cloned, hashed); the loop sets them at the game's points; `CombatStatus.IsEnding` tests `IsInProgress` first; the hook guard reads `IsStarting`; `LoseCombat` only while in progress (`Commands/CreatureCmd.cs:2401-2404`); the move graph is built after the shuffle (loop spec §2.2–2.3) | `LoopTests.SetUpWindowGatesCommandsLikeTheGame` (the `CubexConstruct` case: 0 block from `AfterAddedToRoom`), `PlayerDeathOutsideAnActiveCombatRecordsNoLoss`, `SetUpThenStartReachesFirstDecision`; `DeterminismTests` hash/clone coverage of both flags |

The canonical layout gained the two flags, so the pinned seed-1 hashes were refreshed once
(§3.2). The A0 trajectories and every hand-computed A0 value are unchanged.

### 6.2 Minor findings

Fixed: `CombatStatus.IsEnding` after a processed loss (now false, as `:6550-6553`); the
`AfterCardPlayed`, `CheckForEmptyHand` and `AddGeneratedCardsToCombat` gates read `IsInProgress`
(true while a loss is pending); the move graph is built in `SetUpCombat` after the opening shuffle
and once every enemy exists (monsters spec §4); `CombatSim.Create(RunRngSet, ...)` for mid-run
streams; `AttackCommand` throws for a detached attacker and refuses random targeting by a monster
attacker (pipeline spec §2, §9); `AttackIntent.GetSingleDamage` evaluates the damage closure twice
as the game does (monsters spec §6); `MoveStateDef.FollowUpStateId` is init-only as in the game;
`MonsterState.IsHittable` deleted.

Documented, not changed (each is unreachable in P0 or a structural/naming difference; the owning
spec has the details): rng — culture-sensitive `StartsWith` (§5 triage row), `GetMockInstance`
sharing (rng §7), `HashedSeed` round trips (rng §11), array vs dictionary exception types (rng §5.3),
per-monster and encounter `Rng` (monsters §8), `PlayerSetFromHashedRunSeed` for `old` seeds
(rng §6.3), `Rng.Chaotic`/`TestRngInjector` (rng §7); pipeline — detached first target in the
`Damage` list overload, `Died` event, combat history, flat `Results` plus `HitResults`,
Cruelty lookup (pipeline §8–§9); monsters — `BeforeRemovedFromRoom`/`ResetStateMachine`,
`EncounterDef` run-level data, `UNSET_MOVE` exception path (monsters §8); loop —
`Player.AfterCombatEnd` after victory, side-turn `participants`, `ModifyKeywordsInCombat`,
undispatched hooks and history, slot sorting, the additional stripped wait sites, interactive
turn-end-card timing (loop §3.6, §7, §9).

## 7. W1 integrate, round 1 (2026-11-01)

Owner: the W1 integrate step (CONTRACTS Part B §B11). Branch `claude/sim-source-recreation-9392df`,
base `94424e5` (phase-0 scaffold `46d1918` plus its fixes). .NET SDK 9.0.117, runtime 9.0.16,
Apple M4 Pro (Arm64, 12 cores); other agents were running (load average about 3).

### 7.1 Commits and merges

| Step | Result |
| --- | --- |
| In-place INV track | `687077a` "specs: inventory normalisation (INV track)", 154 files: `sim/tools/inventory/**` (classifier pipeline `classify/`, `test_classify.py`, `extract_models.py`, `test_inventory.py`), `sim/specs/inventory/**` (new `acts`, `afflictions`, `characters`, `enchantments`, `modifiers`, `orbs` `.classified.json`, `batches.json`, `requirements.json`, `version-deltas.json`; refreshed classified manifests, `kernel-gaps.json`, `subsystems.json`, `summary.json`), `docs/sim-port/inventory-*.md` (new `inventory-minor-kinds.md`). |
| In-place REC track | `4094c1e` "recorder: passive anchor additions R0-R5/R7/R11 (REC track, not installed)", 18 files: `mod/**` and `docs/hook-map.md`. Nothing was installed into the game's `mods/` directory. |
| Merge list | Nine entries, every one `819d7c6873f0703198892190f24a244407301e25` (the `origin/main` tip, "Merge pull request #28", the P0 merge and an ancestor of the base). Each `git merge --no-ff --no-edit` printed "Already up to date." and made no commit. No W1 track was integrated (§7.5, row 1). |
| Conflicts | None (no merge applied a change). |
| Trailers | `Co-Authored-By: Claude Opus 5.5`, the attribution this execution session must use, in place of the brief's `Claude Fable 5.1`. The two track commits were first written with the brief's trailer (`f1fb42b`, `18c6908`) and re-created with identical trees. |

### 7.2 Build and tests

| Gate | Result |
| --- | --- |
| `dotnet build sim/Sts2Sim.sln` (incremental and `--no-incremental`) | 0 warnings, 0 errors; no edit needed. |
| `dotnet run --project sim/Sts2Sim.Tests` | 367 passed, 0 failed, 367 total: the Part A suite. No W1 suite (`ChoiceTests`, `HookTests`, `StateKernelTests`, `CardKernelTests`, `TurnFlowTests`, `RelicPotionTests`, `CharacterMechanicsTests`, `MonsterKernelTests`) is in the tree. |
| Diff gate (`Sts2Sim.Diff -c Release`, P0 bundle set, v0.107.1) | Bundles 4 (agree 4, hard divergence 0, skipped 0, error 0, sim exception 0); decisions 69/69 (100.00 %), 0 unobserved; outcome 4/4; soft diffs 0. |
| Recorder core tests, a check of `4094c1e` (`dotnet test mod/tests/Sts2Recorder.Core.Tests.csproj`) | 78 passed, 0 failed. `mod/Sts2Recorder.csproj` compiles with 0 warnings, 0 errors (output in `mod/bin`, nothing installed). |
| Inventory tool tests, a check of `687077a` (`python3 -m unittest discover -s sim/tools/inventory -p 'test_*.py'`) | 42 passed. |

Failures fixed: none. Test changes: none. Cross-area edits: none; the integration commit changes
only this file.

### 7.3 Bench (`dotnet run -c Release --project sim/Sts2Sim.Bench`, `NIBBITS_WEAK`, 1 thread)

| Run | decisions/s | `Clone()` | `StateHash()` |
| --- | --- | --- | --- |
| 1 | 664 642 | 2.645 µs | 1.266 µs |
| 2 | 666 603 | 2.627 µs | 1.263 µs |
| 3 | 676 647 | 2.872 µs | 1.260 µs |
| Median | 666 603 | 2.645 µs | 1.263 µs |

Part B gates: ≥ 300 decisions/s/core passes (about 2 200×); `Clone()` ≤ 1 ms passes (about 380×).
Against the recorded P0 figures (705 048 decisions/s, 2.53 µs clone, 0.87 µs hash; `sim/BENCH.md`,
2026-10-30): throughput 0.945×, clone 1.045×, hash 1.45×.

### 7.4 Where the regression comes from

Only two commits since P0 change sim code: the phase-0 scaffold `46d1918` and its fixes `94424e5`.
To separate code from machine load, the P0 tree (`83c7ee9`, whose `sim/` code equals `819d7c6`) was
exported with `git archive`, built in Release and benched interleaved with this tree in the same
session:

| Tree | decisions/s (3 runs → median) | `Clone()` median (range) | `StateHash()` median (range) |
| --- | --- | --- | --- |
| P0 `83c7ee9` | 633 752 / 610 856 / 563 744 → 610 856 | 2.641 µs (1.845–2.778) | 1.148 µs (1.003–1.261) |
| W1 round 1 `4094c1e` | 582 754 / 594 387 / 610 221 → 594 387 | 2.911 µs (2.788–2.948) | 1.265 µs (1.228–1.338) |
| W1 / P0 | 0.973× | 1.10× | 1.10× |

Allocation per call on the bench's mid-fight state (10 000 calls,
`GC.GetAllocatedBytesForCurrentThread`), and canonical length of that state:

| Measure | P0 | W1 round 1 | Change |
| --- | --- | --- | --- |
| `Clone()` | 5 384 B | 6 640 B | +1 256 B (+23 %) |
| `CombatSim.Create` | 7 520 B | 8 776 B | +1 256 B (+17 %) |
| `StateHash()` | 0 B | 0 B | 0 |
| Canonical bytes | 2 042 B | 2 368 B | +326 B (+16 %) |

The scaffold wired the Part B state additions into clone and canonical:

- D4 (every `CombatState` has a `Run`): `CombatState.DeepClone` first builds a `RunState` through
  `RunState.CloneWithoutCombat` (a player list, a modifier list, `ChoiceRuntime.DeepClone`,
  `FidelityLog.DeepClone`), then `CloneInto`; `WriteCanonical` writes the run fields (ascension,
  `UnlockProfile`, player list, RNG, modifiers, choice runtime, fidelity log) before the combat
  fields.
- B2 combat fields: an ally list, `CombatHistory` (deep-cloned and hashed),
  `PlayersTakingExtraTurn`, `CardOrPotionEffectDepth`.
- D3 player fields on `PlayerState`: gold, relic list, potion-slot array, base orb slots,
  `CanUseOrRemovePotions`, deck list, `OrbQueue`, pet ids; on `MonsterState`: `PetOwnerId`,
  `Listener`. Each list or array is a new allocation per clone even when empty, and each writes a
  tag and a count.

The step path gained only constant-cost changes: the ported dispatchers take the `RunState` and
check `ReferenceEquals(rs.Combat, cs)` (`Hook.RequireAttachedCombat`), and the side-turn hooks pass
the existing monster or ally list as `participants` (no allocation); every new dispatcher, model
kind and `Choice` member is a stub that no P0 content reaches. Decisions/s therefore moves through
those checks and the larger `Create` (+1 256 B per episode of about 20 decisions): −2.7 % at the
same load, inside the run-to-run spread (the P0 tree alone measured 564k–634k in this session).
`Clone()` and `StateHash()` rise about 10 % at the same load, from the +23 % allocation and +16 %
canonical bytes on top of fixed per-object costs. The rest of the gap to the recorded P0 figures is
machine load: the unchanged P0 tree measured 0.87× of its recorded throughput in this session.

### 7.5 Residual issues

| # | Issue | Next step |
| --- | --- | --- |
| 1 | The merge list carried the `origin/main` tip nine times instead of the nine W1 track commits (B11 has nine owner rows besides scaffold and integrate: choice, hooks, state, pipeline, cards-kernel, loop, relics-potions, character-mechanics, monsters-kernel). No commit descending from the base `94424e5` exists in the object store apart from this round's own commits (every ref, reflog and unreachable commit searched), so the tracks either never committed or ran from `origin/main`, which lacks the Part B scaffold. | Re-collect the track SHAs (each based on `94424e5`) and run round 2. |
| 2 | Gates F1 (clone/hash equality at random points with pilot relics, potions and prompt cards), F4 (legal-action closure with prompts), F5 (invariants after every step) and the B2 chain (`GremlinHorn` or `DarkEmbracePower` + `HellraiserPower` + `SeekerStrike`) cannot be evaluated: their content and suites are absent, and 137 `throw new NotImplementedException` scaffold sites remain in 15 files of `Sts2Sim.Core` (73 in `Combat/Pipeline/Hook.cs`, 22 in `State/CombatHistory.cs`). | Round 2. |
| 3 | Only build, the Part A suite, the diff gate and the bench hold, so the W1 wave is not complete. | Round 2. |
| 4 | The bench covers the scaffold only (§7.4); the track content (relics, potions, orbs, history appends, hook dispatch) will change it. | Re-run after round 2. |
| 5 | The REC track (`4094c1e`) is compiled and tested but not installed; the game keeps the previously installed `Sts2Recorder`. | `scripts/install_mod.sh` when the recorder change is to go live. |

## 8. W1 integrate, round 2 (2026-11-01)

Owner: the W1 integrate step (CONTRACTS Part B §B11). Branch `claude/sim-source-recreation-9392df`,
start `ef7c7b9` (round 1). .NET SDK 9.0.117, runtime 9.0.16, Apple M4 Pro (Arm64, 12 cores); other
agents were running (load average 2.4 to 4.5).

### 8.1 Commits and merges

| Step | Result |
| --- | --- |
| In-place tracks | `git status --short` was empty: the INV and REC work was committed in round 1 (`687077a`, `4094c1e`), so no track commit was made. |
| Merge list | Nine entries, again every one `819d7c6873f0703198892190f24a244407301e25` (the `origin/main` tip, "Merge pull request #28", an ancestor of `94424e5`). Each `git merge --no-ff --no-edit` printed "Already up to date." and made no commit; `HEAD` stayed at `ef7c7b9`. |
| Search for the track work | No ref, reflog entry or unreachable commit (`git fsck --unreachable --no-reflogs`) descends from `94424e5` apart from round 1's own commits and their first versions with the brief's trailer (`f1fb42b`, `18c6908`). No other worktree holds a W1 branch. No W1 track was integrated (§8.6, row 1). |
| Conflicts | None. |
| Trailer | `Co-Authored-By: Claude Opus 5.5`, the attribution this execution session must use, as in round 1 (§7.1), in place of the brief's `Claude Fable 5.1`. |

### 8.2 Build, tests, diff gate

| Gate | Result |
| --- | --- |
| `dotnet build sim/Sts2Sim.sln` (Debug and Release, `--no-incremental`) | 0 warnings, 0 errors. No edit was needed. |
| `dotnet run --project sim/Sts2Sim.Tests` | 371 passed, 2 failed, 373 total: the Part A 367 plus the 6 new gate tests (§8.3). The two failures are the gates whose content is absent: `Test_W1Gate_B2Chain_StepsAndRecordsDeferredListenerPromptInline` and `Test_W1Gate_PilotContentIsRegistered`. Release (`-c Release`): the same 371 / 2. |
| Diff gate (`Sts2Sim.Diff -c Release`, P0 bundle set, v0.107.1) | Bundles 4 (agree 4, hard divergence 0, skipped 0, error 0, sim exception 0); decisions 69/69 (100.00 %), 0 unobserved; outcome 4/4; soft diffs 0. |

Failures fixed: none (no merged code). Test changes: the new `sim/Sts2Sim.Tests/W1GateTests.cs`; no
existing test changed. Cross-area edits: none. Changed files: `W1GateTests.cs` (new), `sim/BENCH.md`
(W1 rows), this file.

The two failing gate tests fail until the W1 tracks land; they must not be weakened or skipped. Each
message lists the missing ids.

### 8.3 W1 gate suite (`sim/Sts2Sim.Tests/W1GateTests.cs`)

No owner suite covers F1, F4, F5 or the B2 chain (none of the eight W1 suites is in the tree), so the
integrate step added them. Content: registered content only, selected by id string, plus test doubles
declared in the file (never discovered by the registries). Every playout starts from
`CombatSim.Create(seed, deck, encounter, ascension)` on every registered encounter (8), at A0 for odd
seeds and A10 for even seeds; the deck is the Ironclad starter plus every registered B5 prompt-card
pilot (`ARMAMENTS`, `ACROBATICS`, `HEADBUTT`, `SEANCE`, `DISCOVERY`, `SEEKER_STRIKE`). At the first
decision the player is equipped: gold 50 + seed mod 200, every registered relic (a seeded sample of 12
when more exist) plus the test double `GateCounterRelic` (a counter and a list in its extra canonical
state), and the potion slots filled (all slots for even seeds, all but the last for odd seeds) with
registered potions, or with the test double `GateChargePotion` (`CombatOnly`, `Self`, a charge
counter, inert `OnUse`) when none is registered. The policy is SplitMix64, seeded per encounter, seed
and gate (FNV-1a of the encounter id).

| Test | Result | Counts | Checks |
| --- | --- | --- | --- |
| `Test_W1Gate_F1_CloneHashEqualityOnRandomPlayouts` | pass | 32 playouts (8 encounters × seeds 1–4, 0 capped), 647 steps, 176 clone checks, 319 clone steps, 408 lockstep steps, 192 hash-coverage probes | At a random third of the decision points and at the terminal state: `Hash(Clone(x)) == Hash(x)`, equal `CombatSim.WriteCanonical` bytes, equal outcome and legal actions, `Hash(Clone(Clone(x)))`; the clone then takes 1–4 random legal steps and its gold, relic counter and log and potion charges are changed, after which the source's hash and bytes, the clone of the clone and a sibling clone are unchanged; the sibling then follows the source in lockstep for 3 steps with equal hashes. Per playout, six probes on clones (gold, relic counter, relic log, relic list, potion charges, potion slot) must each change the clone's hash and leave the source's. |
| `Test_W1Gate_F4_LegalActionClosure` | pass | 24 playouts (seeds 11–13), 531 decision states, 0 prompt states, 2 629 listed actions stepped on clones, 42 062 candidates checked with `IsLegal`, 2 124 illegal actions stepped | A terminal state lists nothing; a non-terminal state lists at least one action, without duplicates; every listed action is `IsLegal` and steps on a clone without an exception; the candidate set (plays `(-1..hand) × (-2..monsters)`, `EndTurn` with and without stray indices, `UsePotion(-1..slots, -3-allies..monsters)`, `DiscardPotion(-1..slots)`, `Pick(-1..12)`, `Confirm`, `Cancel`, an unknown kind) has no legal unlisted action except a `Pick` (B1 lists only ascending picks for unordered prompts); four sampled illegal actions per state throw `InvalidOperationException` (Part A §4.2) and leave the hash and bytes unchanged. |
| `Test_W1Gate_F5_InvariantsHoldAfterEveryStep` | pass | 32 playouts (seeds 21–24), 658 steps, 690 invariant checks | After `Create` and after every step: `Allies[0]` is the player; per creature (player, monsters, allies) HP in `[0, MaxHp]`, block ≥ 0, a unique id below `NextCreatureId`, every power owned by that creature object and held once; energy, stars, gold ≥ 0; potions ≤ slots and owned by the player; relics owned by the player; card conservation: no card object in two places among the five piles and the deck, unique instance ids in the piles and in the deck, every pile id below `NextCardInstanceId`, a pile card owned by the player when it has an owner, and a card that left every pile never returns; `NextCreatureId`, `NextCardInstanceId` and the counter of every one of the 12 run RNG streams never decrease. |
| `Test_W1Gate_F5_LedgerRejectsBrokenStates` | pass | 10 violations | The F5 ledger rejects, for the expected reason, a card in two piles, a card that returns after leaving, a deck card also in a pile, HP above max, negative energy, negative gold, a duplicate creature id, a power held by a creature it is not attached to, a rewound RNG counter and a relic of another player. |
| `Test_W1Gate_B2Chain_StepsAndRecordsDeferredListenerPromptInline` | **fail** | 0 playouts | Content absent: `DARK_EMBRACE_POWER` (power) or `GREMLIN_HORN` (relic), `HELLRAISER_POWER` (power), `SEEKER_STRIKE` (card). |
| `Test_W1Gate_PilotContentIsRegistered` | **fail** | 23 missing | B5 pilots absent: relics (0 registered, 6 needed; `GAMBLING_CHIP`, `PAPER_PHROG`, `PAPER_KRANE`), potions (0, 4 needed; `FAIRY_IN_A_BOTTLE`, `ATTACK_POTION`, `ASHWATER`), the six prompt cards, the five pilot powers, an enchantment, an affliction, five orbs, `OSTY`. |

What F1, F4 and F5 do not cover yet: with 0 relics, 0 potions, 0 orbs and 0 prompt cards registered,
the slots hold only the inert doubles (they reach clone and canonical but no hook: the P0 listener
snapshot visits powers only, `Combat/Pipeline/ListenerSnapshot.cs`), `LegalActions` lists no potion or
prompt action (the B1 `LegalActionGenerator` and `CombatSim` work of the choice track is absent), and
no prompt is ever exposed. The pilot clauses of F1 and F4 are therefore unevaluated; they apply
automatically once the pilots are registered, because the suite selects content by id.

B2 chain scenario (runs once the content is registered): deck 6 `SEEKER_STRIKE` + 4 `ASCENDERS_BANE`,
`NIBBITS_WEAK`, seeds 31–38; at the first decision `PowerCmd.Apply` gives the player
`DARK_EMBRACE_POWER` 1 (when registered) and `HELLRAISER_POWER` 1, and `GREMLIN_HORN` is added to the
relics when registered; random legal play (prompt answers included) for up to 1 000 steps per seed
must not throw, and at least one seed must record `FidelityFlag.DeferredListenerPromptInline` on
`State.Run.Fidelity`. Path: at the player's turn end the ethereal Ascender's Bane is exhausted
(`causedByEthereal`, sim `CombatLoop.DoTurnEnd`) and DarkEmbrace counts it
(`Models.Powers/DarkEmbracePower.cs:64-78`); `AfterSideTurnEnd` draws `Amount × etherealCount`
(`:141-157`) inside the per-listener `HookPlayerChoiceContext` (`Hooks/Hook.cs:6170`); Hellraiser
auto-plays every drawn Strike-tagged card (`Models.Powers/HellraiserPower.cs:69`, `:93`); Seeker Strike
(Strike tag `Models.Cards/SeekerStrike.cs:178`) opens `FromCombatPile` on the draw pile (`:140-141`),
a prompt inside a per-listener context (D2). GremlinHorn's path is `AfterDeath` (`Hooks/Hook.cs:2364`)
→ `CardPileCmd.Draw` (`Models.Relics/GremlinHorn.cs:91`, `:130`).

Seams to revisit when the tracks land:

| # | Seam | Why |
| --- | --- | --- |
| 1 | `EquipPlayer` adds relics with `AttachTo` + `Relics.Add` and fills `PotionSlots` directly | No `RelicCmd` / `PotionCmd` exists yet; if the hooks track caches the listener array behind a mutation version counter (B3), equip through `RelicCmd.Obtain` / `PotionCmd.TryToProcure` (or bump the counter) so the relics are visited. Relics are equipped after `Create`, so their turn-1 hooks (`GamblingChip`) are not exercised by F1. |
| 2 | B2 applies the powers with `PowerCmd.Apply` at the first decision, outside a `Step` | Under the B1 tape engine this state becomes `S0` of the next step; keep the call if the pipeline facade stays. |
| 3 | F4 does not offer `Confirm` / `Cancel` / `DiscardPotion` with stray indices as candidates | B1 does not say whether the unused indices are validated. |
| 4 | F5's "a card that left every pile never returns" | Holds for every v0.111.0 path read so far (`SwipePower` returns the stolen card's deck version through the run layer, `Models.Powers/SwipePower.cs:131-139`, not the combat copy); a port that needs otherwise must cite the decompile and relax the rule in this file. |

### 8.4 Bench (`dotnet run -c Release --project sim/Sts2Sim.Bench`, `NIBBITS_WEAK`, 1 thread)

| Run | decisions/s | `Clone()` | `StateHash()` |
| --- | --- | --- | --- |
| 1 | 664 117 | 2.652 µs | 1.107 µs |
| 2 | 671 595 | 2.739 µs | 1.133 µs |
| 3 | 667 858 | 2.623 µs | 1.192 µs |
| Median | 667 858 | 2.652 µs | 1.133 µs |

Part B gates: ≥ 300 decisions/s/core passes (about 2 200×); `Clone()` ≤ 1 ms passes (about 380×).
Against the recorded P0 figures (705 048 decisions/s, 2.53 µs clone, 0.87 µs hash; `sim/BENCH.md`):
throughput 0.947×, clone 1.048×, hash 1.30×.

Same-session control, as in §7.4: the P0 tree (`819d7c6`, `git archive` of `sim/Sts2Sim.Core` and
`sim/Sts2Sim.Bench`) and this tree, Release, interleaved P0, W1, P0, W1, P0, W1:

| Tree | decisions/s (3 runs → median) | `Clone()` median (range) | `StateHash()` median (range) |
| --- | --- | --- | --- |
| P0 `819d7c6` | 673 354 / 662 310 / 670 718 → 670 718 | 1.841 µs (1.791–1.889) | 1.006 µs (0.992–1.010) |
| W1 round 2 | 674 732 / 680 741 / 677 960 → 677 960 | 2.634 µs (2.627–2.711) | 1.156 µs (1.098–1.184) |
| W1 / P0 | 1.01× | 1.43× | 1.15× |

Where the regression comes from: `sim/Sts2Sim.Core`, `sim/Sts2Sim.Bench` and `sim/Sts2Sim.Diff` are
byte-identical to round 1 (`git diff 94424e5 HEAD` is empty for them), so the per-call allocation and
canonical-length figures of §7.4 still hold (`Clone()` 6 640 B against P0's 5 384 B, canonical
2 368 B against 2 042 B). The extra clone cost comes from the phase-0 state additions only: 18 more
objects per clone (`RunState.CloneWithoutCombat`: the `RunState`, the player list and its array, the
modifier list, `ChoiceRuntime` and its selector list, `FidelityLog` and its entry list; the player
copy: relic list, potion-slot array, deck list, `OrbQueue` and its orb list, pet-id list; the ally list
and its array; `CombatHistory`, whose 17 list fields make it about 160 B; `PlayersTakingExtraTurn`),
plus about 40 B more in each card copy for the B2 `CardInstance` fields (owner, enchantment,
affliction, listener, `HasBeenRemovedFromState`, `DeckVersionId`), which together make the +1 256 B.
Clone time tracks the object count more than the bytes, so it rises more than the +23 % in bytes:
1.43× in this session and 1.10× in round 1's session; the P0 tree's own clone time moved between 1.79
and 2.78 µs across the two sessions while this tree stayed between 2.6 and 2.9 µs. The hash covers 326 more bytes (+16 %), 1.15×
at equal load. Throughput is flat (1.01×) because a decision does not clone: the step path gained only
constant-cost checks (§7.4) and the larger `Create` is amortised over about 20 decisions per episode.
The gap to the recorded P0 throughput is machine load: the unchanged P0 tree measured 0.95× of its
recorded throughput in this session. No track content (relic and potion listeners, history appends,
hook masks) is measured yet; re-bench after the tracks merge.

### 8.5 Residual issues

| # | Issue | Next step |
| --- | --- | --- |
| 1 | The merge list again carried the `origin/main` tip nine times instead of the nine W1 track commits (choice, hooks, state, pipeline, cards-kernel, loop, relics-potions, character-mechanics, monsters-kernel). No commit descending from `94424e5` other than the integrate commits exists in the object store, so the tracks either never committed or reported their base. | Re-collect nine track SHAs that descend from `94424e5` (check `git merge-base --is-ancestor 94424e5 <sha>` before dispatching) and run round 3. |
| 2 | W1 gates not met: B2 chain and pilot coverage fail (content absent); F1, F4 and F5 pass on P0 content and inert doubles only, with their pilot and prompt clauses unevaluated (§8.3). | Round 3, after the tracks merge. |
| 3 | 137 `throw new NotImplementedException` scaffold sites remain in 15 files of `Sts2Sim.Core` (73 in `Combat/Pipeline/Hook.cs`, 22 in `State/CombatHistory.cs`, 8 in `Orbs/OrbQueue.cs`, 7 in `Choice/CardSelectCmd.cs`, 4 in `Cards/Enchantments/EnchantmentModel.cs`, 3 each in `State/CombatState.cs`, `Potions/PotionModel.cs`, `Combat/Loop/PotionActions.cs`, `Choice/ChoiceRuntime.cs`, `Cards/Afflictions/AfflictionModel.cs`, 2 each in `Relics/RelicModel.cs`, `Random/RngSets.cs`, `Modifiers/ModifierModel.cs`, 1 each in `Orbs/OrbModel.cs`, `Cards/CardInstance.cs`). | The tracks. |
| 4 | The bench covers the scaffold only (§8.4). | Re-run after round 3. |
| 5 | The REC track (`4094c1e`) is still compiled and tested but not installed. | `scripts/install_mod.sh` when the recorder change is to go live. |
| 6 | Gate-suite seams §8.3 rows 1–4. | Round-3 integrator. |

## 9. W1 integrate, round 3 (2026-11-01)

Owner: the W1 integrate step (CONTRACTS Part B §B11, §B12). Branch `claude/sim-source-recreation-9392df`, start
`263810d` (phase-0b amendments). `git status --short` was empty at the start. .NET SDK 9.0.117, runtime 9.0.16, Apple
M4 Pro (Arm64, 12 cores); other agents were running (load average 3.4 to 9).

### 9.1 Merges

All nine track commits descend from `263810d` (`git merge-base --is-ancestor`), so none was skipped. Each was merged
with `git merge --no-ff --no-edit` in the dependency order of the brief.

| Track | Commit (commits above the base) | Merge commit | Textual conflicts |
| --- | --- | --- | --- |
| hooks | `152006c` (1) | `401d6d1` | none |
| state | `0ba84d7` (1) | `f906788` | none |
| cards-kernel | `cbf3459` (1) | `f34f151` | none |
| choice | `bb52894` (1) | `d180da4` | none |
| pipeline | `9e4bc7b` (3) | `7fc82f6` | none |
| monsters-kernel | `6b7ecd6` (1) | `75150c3` | none |
| character-mechanics | `76378e2` (1) | `b16a682` | none |
| relics-potions | `1667af4` (2) | `5e44bec` | none |
| loop | `7545932` (1) | `674ee65` | none |

Every track touched only its B11 files (checked with `git diff --stat 263810d <sha>`), so git reported no conflict.
One build-level conflict surfaced instead: the type `CardPileAddResult` was defined by cards-kernel
(`Cards/CardPileAddResult.cs`) and by the loop (`Combat/Loop/CardPileCmd.cs:3`), see §9.2. The merge commits carry
git's default messages; every fix of this section is in the commit `sim: W1 integrate round 3`, so the merge commits
after cards-kernel, relics-potions and loop do not build on their own.

### 9.2 Build after each merge (`dotnet build sim/Sts2Sim.sln`)

| After | Result | Smallest fix |
| --- | --- | --- |
| hooks, state, choice, pipeline, monsters-kernel, character-mechanics | 0 warnings, 0 errors | none |
| cards-kernel | 2 × CS8602, `Combat/Pipeline/ListenerSnapshot.cs:400`, `:405` (hooks) | cards-kernel made `AfflictionModel.Card` and `EnchantmentModel.Card` nullable (B12 A23: null where the game's getter is); the two liveness checks pattern-match `Card is { } card`, the same test as `HasCard && Card…` (`Combat/CombatState.cs:1051-1053`) |
| relics-potions | 2 × CS0200 in tests: `HookTests.cs:842` sets `PotionModel.HasBeenRemovedFromState`, `StateKernelTests.cs:1044` sets `RelicModel.StackCount` | relics-potions made both setters private as in the game; `HookTests` calls `q1.RemoveBeforeUse()` (the same two writes, `Models/PotionModel.cs:595-599`), the state probe calls `IncrementStackCount()` on its test relic, which now overrides `IsStackable` |
| loop | 6 × CS0104, `CardPileAddResult` ambiguous between `Sts2Sim.Core.Cards` and `Sts2Sim.Core.Combat.Loop` | one type, cards-kernel's (`Entities.Cards/CardPileAddResult.cs:6-17`: `oldPile` is a nullable `CardPile`); the loop's copy is deleted, `CardPileCmd.AddCore` stores `null` for "no old pile" and passes `OldPile ?? PileType.None` to `AfterCardChangedPiles` as the game does (`Commands/CardPileCmd.cs:282-314`, `:440-444`) |

Final: `dotnet build sim/Sts2Sim.sln` 0 warnings, 0 errors (Debug incremental and `--no-incremental`, Release).

### 9.3 Tests (`dotnet run --project sim/Sts2Sim.Tests`)

First run after the nine merges: 691 passed, 30 failed, 721 total, `PENDING total: 5`. Final: **721 passed, 0 failed,
721 total, `PENDING total: 0`** in Debug and in Release (`-c Release`). The base had 374 tests (the Part A 367, the six
gate tests, `W1Pending`'s self-test); the tracks added 347 (Choice 31, Hook 34, StateKernel 43, CardKernel 45,
TurnFlow 22, RelicPotion 47, CharacterMechanics 60, MonsterKernel 28, Pipeline +37). No test was skipped, and no
gate test was weakened.

| Failure (count) | Cause | Fix | Citation |
| --- | --- | --- | --- |
| 4 PENDING (two Hellraiser tests, AttackPotion, GamblingChip); `W1Gate_B2Chain` and `W1Gate_F1` failed on the same stubs | the forwarding seams of pipeline and relics-potions still threw; their targets exist now | `Powers/CardCmdForwarders.cs` → `CardCmd.AutoPlay`, `CardCmd.Discard`, `CardCmd.TransformToRandom`; `Relics/PendingKernelCalls.cs` → `CardCmd.Upgrade`, `CardCmd.DiscardAndDraw`, `CardFactory.GetDistinctForCombat(cs, player, CardPools.ForCharacter(player.CharacterId).GetUnlockedCards(cs.Run.Unlocks, CardPools.MultiplayerConstraintFor(cs.Run)).Where(c => c.Type == type), count, rng)` | `Models.Potions/AttackPotion.cs:71`, `Models.Relics/Whetstone.cs`, `GamblingChip.cs` |
| PENDING `MonsterKernel_KillDuringItsOwnMoveRunsBeforeRemovedFromRoom` | `CreatureCmd.Kill` never ran `CombatManager.RemoveCreature` | `MonsterLifecycle.RemoveCreature(cs, monster)` (`BeforeRemovedFromRoom`, `ResetStateMachine`) for every enemy that `Kill` removes, before the `IsPerformingMove`-gated list removal, and in `Escape` before the list removal | `Commands/CreatureCmd.cs:2812-2822`, `:3936`; `Combat/CombatManager.cs:7171-7180` |
| 10 StateKernel tests: `PushSelector` throws | choice requires an `IChoiceSelector` with a stable id, and a selector with canonical state must be an `IStatefulChoiceSelector` so clones never share it (B12 A14) | the test double `KernelSelector` implements `IStatefulChoiceSelector` (`SelectorId`, `DeepClone`); its label stays in its canonical form | B12 A14 |
| 4 StateKernel tests: "the clone shares mutable objects: StatelessListener" | hooks added `AbstractModel._overriddenHooks`, a per-type `HookMask` cache (copied by `MemberwiseClone`, not hashed); the reflective inspector counts any non-readonly field as state | the inspector skips that field (declared on `AbstractModel`, type `HookMask`) | hooks.md §9.1 |
| 6 RelicPotion tests, `W1Gate_F4`, `W1Gate_F5`: `InvalidCastException` `DynamicVar` → `CardsVar` / `EnergyVar` | four relics built `new DynamicVar(CardsName, …)` because the typed vars did not exist at the base; cards-kernel's `DynamicVarSet.Cards` and `.Energy` cast like the game's | the relics use `CardsVar` / `EnergyVar` as the game does | `Models.Relics/Nunchaku.cs:183-184`, `HappyFlower.cs:188`, `Whetstone.cs:20`, `CentennialPuzzle.cs:111` |
| `RelicPotion_RelicsAndPotionSlotsAreCombatListenersInGameOrder` | the test expected the powers of an inactive player to stay listed | test expects only the monster's power: the list stops after the inactive player's powers and those powers are not live at yield | `Combat/CombatState.cs:870-873`, `:1061` |
| `RelicPotion_FairyInABottlePreventsTheOwnersDeathThroughCreatureCmd` | the test read `Monsters[0]` after killing it; pipeline's `Kill` removes dead enemies | the test keeps the monster reference | `Commands/CreatureCmd.cs:2812-2822` |
| `RelicPotion_AttackPotionOffersThreeAttacksAndMakesThePickFree` | the test hard-coded three offered attacks; of the generatable Ironclad attacks only `HEADBUTT` is ported, and `GetDistinctForCombat` draws over the ported options and records `ApproximatedRule` | the test expects `min(3, ported)` options, clips its pick, asserts the flag whenever options were dropped, and asserts the `CombatCardGeneration` draw only with two or more options (a one-element `UnstableShuffle` draws nothing) | cards-kernel.md §7.4; relics-potions.md §11 item 14 |
| `CharacterMechanics_SummonCreatesOstyThroughAddPetAndDieForYou`, `TurnFlow_AddPetRegistersThePetOnItsOwner`: "already in this combat" | the loop's `PlayerCmd.AddPet(cs, owner, def)` called `MonsterSpawner.SpawnPet`, which already adds the pet, then `AddPet(cs, pet, owner)`, which adds it again | `MonsterSpawner.CreateCreature(cs, def, owner.Side, null)`, then `AddPet(cs, pet, owner)` = `AddPetInternal` + `CreatureCmd.Add`, the game's sequence | `Commands/PlayerCmd.cs:946-967`, `:123-124` |
| `Hooks_AfflictionListenerFollowsItsCard`, `Hooks_ModifyBlockAppliesTheEnchantmentPrePassFirst` | the tests made an unbound affliction or enchantment by assigning the card slot; cards-kernel's slot setters now bind the model | the tests unbind with `ClearInternal()` right after the assignment, the state they meant | `Models/AfflictionModel.cs:252-256`, `Models/EnchantmentModel.cs:350-354` |
| `Cards_UpgradedDeckReplaysDeterministicallyThroughReshuffles` | asserted after every step, the victory included, that the combat piles hold every card; A18 clears them at victory | at a victory the test asserts empty piles and a full deck; every other step is unchanged | `Entities.Players/Player.cs:1249-1254`; loop.md §12.16 |
| `Choice_TurnSetupFirstPauseStepsRunOnceBeforeTheAnswer` | the probe's TurnSetup frame now sits above the Inline frame that `ActionExecutor.PlayCard` pushes for the play action (B1) | the steps run at depth 1 (the runtime truncates the stack to the frames below the TurnSetup frame, choice.md §3.3) and the depth after the answer is 2 | B1 context table |
| `Choice_TurnSetupStepsThatEndTheCombatUnwindToTheTerminalState` | asserted five hand cards at the victory; A18 clears the piles | the test asserts empty piles; the unwind itself stays covered by the absent prompt, the equal fast-path hash and the direct `ChoiceUnwindException` | B12 A18 |
| `EndToEnd_CanonicalHashesArePinnedAcrossProcesses` | opening pin | `0x79C886C6235C2D9B` → `0x07F5FF6679103FF9`; the final pin `0x77FA4D8665BA0CC3` is unchanged (below) | |
| `RelicPotion_StackCountAndRemovalFlagsChangeOnlyThroughThePortedMutators` (after the relic CR of §9.8) | it pinned the interim `internal set` | asserts the game's `private set` | `Models/RelicModel.cs:420-427` |

Pin attribution. Seed-1 hashes of the base plus one track each (scratch copies, Release):

| Tree | Opening | Final |
| --- | --- | --- |
| base `263810d`; + hooks, state, cards-kernel, choice, character-mechanics or relics-potions | `0xCA1423508C1168E3` | `0xD7B055DC9B9E326E` |
| + pipeline | unchanged | `0xB899C4BCA7235886` (history entries, pipeline.md §11) |
| + monsters-kernel | `0x870EF2BE2AE3F04D` (the monster listener's presence byte) | `0xF07E158ED5F0B079` (`MonsterPerformedMove`) |
| + loop | `0x79C886C6235C2D9B` (deck A13, history B6) | `0x77FA4D8665BA0CC3` (A18) |
| merged | `0x07F5FF6679103FF9` | `0x77FA4D8665BA0CC3` |

The merged opening is the loop's form plus the monsters' listener bytes. The merged final equals the loop's because at
the victory A18 has cleared the history and the combat piles and the dead monsters have left the list, so neither the
pipeline's entries nor the listener bytes reach that state. Both values were reproduced in three processes (Debug
twice, Release once). The trajectory is unchanged: the EndToEnd reference-model tests and the victory snapshot pass,
and the diff gate agrees on every decision.

### 9.4 Gate-suite seams of §8.3 and the A17 overloads

| Row | Round 3 |
| --- | --- |
| 1 | `EquipPlayer` equips through `RelicCmd.Obtain(rs, relic, player)` and `PotionCmd.TryToProcure(rs, potion, player, slot)` (success asserted); the B2 chain's `GREMLIN_HORN` branch uses `RelicCmd.Obtain` as well. `AfterObtained` now runs (Whetstone upgrades two deck attacks on `Niche`) and so do `ShouldProcurePotion` / `AfterPotionProcured`. Relics are still equipped after `Create`, so their turn-1 hooks (GamblingChip) stay unexercised by F1 |
| 2 | unchanged: the B2 chain applies its powers with `PowerCmd.Apply` at the first decision, outside a `Step` |
| 3 | F4 candidates add `DiscardPotion(slot)` with a stray `TargetIndex`, `Pick(0)` with a stray `TargetIndex`, and `Confirm` / `Cancel` with each stray index, the cases choice.md §6.2-§6.3 validates; a legal unlisted candidate must now be a `Pick` with `TargetIndex == NoIndex` (B1 lists only ascending picks at unordered prompts). All the new candidates are rejected |
| 4 | unchanged: no v0.111.0 path read so far returns a card that left every pile |

The scaffold's live-list overloads `Hook.BeforeSideTurnStart / AfterSideTurnStart / BeforeSideTurnEnd /
AfterSideTurnEnd(cs, side)` are kept. Core has no caller left (the loop passes the `MonsterLifecycle` snapshots, A17),
but `PipelineTests.cs` (13 calls) and `WurmBeetleTests.cs` (7 calls) still use them, and A17 deletes only overloads
without a caller. Moving those 20 calls to the three-argument overloads (`cs.Allies` / `cs.Monsters`, today's
behaviour exactly) lets the next integrator delete them.

### 9.5 W1 gates (CONTRACTS §B11)

| Gate | Result | Numbers |
| --- | --- | --- |
| Build | pass | 0 warnings, 0 errors (Debug, `--no-incremental`, Release) |
| Suite | pass | 721 passed, 0 failed; `PENDING total: 0`; Release the same |
| Diff harness (`Sts2Sim.Diff -c Release`, P0 bundle set, v0.107.1) | pass | bundles 4 (agree 4, hard divergence 0, skipped 0, error 0, sim exception 0); decisions 69/69 (100.00 %), 0 unobserved; outcome 4/4; soft diffs 0 |
| F1 clone/hash equality | pass | 32 playouts (8 encounters × 4 seeds, 0 capped), 708 steps, 183 clone checks, 337 clone steps, 431 lockstep steps, 160 hash-coverage probes, 116 prompt answers, 63 potion actions; per playout 7 pilot relics + the counter double, 64 pilot potions in all |
| F4 legal-action closure | pass | 24 playouts, 663 decision states, 142 prompt states, 3 854 listed actions stepped on clones, 60 562 candidates checked, 2 652 illegal actions stepped (all threw, hash and bytes unchanged), 48 potion actions |
| F5 invariants | pass | 32 playouts, 780 steps, 812 invariant checks, 155 prompt answers, 64 potion actions; the ledger still rejects its 10 broken states |
| B2 chain | pass | 8 playouts on `NIBBITS_WEAK` (`DARK_EMBRACE_POWER` + `HELLRAISER_POWER`, deck 6 `SEEKER_STRIKE` + 4 `ASCENDERS_BANE`), 44 steps, 19 prompt answers, 5 playouts recorded `DeferredListenerPromptInline` |
| Pilot coverage | pass | relics 7, potions 6, the six prompt cards, the pilot powers, orbs 5, enchantment `GLAM`, affliction `HEXED`, monster `OSTY` |
| Bench | pass | §9.7 |

The pilot clauses that rounds 1–2 could not evaluate (§8.3) now run: F1 and F5 step real relic and potion listeners,
F4 sees 142 exposed prompts and 48 potion actions, and the B2 chain reaches the per-listener prompt inline.

### 9.6 Cross-area edits (outside the integration files)

| File (owner) | Edit | Reason |
| --- | --- | --- |
| `Combat/Pipeline/ListenerSnapshot.cs` (hooks) | affliction and enchantment liveness use `Card is { } card` | CS8602 after cards-kernel's nullable `Card` (A23); same rule (`Combat/CombatState.cs:1051-1053`) |
| `Combat/Loop/CardPileCmd.cs` (loop) | the duplicate `CardPileAddResult` is deleted; `AddCore` stores `null` for no old pile | one result type, cards-kernel's (§9.2) |
| `Combat/Loop/PlayerCmd.cs` (loop) | `AddPet(cs, owner, def)` creates the creature with `MonsterSpawner.CreateCreature` instead of `SpawnPet` | the pet was added twice (§9.3) |
| `Combat/Pipeline/CreatureCmd.cs` (pipeline) | `MonsterLifecycle.RemoveCreature` in `Kill` and `Escape` | wiring requested by pipeline and monsters-kernel (§9.3) |
| `Powers/CardCmdForwarders.cs` (pipeline) | three forwarders call `CardCmd` | the seam's targets exist |
| `Relics/PendingKernelCalls.cs` (relics-potions) | three seams call `CardCmd.Upgrade`, `CardCmd.DiscardAndDraw`, `CardFactory.GetDistinctForCombat` | the seam's targets exist |
| `Relics/Defs/{Nunchaku,HappyFlower,Whetstone,CentennialPuzzle}.cs` (relics-potions) | `CardsVar` / `EnergyVar` | cards-kernel's typed accessors cast (§9.3) |
| `Relics/RelicModel.cs` (relics-potions) | `HasBeenRemovedFromState` setter `internal` → `private` | relics-potions CR, once state calls `RemoveInternal` |
| `State/CreatureState.cs` (state) | `RemoveRelicInternal` calls `relic.RemoveInternal()` | relics-potions CR; `Entities.Players/Player.cs:913` |
| `Cards/Defs/SovereignBlade.cs` (cards-kernel) | also implements `ISovereignBladeDef` (its `AddDamage(card, amount)` already matches) | `ForgeCmd` looked for `ISovereignBladeDef`, the blade implemented only `IForgeBladeDef`, so every forge of the real blade took the flagged fallback and skipped `CurrentDamage` (`Models.Cards/SovereignBlade.cs:359-364`) |
| `Tests/HookTests.cs` (hooks) | `RemoveBeforeUse`; two `ClearInternal` calls; `BindAffliction` calls `CardInstance.AfflictInternal` instead of reflection | §9.2, §9.3; hooks CR 1 |
| `Tests/StateKernelTests.cs` (state) | relic stack probe; `KernelSelector` as `IStatefulChoiceSelector`; the inspector skips the `HookMask` cache | §9.2, §9.3 |
| `Tests/RelicPotionTests.cs` (relics-potions) | inactive-player listener list; FairyInABottle monster reference; AttackPotion offer count; setter visibility | §9.3 |
| `Tests/CardUpgradeTests.cs` (cards, P0) | the victory step asserts empty piles and a full deck | A18 |
| `Tests/ChoiceTests.cs` (choice) | TurnSetup depths; A18 piles | §9.3 |
| `Tests/EndToEndTests.cs` (loop) | opening pin | §9.3 |
| `Tests/CharacterMechanicsTests.cs` (character-mechanics) | one added assertion: a forge of the registered `SOVEREIGN_BLADE` records no fallback flag | checks the `SovereignBlade` wiring |

`Tests/W1GateTests.cs` (the seams of §9.4) and this file are the integrate step's own.

### 9.7 Bench (`dotnet run -c Release --project sim/Sts2Sim.Bench`, `NIBBITS_WEAK`, 1 thread)

The machine alternated between two speed states for every tree (the same binary measured about 150k and about 60–80k
decisions/s in consecutive runs; the P0 tree 667k and 213k), so only same-session ratios rank anything.

Runs of this tree (5 s windows):

| Run | decisions/s | `Clone()` | `StateHash()` |
| --- | --- | --- | --- |
| 1 | 147 982 | 2.201 µs | 2.479 µs |
| 2 | 135 696 | 2.138 µs | 2.498 µs |
| 3 | 143 311 | 2.273 µs | 2.578 µs |
| 4 | 75 419 | 2.162 µs | 6.955 µs |
| 5 | 68 788 | 5.476 µs | 4.057 µs |
| Median | 135 696 | 2.201 µs | 2.578 µs |

Part B gates: ≥ 300 decisions/s/core passes (the slowest run of the session, 55 446, is about 185×); `Clone()` ≤ 1 ms
passes (the slowest clone of the session, 10.9 µs, is about 90×). Against the recorded P0 figures (705 048
decisions/s, 2.53 µs clone, 0.87 µs hash; `sim/BENCH.md`): throughput 0.19×, clone 0.87×, hash 2.96× (median runs).

Same session, equal load. Three ways, all with the P0 tree (`819d7c6`) and the base (`263810d`) exported with
`git archive` and built in Release:

| Comparison | decisions/s merged / P0 | merged / base | `Clone()` merged / P0 | `StateHash()` merged / P0 |
| --- | --- | --- | --- | --- |
| interleaved, 11 runs per tree, medians (P0 314 391, base 328 960, merged 66 592 decisions/s) | 0.21× | 0.20× | 1.79× (5.848 / 3.275 µs) | 1.93× (4.016 / 2.076 µs) |
| side by side (the three processes at once), 4 rounds, median of the per-round ratios | 0.25× (0.18–0.25) | 0.24× (0.19–0.25) | 1.34× (0.98–2.17) | 2.10× (1.54–2.64) |

Deterministic measures (a scratch harness with the bench's seeds and policy, `GC.GetAllocatedBytesForCurrentThread`,
2 000 episodes; every tree plays the same 40 299 decisions with an equal trajectory digest, so the W1 port changes no
outcome for P0 content):

| Measure | P0 | base | merged | choice alone | loop alone |
| --- | --- | --- | --- | --- | --- |
| allocated bytes per decision, `Step(a)` | 935 | 1 008 | 17 165 (3 373 on the fast path) | 7 732 | 2 232 |
| allocated bytes per `Clone()` | 5 384 | 6 648 | 12 752 | 6 656 | 10 896 |
| canonical bytes, the bench's mid-fight state | 2 042 | 2 373 | 3 792 | 2 373 | 5 102 |

Fast path: in one process, alternating 1.5 s windows (6 each), `Step(a)` gave a median of 147 554 decisions/s and
`Step(a, answerer)` 194 041 (1.32×; best runs 149 892 and 238 153, 1.59×).

Where the regression comes from. Each track was benched alone on the base in the same 11 interleaved runs (median
decisions/s, ratio to the base): hooks 192 028 (0.58), choice 169 040 (0.51), loop 216 265 (0.66), cards-kernel
252 359 (0.77), pipeline 291 933 (0.89), relics-potions 294 774 (0.90), monsters-kernel 304 858 (0.93),
character-mechanics 314 149 (0.95), state 316 480 (0.96). All the cost is contract-required work:

1. B1 tape engine (choice, choice.md §5.1): `Step(a)` takes a full `DeepClone` of the state before every action as the
   pre-step backup. On the merged state that copy allocates 12.8 KB, most of the 17.2 KB per decision, and it is 25–37 %
   of the merged decision: the fast path, which skips it, is 1.32–1.59× faster.
2. B3 listener walk (hooks, hooks.md §9.1): every dispatch now visits every slot the game visits (relics, potion
   slots, orbs, each card of the five piles with its affliction and enchantment slots, monster listeners, modifiers),
   where P0 visited powers only (56 passes per decision, counted on P0 content).
3. B6 turn flow (loop, loop.md §12.19): the auto phases and per-turn dispatchers, `AfterCardChangedPiles` after every
   pile move, the history appends, and the run deck in clone and canonical form (A13; with its history entries the
   loop alone adds 4.2 KB per clone).
4. B7 keywords (cards-kernel, cards-kernel.md §15): every in-combat keyword query runs `Hook.ModifyKeywordsInCombat`,
   about seven times per decision.
5. History entries (pipeline, monsters-kernel; D11) and the relic and potion slots (relics-potions).

The parts compound: the per-step clone copies the deck and the history the other tracks add, and each keyword query
pays the full listener walk. In time units the per-track deltas add up to 8.8 µs over the base's 3.0 µs per decision
(about 0.26×), and the merged tree measures 15.0 µs (0.20×) in the same runs. The clone grows less than its
allocation (2.37× the P0 bytes, 1.3–1.8× the P0 time at equal load): the history clones as 19 plain list copies with
no per-entry work (state.md W1.6.6). The canonical form is 1.86× the P0 bytes and the hash takes about 2× the time;
the form stays below the loop track's own 5 102 bytes because the merged history writes a per-type digest chain
instead of its entries (state.md W1.6.7).

Next steps (none needed for the W1 gates; listed in §9.9): H3, a listener array cached behind structural version
counters (A20); C9, skipping the keyword snapshot when no live listener overrides it; P8, sharing history between
clones; `ListenerSnapshot.InlineCapacity` retuned with W1 content; rollouts on the fast path.

### 9.8 Contract change requests of the nine owners, with dispositions

Dispositions: **resolved** (done in this round, or already satisfied by the merged code; the evidence is named),
**needs contract change** (the code is consistent; the text of CONTRACTS, a design note or a later wave must record or
decide it), **open** (a real follow-up for the named owner, listed again in §9.9), **rejected** (with the reason).

| # | From → to | Request (short) | Disposition |
| --- | --- | --- | --- |
| H1 | hooks → cards-kernel | port `AfflictInternal` / `EnchantInternal` onto `CardInstance`; switch `HookTests.BindAffliction` from reflection | resolved: both exist (`Cards/CardInstance.cs:576-593`); `BindAffliction` now calls `AfflictInternal` |
| H2 | hooks → design note owner | `design-combat-kernel.md` §2.2: only `AfterSideTurnEnd` joins after each pass; `BeforeFlush`, `BeforeSideTurnEnd` once after all passes; `AfterDeath`, `AfterDiedToDoom`, `BeforeSideTurnStart` never | needs contract change (doc fix; the code follows `Hooks/Hook.cs` and hooks.md §4.5) |
| H3 | hooks → state, cards, relics, orbs, monsters | optional structural version counters for an O(1)-validated cached listener array | open (performance, §9.7; A20 allows the cache only with full invalidation) |
| H4 | hooks → integrate | the A17 deletion breaks `PipelineTests` (13 calls) and `WurmBeetleTests` (7 calls) | resolved: the overloads are kept until those callers move (§9.4) |
| S1 | state → pipeline | A19: `CombatStatus` attachment and side queries count `cs.Allies`; `Kill` drops a dead pet from `PetIds` right after the `ShouldDie` gate | resolved: `CombatStatus.IsAttached` / `GetCreaturesOnSide` already walk `cs.Allies` then `cs.Monsters`; `KillWithoutCheckingWinCondition` calls its `InvokeDiedEvent` right after `ShouldDie` (`Commands/CreatureCmd.cs:2770`), with the `PlayerCombatState.OnPetDied` rule (`Entities.Players/PlayerCombatState.cs:433-445`); delegating to `cs.ContainsCreature` is cleanup (§9.9) |
| S2 | state → loop | `ResetCombatState` at setup; `CombatStateAfterCombatEnd` at A18; `AddPet` = `AddPetInternal` + `CreatureCmd.Add`; `CardPlay` fields stable between the two history appends | resolved: `CombatLoop.ResetCombatState` and `PlayerAfterCombatEnd` have the same effects as the state members (piles, energy, stars, pets, orb queue to `BaseOrbSlotCount`; powers, piles, pets, block); `AddPet` fixed here; `CardPlay` is written only in its object initializer (`CardCmd.Play.cs` `PlayLoop`) |
| S3 | state → cards-kernel | `CardInstance.WriteCanonical` should write a stateful listener's form; `CardInstance.DeepClone` keeps the owner | resolved: `WriteCanonical` writes presence, type name and any `ICanonical` listener state (`Cards/CardInstance.cs:776-786`); `CardPile.DeepClone` refuses owned cards and the state clones through `CloneFor(newOwner)` |
| S4 | state → choice | `ChoiceRuntime.DeepClone` shared selector objects | resolved: `IStatefulChoiceSelector.DeepClone` (B12 A14); the state test double now uses it |
| S5 | state → CONTRACTS | per-history side stores, the new snapshot fields, `HistoryCardPlay` equality by `PlayId`, the history canonical form | needs contract change (B2 / A22 text) |
| S6 | state → W2 | `RunState.Combat` rejects a combat of another run | needs contract change (W2 note) |
| C1 | cards-kernel → CONTRACTS | `CanAfflict(cs, card)`, `PickRandomTargets(cs, rng, cards, count)` (the game reads the combat keywords, `Models/AfflictionModel.cs:206`) | needs contract change (no other caller) |
| C2 | cards-kernel → loop | `ACROBATICS` should reach `CardCmd.Discard` | resolved: `CardPileCmd.Discard` forwards to `CardCmd.Discard` (`Combat/Loop/CardPileCmd.cs:346`) |
| C3 | cards-kernel → loop | deck via `AttachTo`, copies via `CloneForCombat`, `cs.CreateCard`, combat keywords at the play and turn-end sites, Retain/Sly at the flush, card → enchantment → affliction `OnPlay`, `ShouldStartAtBottomOfDrawPile` on turn 1, X capture in `AutoPlay`, pins | resolved: `CombatLoop.BuildDeck` / `PopulateCombatState`, `CardPileCmd.HasKeywordInCombat` / `IsSlyThisTurn` / `ShouldRetainThisTurn` (the same rule as `CardInstance.HasKeywordInCombat`), `PlayLoop` order, `TurnFlow.cs:147`, `AutoPlay` inlines `CaptureXValuesForAutoPlay`'s two writes, pins §9.3; the duplicate keyword helpers are cleanup (§9.9) |
| C4 | cards-kernel → state | `RunState.CardMultiplayerConstraint` (`Runs/IRunState.cs:74-84`) | needs contract change (W2; `CardPools.MultiplayerConstraintFor(rs)` serves) |
| C5 | cards-kernel → loop | `CardPileCmd.Add` returns `Cards.CardPileAddResult` | resolved (§9.2) |
| C6 | cards-kernel → character-mechanics | `ForgeCmd` should reach the blade through `IForgeBladeDef` | resolved the other way round: `SovereignBlade` also implements `ForgeCmd`'s `ISovereignBladeDef` (one method serves both); `CreatedThroughForge` stays unwritten, a VFX-only flag (character-mechanics.md §7) |
| C7 | cards-kernel → hooks | per pile card visit card listener, affliction, enchantment with their liveness | resolved: `ListenerSnapshot.AppendPile` / `IsLive` (the holder stands in for an ownerless card); the card-listener dispatch tests pass with 0 PENDING |
| C8 | cards-kernel → state | `CombatHistory`'s private `HasKeywordInCombat` can call `card.HasKeywordInCombat` | open (cleanup, state; `State/CombatHistory.cs:1120`) |
| C9 | cards-kernel → hooks | skip the `ModifyKeywordsInCombat` snapshot when no live listener overrides it | open (performance, §9.7) |
| C10 | cards-kernel → CONTRACTS | B7 counts: 24 subclasses + `DynamicVar` + `DynamicVarSet`; 13 of 19 `EnchantmentModel` virtuals ported, 6 UI-only stripped | needs contract change |
| Ch1 | choice → CONTRACTS | `ChoiceSource source` required on the `CardSelectCmd` entry points | needs contract change |
| Ch2 | choice → CONTRACTS | record the pending-tuple encoding and that `State` keeps its identity (the pre-step `DeepClone` is the backup) | needs contract change |
| Ch3 | choice → state / W3 | end-turn readiness set for `VoidForm` / `WhisperingEarring`, cleared by `UndoEndTurnIfNecessary` | needs contract change (W3 content; same field as L5) |
| Ch4 | choice → CONTRACTS | B1 unordered-prompt legality lists only completable picks | needs contract change |
| Ch5 | choice → CONTRACTS | `UsePotion` `TargetIndex -2` never legal; the player is `NoIndex` | needs contract change |
| Ch6 | choice → CONTRACTS | `PushSelector` could take `IChoiceSelector` | needs contract change |
| P1 | pipeline → integrate | point `CardCmdForwarders` at the real `CardCmd` members | resolved |
| P2 | pipeline → monsters-kernel | `MonsterSpawner.SpawnForAdd` / `AddToCombat` duplicate `CreatureCmd.Add(cs, creature)` | open (dedupe; both bodies follow `Commands/CreatureCmd.cs:219-330` step for step, so no rule differs) |
| P3 | pipeline → monsters-kernel / integrate | wire `MonsterLifecycle.RemoveCreature` into `Kill` and `Escape`; `MonsterLifecycle.InvokeDiedEvent` / `OnPetDied` duplicate the pipeline's Died bookkeeping | resolved for the wiring (§9.3); the duplicate is open (dedupe; the pipeline's copy is the live one) |
| P4 | pipeline → character-mechanics | `PlayerPets.GetOsty` duplicates `CombatStatus.GetOsty` | open (dedupe; same rule) |
| P5 | pipeline → state | `CreatureState.HpDisplay` for Hellraiser's `IsInfinite` | needs contract change (exact until `HardenedShellPower` / `WaterfallGiant` are ported) |
| P6 | pipeline → state | escaped-creature list (`Combat/CombatState.cs:618`, `:733-737`) | needs contract change (W2 reward gold) |
| P7 | pipeline → state | `GetCreatureById` should search pets | resolved: it walks `cs.Allies` before `cs.Monsters` (`State/CombatState.cs:115-134`) |
| P8 | pipeline → state | `CombatHistory.DeepClone` copies every entry list on each clone | open (performance, §9.7) |
| P9 | pipeline → loop | recompute the EndToEnd final pin | resolved: with A18 the final pin stays the loop's `0x77FA4D8665BA0CC3`; the opening pin moved (§9.3) |
| P10 | pipeline → loop | a batch `CardPileCmd.RemoveFromCombat(cards)` would drop `HandlePlayerDeathFidelitySite` | open: the loop's batch overload exists now (`Combat/Loop/CardPileCmd.cs:190`); pipeline can switch `HandlePlayerDeath` (multiplayer-only path) |
| P11 | pipeline → hooks | the snapshot must include the powers of pets in `cs.Allies` | resolved: `AppendCombatPart` walks `cs.Allies` |
| P12 | pipeline → CONTRACTS | calculated damage is a `Func<CombatState, CreatureState?, decimal>` delegate | needs contract change |
| P13 | pipeline → CONTRACTS | `PowerCmd.Apply<T>(cs, targets, …)` returns `IReadOnlyList<T>` | needs contract change |
| P14 | pipeline → itself | delete `ShrinkPower.IsTurnParticipant` once `WurmBeetleTests` uses `CombatStatus.ContainsById` | open (cleanup) |
| M1 | monsters-kernel → hooks | the monster listener right after its creature's powers, allies first; live while in `cs.Monsters` or `cs.Allies` | resolved: `AppendCreature`; the dispatch-order test passes |
| M2 | monsters-kernel → pipeline | `Kill`: Died event, `RemoveCreature`, allies attached, owner death kills an alive Osty; `Escape`: `RemoveCreature`; `CreatureCmd.Add` routes to the spawner | resolved: `RemoveCreature` wired here; Died bookkeeping, allies and the Osty kill are the pipeline's equivalent bodies (`CombatStatus.GetOsty`); `CreatureCmd.Add(cs, def)` calls `SpawnForAdd`; the `Add(cs, creature)` duplicate is P2 |
| M3 | monsters-kernel → loop | `PlayerCmd.AddPet` must not add twice; `SwitchSides` calls `OnSideSwitch` for pets | resolved: `AddPet` fixed here (the game's `CreateCreature` + `AddPet(pet, player)` rather than `SpawnPet`); `SwitchSides` walks `SnapshotMonsterCreatures`, pets included |
| M4 | monsters-kernel → state | implement `AddAlly` / `RemoveAlly` | resolved (the four pending tests run) |
| M5 | monsters-kernel | rebuttal: no `MonsterState.ResetAi()` | accepted (no change) |
| CM1 | character-mechanics → loop | orb queue, stars and pets reset at setup | resolved: `CombatLoop.ResetCombatState` (`OrbQueue.Clear` + `AddCapacity(BaseOrbSlotCount)`, stars 0, pets cleared) |
| CM2 | character-mechanics → loop | `OrbQueue.BeforeTurnEnd` first in `DoTurnEnd`; `AfterTurnStart` after the player side's `AfterSideTurnStart` | resolved: `TurnFlow.TurnEnd.cs:62`, `TurnFlow.cs:299` (a TurnSetup first-pause step) |
| CM3 | character-mechanics → loop | spend stars through `StarsCmd.SpendStars` | resolved: `ActionExecutor.SpendStars` |
| CM4 | character-mechanics → loop | `AddPet` through `PlayerPets.AddPetInternal` | open (dedupe: four copies of the rule, in `PlayerCmd`, `MonsterSpawner`, `PlayerPets` and `PlayerState`) |
| CM5 | character-mechanics → pipeline | pet Died bookkeeping; a dying player clears its orbs, then kills an alive Osty | resolved (pipeline's equivalent bodies, `Combat/Pipeline/CreatureCmd.cs` `KillWithoutCheckingWinCondition`) |
| CM6 | character-mechanics → cards-kernel | `SOVEREIGN_BLADE` implements `ISovereignBladeDef.AddDamage` | resolved (C6) |
| CM7 | character-mechanics → hooks | orbs after potions in queue order with their liveness; the 12 area dispatchers guarded; `null` modifiers when empty | resolved: `AppendCreature`, `ListenerSnapshot.Guarded` in all of them, lazy modifier lists |
| CM8 | character-mechanics → CONTRACTS | star commands live in `StarsCmd`; A16 cites `Models/CardModel.cs:1432-1436`; no capacity cap in the queue | needs contract change |
| RP1 | relics-potions → state | `RemoveRelicInternal` → `relic.RemoveInternal()`; then a private setter | resolved here (§9.6) |
| RP2 | relics-potions → hooks | relics then potion slots between powers and orbs; the five potion dispatchers | resolved (the seven pending tests run) |
| RP3 | relics-potions → integrate | route `PendingKernelCalls` | resolved |
| RP4 | relics-potions → loop / choice | win check after potion actions; listing through `PotionActions.CanUse` / `CanDiscard`; no potions at a prompt; `PotionModel` should call the loop's effect helpers | resolved except the last point: `CombatLoop.ExecuteResolvedAction` checks the win after both potion kinds, `LegalActionGenerator` lists `UsePotion` through `CanUse` and gates `DiscardPotion` with the same rules, prompts allow answers only; `PotionModel`'s private effect pair is open (it lacks the dead-player card removal of `Combat/CombatManager.cs:1430-1480`, reachable only with another player alive) |
| RP5 | relics-potions → pipeline | look PaperPhrog / PaperKrane up by id (`GetRelic<T>`) | rejected for W1: B12 A5 prescribes the interface scan, which picks the same relic while only these two relics implement the interfaces |
| RP6 | relics-potions → W2 | `TotalFloor`, grab bags, map history, out-of-combat potions | needs contract change (W2) |
| L1 | loop → cards-kernel | `CardCmd` is `public static partial` in `Sts2Sim.Core.Combat.Pipeline` | resolved (compiles) |
| L2 | loop → cards-kernel | `Slimed` through `cs.CreateCard`; the `CardUpgradeTests` terminal assert | resolved: `Slimed.AddToCombatAndPreview` uses `cs.CreateCard`; the test fixed here (§9.3) |
| L3 | loop → choice | `TryResolve` resolves `UsePotion` / `DiscardPotion` | resolved (F4: 48 potion actions) |
| L4 | loop → choice | `PushContext(kind, onFirstPause)` semantics | resolved (the TurnSetup tests pass under the real loop) |
| L5 | loop → state | cloned and hashed ready-to-end-turn request for `PlayerCmd.EndTurn` | needs contract change (W3 content, with Ch3) |
| L6 | loop → state | `CombatState.AddAlly` | resolved |
| L7 | loop → hooks | relics and potions in the combat snapshot | resolved |
| L8 | loop → pipeline | `OnPetDied` in the death path | resolved (S1) |
| L9 | loop → pipeline | `EnergyCmd.SpendEnergy` appends `EnergySpent` | resolved (`Combat/Pipeline/EnergyCmd.cs`, one entry per spend) |
| L10 | loop → character-mechanics | `OrbQueue` stubs | resolved |
| L11 | loop → CONTRACTS | A13 says deck ids `1..n`; the allocator starts at 0 | needs contract change (text: "the first n ids") |

### 9.9 Residual issues

| # | Issue | Next step |
| --- | --- | --- |
| 1 | Throughput regression (§9.7): about 0.2× of P0 on the decision API at equal load. Every contributor is contract-required (B1 tape engine, B3 listener walk, B6 turn flow and history, B7 keywords), and the W1 gate holds by orders of magnitude, but rollouts that need P0-like speed must use the fast path `Step(a, answerer)` until the walk is cheaper | a performance pass before W2 rollouts: H3 (cached listener array under A20), C9 (keyword fast path), P8 (history clone sharing), `ListenerSnapshot.InlineCapacity` retuned with W1 content |
| 2 | Duplicate implementations of one rule (no behaviour difference found): `MonsterSpawner.AddToCombat` vs `CreatureCmd.Add(cs, creature)` (P2); pet Died bookkeeping in pipeline, `MonsterLifecycle`, `PlayerPets` and `CombatState` / `PlayerState` (P3); `AddPetInternal` ×4 (CM4); `GetOsty` ×3 (P4); `HasKeywordInCombat` in `CardPileCmd` and `CardInstance` (C3); `ResetCombatState` in the loop and `PlayerState` (S2); the X capture inlined in `AutoPlay`; `PotionModel`'s private effect pair (RP4, multiplayer-only difference) | one owner each: keep the live copy, make the others call it |
| 3 | `AttackPotion` offers one attack: `HEADBUTT` is the only ported generatable Ironclad attack, and the draw over ported options is flagged `ApproximatedRule` (cards-kernel.md §7.4) | W3 content; the test switches back to three offers automatically |
| 4 | The four `(cs, side)` side-turn overloads stay in `Hook.cs` for 20 calls in `PipelineTests` / `WurmBeetleTests` (§9.4) | pipeline (and the P0 monsters owner) move the calls; the next integrator deletes the overloads |
| 5 | Twelve `throw new NotImplementedException` sites remain in Core (137 at round 2), plus the nine placeholder members of `UnportedCardDef`; none is on a W1 path: W2 run layer (`ModifierModel` ×2, deck-card transform, `MimicRestSiteHeal`, transforms outside combat, `IsBeforeAct3TreasureChest(rs)`, out-of-combat potions), `PlayerCmd.EndTurn` (needs L5), the unported-card guards (`CardInstance`, `CardFactory.CreateRandomCardForTransform`), and the game's own `RngSet` seed guards (×2) | W2 / W3 |
| 6 | Contract text: the 20 "needs contract change" rows of §9.8 | the Part B owner folds them into CONTRACTS |
| 7 | Cleanups: C8, P10, P14 | owners named in §9.8 |
| 8 | The REC track (`4094c1e`) is still compiled and tested but not installed | `scripts/install_mod.sh` when the recorder change goes live |

## W2/W3 integrate A (2026-11-01)

Base `72edde8` (W2 scaffold). All 32 W3 branches were cut from `14c25e8` (the scaffold's parent), so none descends from the base; none touches a file the scaffold changed, so they were merged (3-way) rather than skipped.

Merged, in order: powers-1-1, powers-1-2, powers-2-1, powers-2-2, powers-3457, powers-6, cards-1-1, cards-1-2, cards-1-3, cards-1-4, cards-2-1, cards-2-2, cards-2-3, cards-3-1, cards-3-2, cards-3-3, cards-4-1, cards-5-1, cards-6-10, cards-7-1, cards-8-1, cards-9-1, cards-9-2, relics-1-1, relics-1-2, relics-2-4-6, relics-3-1, relics-3-2, relics-5-1, potions-1, potions-2, orbs-ench-affl.

Conflicts: only `sim/Sts2Sim.Tests/CardKernelTests.cs` (the two Ironclad-pool tests, edited the same way by eight owners); HEAD side kept each time. Duplicate kernel members (`PowerModel.IsVisible`/`IsVisibleInternal`, `CardInstance.IsClone`) merged to a single copy.

Fixes:
- `AmbergrisPower.IsVisibleInternal`: `public` hid the base member (CS0114); now `protected override` (Models.Powers/AmbergrisPower.cs:87).
- `Nightmare.OnPlay`: wired `PowerCmd.Apply<NightmarePower>(...)!.SetSelectedCard(cs, selected)` (Models.Cards/Nightmare.cs:173-175), replacing the W3-gap throw.
- `RollingBoulderPower` ported (unowned; Models.Powers/RollingBoulderPower.cs): AfterPlayerTurnStart deals Amount unpowered to hittable enemies, then Amount += 5.
- `VoidForm` card marked `[SimExclude]` (calls `PlayerCmd.EndTurn`, a kernel stub, see L5); removed from the cards-9-2 ported-id list.

Gates: build 0 warnings 0 errors; tests 830/830, PENDING 0; content soak green (cards 1084, encounters 24, potions 128, relics 340); diff 4/4 bundles, 69/69 decisions; bench row in `sim/BENCH.md`.

Pending: none. Excluded defs: `VOID_FORM` (NotImplementedException from `PlayerCmd.EndTurn`).

## W2/W3 integrate B (2026-11-01)

Base `0a01a90` (integrate A). All 14 branches descend from the base; none skipped.

Merged, in order: w2/run-state, w2/map, w2/odds-rewards, w2/events, w2/rooms, w2/loop, w2/save, w3/monsters-1, w3/monsters-2, w3/monsters-3, w3/monsters-4, w3/powers-9, w3/encounters-1, w3/encounters-2.

Conflicts and resolutions:
- `MonsterCanonicalTags.cs`: all owners' tags kept; collisions renumbered inside 0x4Dxx: monsters-3 `ToughEggExtra..WrigglerExtra` → 0x4DE1-0x4DE4, monsters-4 `AxebotExtra..TestSubjectExtra` → 0x4DD0-0x4DD9 (monsters-1 keeps 0x4DA0/0x4DA1, monsters-2 keeps 0x4DC0-0x4DC3).
- 31 monster-coupled powers ported by both a monsters owner and powers-9 (add/add): the monsters owner's copy kept (already exercised by its monster tests; both sides were full ports). `CrabRagePower` (monsters-1 vs monsters-2): monsters-1 copy kept. `ReattachPower`: monsters-4 copy (cs overload) kept.
- `EncounterDef` (`MonsterDef.cs`): one copy of `RoomType`; `Tags` typed `IReadOnlyList<EncounterTag>` (encounters-1) replacing run-state's string tags; gold reward members from encounters-1; `AllPossibleMonsters` from encounters-2. Duplicate `enum EncounterTag` in `MonsterDef.cs` deleted (kept in `Enums/RunEnums.cs`).
- `EncounterMonsterConfig`: both APIs kept; `Set(string, object)` / `SetExtra` delegate to encounters-2's `Set(MonsterState, string, object)`.

Fixes:
- `PowerModel.ShouldOwnerDeathTriggerFatal(cs)` now the called overload (`Feed`, `HandOfGreed`, `BookRepairKnife`), so `ReattachPower` is honoured.
- `InfestedPower.AfterDeath` ported (Models.Powers/InfestedPower.cs:64-93): 4 × `SpawnForAdd(WRIGGLER, slot wriggler{i+1}, StartStunned = true)`, replacing the W3-gap throw.
- `RunLoop.SelectActs` throws `NotImplementedException` when no `ActModel` def is registered (acts are unported W3 content), so run-layer tests report PENDING instead of an index error.
- Tests: `RunStateTests` encounter double retyped to `EncounterTag`; `RoomsTests` helper sets player odds / relic grab bags / run odds; `RewardsTests` picks a rewarding Monster encounter; `EventsTests` combat test needs an act; merchant test routes an unported relic id to PENDING; `Content_encounters_2Tests` skips the excluded ids.

Gates: build 0 warnings 0 errors; tests 922/922, PENDING 6; content soak green (encounters 252 cases, 0 failures); diff 4/4 bundles, 69/69 decisions; bench row in `sim/BENCH.md`.

Pending (later owners): act defs (`Models.Acts/*`: Events.EventCombatGoesThroughRoomStack, Test_RunSim_NewRunStartsAtNeowAndClonesHashEqual, Test_RunSoak_RandomRuns, Test_Map_CreateForUsesTheActMapStream, Test_SaveImport_FullStateThroughRunLayerOwners); relic `MEAL_TICKET` (Test_Rooms_MerchantInventoryLayout).

Excluded defs (`[SimExclude]`, monster not ported → `KeyNotFoundException`): `BATTLEWORN_DUMMY_EVENT_V1_ENCOUNTER` / `_V2_` / `_V3_` (BATTLE_FRIEND_V1..V3), `GREMLIN_MERC_NORMAL` (GREMLIN_MERC), `THIEVING_HOPPER_WEAK` (THIEVING_HOPPER).

## W2/W3 integrate C (2026-11-01)

Base `8eda7fb` (integrate B). All 12 branches descend from the base; none skipped.

Merged, in order: w2/oracles, w3/events-12-1, w3/events-3456-1, w3/events-3456-2, w3/relics-7-1, w3/relics-7-2, w3/relics-8-1, w3/relics-8-2, w3/relics-9-1, w3/relics-10-1, w3/cards-11-monsters-6-8, w3/characters-acts.

Conflicts and resolutions:
- `CreatureCmd.cs` (relics-7-1 vs relics-7-2): relics-7-1's RunState overloads kept (`LoseMaxHp` goes through the hooked `Damage(RunState, ...)`).
- `CardCmd.cs`: both kept — relics-7-1 `Transform(RunState, IEnumerable<CardTransformation>, Rng?)` + `CreateRunTransformReplacement`, relics-7-2 `TransformInDeck(RunState, IReadOnlyList<(Original, Replacement)>)`.
- `CreatureCmd.Run.cs` (events-3456-2) deleted: `Damage/Heal/GainMaxHp(RunState, ...)` duplicated relics-7-1. Its out-of-combat `Heal` fired `AfterCurrentHpChanged`; the game fires it only when `creature.CombatState != null` (Commands/CreatureCmd.cs Heal), so the relics-7-1 copy is the correct one.

Fixes:
- Tests: `Content_relics_9_1Tests.WithObtainPrompts` populates the player / shared relic grab bags when null (CALLING_BELL, LARGE_CAPSULE, SMALL_CAPSULE, TOY_BOX threw in the relic soak and W1 F5).
- Oracles: `RunOracleState.NewRunState` gives the oracle player the character's starting deck from `CharacterRegistry` (event `IsAllowed` such as THE_LEGENDS_WERE_TRUE needs a non-empty deck).
- `ActModel.DeclaredEncounterIds` (overridden by the four acts) exposes the unfiltered encounter list; `RunStartOracle.Generate` and `MapOracle.CheckGenerated` report Unsupported when an act or saved room set names an unported encounter (GREMLIN_MERC_NORMAL), instead of a divergence / KeyNotFoundException.

Gates: build 0 warnings 0 errors; tests 965/965, PENDING 3; content soak green (cards 1108, encounters 252, potions 128, relics 586, 0 failures); diff 4/4 bundles, 69/69 decisions; bench row in `sim/BENCH.md`.

Pending (later owners): W2 R1 `EventRoom.EnterInternal` (Test_RunSim_NewRunStartsAtNeowAndClonesHashEqual, Test_RunSoak_RandomRuns); W3 encounter GREMLIN_MERC_NORMAL (Test_SaveImport_FullStateThroughRunLayerOwners).

Excluded defs (owner-declared `[SimExclude]`, unchanged here): cards `FORBIDDEN_GRIMOIRE`, `MAD_SCIENCE`, `ROYALTIES`; relics `ASTROLABE`, `CLAWS`, `NEW_LEAF`, `PRECARIOUS_SHEARS`.

## W3.5/W4 integrate 1 (2026-11-01)

Base `de79cbc` (W2/W3 status log after integrate C). All 7 branches descend from the base; none skipped.

Merged, in order: w4/run-glue `def0bde`, w4/kernel-gaps `d499e14`, w4/content-leftovers `db987fd`, w4/exporter-v2 `6fa744b`, w4/mcr-decoder `160de98`, w4/harness-v2 `a20513c`, w4/ftests `bbfc184`.

Conflicts and resolutions:
- `GremlinMercNormal.cs` (kernel-gaps vs content-leftovers): kernel-gaps' implementation kept (`Extra.GoldWasStolen`, `MarkGoldStolen`, `CalculateGoldProportion` override); content-leftovers' NotImplemented stubs dropped.
- `MonsterCanonicalTags.cs`: both kept (`GremlinMercNormalEncounterExtra` 0x4DFA, `BattlewornDummyEventEncounterExtra` 0x4DFB, `ThievingHopperExtra` 0x4DF0; no collision).
- `Sts2Sim.Diff/Program.cs` (mcr-decoder vs harness-v2): both routes kept (`replay*` → `ReplayCli`, then `--v2/--baseline/--update-baseline` → `V2Cli`, then v1).
- `RunActionExecutor.ClaimReward`, `RunImporter.LoadRelicGrabBag/LoadSavedMap` (run-glue vs ftests): run-glue side kept (same intent; run-glue also has `CreatePreFinishedRoom`).

Fixes:
- `GREMLIN_MERC_NORMAL` un-excluded; `CombatRoom.OnCombatEnded` calls `Encounter.CalculateGoldProportion(cs)` (was 1f) and keeps a clone of `cs.EncounterExtra`; `BattlewornDummy.RanOutOfTime` reads it (Models.Events/BattlewornDummy.cs:62-67).
- Starting deck `FloorAddedToDeck = 1` (Player.cs:1108); save import/export of `floor_added_to_deck`; F3 ignore entry removed.
- AMBERGRIS / BLOOD_POTION / FRUIT_JUICE out of combat through the RunState `CreatureCmd` overloads (the 169 soak content gaps).
- Harness v2 `AnchorLoader`: "saved properties … are not mapped" classified `content_not_ported` (was `out_of_scope`); the fixture save now first fails on unmapped SPOILS_MAP / GIRYA saved properties instead of unported content.
- Tests: W1Pending routes removed and made real — `Test_SelfConsistency_F4F5_NewRunWithNeowAndEvents` (run-glue), `Test_Content_leftovers_SurpriseHandsStolenGoldToFatGremlinHeist` (now a real GREMLIN_MERC_NORMAL combat, asserts `GoldWasStolen`), `Test_Content_leftovers_BattlewornDummyOffersThreeSettings` (player rng/bags set up, asserts the V3 relic reward), `Test_SaveImport_FullStateThroughRunLayerOwners.GremlinMerc` (unedited fixture imports). `Content_encounters_2Tests` no longer skips excluded ids. New `Test_Content_leftovers_BattlewornDummyRanOutOfTimeReadsTheFinishedCombat`. F2 pins refreshed (Release = Debug verified): combat streams change only by the kernel-gaps canonical format; run seed 1 walk is now 136 steps (was 120; the walk no longer stops at a content gap).

Gates: build 0 warnings 0 errors; tests 1021/1021, PENDING 0; run soak 10⁴ runs × 5 characters, 0 failures, 0 content-gap stops; content soak green (cards 1118, encounters 267, potions 128, relics 594, 0 failures); diff v1 4/4 bundles, 69/69 decisions; sts2rec 701 passed, 4 skipped; bench row in `sim/BENCH.md`.

Pending: none. Excluded content defs: none (only the `UnportedCardDef` / `SimStubEvent` placeholders carry `[SimExclude]`).

Headline numbers (not a gate), mcr-decoder bundles `tmp/diff-v2/mcr` (723 replays: v0.107.1 611, v0.110.1 41, v0.111.0 71; 0 eligible — model_table_unverified 652, unsupported_action 747, outcome_not_terminal 649, no_checksum_states 606, hand_index_unresolved 606, enchanted_card_in_deck 371, encounter_unresolved 61, non_standard_run 26): v1 harness `--include-ineligible` 6 hard divergences (all v0.107.1, `action.card_id` at decision 0-3, consistent with hand_index_unresolved), 717 skipped, 0 errors; v2 harness 723 segments skipped (content_not_ported 717, exporter_ineligible 6). No exporter-v2 bundles were present under `tmp/diff-v2`.

Open issues:
- No content def carries `[SavedProperty]` yet (e.g. GIRYA.TimesLifted, SPOILS_MAP.SpoilsActIndex are `[SavedProperty]` in the game), so saves holding such cards/relics are rejected by `RunImporter.CheckSupported`.
- `RunImporter.CreatePreFinishedRoom` does not call `EncounterModel.LoadCustomState`; a pre-finished BATTLEWORN_DUMMY room loses `RanOutOfTime` on import.
- mcr converter: v0.107.1 model table checksum mismatch (owner note) and hand-index resolution; harness-v2 run-level pairs are logical rather than recorded order (exporter-v2 note).

## W3.5/W4 integrate 2 (2026-11-01)

Base `c145de0` (W3.5/W4 integrate 1). Both branches descend from the base; none skipped.

Merged, in order: w4/replay-combats `36909de`, w4/replay-runs `3c893f7`. No conflicts.

Fixes: none needed (both merges built and gated clean).

Gates: build 0 warnings 0 errors; tests 1028/1028, PENDING 0 (the W1Pending routes named for this stage — ftests → run-glue, content-leftovers → kernel-gaps, save import → GREMLIN_MERC — were already made real in integrate 1; the remaining W1-era `W1Pending.Run` wrappers all execute their bodies); run soak 10⁴ runs × 5 characters (PortedOnly), 688135 steps, 0 failures, 0 content-gap stops; content soak green (cards 1118, encounters 267 (2 capped), potions 128, relics 594, 0 failures); `Test_SelfConsistency_F2_RunHashStreamsArePinnedAndRepeatable` passed in the full run (replay-combats owner reported one earlier flake, not reproduced); diff v1 4/4 bundles, 69/69 decisions; sts2rec 701 passed, 4 skipped; bench row in `sim/BENCH.md`.

Pending: none. Excluded content defs: none (only `UnportedCardDef` / `SimStubEvent` placeholders carry `[SimExclude]`).

Headline numbers (not a gate), `tmp/diff-v2` (exporter-v2 run bundles for 19 lineages + mcr 611 v0.107.1 replays):
- harness v2 `--v2`: 2387 segments, 2 agree, 0 hard divergence, 2385 skipped, 0 errors; decisions 31/31, outcome 2/2. v0.107.1 combat 611 (content_not_ported 605, exporter_ineligible 6); v0.110.1 562 (out_of_scope 521, content_not_ported 41); v0.111.0 1214 (combat 350: agree 2, content_not_ported 264, exporter_ineligible 84; map 415 + room 449 exporter_ineligible).
- G3 seed replay (`Sts2Sim.Diff g3`): 11 runs, 0 reach the recorded end, decisions 748/753, floors agreed sorted `0, 1, 2, 3, 4, 5, 7, 7, 10, 10, 13`; first divergence by class: port_bug 7, projection_gap 2, content_not_ported 1 (CRYSTAL_SPHERE event unregistered), out_of_scope 1 (old-prefix lineage).

Open issues:
- G3 first divergences (port_bug): FOSSIL_STALKER select candidates order, GREMLIN_MERC_NORMAL / LIVING_FOG_NORMAL floor_summary monster count (spawned minions), shop entry gold (54 vs 129), NIBBITS_WEAK discard count, TERROR_EEL_ELITE VIGOR_POWER on enemy, ROOM_FULL_OF_CHEESE relic count; see `sim/specs/diff-report-w4-run.md`.
- Diff projection lacks `DisplayAmount` rules for HARDENED_SHELL_POWER and SLOW_POWER (projection_gap).
- EVENT.CRYSTAL_SPHERE not registered (content_not_ported).
- Carried from integrate 1: no `[SavedProperty]` on content defs; `CreatePreFinishedRoom` skips `LoadCustomState`; mcr v0.107.1 model-table checksum / hand-index resolution.
- v0.111.0 v2 reader rejects 83 segments (`deck: null` / `action: null`), per replay-combats report.

## W4.1 integrate (2026-11-01)

Base `4809101` (integrate 2); merged onto HEAD `efb0102` (docs + w5/client + w5/server). Both branches descend from the base; none skipped.

Merged, in order: w41/combat-fixes `96f122f` (merge `5e34a7b`), w41/run-fixes `14fa45f` (merge `9175c70`). Conflicts: `Sts2Sim.Diff/V2/RunBundle.cs` (combat-fixes' `McrJoin` fields and join loop vs run-fixes' `UnavailableFields`; both kept) and `sim/specs/diff-harness-v2.md` (both W4.1 lines kept).

Fix (integration): the merged reader marked `start.deck: null` as unavailable, which skipped exactly the `recorder_gap:deck_order` segments the mcr join exists to fill (`Test_W41_McrJoinTakesTheDecodedStart` failed). `McrJoin` now runs when `start.deck` is the only unavailable field and clears it on a successful join; unavailable actions still skip the segment (diff-harness-v2.md W4.1 integrate line; assertion added to that test).

Gates: build 0 warnings 0 errors; tests 1057/1057, PENDING 0; run soak 10⁴ runs × 5 characters, 687034 steps, 0 failures, 0 content-gap stops; content soak green (cards 1118, encounters 267 (2 capped), potions 128, relics 594, 0 failures); F2 run pins pass (combat-fixes refreshed seeds 1 and 4); diff v1 G0 4/4 bundles, 69/69 decisions; sts2rec 700 passed, 4 skipped, 1 failed — `test_text_env.py::test_vector_steps_workers_concurrently`, a wall-clock assertion (`elapsed < 0.09` for two 50 ms steps), fails intermittently under the machine load of this session (game client running) and passes on rerun; no merged branch touches it. Bench row in `sim/BENCH.md` (machine loaded; equal to the integrate-2 tree benched interleaved).

Replays (inputs regenerated: `export-bundles-v2 tmp/diff-v2-w41i`, `replay-convert` → `tmp/diff-v2-w41i/mcr` 723 replays; v1 bundles from the combat-fixes scratchpad export):
- Widened combat gate (`Sts2Sim.Diff <g0 + g0-ineligible + v2> --v2`, 2531 segments, 1294 combat). Before (integrate 2) → after: compared combat segments 8 → 117, agree 8 → 109, decisions 131/131 → 1599/1607 (99.5 %), outcome 109/109. By version: v0.107.1 compared 4 → 84, agree 4 → 80, decisions 69/69 → 1084/1088; v0.110.1 0 → 6, 0 → 6, 0/0 → 85/85; v0.111.0 4 → 27, 4 → 23, 62/62 → 430/434 (v0.111.0 segments read 1214 → 1225 with the reader fix). 8 divergences: 4 v0.107.1 `exporter_ineligible` rows, Q3X6PF8S37GA s5 (recorder_gap, discard count), GM28D28DX794 s26/s54/s65 decision 0 (relic run-time state at combat start).
- G3 (`Sts2Sim.Diff g3 tmp/diff-v2-w41i`): decisions 748/753 → 1532/1537 (combat-fixes alone 1318/1324, run-fixes alone 896/900); runs reaching the recorded end 0 → 1 (K4FB4086NTRE). Floors agreed per run (integrate 2 → W4.1): G0G2JMFEYHRS 1 → 13, NBNDT3S61AUM 7 → 11, DSB363J2LQWZ 4 → 22, P2UZ19X1GGNQ 5 → 13, Q3X6PF8S37GA 2 → 2, GM28D28DX794 13 → 27, K4FB4086NTRE 7 → 17 (end), EC039PTFQWV5 10 → 11, PLRUV49CRFQW 10 → 11, G1YFC196B2H2 3 → 7 (truncated at s16#21 `decision_unresolved`), old-prefix lineage 0 → 0. No run drops; ratchet holds. First divergences by class: port_bug 7, projection_gap 1, out_of_scope 1, truncated 1, none 1.

Open issues (G3 first divergences):
- G0G2 s34 f14 PUNCH_OFF_EVENT_ENCOUNTER: the oracle starts a combat from the event, the sim stays in `Event` (event → combat transition).
- NBND s27 f12 `player.relics[5].counter` 1 vs 0 after TERROR_EEL_ELITE (not investigated).
- DSB3 s64 f23 shop `entries[4].on_sale` not projected (projection_gap).
- P2UZ s31 f14 PUNCH_CONSTRUCT_NORMAL opening hand `hand[5]` PIERCING_WAIL vs LEADING_STRIKE (deck order / draw).
- Q3X6 s5 NIBBITS_WEAK discard count (recorder_gap, sim right; classed port_bug by G3).
- GM28 s74 DECIMILLIPEDE_ELITE `hand[0].cost` 0 vs 1 (not investigated).
- EC03 s29 / PLRU s32 rest site: oracle `select` vs sim `rest_site` screen.
- Carried: relic run-time state at combat start (TEA_OF_DISCOURTESY, VENERABLE_TEA_SET) → start from decoded `SerializableRun`; mcr `enchanted_card_in_deck`, `?` rooms decoded as `NONE`; v0.107.1 model-table checksum; `CombatRoom` end-of-combat monster-id approximation now redundant (harmless, dedupes).

## E1 integrate (2026-11-01)

Base `353f07d` (E1 scaffold: ObservationBuilder partials, RulesText stub). All three owner branches descend from it; none skipped.

Merged, in order: e1/eval-runner `94bb476` (merge `3a598d4`, clean), e1/rules-text `03d20f4` (merge `d03fb4b`, clean), e1/combat-view `9056b0f` (merge `63ac15c`). Conflicts: only `sim/PLAY.md`, two hunks. (1) Observation field table: combat-view's `player` / `combat` / card (hand) / enemy / power `detail` / `combat.danger` rows kept, with rules-text's `map` row (`options` + `nodes`) replacing combat-view's older `map` row. (2) The prose after the table: combat-view's card paragraph merged with rules-text's sentence that keywords are listed in `keywords`, not repeated in `text`; combat-view's combat-text description and its `Creature uid and index`, `Effective numbers`, `Danger lines`, `Piles` and `Step log` sections kept, followed by rules-text's `Rules text` section. `ObservationBuilder.cs` (Text() vs AppendScreen()) and `PlayService.cs` (Catalog() vs Piles/Step) auto-merged; `Program.cs` and `HttpHost.cs` only changed on rules-text.

Fixes (integration, in the combat-view merge commit `63ac15c`; the owners' open items for each other):
- Power text now uses the live amount: `ObservationBuilder.Combat.cs` `PowerNodes` calls `RulesText.PowerText(id, power.Amount)` instead of `PowerText(id)` (the `N` placeholder). After combat-view's refactor all three scaffold call sites (Player :41, Combat :38, :61) route through `PowerNodes`, so it is one site.
- Intent move titles: combat-view fills `intents[].move` with the move id (also used by the step log); rules-text documented `move` as the localised name. Kept `move` = id and added `intents[].move_name` = `RulesText.EnemyMoveTitle(monsterId, moveId)` (null when rules text is not loaded); text renders `[MOVE_ID (Title)]`, e.g. `intent: [WHIRL_MOVE (Whirl)] Attack 7`. PLAY.md rows updated (`combat` field list, enemy row, rules text fields row).

Gates:
- Build: `dotnet build sim/Sts2Sim.sln -c Release` 0 warnings 0 errors after each merge and on the final tree; Debug 0 warnings 0 errors.
- Tests (full suite, Release): 1077/1077 passed, 0 failed, PENDING 0 (413 s). Server filter alone 30/30 (11 ServerTests + 9 ServerRulesTextTests + 10 ServerCombatViewTests).
- Run soak: 10⁴ runs × 5 characters, 687034 steps, 0 victories, 10000 defeats, 0 capped, 0 content/kernel-stub stops. Step count identical to W4.1 (687034), consistent with no kernel behaviour change.
- Content soak green: cards 1118 (0 capped), encounters 267 (2 capped), potions 128, relics 594, 0 failures. W1Gate F1 clone/hash: 356 playouts pass.
- Server self test: `--selftest 300 --content ported_only` 300 completed, 0 crashed, 0 capped, 0 server errors, mean 68.9 steps (15.6 s); extra `--selftest 100 --policy biased` 100 completed, 0 crashed, 0 server errors, mean 147.2 steps. Startup line: `rules text: loaded from .../knowledge/source (discovered; versions v0.107.1,v0.111.0): 625 cards, 306 relics, 66 potions, 296 powers, 70 events, 126 enemies, 14 keywords`.
- Diff v1 G0 (`Sts2Sim.Diff <sim-source-recreation-9392df>/tmp/handoff/diff-bundles/bundles --report /tmp/e1-diff.json`): bundles 4 (agree 4, hard divergence 0, skipped 0, error 0, sim exception 0); decisions 69/69 (100.00 %), 0 unobserved; outcome 4/4; soft diffs 0.
- Bench: 143,509 decisions/s, Clone 2.179 us, StateHash 2.566 us (row in `sim/BENCH.md`, load average 3.3 to 4.4).
- Eval runner: `uv run --project sim/tools/eval pytest -q sim/tools/eval` 31 passed (includes 2 integration tests that spawn the real server); end-to-end smoke below, 10/10 games, report.md written.
- sts2rec: `uv run pytest -q` 701 passed, 4 skipped (the known-flaky timing test passed first time).
- `git status --short` before the commit: only `sim/BENCH.md` and this file; no tmp/, bundles or saves.

Eval smoke (`uv run --project sim/tools/eval sts2eval run --seedset a0-s10 --policy random --concurrency 10 --spawn --out tmp/eval/e1-smoke`, merged server `63ac15c`, rules text loaded from `knowledge/source` v0.107.1+v0.111.0):

| metric | value |
| --- | --- |
| games | 10 |
| results | loss 10 |
| win rate (Wilson 95%) | 0.0% [0.0%, 27.8%] |
| floor mean / median / max | 4.2 / 4.5 / 7 |
| progress (floor / 48) | 8.8% |
| act reached | act 1: 10 |
| steps mean (total) | 52.2 (522) |
| illegal actions per game | 0.00 |
| crashes | 0 |
| api errors / refusals / timeouts / max_tokens stops | 0 / 0 / 0 / 0 |
| random fallbacks | 0 |
| cost USD | $0.0000 |
| minutes per game (mean) | 0.02 |
| replayable histories | 10/10 |

Identical to the eval-runner owner's pre-merge smoke (522 steps), as expected: the random policy's choices are seeded and the merged server changes only observations and prompt pick listing. Transcript check (`games/IRONCLAD-E1A0IC01.jsonl`): the observation text carries rules text (`STRIKE_IRONCLAD (Strike) 1e Attack->AnyEnemy (6 dmg) — Deal 6 damage.`, relic lines with text, `TOADPOLE (Toadpole)`), uids and move titles (`[1] u2 TOADPOLE (Toadpole) 25/25 | intent: [WHIRL_MOVE (Whirl)] Attack 7`), the danger line (`incoming: 7 dmg in 1 hit (u2 7x1), block 0, net -7 HP -> 73/80`) and the v2 step log (`enemy turn:` / ` u2 TOADPOLE: WHIRL_MOVE (Whirl) — attack 7 x1 -> player: 0 blocked, 7 dmg`, `u1 +THORNS_POWER 2`, `cards: drew 5`). Power lines read e.g. `STRENGTH_POWER — Increases attack damage by 1.` for amount 1.

Open issues:
- The Claude policy has not been run against the real API (no key in the integration session); only fake-client tests cover it. First real run: `ANTHROPIC_API_KEY=... uv run --project sim/tools/eval sts2eval run --seedset a0-s10 --policy claude --model claude-sonnet-5-5 --effort low --concurrency 10 --spawn --max-cost-usd 30`. Cache reads on the ~1.3-1.4k-token system prompt are unconfirmed; `--max-tokens 1024` includes thinking, so watch `max_tokens stops` at higher effort.
- Eval cause of death is the room type of the last travel plus the last combat's enemy ids; the observation has no encounter id field.
- Event option texts with run-time-chosen values still show `?` (7 of 81 options seen in 40 random runs: SAPPHIRE_SEED PLANT enchantment, SLIPPERY_BRIDGE HpLoss and RandomCard, WOOD_CARVINGS card and enchantment names); fixing needs per-event read-only accessors in Core.
- Shop `base_price` is reconstructed as 2 x the sale price (exact within 1 gold); an exact value needs a Core accessor.
- Card `text` has base instance values and no keyword lines (keywords stay in `keywords`); in-combat numbers are in `effective`.
- `effective.times` is best-effort (Repeat var, else a const HitCount by reflection, else 1; null for X cost); cards computing hits at play time report 1 or null.
- The danger line uses intent projections only: no Intangible / Buffer / Tungsten-Rod-style hp-loss modifiers, no end-of-turn block (Metallicize, Plated Armor).
- The last enemy's killing blow logs `hp X -> 0` without the damage note (the kernel writes no DamageReceived entry once the combat is ending); summons with no history entries during the move are named only by the `uN X spawned` line.
- `scripts/export_sts2_knowledge.py` takes zhs as PCK occurrence -1, which is Traditional Chinese on v0.111.0; do not rerun it on the current game without fixing that (`export_sts2_localization.py` is correct).
- Observation text changed by design (uids, danger line, eff, move titles, act-map grid, prompt desc): byte-identical comparisons against pre-E1 servers no longer hold.
- Owner commits carry `Co-Authored-By: Claude Opus 5.5` (session attribution rule) rather than the brief's Fable line; not amended.

## E2 integrate (2026-11-01)

Base: session branch `claude/sts2-eval-pipeline-a3aee7` at `d2c8be6` (E1 docs on top of `6114e81`, CONTRACTS Part D). Scaffold `af640b7` (e2/scaffold, parent `6114e81`); all three owner branches descend from it; none skipped.

Merged, in order (all `--no-ff`): e2/scaffold `af640b7` (merge `fded05c`, clean), e2/encoder (V1) `d1bcb3a` (merge `bcda241`, clean), e2/env-core (V2) `c234ffd` (merge `39cbb65`), e2/python (V3) `e74907b` (merge `e402777`, clean). Release build 0 warnings 0 errors after each merge.

Conflicts: only `sim/VEC.md`, one hunk in the schema buffer table — V1 had removed the `TODO(V1)` markers from rows 0–25, V2 had documented rows 26–27 (`cand`, `cand_n`) against the scaffold's TODO rows. Kept V1's rows 0–25 and V2's rows 26–27. `VecTests.cs` (both owners: `static class` → `static partial class`; V1 actually used its own class `VecEncoderTests`) and `sim/BENCH.md` auto-merged.

Fixes (integration):
- `VecEnv.WriteLastEpisode` now calls V1's `ObservationEncoder.FillEpisodeSummary(sim, last_episode)` for floor / act / final_hp / final_max_hp / last_encounter_id instead of V2's inline LINQ copy (V2 had shipped no stub, only the inline loop; win / steps / end_kind stay with VecEnv). Done in the env-core merge commit `39cbb65`.
- Core content gap reported by V3 (random soak, 1 CONTENT_GAP): `FairyInABottle.OnUse` called `RequireCombat` and threw `NotImplementedException` ("W2: a potion effect outside combat …") when death was prevented outside combat (event / run-layer HP loss). The decompile (`Models/Potions/FairyInABottle.cs` OnUse: `CreatureCmd.Heal(target, max(MaxHp × 0.3, 1))`) heals with no combat requirement, so the port now calls the run-layer `CreatureCmd.Heal(rs, target, …)` (dispatches to the combat overload when in combat; same as `BloodPotion`/`Ambergris`). V3's diagnosis that LegalActions offers it out of combat was wrong: `CanUsePotionOutOfCombat` requires `PotionUsage.AnyTime`; the trigger is the automatic death-prevention path. It was the only non-`CombatOnly` potion calling `RequireCombat`. RunSoak step count unchanged (687034), G0 unchanged.
- Python `needs V1/V2` tests: no change needed — with the merged build all 8 run and pass (`STS2VEC_REQUIRE_FULL=1`).
- `sim/VEC.md`: status paragraph rewritten (integrated), measured bench table and the Python bench's cold-pool caveat added, three known-gap bullets added (no ABI leak test, clone-free stepping not implemented, random soaks reach no victory). `sim/BENCH.md`: "E2 integrate" section.

Gates:
- Build: `dotnet build sim/Sts2Sim.sln -c Release` 0 warnings 0 errors (after each merge, `--no-incremental` after the last, and on the final tree); Debug 0 warnings 0 errors.
- Tests (full suite, Release, final Core): 1098/1098 passed, 0 failed, PENDING 0 (427 s). Includes 15 VecTests + 6 VecEncoderTests. Server-named tests 31/31 (the 30 previous + `VecEncoderTests.Test_VecEncoder_ParityWithServer`). RunSoak 10⁴ runs × 5 characters, 687034 steps, 0 victories, 10000 defeats, 0 capped, 0 content/kernel-stub stops (identical to E1). Content soak: cards 1118 (0 capped), encounters 267 (2 capped), potions 128, relics 594, 0 failures; W1Gate F1 356 playouts pass.
- G0 (`Sts2Sim.Diff <sim-source-recreation-9392df>/tmp/handoff/diff-bundles/bundles --report /tmp/e2-diff.json`): bundles 4 (agree 4, hard divergence 0, skipped 0, error 0, sim exception 0); decisions 69/69 (100.00 %), 0 unobserved; outcome 4/4; soft diffs 0.
- Server self test `--selftest 100 --content ported_only`: 100 completed (0 wins), 0 crashed, 0 capped, 0 server errors, mean 67.6 steps (4.2 s).
- Python: `STS2VEC_REQUIRE_FULL=1 uv run --project sim/tools/vec pytest -q -rs` 63 passed, 0 skipped (64 passed with the NativeAOT dylib built by `scripts/vec_publish_aot.sh`, which adds the AOT round-trip test). `sts2vec check`: OK (64 envs × 200 steps, threads 1 vs 8: hashes equal, buffers byte-identical every step); also OK through the AOT dylib. Cross-process: `sts2vec digest --envs 256 --steps 300` in two processes, threads 1 and 8 → identical `state_hash_sha256` and `obs_sha256`. Eval runner `uv run --project sim/tools/eval pytest -q sim/tools/eval` 32 passed.
- Random soak `sts2vec bench --envs 1024 --steps 2000`: seed 0 / pool 64 → 2,048,000 actions, 29,864 episodes, 0 SIM_ERROR, 0 CONTENT_GAP, 0 ILLEGAL_ACTION; seed 1 / pool 1024 → 29,449 episodes, 0 / 0 / 0.
- Bench (rows in `sim/BENCH.md` "E2 integrate", load average 2.6–7.0): C# `vec` N 64/1024/4096 × threads 1/8 = 18.6k / 89.1k, 27.4k / 116.4k, 41.5k / 144.4k env-steps/s; Python `sts2vec bench` pool 1024 = 4.4k / 5.7k, 23.8k / 73.2k, 39.7k / 162.9k (pool 64: 12.1k / 20.7k, 29.6k / 54.1k, 39.9k / 86.7k). §D7 target ≥ 50k at N = 1024 / 8 threads met. Combat bench 142,439 decisions/s, Clone 2.172 us, StateHash 2.625 us.
- sts2rec: `uv run pytest -q` 701 passed, 4 skipped.
- `git status --short` before the commit: only tracked doc/source edits; no tmp/, bundles or saves.

Open issues:
- Core clone fidelity (V2 finding, kernel, not fixed): `RunSim.Clone()` breaks the aliasing between a claimed `PotionReward.Potion` and the belt copy (`BasicRewards.cs:93`, `Potion.CloneFor`); after a clone, `DiscardPotion` on the still-open Rewards screen flags only the belt copy, so clone-then-step and step-original hash differently. No gameplay effect found. Fix: alias to the cloned belt potion when `_wasTaken`.
- A snapshot of a slot still at a save-import load prompt would share `Pending.Load` between clones (no such save seen; StartPool re-imports pending saves per start).
- The Python bench does not pre-warm the pool (cold `NewRun` ~16 ms per first entry use); small-N Python rows with a large pool understate steady-state throughput. Consider a `--warm` option or an export that materialises templates.
- No draw-order leak test through the C ABI (C# only); prompt-option overflow (> 128) is untested (deck overflow and > 192 candidates are tested).
- The encoder fails closed (exceptions → SIM_ERROR/CONTENT_GAP); none seen in ~60k soak episodes.
- Clone-free `Step(a, answerer)` stepping (D7 later ticket) not started; resets are ~0.5–2 k/s at N = 1024.
- Owner commits carry `Co-Authored-By: Claude Opus 5.5` (session attribution rule) rather than the brief's Fable line; not amended.

## E2b integrate (2026-11-01)

Base: session branch `claude/sts2-eval-pipeline-a3aee7` at `b3ac9d6` (E2 docs). All four owner branches are single commits on `b3ac9d6`; none skipped.

Merged, in order (all `--no-ff`): e2b/linux `f842712` (merge `5e76601`), e2b/docker `e9bb9d2` (merge `4f401fe`), e2b/protocol `5c97a14` (merge `b715fc8`), e2b/version-delta `7871055` (merge `40e4c37`).

Conflicts: none. The four branches touch disjoint files (linux: loader.py, BENCH.md, VEC.md, vec README, vec_publish_aot.sh, new linux_validate.sh and docs; docker: Dockerfile, compose, docker/, Server Program.cs/HttpHost.cs, ServerBindTests.cs, PLAY.md flags paragraph; protocol: sim/tools/eval, protocol v0.1/v0.2 docs, handoff §11; version-delta: new scripts/ files and the migration playbook), so every merge was clean.

Fixes (integration): none needed. Two owner open items checked and closed:
- linux: "VEC.md claims a NativeAOT round-trip pytest that does not exist" — it exists (`sim/tools/vec/tests/test_loader.py::test_native_aot_library_in_subprocess`, defined only when `sim/Sts2Sim.Vec/bin/aot/Sts2Sim.Vec.{dylib,so}` exists, platform-aware). With the dylib present here the vec suite is 64 tests; on Huginn the AOT .so was published after pytest ran, hence 63.
- linux: "gate commands run `uv run --project sim/tools/eval pytest -q` without the path" — `git grep` finds no such command in the tree; all committed instances carry `sim/tools/eval`.

Gates (macOS, M4 Pro):
- Build: `dotnet build sim/Sts2Sim.sln -c Release` 0 warnings 0 errors; Debug 0 warnings 0 errors.
- Tests: `dotnet run -c Release --project sim/Sts2Sim.Tests -- Server Vec Parity EndToEnd SelfConsistency` 97 passed, 0 failed, PENDING 0 (27.2 s; includes the 3 new ServerBindTests).
- Server self test `--selftest 100 --content ported_only`: 100 completed (0 wins), 0 crashed, 0 capped, 0 server errors, mean 67.6 steps (4.2 s) — identical to E2.
- G0 (`Sts2Sim.Diff <sim-source-recreation-9392df>/tmp/handoff/diff-bundles/bundles --report /tmp/e2b-diff.json`): bundles 4 (agree 4, hard 0, skipped 0, error 0, sim exception 0); decisions 69/69 (100.00 %), 0 unobserved; outcome 4/4; soft diffs 0.
- Python: `uv run --project sim/tools/eval pytest -q sim/tools/eval` 67 passed; `STS2VEC_REQUIRE_FULL=1 uv run --project sim/tools/vec pytest -q` 64 passed (incl. the AOT round-trip); `uv lock --check` clean for both projects (the Dockerfile's `uv sync --frozen` depends on it).
- `sts2eval report` (v0.2 metric) recomputed in place on `tmp/eval/baseline-sonnet-a0-s10-v2` (smoke-a0-v1): progress 0.594 [0.444, 0.744], median 0.583, win 0/10 Wilson [0, 27.8 %], floor/48 63.1 %; and `tmp/eval/baseline-sonnet-a10-s20` (std-a10-v1): progress 0.310 [0.243, 0.377], median 0.314, win 0/20 Wilson [0, 16.1 %], floor/49 34.0 %. Both match the protocol owner's numbers; v0.1 reports kept beside them as `report.v01.{md,json}` (git-ignored).
- Version delta: `python3 -m unittest discover -s scripts -p test_version_delta.py` 19/19 OK; demo `scripts/version_delta.py decompiled-v0.107.1 decompiled-9cb4f1ad` 10.1 s: identical 2449, cosmetic 8, modified 957, moved 3, added 120, removed 7; content 546 / kernel 134 / out of scope 407; rerun plan full_suite, G0, G3, 101 filters, registry + knowledge moves (identical to the owner run). Report in `tmp/version-delta/v107-9cb/` (git-ignored).
- Docker proxy (no docker on this Mac): `dotnet publish -c Release -r linux-x64 --self-contained false sim/Sts2Sim.Server -o tmp/e2b/publish-linux-x64` succeeded, 0 warnings, ELF x86-64 apphost, 3.9 MB; `bash -n` on docker/entrypoint.sh and the new scripts clean.

Owner-run numbers carried into this record:
- Linux x86_64 (Huginn, 8 vCPU Xeon KVM, Ubuntu 24.04, .NET 9.0.22 user-level): build 0 warnings; full suite 1103/1103 (1171 s); filtered 100/100; selftest 300/300; vec pytest 63, eval pytest 43; AOT linux-x64 .so (gcc) PASS. Deterministic counts identical to the Mac (combat hash sink 40788C706BFA21E0, vec episodes 7,286 / 44,417 / 29,235 / 463). Throughput: combat 41.4k decisions/s (Mac 147k); C# vec 1024x8 28.1k env-steps/s at 500 steps (JIT warm-up), 50.5k at 3000 steps (§D7 met at steady state); Python sts2vec 1024x8 pool 64 hostfxr 21.6k, NativeAOT 61.4k. DOTNET_TieredCompilation=0 reaches 51.8k immediately. Details: docs/linux-validation-2026-11-01.md, sim/BENCH.md "Linux x86_64 (Huginn)".
- Docker (Tokyo VM, linux/arm64, 2 vCPU): cold build 73 s, image 578 MB, in-image build 0 warnings and RUN_TESTS=Server 39/39; selftest 200/200; /health 257 ms after start; SIGTERM stop 268 ms exit 0; vec-check OK; vec-bench 1024x4 15.7k env-steps/s (C# 19.2k); compose 2 replicas healthy; eval profile (random, 10 games) 10/10 replayable. VM returned to its starting state. Details: docs/docker.md.
- Protocol v0.2: docs/sts2-bench-protocol-v0.2-2026-11-01.md; frozen seed sets std-a0-v1 / std-a10-v1 / smoke-a0-v1 (sha256 in the seed files); random policy progress 0.065 (A0) / 0.051 (A10).

Open issues:
- Docker image validated on linux/arm64 only; the amd64 build and x86 container throughput are unmeasured (Huginn has no docker). The server has no auth; compose binds 127.0.0.1 by default. A runner must talk to one replica, never a balancer (game ids are per process).
- Huginn: `~/.cache/uv` is a dangling root-owned symlink (admin fix needed; linux_validate.sh uses a run-local UV_CACHE_DIR); root fs at 91 %; ~/.dotnet (579 MB) kept for reruns. Full suite at 19.5 min is near the 20 min timeout — a sharded/parallel runner would help Linux CI.
- Short Linux jobs are JIT-warm-up bound; consider documenting or setting TieredCompilation/TieredPGO after a cross-platform measurement (no runtime config changed).
- Protocol: no Sonnet row on the official std-a0-v1 set (~$43); Sonnet rows were produced on server c64217d (tree now includes 8c65874's display-path change) — declare equivalence or rerun before mixing rows; no NN eval entry point (`sts2vec eval --seedset`) and no replay-audit script yet; A10 act-3 second boss and GOLDEN_COMPASS scoring are unit-test only.
- Version delta: 28 content files with unique edits are missing from W0 version-deltas.json (e.g. Swift, EvilEye) and need triage; test filters are broad (token-based mapping); kernel-to-sim mapping finds 45/134; G0/G3 steps in version_update.sh need --bundles/--bundles-v2.
- Owner commits carry `Co-Authored-By: Claude Opus 5.5` (session attribution rule) rather than the brief's Fable line; not amended (amending would rewrite the merged owner commits).

## Gates runner

From 2026-11-02 the gates of each integration record are run by `scripts/gates.sh` (details and knobs:
[`docs/gates.md`](../../docs/gates.md)). Default = full set; `--quick` = the filtered suite of the E2b record.

| step | PASS when |
| --- | --- |
| `build-release`, `build-debug` | `dotnet build sim/Sts2Sim.sln -c <cfg> --no-incremental`: 0 warnings, 0 errors (English or Chinese MSBuild summary) |
| `tests` | full suite (`--quick`: `Server Vec Parity EndToEnd SelfConsistency W1Gate`): all passed, `PENDING total: 0` |
| `selftest` | `--selftest 100 --content ported_only`: all completed, 0 crashed, 0 server errors |
| `g0` | `Sts2Sim.Diff <STS2SIM_G0_BUNDLES>`: `overall:` bundles 4 agree 4, decisions 69/69 |
| `pytest-eval`, `pytest-vec`, `sts2rec` | pytest green (`STS2VEC_REQUIRE_FULL=1` for vec; sts2rec rerun once for the known wall-clock flake) |
| `py-scripts` | `scripts/test_*.py` unittests; `scripts/kernel_map.py --check` when the decompile tree is present |
| `bench-combat`, `bench-vec` | `--bench "<label>"` only: rows appended to `sim/BENCH.md` under the label |

An integrator records the closing table of `tmp/gates/<timestamp>/SUMMARY.txt` in the wave's section instead of
retyping the numbers.

## G integrate (2026-11-02)

Base: session branch `claude/sts2-eval-pipeline-a3aee7` at `9e95fd6` (docs/HANDOVER.md on top of `ebc055f`). All four owner branches are cut from `ebc055f`; none skipped.

Merged, in order (all `--no-ff`): g/gates-mapping `eb8e9cc` (merge `0171035`), g/compact-obs `575b248`+`b973d18` (merge `eb5d77a`), g/train-smoke `b438455` (merge `5ba20d9`), g/docker-amd64 `daa2a9e` (merge `d4e59ec`).

Conflicts: none. The expected hot spots auto-merged: the protocol doc (compact-obs appended §11, train-smoke edited §7), `sim/BENCH.md` and `docs/docker.md` (only docker-amd64 appended), `sim/tools/eval/README.md` (only compact-obs). `kernel_map.py --check` after all merges: still OK (no other branch added Core types or spec lines).

Fixes (integration, docs only):
- Protocol §7: the LLM policy interface now reads `choose(..., feedback, context=None)` (compact-obs added the kwarg; new policy classes must accept it).
- Protocol §10: the "dedicated NN eval entry point" open item struck (train-smoke's `sts2train-smoke eval`).
- `docs/HANDOVER.md` §7/§9: wave G marked landed with its headline numbers; kernel-map ticket closed; doc index gains `docs/gates.md`, `docs/train-smoke-2026-11-02.md`, `sim/specs/kernel-map.json`.

Gates (macOS M4 Pro, load average 6.4–8.7 from the concurrent E3 wave), `scripts/gates.sh` full on `d4e59ec`, 571 s, `tmp/gates/20261102T074217Z/SUMMARY.txt`:

| step | status | secs | key numbers |
| --- | --- | --- | --- |
| build-release | PASS | 8 | Release: 0 warnings, 0 errors |
| build-debug | PASS | 6 | Debug: 0 warnings, 0 errors |
| tests | PASS | 457 | full suite: 1115/1115 passed, 0 failed, PENDING 0, 456 s in runner |
| selftest | PASS | 6 | 100/100 completed, 0 crashed, 0 capped, 0 server errors, mean 67.6 steps |
| g0 | PASS | 1 | bundles 4/4 agree (hard 0, error 0, sim exception 0); decisions 69/69, 0 unobserved; outcome 4/4 |
| pytest-eval | PASS | 4 | 76 passed (67 + compact-obs's 9) |
| pytest-vec | PASS | 15 | 67 passed (`STS2VEC_REQUIRE_FULL=1`, incl. AOT round-trip) |
| sts2rec | PASS | 73 | 701 passed, 4 skipped |
| py-scripts | PASS | 0 | unittest 30 OK; kernel_map --check OK: 564 kernel files, mapped 374, no_port 190 (lines 104,637 / 34,344) |
| bench-combat, bench-vec | SKIP | 0 | no `--bench` |

Also: `uv run --project sim/tools/train pytest -q` 8 passed (13.9 s); `uv lock --check` clean for sim/tools/train and sim/tools/eval. Version-delta demo (`scripts/version_delta.py decompiled-v0.107.1 decompiled-9cb4f1ad`, 13.0 s): content 546, kernel 134, out_of_scope 407; rerun plan full_suite/G0/G3, 4 soak tests, 101 filters, registry + knowledge; kernel map: 95/134 changed kernel files with sim files (49 via the map only), mapped 91, no_port 43, unmapped 0 — identical to the owner run.

Owner-run numbers carried into this record:
- compact-obs: `--obs-mode compact` (renderer compact-v1, prompt_hash `01628730282753a2`; full mode byte-identical to v0.2, `e01daedf52519a6c`) is the CLI default; library default stays full. Probe smoke-a0-v1 ×3 games, Sonnet 5.5 effort low, 360 steps per mode: per call uncached input 1,253.7 → 790.2 (−37.0 %), cache read 2,186.6 → 2,783.6, cache write 18.4 → 64.7, output 91.3 → 83.9; cache hit 63.2 % → 76.5 %; cost/step $0.00390 → $0.00314 (−19.5 %), same-state −16.5 %; 0 illegal / fallbacks / API errors; spend $2.535. Parity: 7,290/7,290 baseline observations and 1,122/1,122 random-game observations, 0 failures, 0 unstable contexts. Run context = 3 cached system blocks (432 vs 580 µ$/step for one block).
- gates-mapping: owner full gates 570 s PASS (1114/1114); kernel map 564 files (type 201, spec 79, cite 23, pattern 116, curated 66; 26 patterns, 143 curated records); version-delta kernel coverage 45 → 95/134; W0 version-deltas 518 → 546 entries (MusicBox judged changed, 27 refactor_only). Scratch bench: combat 135,441 decisions/s, Clone 2.477 µs, StateHash 2.727 µs; vec 1024×8×500 78,141 env-steps/s.
- train-smoke: vec aux 10 → 14 columns (act_floor, map_row, map_col, act_rooms), encoder_version `dff07a6bc984b119` → `fc2eb34c07bc8513`, 26,710 → 26,726 bytes/env. 948,750-parameter PPO policy, 52 min shaped A0 on MPS: 20.3M env-steps, 83,190 episodes, 6,505 env-steps/s with learner (134,475 in env.step only; update 73 % of wall), 0 sim errors. std-a0-v1 NN eval: progress 0.277 [0.225, 0.329] (random-init 0.170, uniform random 0.065, Sonnet 5.5 0.499), 0/20 wins, 14 loss / 6 stuck (merchant removal loop), replay 20/20.
- docker-amd64 (Huginn x86_64, Docker 29.9.2): cold build 79.3 s, image 563 MB; in-image build 0 warnings, Server tests 39/39; selftest 200/200 18.6 s; /health 0.41 s; 20 random games 1,379 steps 4.19 s; vec-check OK; Python vec-bench 1024×8 19.1k/21.1k env-steps/s; C# vec 1024×8×500 28.0k/28.3k, ×3000 50.9k; combat 41,203 decisions/s, hash sink 40788C706BFA21E0 (= native). Fix: `/app/tmp → /data/tmp` symlink so `eval --spawn` without `--out` works as uid 1654.

Open issues:
- Compact obs reached −19.5 % cost/step, not the 30–40 % target (output tokens ~27 % of cost and Recent history unchanged). The 3-block run context (vs the brief's single block) is accepted here on the offline measurement. No full-vs-compact A/B on std-a0-v1 yet, so compact rows are listed separately (§11); sts2eval `__version__` still 0.2.0.
- Train smoke: the shaped objective learns a non-progress merchant loop (needs a stall cap / step cost / loop masking in the training env); the learner dominates wall time; sts2vec `add_fresh(act1='random')` vs server `POST /runs` start equivalence unchecked; the NN row is not leaderboard-eligible (smoke objective, `-dirty` repo sha).
- gates.sh has not run on Linux (Huginn needs `--skip g0`; no `timeout` on stock macOS); `sim/BENCH.md` has no gates.sh `--bench` row yet. Kernel-map spec source is heuristic; curated verdicts need review at the next game version; `version_update.sh` only prints a reminder to regenerate the map. MusicBox "changed" verdict is code-reading only.
- Docker: image `sts2sim:validate` (563 MB) left on Huginn; Huginn root fs 2.8 GB free; the Tokyo arm64 row claiming the compose eval profile passed at `ebc055f` is unexplained (it fails there without the fix).
- Running `uv run --project sim/tools/eval pytest -q` without the path still collects vec/train tests and fails on imports; always pass `sim/tools/eval`.

## J integrate (2026-11-02)

Scope: environment only (no training loops, no new LLM evaluation runs, no MCTS). Base: session branch
`claude/sts2-eval-pipeline-a3aee7` at `d69c9c3`; all six owner branches are single commits cut from `d69c9c3`.

Merged, in order (all `--no-ff`): j/fast-path `7e5565b` (merge `586d6b4`), j/determinize `70a2904` (merge `71428cf`),
j/parity-audit `69c53b6` (merge `1dff707`), j/env-spec-eval `1d077ba`+`7831e4f` (merge `69d44d1`), j/huginn-scale
`10462a9` (merge `52188c6`), j/version-complete `2b7e21b` (merge `6df05f0`).

Conflicts: two, both docs, both resolved by keeping both sides.
- `sim/BENCH.md`: fast-path's "Vec fast path" section and huginn-scale's "Docker host capacity" rows appended at the same
  place; kept both, fast path first.
- `docs/HANDOVER.md` §1: huginn-scale rewrote row 1.4, version-complete rewrote row 1.5; kept huginn-scale's 1.4 and
  version-complete's 1.5.
The expected code hot spots auto-merged without conflict: VecEnv (fast-path step call vs determinize's
`CloneDeterminized`), RunSim (fast-path made it `partial` and added poison checks; determinize added
`CloneDeterminized` and `Pending.Hooks`), `scripts/gates.sh` (fast-path's py-scripts step vs version-complete's header
and `GATES_COMMIT`). Interplay checked: `CloneDeterminized` goes through `Clone()`, so it throws on a poisoned run like
`Clone()`; a pending prompt (with or without determinize hooks) is a fast-path blocker, so the hook re-run path never
runs clone-free.

Fixes (integration):
- `sim/Sts2Sim.Core/Choice/PromptSites.g.cs` regenerated (`gen_prompt_sites.py`): parity-audit's read-only accessors on
  17 event classes moved the cited line numbers of 17 event prompt sites. Same 153 hazardous models, same 4 room sources,
  185 inventory records cross-checked; only `file:line` citations changed. Without this the py-scripts gate fails.
- `sim/specs/kernel-map.json` regenerated (`kernel_map.py`; one line, determinize's new `Random/Determinizer.cs`
  citation). Same 564 kernel files, mapped 374 / no_port 190.
- Local NativeAOT library rebuilt (`scripts/vec_publish_aot.sh`; git-ignored output): the stale dylib rejected the new
  `fast_path` config key and failed `test_native_aot_library_in_subprocess`. Not a code change; any checkout with an old
  `sim/Sts2Sim.Vec/bin/aot/` must rebuild it.
- Docs: `docs/HANDOVER.md` §1 rescored (five criteria 0-10 with evidence and the environment-level gaps) plus a new §1.6
  "what the environment still lacks", §2 map, §3 commands (`sts2vec eval`, fast-path A/B bench, `version_update.sh`,
  `hook_drift.py`, `gen_prompt_sites.py`, `sts2_cluster.sh`, `ci_linux.sh`), §4 leaderboard observation-version note
  and NN-track rows, §7/§8 rewritten (the "fast path 2.6x" and "event options 7/81" tickets closed with measured
  numbers). Protocol §8: note that rows 1-6 were measured on the pre-audit observation (compact-v1).
- `sim/ENV_SPEC.md`: no regeneration needed (`gen_env_spec.py --check` up to date; encoder `fc2eb34c07bc8513`; neither
  the fast path nor determinize changed the schema or added status bits).

Gates (macOS M4 Pro; load average 7-11 during the run), `scripts/gates.sh` full on `6df05f0` + the two regenerated
files, logs `tmp/gates/j-integrate/`:

| step | status | secs | key numbers |
| --- | --- | --- | --- |
| build-release | PASS | 3 | Release: 0 warnings, 0 errors (`--no-incremental`) |
| build-debug | PASS | 3 | Debug: 0 warnings, 0 errors |
| tests | PASS | 454 | full suite: 1131/1131 passed, 0 failed, PENDING 0 (incl. F2 pins, F7 clone-then-step soak, ChoiceFastPath soak, VecDeterminize, ParityAudit) |
| selftest | PASS | 4 | 100/100 completed, 0 crashed, 0 capped, 0 server errors |
| g0 | PASS | 0 | bundles 4/4 agree; decisions 69/69, 0 unobserved; outcome 4/4; soft diffs 0 |
| pytest-eval | PASS | 5 | 80 passed |
| pytest-vec | PASS | 11 | 95 passed (`STS2VEC_REQUIRE_FULL=1`, incl. AOT round-trip, env-spec, protocol parity, determinized clone) |
| sts2rec | PASS | 75 | 701 passed, 4 skipped |
| py-scripts | PASS | 0 | unittest 53 OK; `gen_prompt_sites --check` OK; `kernel_map --check` OK (564 files, 0 unmapped) |

Also: `uv run --project sim/tools/train pytest -q` 8 passed; `gen_env_spec.py --check` up to date.

Bench (`sim/BENCH.md` "J integrate"): Vec 1024 envs x 8 threads x 500, medians of 3 interleaved pairs under load 5-14:
A0 104,858 (off) / 107,522 (on) env-steps/s, 73.6 / 69.6 CPU µs; A10 111,391 / 121,284, 69.6 / 63.5 CPU µs; fast
fraction 0.832 / 0.820; gen0 collections 15-17 → 4-6; episode counts identical. Light-load single run: 126,522
env-steps/s, 61.0 CPU µs.

Owner-run numbers carried into this record (details in each owner's docs):
- fast-path: soak 2,000 runs / 577,867 steps, 72.1 % clone-free, 0 prompts from unlisted sources, 0 prompts after
  `MayPrompt == false`; 20,516 sampled clone-free steps hash-equal to the tape engine; RunSim step 1.6-1.9x.
- determinize: 900 states same observation bytes / different hash; 4,506 RNG objects all reseeded; 47 prompt hooks
  resolve differently with no tape divergence; CloneDeterminized ≈ Clone() + 2-8 µs.
- parity-audit: 1 leak fixed (AMBERGRIS_POWER); 39 event option keys resolved (walk 61/64 events, 0 `?`); 1,500-state
  server↔Vec checklist; +185 chars per server-text observation (12.5 %).
- env-spec-eval: NN-track baselines random 0.087 / 0.045, first 0.193 / 0.102, heuristic 0.221 / 0.104 (A0 / A10).
- huginn-scale: Huginn 26-28 random games/s per host, 110-150 MiB per server, AOT Vec 62-63k env-steps/s in container.
- version-complete: synthetic version bump end to end 476 s exit 0; hook drift finds exactly the 1 planted change;
  `ci_linux.sh` on Huginn PASS (326 s).

Open issues (environment): see `docs/HANDOVER.md` §1.6. Integration-specific: `scripts/gates.sh` was run on macOS only
in this wave (Linux via `ci_linux.sh` ran on version-complete's commit, not on the merged tree); the merged tree's
Docker image was not rebuilt; Vec has no stall cap (only `step_cap`).

## E3 integrate (2026-11-02)

Scope: the fidelity loop (REC recorder on the real game, MCP coverage farm, exporter save anchors, G3 anchor modes, port
fixes from the farm triage). Base: session branch `claude/sts2-eval-pipeline-a3aee7` at `939ceb5` (J integrate). The
three owner branches are cut from `ebc055f`, before waves G and J.

Merged, in order (all `--no-ff`): e3/recorder `da3b736`+`d6b8ce3` (merge `19b87eb`), e3/farm `6dfde01`+`4568426`+
`0fdd411`+`d8a5c31` (merge `28f7655`; `6dfde01` is the cherry-pick of `da3b736`, same tree, so it merged as a no-op),
e3/triage `160afdb`+`136449c` (merge `d7bd066`).

Conflicts: none. One file changed on both sides and auto-merged: `Combat/Pipeline/PlayerView.cs` (J parity-audit:
`IsVisibleToPlayer` reads `power.IsVisible`; triage: `TryMappedDisplayAmount` maps AUTOMATION_POWER, so the strict diff
projection stops throwing a gap for it; `DisplayAmountToPlayer` keeps its own AUTOMATION case, now shadowed by the
mapped one with the same value).

Seams checked, no code change needed:
- The triage port fixes are intact on the merged tree: `RelicCmd.Obtain` grab-bag removals and `FloorAddedToDeck`
  (`ApproximatedRule` flag gone), starting relics on floor 1, combat-end hooks get the `CombatRoom`,
  `RunImporter.ImportedPlayerCombatId` 0, `Tags` on six weak encounters, AUTOMATION display amount (E3TriageTests 8/8).
- J fast path: none of the fixes adds a prompt site; `gen_prompt_sites.py --check` is OK with the committed table
  (153 hazardous models, 4 room sources, 185 inventory records). The fast-path soak and hash-equality tests pass in the
  full suite.
- J determinizer: `RelicGrabBag.ReshuffleDeques` reshuffles what is left in the bags; after the triage fix an obtained
  relic is no longer in them, as in the game. VecDeterminize tests pass.
- F2 run pins: triage's refreshed `PinnedRunStreams` (`1:136:5F70FE94F60BE08F 2:114:064C282C22EA469C
  3:160:CC4B958FA4D42499 4:400:BE4777CCA5361B81`) pass unchanged; wave J changes no run hash, so nothing was regenerated.

Fixes (integration):
- `sim/specs/kernel-map.json` regenerated (`scripts/kernel_map.py`): the triage files' new decompile citations
  (RunImporter → Combat/CombatState.cs, RunLoop → Entities.Players/Player.cs, the six weak encounters →
  Models/ActModel.cs, TurnFlow.CombatEnd → Combat/CombatManager.cs) and the spec lines of the E3 diff report. Same 564
  kernel files, mapped 374 / no_port 190, 0 unmapped; files with a `cite` source 23 → 25. Without it the py-scripts gate
  fails.
- Docs: `docs/HANDOVER.md` (§1 fidelity row, §1.6, §2, §3 farm / export / G1-G3 commands, §4 NN rows re-measured, §5,
  §7, §8, §9) and this record.

Gates (macOS M4 Pro; load average 3.3 / 3.4 / 4.3 at the start), `scripts/gates.sh` full on `d7bd066` + the regenerated kernel map,
556 s, logs `tmp/gates/e3-integrate/`:

| step | status | secs | key numbers |
| --- | --- | --- | --- |
| build-release | PASS | 3 | Release: 0 warnings, 0 errors (`--no-incremental`) |
| build-debug | PASS | 2 | Debug: 0 warnings, 0 errors |
| tests | PASS | 452 | full suite: 1139/1139 passed (J's 1131 + E3TriageTests 8), 0 failed, PENDING 0 (incl. F2 pins, F7 clone-then-step, ChoiceFastPath soak, VecDeterminize, ParityAudit, RelicPotion, E3Triage) |
| selftest | PASS | 5 | 100/100 completed, 0 crashed, 0 capped, 0 server errors, mean 67.8 steps |
| g0 | PASS | 1 | bundles 4/4 agree (hard 0, error 0, sim exception 0); decisions 69/69, 0 unobserved; outcome 4/4 |
| pytest-eval | PASS | 4 | 80 passed |
| pytest-vec | PASS | 11 | 95 passed (`STS2VEC_REQUIRE_FULL=1`) |
| sts2rec | PASS | 74 | 705 passed, 4 skipped (701 + the exporter's 4 new tests) |
| py-scripts | PASS | 0 | unittest 53 OK; `gen_prompt_sites --check` OK; `kernel_map --check` OK (564 files, mapped 374, no_port 190, 0 unmapped) |

Also: local NativeAOT library rebuilt (`scripts/vec_publish_aot.sh`, git-ignored output; the old one was built before
the E3 Core fixes); `STS2VEC_AOT_LIB=… sts2vec check` OK and `sts2vec eval --policy heuristic --seedset std-a10-v1`
gives the managed build's 0.110 through it.

Fidelity on the merged tree. Same commands as `sim/specs/diff-report-e3-farm-2026-11-02.md` §1; inputs and reports in
the git-ignored `tmp/farm/2026-11-02/integrate/`. Every report equals the triage owner's final report run for run
(paths normalised): the merge changed no fidelity number.
- Export (`export-bundles-v2 --root <farm>/sessions-all --sim-core ../sim/Sts2Sim.Core`, merged exporter and Core
  registry): 39 runs, 787 segments (combat 177, room 306, map 304); eligible combat 117, room 249, map 304; 4,288
  decisions, 2,806 eligible. Bundles and save copies are byte-identical to the triage export; only the paths in
  `reasons.json` differ.
- G1/G2 (`--v2 --baseline sim/specs/diff-baseline-e3-farm.json`): 787 segments, agree 126, hard 288, skipped 373, error 0,
  sim exception 0; decisions 2,664/2,919 (combat 2,664/2,673); outcome 126/159; ratchet pass (787 segments, 2,664
  agreed decisions checked, 0 violations, 0 newly agreeing), i.e. equal to the baseline. The 42 combat segments that do
  not agree: `final_hp` 33 (recorder_gap T7, the recorder writes HP before end-of-combat heals), `select.screen_type` 8
  (projection_gap), `turn.phase` 1. The 246 room divergences are schema only at decision 0 (`screen` 131, `player`
  relics 115): V2Runner still has no G3Schema adapter, so the run-level G2 numbers are those of `g3 --anchors-every`.
- G3 from the seed: 39 runs, 30 reach the recorded end; decisions 3,320/3,324 (99.88 %); first divergence none 30,
  port_bug 7, truncated 2.
- G3 `--anchors` (resume at the next save after a divergence): 785 segments replayed, 778 agree, 2 skipped; decisions
  3,578/3,582; 7 divergences (harness label port_bug; triage classes below).
- G3 `--anchors-every` (every anchored segment from the game's save: the G2 save-import check): 782 replayed, 774
  agree, 5 skipped; decisions 3,601/3,603 (99.94 %); 31 runs reach the end. By start: seed 39/39 segments (93/93
  decisions), room_entry save 303/300 (3,203/3,205), combat_exit save 139/135 (197/197; the 4 others out_of_scope,
  `extra_rewards` on SFY7CMFJHB), chained 301/300 (108/108). Divergences port_bug 4, out_of_scope 4.
- G3 `--save-check`: 437 chained segments with a save anchor; 258 equal except `unlock_state` (the sim runs
  `UnlockProfile.All`) and `map_point_history` (known R1 gap), 1 of them fully equal; 179 not exportable (174 inside a
  combat, 4 `extra_rewards`, 1 pending prompt); 0 other differences.
- G0: in the gates table (4/4 bundles, 69/69).
- Human corpus, against W4.1 (the W4.1 inputs: `sim-source-recreation-9392df/tmp/diff-v2-w41i` for G3, and for the
  combat gate the W4.1 integrator's scratchpad dir `w41i/in` = v1 g0 + g0-ineligible bundles + `diff-v2-w41i`):
  - G3: decisions 1,532/1,537 (W4.1) → 1,921/1,924 (99.84 %); runs at the recorded end 1 → 1 (K4FB4086NTRE). Floors
    agreed per run (W4.1 → E3): G0G2 13 → 13, NBND 11 → 29, DSB3 22 → 35, P2UZ 13 → 13, Q3X6 2 → 2, GM28 27 → 27,
    K4FB 17 → 17 (end), EC03 11 → 11, PLRU 11 → 11, G1YF 7 → 7 (truncated), old-prefix 0 → 0. No run drops. First
    divergences: port_bug 7, content_not_ported 1 (DSB3 s99, FAKE_MERCHANT is still a stub), out_of_scope 1, truncated
    1, none 1.
  - Widened combat gate (`--v2`): 117 compared combat segments, 109 agree, decisions 1,599/1,607 (99.50 %), outcome
    109/109: unchanged from W4.1, the same 8 rows (4 v0.107.1 exporter_ineligible, Q3X6 s5 discard count, GM28
    s26/s54/s65 relic run-time state at combat start).
- NN track re-measured on the merged tree (`sts2vec eval`, std-a0-v1 / std-a10-v1): random 0.087 / 0.045, first
  0.194 / 0.103, heuristic 0.221 / 0.110. Protocol §8 lists 0.087 / 0.045, 0.193 / 0.102, 0.221 / 0.104, measured
  before the E3 rule fixes; the rows were not changed.

Open (fidelity), from the triage report §4, unchanged by the merge:
- 7 open divergences under `--anchors`: VSQP6ESN1F f7 PUNCH_OFF_EVENT_ENCOUNTER (event → combat transition, port_bug;
  also G0G2 on the human corpus); UF51XE6ZV1 f11 GAMBLING_CHIP start-of-turn hand select phase name (projection_gap);
  UUR25UNBL5 f5 a dismissed card reward emitted as an event `choose` (exporter_bug); 3ZGJFKTD64 f9 STRATAGEM_POWER pile
  select order (recorder_gap candidate); K4P3FWUZYN f13, 545XJP726S f8, SDR1TFEZ2U f15 only on the chained replay
  (hidden draw-pile state, turn counter; each agrees from its save).
- Exporter: R2 per-combat replays (`native/replays/*.mcr`, 162 files) are not indexed (the saves cover every combat
  start); ancient_exit saves are indexed but not bound; 45 segments `content_not_ported:event`; 4 combat_exit saves with
  `extra_rewards` are out of scope for the importer.
- Coverage: the farm reached act 2 once (floor 22) and act 3 never; A10 runs stopped by floor 14. Acts 2 and 3 need a
  stronger driver (a sim-trained policy or an LLM).
- 8 real-game worker crashes in the farm (5 Defect; the two with a macOS crash report are unhandled managed exceptions,
  their text lost to the old relaunch truncation of `game.log`; `run_farm.py` now copies `game.log` to
  `<farm>/crashlogs/` before a restart). Not triaged.
- G1 on the human combat gate is 99.50 %, under the 99.9 % target. Farm combat G1: decisions 2,664/2,673 (99.66 %;
  the 9 are 8 `select.screen_type` projection gaps and 1 `turn.phase`), outcomes 126/159 (the 33 are `final_hp`,
  recorder timing).

## Train-cycle integrate (2026-11-03)

Scope: the environment and trainer branches of the Silent A10 training cycle. Base:
`claude/recorder-performance-lag-0f9d6d` at `b1d4a21` (PR #30); the three owner branches are cut from `b1d4a21`.

Merged, in order (all `--no-ff`): train/env-cap-schema `5fd17d6`+`c20f882` (merge `86d2ac5`), train/trainer-colab
`3540ea7`+`19857d5`+`ef35697` (merge `5918a05`), train/silent-seedsets `8eb7382` (merge `353d136`). Conflicts: none
(Vec + `sim/tools/vec`; `sim/tools/train` + `scripts/train`; seed sets + two tests).

Seams checked:
- Schema: the libraries built from `b1d4a21` and from the merged tree differ only in encoder_version
  (`fc2eb34c07bc8513` → `5dbfa09dd9dcb5ed`), the six appended buffers, four caps, `EndKind` (+FloorStall) and the new
  `ItemKind` enum; the 35 old buffer entries (dtype, shape, fields) are identical. The trainer's 27 inputs, its
  vocabularies and embedded enums are unchanged; preset sizes too (948,750 / 1,175,150 / 15,804,270).
- Floor cap: the trainer passes `--floor-step-cap` only when given; unset, `VecEnv`'s default (80) applies on the new
  library, so the cap is on in training by default. `sts2vec eval` now uses the env cap; NN track re-measured on the
  merged tree: random 0.087 / 0.045, first 0.194 / 0.103, heuristic 0.221 / 0.110 (unchanged), replay 20/20.

Follow-ups (integration):
- `5cf12ec` resume across encoder versions (`features.check_spec_compat`): a checkpoint resumes when the model's inputs
  are identical (27 buffers' shapes and column maps, MODEL_VOCABS / ENUMS sizes, pointer-enum members, card_vars);
  anything else is refused before the env is built (exit 2, every difference listed, run directory untouched).
  config.json records `encoder_version`, `checkpoint_encoder_version`, `resume_compat` (also per session) and
  `env.floor_step_cap` (the cap in effect); metrics count `floor_stall`. `sts2train eval` applies the same rule.
  `tests/test_resume_compat.py` (14 tests) pins the real Stage-1 spec (`tests/data/spec-fc2eb34c07bc8513.json`).
- `0b1bbd3` `smoke_bundle.sh`: the CPU run passes `--floor-step-cap 80` and checks it in config.json; new step checks
  the six appended buffers (documented shapes, filled) under the bundled library.
- `79acb96` docs: `docs/HANDOVER.md` (§1 rows 1.1 / 1.2, §1.6 items 2-3 done, §2, §3 training / bundle commands, §4
  Silent sets, §8); the trainer README changed with `5cf12ec` / `0b1bbd3`; this record.

Gates (macOS M4 Pro, `DOTNET_PROCESSOR_COUNT=6`), `scripts/gates.sh --quick` on `79acb96` (docs only after `0b1bbd3`),
161 s, logs `tmp/gates/train-cycle-integrate-quick/`:

| step | status | secs | key numbers |
| --- | --- | --- | --- |
| build-release | PASS | 5 | Release: 0 warnings, 0 errors (`--no-incremental`) |
| build-debug | PASS | 3 | Debug: 0 warnings, 0 errors |
| tests | PASS | 40 | quick (Server Vec Parity EndToEnd SelfConsistency W1Gate): 125/125 passed, 0 failed, PENDING 0 |
| selftest | PASS | 6 | 100/100 completed, 0 crashed, 0 capped, 0 server errors, mean 67.8 steps |
| g0 | PASS | 0 | bundles 4/4 agree (hard 0, error 0, sim exception 0); decisions 69/69, 0 unobserved; outcome 4/4 |
| pytest-eval | PASS | 8 | 84 passed |
| pytest-vec | PASS | 18 | 104 passed (`STS2VEC_REQUIRE_FULL=1`) |
| sts2rec | PASS | 77 | 762 passed, 6 skipped (no rerun) |
| py-scripts | PASS | 1 | unittest 53 OK; `gen_prompt_sites --check` OK; `kernel_map --check` OK (564 files, mapped 374, no_port 190): the decompile tree no longer has the 3,761 `._*` sidecars (0 left) |

Also on the final tree: full C# suite 1,144/1,144 passed (E3's 1,139 + VecTests FloorCap 2 + Append 3), 0 failed,
PENDING 0, 461 s in the runner; `-- Vec` 50/50; `uv run --project sim/tools/train pytest -q` 41 passed (27 + the 14
resume-compat tests); `gen_env_spec.py --check` up to date (5dbfa09dd9dcb5ed).

Colab bundle `tmp/colab/sts2train-0b1bbd3a4e.tar.gz` (5,450,745 bytes, sha256
`c754754f5f284f326c62d1b1a81551fbbb6bbbda4056827de31543b96365d74b`; AOT key `64d964770dd0`, built on raven in 160 s,
`Sts2Sim.Vec.so` sha256 `99bfc2b6…d8793c`, newest symbol GLIBC_2.34; copy on raven under
`/data/raven/hack-sts2/tmp/integrate-train-20261103/`). Validated on raven (8 vCPU, idle) in a plain `python:3.13`
container, no dotnet:
- `sts2vec check` on the AOT library: `version sts2vec/5dbfa09dd9dcb5ed`, 41 buffers, 30,942 bytes/env, threads 1 vs 8
  byte-identical, `check: OK`.
- `smoke_bundle.sh` (small preset, 256 envs, 2 min, `--floor-step-cap 80`): config.json `config.floor_step_cap` 80 and
  `env.floor_step_cap` 80; 4 updates, 32,768 env steps, 395 episodes, 370 env-steps/s with the CPU learner (env only
  43,328), 0 sim errors / content gaps, std-a0-v1 eval at u4 progress 0.151 (20/20 replayable); raw bench 74,709
  env-steps/s (1024 envs x 8 threads).
- Appended buffers under NativeAOT (256 envs x 400 random steps): event_option_rows 9,438, with_numbers 2,014,
  with_items 873, numbers_nonzero 2,503, ancient_shown 102,400, discard_rows 285,346, exhaust_rows 25,675,
  draw_marked_rows 2,190, hand_x_nonzero 303, equal to the managed build on the same rollout. Per-buffer sha256 of a
  seeded 256 x 400 rollout: all 41 buffers and the state hash (`7735fd5fd42aa068`) identical between the NativeAOT
  linux-x64 library and the managed macOS build, so the publish keeps every reflected event member (no rd.xml /
  DynamicDependency needed; Sts2Sim.Core is a TrimmerRootAssembly).
- Resume across encoder versions with real bundles: the old bundle (`sts2train-ef356974fa`, fc2eb34c07bc8513) trains 2
  updates; the new bundle's `run_train.sh` resumes that run (`--resume auto`) to update 4: config.json
  `checkpoint_encoder_version` fc2eb34c07bc8513, `encoder_version` 5dbfa09dd9dcb5ed, `resume_compat.model_inputs`
  identical (other differences: encoder_version, EndKind 5 -> 6, ItemKind new), `env.floor_step_cap` 80. `sts2train
  eval` of the old checkpoint on silent-a10-v1 (seed set from the bundle): 20 games, 20/20 replayable.

## Deckbuild integrate (2026-11-04)

Scope: the two simulator-side branches of the Silent A10 cycle and one trainer follow-up. Base:
`claude/recorder-performance-lag-0f9d6d` at `ef70ee6` (PR #30); both owner branches are cut from `e80ea22`.

Merged, in order (both `--no-ff`): train/vec-branch `9fad18f` (merge `568710c`), train/env-deckbuild
`810eae7`+`874ce2f`+`f2430c6`+`5be2376` (merge `4ba5de4`). Textual conflicts: none (git merged `sim/ENV_SPEC.md`,
`VEC.md`, `CONTRACTS.md`, `VecEnv.cs`, `VecTests.cs`, the trainer README and `gen_env_spec.py` on disjoint hunks).

Seams resolved in `4ba5de4`:
- `sim/ENV_SPEC.md`: the generated §5 block was already right for the merged library (`gen_env_spec.py --check`:
  `3b73317739c650bd`, 43 buffers, 35,062 bytes/env, `start_entry >= -1`); the two branches' prose came to 410 lines, so
  it is condensed back to the 400-line budget without dropping a rule (the rewards-screen paragraph folded into the
  `TakeRewardCard` row; §6 rewrapped; §3 / §7 / §8 / §10 sentences shortened); §9 keeps 21 exports; §11 35.1 KB.
- `VecEnv.BeginBranchEpisode` also clears the `Raw` legal-action scratch, as `StartSlot` does since the macros.
  Branch destinations copy `reward_choices` / `map_routes` with the rest of the view (`BranchCopy` = every buffer but
  the signals); the branch tests (determinized copied view = each destination's own encoding) pass on the merged tree.

Merged library: ABI 2, 21 exports, encoder_version `3b73317739c650bd` (branch: `5dbfa09dd9dcb5ed` →
`e9baaefc39811631` macros → `dcbee4daff61c155` map_routes → `3b73317739c650bd` card_static), 43 buffers, 35,062
bytes/env. NN track (`sts2vec eval`, 20 games each, every row 20/20 replayable; std rows equal the branch's):

| seed set | random | first | heuristic |
| --- | --- | --- | --- |
| `silent-a0-v1` | 0.074 | 0.141 | 0.183 |
| `silent-a10-v1` | 0.037 | 0.112 | 0.117 |
| `std-a0-v1` | 0.082 | 0.194 | 0.221 |
| `std-a10-v1` | 0.044 | 0.103 | 0.110 |

Follow-ups (integration):
- `b86167e` enum-widening resume / eval: `features.spec_diff` → (block, widened, other); an embedded enum that only
  gained members appended at its end (checked by member names; `ModelSpec` now records them for all 15 embedded enums)
  is "widened", not refused. `model.ENUM_EMBEDDINGS` names the tables each enum sizes (`RunActionKind`: `kind_emb`,
  `c_kind`); `widen_state_dict` keeps the checkpoint's rows and zeroes the new ones, `restore_checkpoint` does the same
  for their Adam moments; config.json `resume_compat.model_inputs` = `widened` + `widened_enums` (also per session);
  `sts2train eval` builds a widened checkpoint's model with `load_checkpoint(library=...)` and records `spec_compat`.
  Everything else refused before is still refused. `tests/test_resume_compat.py` 25 tests: real Stage-1
  (`fc2eb34c07bc8513`) and pre-macro (`5dbfa09dd9dcb5ed`, new `tests/data` copy from the s4a checkpoint) specs widen
  `RunActionKind` 21 → 23; widened logits of kinds < 21 and values equal the checkpoint model's bit for bit on real
  observations with macro rows; preset pins 948,974 / 1,175,438 / 15,805,070.
- `e877036` `smoke_bundle.sh`: also checks `reward_choices` rows = rewards-screen macro candidates, `map_routes` only at
  map nodes, `card_static`, ABI ≥ 2 and an exact branch; `--torch-threads THREADS`.
- `b26a383` `Test_VecBranch_ConcurrentWithStepSerialises` waits for real overlap (one `-- Vec` run on the loaded box
  saw 2 of the required 10 mid-run branches; the assertion depended on thread scheduling).

Gates (macOS M4 Pro, load 6-9 from other agents, `DOTNET_PROCESSOR_COUNT=4`): `scripts/gates.sh --quick` on
`e877036`, 194 s, logs `tmp/gates/deckbuild-integrate-quick/` (repeated on the final tree, last bullet below):

| step | status | secs | key numbers |
| --- | --- | --- | --- |
| build-release | PASS | 4 | Release: 0 warnings, 0 errors (rc 0) |
| build-debug | PASS | 4 | Debug: 0 warnings, 0 errors (rc 0) |
| tests | PASS | 44 | quick (Server Vec Parity EndToEnd SelfConsistency W1Gate): 142/142 passed, 0 failed, PENDING 0, 42 s in runner (rc 0) |
| selftest | PASS | 5 | 100/100 completed, 0 crashed, 0 capped, 0 server errors, mean 67.8 steps (rc 0) |
| g0 | PASS | 1 | bundles 4/4 agree (hard 0, error 0, sim exception 0); decisions 69/69, 0 unobserved; outcome 4/4 (rc 0) |
| pytest-eval | PASS | 40 | 84 passed in 39.66s |
| pytest-vec | PASS | 16 | 117 passed in 15.59s |
| sts2rec | PASS | 77 | 762 passed, 6 skipped in 76.64s (0:01:16) |
| py-scripts | PASS | 0 | unittest 53 tests OK; prompt_sites --check OK; kernel_map --check OK: 564 kernel files; mapped 374, no_port 190; lines mapped 104637, no_port 34344 |

Also: full C# suite 1,162/1,162 passed, 0 failed, PENDING 0 (481 s in the runner); `-- Vec` 67/67 (after `b26a383`);
`-- Parity Server` 45/45; `uv run --project sim/tools/train pytest -q` 97 passed; `gen_env_spec.py --check` up to date.

Colab bundle `tmp/colab/sts2train-e877036880.tar.gz` (7,029,564 bytes, sha256
`ead2435b9ebb732d85f5bd9fadbe84c66c865f3c6dc8cf832f0d2b3a3e027fba`; AOT key `8f89eba48d17`, built on raven in 236 s,
`Sts2Sim.Vec.so` sha256 `254efb10…f9fd6cc`, newest symbol GLIBC_2.34; 148 Silent A9 start saves; copy on raven under
`/data/raven/hack-sts2/tmp/integrate-deckbuild-20261104/`). `b26a383` and the docs commit change nothing the bundle
ships. Validated on raven in plain `python:3.13` containers (`--cpus 4`, 4 threads; box shared with a lookahead job,
load 9-10), torch 2.15.0+cpu in a venv on `/data/raven`:
- `smoke_bundle.sh` (small preset, 256 envs, 2 min, saves at 0.5): `sts2vec check` ABI 2, 21 exports,
  `sts2vec/3b73317739c650bd`, 43 buffers, 35,062 bytes/env, threads 1 vs 4 byte-identical, `check: OK`; floor cap 80 in
  effect; 1 update (31 s per update on the contended box), eval u1 std-a0-v1 0.078 (20 games); under the NativeAOT
  library the appended-buffer counts equal the managed macOS build's on the same rollout (`reward_choice_rows` 12,777
  = `macro_candidates` 12,777, `map_route_cells` 6,155,123, `event_option_rows` 9,574, ...), card_static 598 × 30, exact
  branch = source; raw bench 32,961 env-steps/s (1024 envs × 4 threads).
- Signal flags (`--aux-heads --deck-counts --acq-shaping --start-saves <bundle>/saves --save-mix 0.35 --floor-step-cap
  80`, small preset, 2 min): 1,210,256 parameters, 4 updates, 16,384 env steps, 0 sim errors / content gaps,
  `aux_win_loss` 0.6939 → 0.6789; final eval on silent-a0-v1 progress 0.162, 20/20 replayable.
- Widening with a real checkpoint: s4a `best.pt` (large preset, update 120, encoder `5dbfa09dd9dcb5ed`, 21 kinds;
  sha256 `f3a10db4…3088fc8ad`) resumed for one update on the new library (cpu, `--allow-big-local`, 32 envs, rollout
  4, minibatch 128, `--max-updates 121`): config.json `resume_compat.model_inputs` widened, `widened_enums`
  RunActionKind 21 → 23 (+TakeRewardCard, +TakeRewardAlternative); checkpoint u121 on `3b73317739c650bd`, 15,805,070
  parameters. `sts2train eval` on silent-a0-v1 (greedy, 20/20 replayable each): `best.pt` on the old library
  (`sts2train-ef70ee6273`) 0.272 [0.244, 0.299]; `best.pt` widened on the new library 0.302 [0.268, 0.336]; u121
  0.291 [0.254, 0.328]. The policy carries over (the new library also differs in the shop potion rule and in how card
  rewards are taken, so the scores need not match).
- raven cleanup: build cache unused for 2.5 h pruned before the AOT build (3.3 GB, on `/data/raven`; the root disk is
  not involved); the venv and checkpoint copies under the validation directory were deleted afterwards.
- Not verified: CUDA (no GPU run of this bundle); the Colab A100 path is unchanged code.
- Final tree: `scripts/gates.sh --quick` on `2aa46dd` (code as of `b26a383`; only this line changed after it), 158 s,
  every step PASS with the same key numbers as above except the timings (tests 142/142 in 41 s, pytest-eval 84,
  pytest-vec 117, sts2rec 762 + 6 skipped), logs `tmp/gates/deckbuild-integrate-final/`; trainer pytest 97 passed.

## Loopbreak integrate (2026-11-04)

Scope: train/loopbreak `059604a` (on `ef70ee6`): a loop breaker for the greedy eval policy. `sts2train/loopbreak.py`
keeps, per env slot, a 64-bit digest of the 27 model-read buffers for the last 64 decisions and the action taken there;
back at a seen observation it masks the rows already taken there, and from the 3rd return (`--loop-breaker-k`) keeps
only the rows that leave the screen (ProceedRewards, LeaveShop, LeaveRestSite, LeaveEvent, SkipTreasureRelic, EndTurn,
PromptConfirm, PromptCancel) when one is legal. On by default in `sts2train eval` and the periodic eval
(`--loop-breaker {off,on}`; summaries gain `loop_breaks` / `no_op_actions` / `cycle_revisits`); `--rollout-loop-breaker`
(default off) applies the same mask to the rollout sampling. Merged `--no-ff` into the Deckbuild tree (`91d43ea`) as
`a8d91cf`.

Conflicts, both sides kept: `evaluate.py` imports, the `evaluate_model` / `evaluate` signatures (`spec_compat` and
`loop_breaker` / `loop_breaker_k`), `evaluate`'s `load_checkpoint(library=...)` and its call; `ppo.py` imports
(`LoopBreaker` next to `widen_state_dict` / `widened_tensors`).

Seams resolved in `a8d91cf`:
- The leave kinds resolve by name against the library's `RunActionKind`: `evaluate_model` builds the breaker from the
  library's spec (`ModelSpec.from_schema(schema_of(load()))`), not from a widened checkpoint's; the trainer from
  `env.schema`'s. config.json `loop_breaker.leave_kinds` lists the names found, so appended kinds shift no id.
- `TakeRewardCard` (21) / `TakeRewardAlternative` (22) are real actions, never leave rows (`ProceedRewards` declines).
- `loopbreak.py` / README: library `3b73317739c650bd` removes the potion no-op and the card-reward cycle at the source;
  illegal indices and the shop removal / cancel cycle remain.
- `tests/test_loop_breaker.py` +3: leave kinds by name (appended / inserted members), the macros on a rewards screen
  (the k-th return takes ProceedRewards over a better-ranked TakeRewardCard; without a leave row the best remaining
  macro row), the library's own enum.

Owner validation (library `5dbfa09dd9dcb5ed`, 4 CPU threads, breaker off → on): s3c A10 u160 on silent-a10-v1 progress
0.156 → 0.160, stuck 2 → 0, no-op actions 152 → 3; s5b A0 u160 on silent-a0-v1 0.330 → 0.500, stuck 12 → 0, no-ops
232 → 9, cycle revisits 681 → 12; 20/20 replayable both.

Gates: `scripts/gates.sh --quick` on `a8d91cf`, 159 s, every step PASS (Release / Debug 0 warnings; tests 142/142;
selftest 100/100; g0 4/4, 69/69; pytest-eval 84; pytest-vec 117; sts2rec 762 + 6 skipped; py-scripts unittest 53,
prompt_sites / kernel_map `--check` OK), logs `tmp/gates/loopbreak-integrate-quick/`; trainer pytest 111 passed (97 +
14 loop-breaker tests).

Colab bundle `tmp/colab/sts2train-a8d91cf636.tar.gz` (7,036,915 bytes, sha256
`2e3ee66cde758a0b9f70de3b1e949ecf8964b5fee4477544b7e9b5b737f362ff`; AOT key `8f89eba48d17`, cached: the Deckbuild
library, `Sts2Sim.Vec.so` sha256 `254efb10…f9fd6cc`; 148 Silent A9 start saves; copy on raven under
`/data/raven/hack-sts2/tmp/integrate-loopbreak-20261104/`, with `validate.log` / `validate-s5b.log`). Validated on raven
in a plain `python:3.13` container (4 threads, torch 2.15.0+cpu in a venv on `/data/raven`):
- `smoke_bundle.sh` (small preset, 256 envs, 2 min, saves at 0.5): `sts2vec check` ABI 2, 21 exports,
  `sts2vec/3b73317739c650bd`, 43 buffers, 35,062 bytes/env, threads 1 vs 4 byte-identical, `check: OK`; config.json
  `loop_breaker` on (k 3), `rollout_loop_breaker` off; 1 update, eval u1 std-a0-v1 0.078 (20 games).
- `sts2train eval` on silent-a0-v1 of two `5dbfa09dd9dcb5ed` checkpoints widened on load, breaker off / on: s4a
  `best.pt` 0.302 [0.268, 0.336] both, s5b `best.pt` (A0 u160) 0.403 [0.345, 0.461] both; 0 loop breaks, 0 no-op
  actions, 0 cycle revisits, 20/20 replayable, `spec_compat` widened, the 8 leave kinds resolved. On this library the
  two policies' loops are gone at the source (shop potion rule, reward macros), so the breaker has nothing to break; the
  owner's s5b gain (0.330 → 0.500) was measured on the pre-macro library.

## Fidelity integrate (2026-11-04)

Scope: train/fidelity `87adda1` (12 commits on `ef70ee6`): the 7 open sim-vs-game divergences of the E3 farm, owner
record in `sim/specs/diff-report-e3-farm-2026-11-02.md` §7. The only Core rule change is the SDR1 turn counter
(`CombatState.TurnsTakenAtCombatEnd`: EndCombatInternal captures TurnNumber, `RunLoop.OnCombatVictory` writes it to the
map-point history; `37c6ac3`); the rest is in Sts2Sim.Diff (turn phase at the recorder's resolution, draw-pile grids as a
multiset, relic run-time state through the mcr join, G1 `final_hp` before the victory heal and not for abandoned
combats), the sts2rec exporter (abandoned card reward → open + `card_reward_skip`, PUNCH_OFF FIGHT back into the event
segment, `deck_index` of same-name cards from the saves, backed-out SMITH selection), tests, specs and the ratchet
baseline. Merged `--no-ff` into the loop-breaker tree (`a8d91cf`) as `9f66087`.

Conflicts: none. `sim/specs/kernel-map.json` merged on disjoint lines (CombatManager.cs gains the RunLoop / CombatState
cites; env-deckbuild's PotionCmd / MerchantEntry / MerchantPotionEntry / Player cites); `scripts/kernel_map.py`
regenerates it byte-identical and `--check` passes (564 files, mapped 374, no_port 190). RunLoop.cs and the sts2rec
tests were touched on one side only.

Seams checked (no code change needed):
- `TurnsTakenAtCombatEnd` is copied by CombatState's copy constructor (clone / branch / restore) and is not canonical
  (set and consumed within one Step); the map-point history's `TurnsTaken` it feeds is. `sts2vec digest` (256 envs x
  400 random steps, seed 0, 4 threads): managed macOS build of `9f66087` = NativeAOT library of the new bundle (state
  hash sha256 `d84ed099…`, obs sha256 `98d362bf…`); the pre-merge library (`sts2train-a8d91cf636`) gives the same obs
  sha256 and another state hash (`97a9a7be…`). So the rule reaches the AOT build, and observations / encoder_version
  (`3b73317739c650bd`) are unchanged.
- env-deckbuild's shop rule (no `BuyEntry` for a potion with a full belt or under Sozu) and the potion-reward refusal
  check change no replay (gates below equal the owner's).

Diff gates on the merged tree (Release build of `9f66087`). The farm's raw recordings are on the unplugged external
drive, so nothing was re-exported here: the inputs are copies of the owner's self-contained re-exported bundles
(`train-fidelity` worktree `tmp/fid/bundles-x56`, `hbundles-x8`, `w41i-in`; shasum-identical copies under the
git-ignored `tmp/fid-int/in/`, logs in `tmp/fid-int/merged1/`). Every log the owner also ran is, path-stripped,
identical to the owner's final run (`tmp/fid/final`, code as of `dbcd128`; the ratchet run against the old baseline
included):

| gate | merged tree (`9f66087`) = owner |
| --- | --- |
| G3 farm, from the seed | 3,522/3,522 decisions; 36/39 runs to the recorded end, 3 at a `decision_unresolved` truncation |
| G3 farm `--anchors` | 787/787 segments, 3,690/3,690, 0 divergences |
| G2 (`g3 --anchors-every`) | 3,676/3,676; only the 4 out_of_scope `extra_rewards` combat_exit imports |
| G3 `--save-check` | 787/787; the 259 exportable segments differ only in `unlock_state` and `map_point_history` (180 not exportable) |
| G1/G2 farm (`--v2`) | 2,810/3,059 decisions; combats 168/177 (outcome 168/168; 9 exporter_ineligible) |
| human combat gate (G1, `w41i-in`) | 1,666/1,671 (99.70 %), 5 rows: 4 v0.107.1 exporter_ineligible, Q3X6PF8S37GA s5 |
| human G3, re-exported (`hbundles-x8`) | 2,348/2,352 (99.83 %) |
| human G3, W4.1 bundles | 1,921/1,924 |
| G0 | 4/4, 69/69 |

Ratchet: against the pre-merge baseline (`a8d91cf`) pass, 2,664 agreed decisions checked, 0 violations, 42 newly
agreeing; `--update-baseline` on the merged tree then writes a file byte-identical to the branch's
`sim/specs/diff-baseline-e3-farm.json` (sha256 `36e1a7ee9d5aac29…`), which passes with 2,810 checked, 0 violations.

Gates (macOS M4 Pro, `DOTNET_PROCESSOR_COUNT=4`, nice 10): `scripts/gates.sh --quick` on `9f66087`, 157 s, every step
PASS, logs `tmp/gates/fidelity-integrate-quick/`:

| step | status | secs | key numbers |
| --- | --- | --- | --- |
| build-release | PASS | 6 | Release: 0 warnings, 0 errors (rc 0) |
| build-debug | PASS | 6 | Debug: 0 warnings, 0 errors (rc 0) |
| tests | PASS | 39 | quick (Server Vec Parity EndToEnd SelfConsistency W1Gate): 142/142 passed, 0 failed, PENDING 0 |
| selftest | PASS | 5 | 100/100 completed, 0 crashed, 0 capped, 0 server errors, mean 67.8 steps |
| g0 | PASS | 1 | bundles 4/4 agree (hard 0, error 0, sim exception 0); decisions 69/69, 0 unobserved; outcome 4/4 |
| pytest-eval | PASS | 4 | 84 passed |
| pytest-vec | PASS | 15 | 117 passed (`STS2VEC_REQUIRE_FULL=1`) |
| sts2rec | PASS | 78 | 767 passed, 6 skipped |
| py-scripts | PASS | 1 | unittest 53 OK; prompt_sites --check OK; kernel_map --check OK (564 files, mapped 374, no_port 190) |

Also: full C# suite 1,170/1,170 passed, 0 failed, PENDING 0 (457 s in the runner; FidelityTests 7, DiffV2Tests 11);
`uv run --project sim/tools/train pytest -q` 111 passed.

Colab bundle `tmp/colab/sts2train-9f6608701d.tar.gz` (7,037,357 bytes, sha256
`708102c126c924b6bb2ddecf32dd4e06c1000671afb09d55da799c8c66b087c0`; AOT key `5c80f4e6ba70`, built on raven in 233 s,
`Sts2Sim.Vec.so` 13,593,256 bytes, sha256 `2d035a9b…fa984ce`, newest symbol GLIBC_2.34; 148 Silent A9 start saves; copy
on raven under `/data/raven/hack-sts2/tmp/integrate-fidelity-20261104/`). The docs commit after `9f66087` changes
nothing the bundle ships. Validated on raven in a plain `python:3.13` container (`--cpus 4`, 4 threads; box shared with
a lookahead job, load about 8), torch 2.15.0+cpu in a venv on `/data/raven`:
- `smoke_bundle.sh` (small preset, 256 envs, 2 min, saves at 0.5), 163 s: `sts2vec check` ABI 2, 21 exports,
  `sts2vec/3b73317739c650bd`, 43 buffers, 35,062 bytes/env, threads 1 vs 4 byte-identical, `check: OK`; floor cap 80 in
  effect; 1 update, 8,192 env steps, 105 episodes, 0 sim errors / content gaps; eval u1 std-a0-v1 0.078 (20 games);
  appended buffers under the NativeAOT library: `reward_choice_rows` 12,777 = `macro_candidates` 12,777,
  `map_route_cells` 6,155,123, `event_option_rows` 9,574, card_static 598 × 30, ABI 2 exact-branch check OK; raw
  bench 37,103 env-steps/s (1024 envs × 4 threads).
- `sts2vec digest`: the cross-check in the seams above.
- raven cleanup: this build's BuildKit cache record (1.565 GB) pruned; the extracted bundles, the old bundle copy and
  the venv removed (the validation directory keeps the tarball, logs, digests and the smoke run, 35 MB).
- Not verified: CUDA (no GPU run of this bundle); the four exporter fixes on a fresh export of the raw farm recordings
  (drive unplugged): the merged tree was checked on the owner's re-exported bundles only.

## Live twin integrate (2026-11-04)

Scope: train/live-twin `87c409f` (2 commits on `ef70ee6`: `ca9cfbd` code + tests + README, `87c409f` README results).
`scripts/live/nn_twin_play.py` + `scripts/live/nn_twin/` let a trained sts2train checkpoint play the real game: the
network reads a sim twin (one sts2vec slot from the run's seed or the game's `current_run.save`), its row is matched by
meaning onto STS2_MCP's candidates (`/api/v1/singleplayer`; `/api/v1/eval` optional) and executed there, and the twin
takes the same row; per-decision view diffs, follow mode after a desync, resync from the game's save at the next save
point (a room-entry save's floor = `len(map_point_history) + 1`); `--dry-run-server` stands the sim play server in for
the game. Owner validation on a headless instance (pre-macro library `5dbfa09dd9dcb5ed`, s3c `best.pt`): 255 decisions,
0 desync, 0 resync, the dry run's result. Guide: `scripts/live/README-nn-twin.md`. Merged `--no-ff` into the Fidelity
tree (`1b2f10a`) as `2011f65`.

Conflicts: none (the branch adds `scripts/live/` only).

Seams resolved in `2011f65` (the branch predates env-deckbuild's rewards-screen macros):
- `Twin._macro` reads its choice from `reward_choices` row `option_index` (card + upgrade, `CardRewardAlt`) and expands
  as `RewardMacros.Expand`: ClaimReward(A), then PickRewardCard(B) / RewardAlternative(B). TakeRewardAlternative used to
  decode as `#B`, which matches no game alternative.
- `Runner.map_row` pairs a macro's open step by card reward A's rank among the twin's unclaimed card rewards (was:
  always the game's first card reward).
- Dry-run audit: a macro row's token is its plan's tokens (`reward A; card B`, `reward A; reroll` / `alt B`), compared
  with the executed wires joined; both dry-run tests failed on this before the fix (no desync).
- A macro step the game does not offer after the open (its card reward differs: hidden state) enters follow mode
  (`macro_step`) and benches the twin until the next save point; in follow mode the step is matched relaxed.
- `Policy.bind` reloads a checkpoint whose embedded enums only widened (`load_checkpoint(library=...)`), as
  `sts2train eval` does. Pre-macro checkpoints (s3c / s4a / s5b) ran unwidened before: the model clamps kinds 21 / 22
  to PromptCancel, so no error, but other scores on every rewards screen with a card reward.
- Tests: `test_mapping` +4 (TakeRewardAlternative plan, open-step rank, stand-in macro tokens, the library's 23
  `RunActionKind` enumerated through `Twin.acts` / `row_to_token`); `test_dry_run` +2 (the CLI dry run with a pre-macro
  checkpoint, widened; a widening check); the random runs must cover macros.

Tests: `uv run --project sim/tools/train pytest -q scripts/live/tests` 54 passed (50 unit + 4 integration that start
their own play server; the 5 random dry runs: 580 decisions, 14 TakeRewardCard, 0 desync, 0 token mismatches);
`uv run --project sim/tools/train pytest -q` 111 passed (its `testpaths` stops at `sim/tools/train/tests`).

Gates (macOS M4 Pro, `DOTNET_PROCESSOR_COUNT=4`, nice 10): `scripts/gates.sh --quick` on `2011f65`, 156 s, every step
PASS (Release / Debug 0 warnings; tests 142/142; selftest 100/100; g0 4/4, 69/69; pytest-eval 84; pytest-vec 117;
sts2rec 767 + 6 skipped; py-scripts unittest 53, prompt_sites / kernel_map `--check` OK), logs
`tmp/gates/livetwin-integrate-quick/`.

No Colab bundle: nothing it ships changed (`git diff 1b2f10a 2011f65` is empty on its source paths), so
`sts2train-9f6608701d` stays current. Not verified: the real game on the merged library (macros through STS2_MCP, a
widened checkpoint live); TakeRewardAlternative outside unit tests (needs Driftwood / Pael's Wing).

## Lookahead + report-card integrate (2026-11-04)

Scope: train/lookahead `5c6ba5d` (7 commits; `6914040` already merged the integration head `b26a383`) and
train/report-card `8fd563e` (2 commits on `a8d91cf`), merged `--no-ff` in that order into the Live twin tree
(`826265f`) as `2ee7e4f` and `c778682`, then `26f1d80` wires the card to the lookahead's run directories.
- `sts2train/lookahead.py` (`python -m sts2train.lookahead eval | paired`): a checkpoint's greedy policy plus a
  combat-only search on determinized clones (K candidates, R roots, turn / combat horizon, `value+hp` leaf), an
  ordinary `sts2vec` policy that the unmodified evaluator plays, scores and replay-audits through a private mirror env;
  ABI 2 `env.branch` path (no pool growth), pool / rebuild path for older libraries; `--record` ExIt rows; per-seed
  paired statistics. Measurements: `docs/lookahead-2026-11-04.md` (A10 dev +0.0152 [+0.0080, +0.0223], below the +0.02
  bar: ExIt go signal not fired). Seed sets `silent-a10-dev-v1` (128), `silent-a10-test-v1` (100), `silent-a0-dev-v1`
  (100).
- `sts2train/reportcard.py` + CLI: `sts2train report-card` (greedy protocol evals run or reused per seed set, cached
  random / first / heuristic baselines, staged tables with Wilson intervals, endings, floor histogram, win-prob
  readout → `model-card.md` / `.json`), `sts2train pack` (tar.gz weights bundle with README-weights.md and a sha256
  manifest, re-read and verified), `sts2train probe` (an eval's games replayed, the checkpoint read at every decision).

Conflicts: none. The two seed-set files both branches add are byte-identical (blobs `a768bf3` / `30903be`); `cli.py`
and the README change on the report-card side only.

`1dfb0c7` (the padded-row cut: 0.71–0.81× the rollout forward time, rounding-level output change) is confined to the
lookahead's rollout forwards (`trim_padding` in `LookaheadPolicy._forward`); the root greedy forward,
`sts2train.evaluate` and the trainer are untouched: `test_perf` (eager update = legacy loop, train default path = head
loop, bit for bit) passes, and K = 1 stays identical to greedy.

Seams resolved:
- `2ee7e4f`: the lookahead branch predates the loop breaker (`a8d91cf`). Its `--greedy` baseline runs
  `sts2train.evaluate.evaluate`, whose greedy policy now goes through the breaker by default, while the lookahead's
  own choices outside the search are the plain argmax. `evaluate_greedy` passes `loop_breaker=False` (config.json
  `loop_breaker.mode` off, stats.json `loop_breaker` off), so the paired comparison still differs only by the search
  and K = 1 = `--greedy` (`test_k1_is_greedy` asserts the mode); doc §7 notes it.
- `26f1d80`: the card's `--lookahead-dir` predates `sts2train.lookahead` on the tree. On real directories a `--greedy`
  run came out as a second "lookahead, determinized" row, and the only comparison on offer was the lookahead row (no
  loop breaker) against the card's greedy row (loop breaker on). Now `lookahead_row` classifies a directory by its
  config.json (search run → "lookahead, determinized: la(K,R,horizon,honest) <score>"; `--greedy` run → kind
  `greedy-ref`, "NN greedy, loop breaker off"; anything else refused before a game is played); every lookahead row is
  paired per seed (`lookahead.paired`) against a `greedy-ref` row of the same weights with the breaker off (like for
  like), else against the card's greedy row (flagged "(loop breaker on)"); `stats.json` counters (searched / changed
  decisions, ms per searched decision, rollout errors, desyncs, api, code sha) land in `search_stats`; model-card.md
  gains a "Lookahead: declared search, paired per seed" table and the CLI JSON a `lookahead` list; README-weights.md a
  lookahead section (and the live twin runner is on this tree now). `reportcard` imports `lookahead.paired` at module
  level (imported lazily under a test that monkeypatches `sts2vec.evaluate.evaluate`, the module kept the patched
  function). `scripts/live/README-nn-twin.md`: `sts2train.lookahead` is merged; `nn_twin_play --lookahead` still exits 2
  (its mirror env cannot follow save resyncs).

Tests: `uv run --project sim/tools/train pytest -q` 137 passed (111 + 16 lookahead + 10 report-card, of which 2 new:
`test_lookahead_dir_rows`, `test_card_reads_lookahead_eval_dirs`, which runs `lookahead eval` and `--greedy` through
their CLI on two games, then `report-card --lookahead-dir`); `uv run --project sim/tools/train pytest -q
scripts/live/tests` 54 passed.

Gates (macOS M4 Pro, `DOTNET_PROCESSOR_COUNT=4`, nice 10): `scripts/gates.sh --quick` on `26f1d80`, 157 s, every step
PASS, logs `tmp/gates/lookahead-reportcard-integrate-quick/`:

| step | status | secs | key numbers |
| --- | --- | --- | --- |
| build-release | PASS | 6 | Release: 0 warnings, 0 errors (rc 0) |
| build-debug | PASS | 6 | Debug: 0 warnings, 0 errors (rc 0) |
| tests | PASS | 39 | quick (Server Vec Parity EndToEnd SelfConsistency W1Gate): 142/142 passed, 0 failed, PENDING 0 |
| selftest | PASS | 4 | 100/100 completed, 0 crashed, 0 capped, 0 server errors, mean 67.8 steps |
| g0 | PASS | 1 | bundles 4/4 agree (hard 0, error 0, sim exception 0); decisions 69/69, 0 unobserved; outcome 4/4 |
| pytest-eval | PASS | 5 | 84 passed |
| pytest-vec | PASS | 15 | 117 passed (`STS2VEC_REQUIRE_FULL=1`) |
| sts2rec | PASS | 78 | 767 passed, 6 skipped |
| py-scripts | PASS | 0 | unittest 53 OK; prompt_sites --check OK; kernel_map --check OK (564 files, mapped 374, no_port 190) |

Lookahead + card on `s7b` (`latest.pt`, A10 update 300, 2.9M parameters, aux heads, encoder `3b73317739c650bd`,
weights `644755f6b76c9fa6`) on `silent-a10-v1`, Mac, 4 threads, all 20/20 replayable:

| row | progress [95% CI] | results | reached the Act-1 boss | wall |
| --- | --- | --- | --- | --- |
| NN greedy (the card's eval, loop breaker on) | 0.240 [0.205, 0.275] | loss 20 | 9/20 | 5 s |
| lookahead, determinized: la(8,1,turn,honest) value+hp | 0.227 [0.181, 0.272] | loss 17, stuck 3 | 8/20 | 26 s |
| NN greedy, loop breaker off (`--greedy`) | 0.217 [0.180, 0.253] | loss 17, stuck 3 | 6/20 | 5 s |
| random / first / heuristic | 0.037 / 0.112 / 0.117 | loss 20 | 0/20 | |

Paired like for like (lookahead − `--greedy`): +0.0101, t [−0.0192, +0.0394], bootstrap [−0.0137, +0.0392], 4 better / 2
worse / 14 same; 2,775 searched decisions (62 % of live), 20.6 % changed, 7.2 ms per searched decision, 0 rollout
errors, 0 desyncs, ABI 2 branch path. On this checkpoint the loop breaker is worth more than the search (+0.023 vs
+0.010: the 3 stuck games are prompt / shop cycles, which the breaker breaks: 4 loop breaks in 3 games); the lookahead
has no loop breaker, so on the card its row sits below the breaker-on greedy row. Composing the two (the breaker on the
lookahead's root greedy) is a lookahead semantics change (`la-v2`), not done here.

Colab bundle `tmp/colab/sts2train-26f1d805a0.tar.gz` (7,077,176 bytes, sha256
`9d69c30fc00152f61ffd0d44f05efabb67d4170899a060e17471f69869617f70`; AOT key `5c80f4e6ba70`, cached, not rebuilt: the
Fidelity library, `Sts2Sim.Vec.so` sha256 `2d035a9b…fa984ce`, newest symbol GLIBC_2.34; 148 Silent A9 start saves;
the wheel ships `lookahead.py` / `reportcard.py`, the bundle 8 seed sets incl. the three new ones; copy on raven under
`/data/raven/hack-sts2/tmp/integrate-la-rc-20261104/`). The docs commit after `26f1d80` changes nothing the bundle
ships. Validated on raven in a plain `python:3.13` container (`--cpus 4`, 4 threads), torch 2.15.0+cpu in a venv on
`/data/raven` (`validate_la_rc.sh`, `validate.log` there):
- `smoke_bundle.sh` (small preset, 256 envs, 2 min, saves at 0.5), 163 s: `sts2vec check` ABI 2, 21 exports,
  `sts2vec/3b73317739c650bd`, 43 buffers, 35,062 bytes/env, threads 1 vs 4 byte-identical, `check: OK`; floor cap 80
  in effect; 4 updates, 32,768 env steps, 698 episodes (384 save-started), 0 sim errors / content gaps; eval u4
  std-a0-v1 0.088 (20 games); appended buffers, card_static, ABI 2 branch OK; raw bench 53,920 env-steps/s.
- The bundle's new entry points on `s7b` / `silent-a10-v1`: `python -m sts2train.lookahead eval --greedy` 17 s, `eval`
  (K 8, turn, value+hp) 66 s (18.0 ms per searched decision, 0 rollout errors, 0 desyncs, branch path), `paired`,
  `sts2train report-card --lookahead-dir` both (with the three baselines) 21 s, `sts2train pack` (31.4 MB, 8 files,
  verified): every number equals the Mac's, and both runs replay the Mac's game histories 20/20.
- raven cleanup: the container ran `--rm`; the venv, the extracted bundle, the checkpoint copy, the weights bundle and
  the smoke run's checkpoints removed (the validation directory keeps the tarball, logs and run directories, 8 MB).
- Not verified: CUDA; the lookahead on the 128 / 100-game sets on the merged tree (the owner's numbers in
  `docs/lookahead-2026-11-04.md` predate the loop breaker; nothing on the search path changed); `--record` through the
  bundle.
