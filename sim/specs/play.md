# Play spec: interactive single-combat CLI for agent evaluation

Files:

| File | Contents |
| --- | --- |
| `Sts2Sim.Play/PlayCli.cs` | verbs, option parsing, exit codes |
| `Sts2Sim.Play/PlaySession.cs` | session file model and JSON read/write |
| `Sts2Sim.Play/PlayGame.cs` | replay from `CombatSim.Create`, cap, outcome, stats JSON line |
| `Sts2Sim.Play/StateRenderer.cs` | the fixed-format state printout |
| `Sts2Sim.Tests/PlayTests.cs` | `Test_Play_*` (replay determinism, invalid handling, rollout, cap, tamper detection) |

## 1. Invocation

Build once (`dotnet build sim/Sts2Sim.sln -c Release`), then always run the prebuilt dll, never
`dotnet run`:

```
dotnet sim/Sts2Sim.Play/bin/Release/net9.0/Sts2Sim.Play.dll new     --session FILE --seed N --encounter ID --ascension A [--hp H] [--max-hp M]
dotnet .../Sts2Sim.Play.dll act     --session FILE K
dotnet .../Sts2Sim.Play.dll show    --session FILE
dotnet .../Sts2Sim.Play.dll stats   --session FILE
dotnet .../Sts2Sim.Play.dll rollout --seed N --encounter ID --ascension A --random [--session FILE] [--policy-seed S] [--hp H] [--max-hp M]
```

Exit codes: 0 success (an invalid action number is a success that prints `INVALID`), 2 usage or
session-file error, 1 replay divergence or internal error.

## 2. Combat set-up

`CombatSim.Create(seed, StartingDecks.Ironclad, encounter, ascension, hp, maxHp)`: the seed is
the numeric `RunRngSet` seed, the deck is the Ironclad starter (the A5+ rule appends Ascender's
Bane, contract §4.2), the encounter Rng uses `CombatRunContext.Default`. HP defaults to the sim
default 80/80; a realistic first combat at A2+ is `--hp 64` (`StartingDecks.IroncladHpAfterNeowArrival`).

## 3. Session file and replay

The session file is the whole persistent state; no combat state is serialized. Fields:
`format` (`sts2sim-play-session/1`), `seed`, `encounter_id`, `ascension`, `current_hp`, `max_hp`
(null = sim default), `action_cap` (100), `policy` (`agent` or `random(policy_seed=S)`),
`actions[]` (`k` = the 1-based number chosen, `kind` `play`/`end`, `card`/`target` hand and
monster indexes, `label`), `invalid_attempts`, `state_hash` (`CombatSim.StateHash()` after the last
action, hex).

Every invocation replays `actions[]` from `Create`. Each stored action must equal
`LegalActions()[k-1]` at its step, and the final hash must equal `state_hash`; otherwise the
command fails with exit 1. `new` and `rollout --session` never overwrite an existing file.

## 4. Action numbering

`LEGAL ACTIONS` lists `CombatSim.LegalActions()` in its own order (hand index, then target index,
then End Turn last), numbered 1..n. There is no de-duplication: two copies of a card give two
numbers. The hand list uses `-` bullets so that its order is not confused with action numbers.
Enemies are tagged `E<CombatId>`, which stays fixed when other enemies die.

## 5. Outcome, cap, stats

Outcome is `victory`/`defeat` from the sim, `cap` once `action_cap` actions are taken without a
terminal state, else `in_progress`. Stats JSON line (also printed after `TERMINAL`):
`outcome, hp_start, hp_end, turns, decisions, invalid_attempts, seed, encounter_id, ascension`.
`hp_start` is the player HP at the first decision, `turns` is `Player.TurnNumber` at the end,
`decisions` counts applied actions (End Turn included).

## 6. Printout

Fixed sections: header (`seed | encounter | A | turn | phase | actions n/cap | invalid`),
`PLAYER`, `HAND` (name, type, cost, upgraded, playability with reasons, effect), `PILES` counts,
`ENEMIES` (hp, block, intent, powers), `LEGAL ACTIONS`. Effects and intents are computed with the
same modify hooks the game previews: card damage `Hook.ModifyDamage(player -> target)`, block
`Hook.ModifyBlock`, intent damage `AttackIntent.GetSingleDamage` and `GetRepeats`. `act` prefixes
`APPLIED k: ...` and `CHANGES:` (HP changes and deaths). Terminal printout: header, `PLAYER`,
`ENEMIES left`, a line `TERMINAL`, then the stats JSON line as the last line.
