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
