# W4 ftests — self-consistency suite (no game) — index

Owner W4 ftests. Design: `docs/sim-port/design-diff-gate.md` §6.6. Files: `sim/Sts2Sim.Tests/SelfConsistencyTests.cs`
(`Test_SelfConsistency_*`), `sim/Sts2Sim.Core/Run/Save/RunExporter.cs`, `sim/Sts2Sim.Bench/Program.cs` (`nightly-soak`).
Format: `check / model — source — limits`.

## Walker (shared by the run-scope checks)

- No-Neow walk — `RunConfig` with `UnlockProfile.All` minus `NEOW_EPOCH` (`Runs/RunManager.cs:3924-3927`: `StartedWithNeow` = epoch revealed), `ContentFilter.PortedOnly`, Ironclad A0, player HP raised to 600/600 after `NewRun` so random play reaches shops, rest sites, treasure and act 2 — splitmix policy; in combat it plays a card 7 times in 8 when one is legal; never discards potions unless nothing else is legal; on the map it never travels to `Unknown`/`Ancient` points (event rooms: `EventRoom.EnterInternal` is run-glue's, and `PortedOnly` still pulls unregistered events such as `CRYSTAL_SPHERE`, `FAKE_MERCHANT`); a walk stops at "only event map points ahead", a `NotImplementedException` (gap, reported in the test line), terminal or 400 steps.
- Neow walk (`Test_SelfConsistency_F4F5_NewRunWithNeowAndEvents`) — default unlocks, events allowed, gaps rethrown; routed through `W1Pending.Run(…, "run-glue", …)` until room entry lands; the integrator removes the route.

## F2 determinism

- Combat streams — `CombatSim.Create(seed, StartingDecks.Ironclad, encounter)` × {NIBBITS_WEAK 11/12, INKLETS_NORMAL 13, FROG_KNIGHT_NORMAL 14}, hash after every step, XXH64 digest of the stream; two in-process runs equal; digest pinned from Release, verified equal in Debug (2026-11-01).
- Run streams — no-Neow walks seeds 1–4, `RunSim.StateHash` after every step; same pins/rule.
- Refresh rule — any change to rules, content or canonical form reached by these streams moves the pins; the failing assertion prints the new string; refresh in Release, confirm with `dotnet run -c Debug --project sim/Sts2Sim.Tests -- SelfConsistency_F2`, say so in the commit.

## F3 save round trip

- `RunExporter.ToSave(RunState, SavedRoom?)` / `ToSave(ImportedRun)` / `ToSave(RunSim)` — inverse of `RunImporter.ImportState` field by field (`Saves/SerializableRun.cs:19-245` subset kept by `RunSaveReader`): acts (room set lists + visited counters, boss/second boss/ancient; `saved_map` only for the current act, as the game), odds, grab bags (`DequeOrder`), player (deck with upgrade level, enchantment, `[SavedProperty]` props; relics with props and floor; potions by slot; rng; odds; extra fields; unlock state), run rng by stream name, visited coords, events seen (sorted: the sim keeps a set), map-point history regrouped per act by `MapPointHistoryEntry.ActIndex`, extra run fields. `ToSave(RunSim)` refuses a pending prompt or a combat in progress; its pre-finished room is the current room when it is a pre-finished `CombatRoom` / `EventRoom`.
- Props — every readable `[SavedProperty]` member is written (the sim attribute has no `SerializationCondition`; the game's default is `AlwaysSave`, `Saves.Runs/SavedPropertyAttribute.cs:12-15`); int / bool / string / int[] only (the importer's set); string vs model-id lists are not distinguishable in the sim (both `string` members).
- `RunExporter.Describe(SavedRun)` — line form of a save for comparison: unordered parts sorted (rng streams, grab-bag rarities, events seen, unlocked epochs, encounters seen, map points by row/col, children), floats by bit pattern, `gold_gained` null = 0.
- Fixture check — fixture `run_save_a8_silent_boss1.save` edited like `SaveImportTests.PortedOnlyFixture` (unported cards/enchantments and cards/relics with props dropped) plus unregistered room-set encounters dropped (GREMLIN_MERC_NORMAL, W3 content); `Describe(ToSave(ImportState(save)))` = `Describe(save)` modulo the ignore list; `Hash(ImportState(ToSave(x))) == Hash(x)`.
- Ignore list — `players[0].deck[*].floor_added_to_deck` (`CardInstance` has no `FloorAddedToDeck`, R7 gap), `players[0].net_id` (no net id in the sim; written as 1). Fields the reader already drops (times, `num_reloads`, `map_drawings`, `player_stats` except `gold_gained`, `platform_type`, player `extra_fields` other than the two ported ones, `discovered_*`) never reach the comparison. The pre-finished room is passed through `ImportedRun.PreFinishedRoom`.
- Walk check — every 3rd non-combat, non-prompt walk state: `s1 = ToSave(sim)`, `x = ImportState(s1)`, `Describe(ToSave(x)) == Describe(s1)` with no ignore list, `Hash(ImportState(ToSave(x))) == Hash(x)`, player HP/gold/deck/relic counts and run RNG equal to the walked state.
- Not checked — `RunSim.FromSave` (it recurses into `RunImporter.FromSave` → `RunSim.FromSave`: stack overflow, R6/R7 contract says it must call `ImportState` + entry); `RunSim`-level equality after a load (loading re-enters the last map coord, so it is not the walked state by design).

## F4 legal-action closure (run scope)

- Per walk state — legal list non-empty and distinct; a pending prompt lists a `Prompt*` answer; every listed action `IsLegal` and steps on a clone without exception (gaps counted), the next non-terminal state has an action, the source hash is unchanged; every 3rd state all 21 kinds × A ∈ {−1, 0, 1, 2, maxA, maxA+1, 99} × B ∈ {−1, 0, 1, 5} agree between `IsLegal` and the list; two unlisted candidates per state are stepped on the sim itself: they throw (`InvalidOperationException` / `ArgumentException`) and the hash is unchanged. Terminal states have no action and nothing is legal. Listed travels to event points are not stepped in the no-Neow walk (counted).

## F5 invariants (run scope)

- After every walk step — one player; HP in [0, max], max > 0; block, energy, stars, gold ≥ 0; potions distinct, owned, ≤ slots; relics owned; deck cards distinct by object and id, owned; in combat: combat piles disjoint from each other and from the deck, distinct ids, the combat's player is the run's player, creature HP/block, unique creature ids, `NextCreatureId` non-decreasing within a combat, power owners; out of combat: combat piles empty; every card id < `RunState.NextCardInstanceId` (non-decreasing), an id that left every pile and the deck never returns; run and player RNG counters non-decreasing. `Test_SelfConsistency_F5_LedgerRejectsBrokenRunStates` proves the ledger trips (negative gold, duplicated deck card, rewound RNG).

## F7 metamorphic

- Replay — clones taken every 17 steps (from step 5) replay the recorded suffix with identical `RunSim.StateHash` at every step.
- Interchangeable copies — combat scope, first decision of starter-deck combats (3 encounters × 6 seeds): adjacent hand cards with the same def, upgrade level, no enchantment/affliction/extra state, same cost and playability; `PlayCard(i, t)` vs `PlayCard(i+1, t)` then 40 identical continuation steps: equal diff-gate projection (`SimProjector.Project` → `ProjectionJson.Write`) + run RNG canonical + outcome + round, equal legal lists. Instance ids differ by design, so the raw state hash cannot be compared.

## F8 cross-mode

- At the first decision of every walk combat — the run's combat is re-homed into `RunState.CreateStandalone(cloned player, cloned rng, ascension, unlocks)` with the same `NextCardInstanceId` (`CombatState.CloneInto`), driven by `CombatSim.FromState`; run and standalone step the same random actions (prompts answered in both) and must have equal combat digests (`WriteCombatFields` + player canonical + rng + card id allocator), equal legal lists (mapped by `RunAction.FromCombat`), the same prompt sites, and end together with equal player HP. Limits: the anchor is post-`StartCombat` (room entry and `AfterRoomEntered` are not re-run standalone); relics reading run-layer fields would show up as a divergence.

## F6 nightly soak (Bench)

- `dotnet run -c Release --project sim/Sts2Sim.Bench -- nightly-soak [runsPerCell=200] [firstSeed=1] [stepCap=6000]` — every registered character × A0/A5/A10, `PortedOnly`, uniform random legal actions, `Hash(x) == Hash(x.Clone())` every 101 steps; exception signatures deduplicated (digits → `#`) with the first example `character/A/seed/phase/step`; `NotImplementedException`/`NotSupportedException` are "gap", others "bug"; exit 1 on any bug. Today every run stops at `EventRoom.EnterInternal` (run-glue).

## Cross-owner edits made here (see the commit)

- `Run/Loop/RunActionExecutor.cs` `ClaimReward` — a card reward now calls `OnSelectWrapper` before opening the CardReward phase (`Rewards/Reward.cs:246` → `Rewards/CardReward.cs:592` `OnSelect` opens the screen); without it every `PickRewardCard`/`RewardAlternative` threw "The card reward screen is not open." (found by the walker).
- `Run/Loop/RunLegalActionGenerator.cs` `IsLegal` and `Run/Loop/RunSim.cs` `StepPrompt` — a `RunAction` is legal only in its canonical form (`RunAction.FromCombat(ga) == action`); `EndTurn(-1, 0)`, `PromptConfirm(3)` etc. were accepted although never listed (F4).
- `Run/Save/RunImporter.cs` — the three W2 stubs wired to the landed R2/R3 members (`UnknownMapPointOdds` setters, `RelicGrabBag.LoadFromSerializable`, `SavedActMap(SerializableActMap)` via the new public `ToSerializableActMap`); history entries carry `ActIndex` = their act list (`ToHistoryEntry(saved, actIndex)`), so an export can regroup them.
