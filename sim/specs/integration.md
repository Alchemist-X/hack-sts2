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
