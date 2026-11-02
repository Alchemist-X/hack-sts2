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
