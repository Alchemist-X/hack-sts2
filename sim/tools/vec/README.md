# sts2vec — batched numpy environments for the Sts2Sim simulator

Python side of the E2 training interface (CONTRACTS.md Part D, `sim/VEC.md`). **Start with `sim/ENV_SPEC.md`**: the
one-page environment spec (observation columns, actions, reward, metric, determinism) generated from the schema. `sts2vec` hosts the .NET simulator
**in-process** (hostfxr + `[UnmanagedCallersOnly]` exports called through ctypes) and steps `N` games per call;
observations are written in place into numpy arrays the package allocates from the published schema. No pipes, no
per-env Python work, the GIL is released during native calls.

## Install and build

```bash
dotnet build sim/Sts2Sim.sln -c Release      # builds sim/Sts2Sim.Vec/bin/Release/net9.0/Sts2Sim.Vec.dll (+ sts2vec.runtimeconfig.json)
uv sync --project sim/tools/vec              # numpy (+ pytest in the dev group); Python >= 3.11
```

Requirements: a .NET 9 runtime (`Microsoft.NETCore.App 9.x`). On Homebrew the dotnet root is
`/opt/homebrew/opt/dotnet@9/libexec`; on Linux a user-level `dotnet-install.sh` root is `$HOME/.dotnet` (both found
automatically, as are distro roots and the real directory of `dotnet` on PATH; `DOTNET_ROOT` overrides). Linux x86_64 is
validated: `docs/linux-validation-2026-11-01.md`, `scripts/linux_validate.sh`.

## Run

```bash
uv run --project sim/tools/vec sts2vec check                       # library/schema/buffers + determinism (threads 1 vs 8)
uv run --project sim/tools/vec sts2vec bench --envs 1024 --threads 8 --steps 500
uv run --project sim/tools/vec sts2vec bench --envs 64 --steps 20 --json
uv run --project sim/tools/vec sts2vec digest --envs 32 --steps 100   # rollout digest (compare across processes)
uv run --project sim/tools/vec pytest -q                            # from the repo root or sim/tools/vec
uv run --project sim/tools/vec sts2vec eval --policy heuristic --seedset std-a0-v1   # protocol score of a policy
uv run --project sim/tools/vec python sim/tools/vec/tools/gen_env_spec.py           # regenerate sim/ENV_SPEC.md §5
```

`eval` (alias `evaluate`) plays every game of a protocol seed set with one policy (one slot per game, no auto-reset,
limits step_cap 1200 / the env's `floor_step_cap` 80 non-combat decisions per floor → `stuck`), scores it with the protocol v0.2 progress metric
(`sts2vec/protocol.py`, a vendored copy of `sts2eval.progress` kept equal by `tests/test_protocol.py`) and writes
`config.json`, `run.json`, `games/<CHAR>-<SEED>.summary.json`, `report.json` and `report.md` in the LLM runner's
layout; a replay audit re-plays every game's candidate indices on a fresh env (result, act, floor and final state hash
must match). Options: `--out DIR` (default `tmp/eval/vec-<policy>-<seedset>-<time>`), `--threads`, `--seed` (policy
RNGs), `--step-cap`, `--floor-step-cap`. Exit status 1 when a replay fails.

Every game summary also carries the run state at the end (`sts2vec.runstats`, 2026-11-05, logging only): `hp`,
`max_hp`, `deck` (size), `upgraded`, `relics`, `gold`, `potions`, `killer` (the encounter of the fight a death
happened in), `boss_entry` (`[{act, floor, hp, max_hp, boss}]` per act-boss room entered), `room` (the final room's
kind; also `death.room_type`, null before 2026-11-05) and `fights` (`[{floor, act, room, enemies, encounter,
hp_before, max_hp_before, result, hp_after, max_hp}]` per combat, `result` won / lost / unfinished); the definitions
are in `sim/tools/train/README.md` "Run-state fields". Summaries without these keys still load in every report.

## Write your own policy in 20 lines

A policy is `act(obs, ctx) -> int array (N,)`: one candidate index (row of `obs["cand"][i]`, `< obs["cand_n"][i]`) per
slot with `ctx.live[i]`. `ctx` has `schema`, `cols(buffer)` (column indices by name), `kind(name)` (RunActionKind
value), `live`, `keys` (`"<CHAR>-<SEED>"`), `ascension`, `step` and `rngs` (one seeded generator per game).

```python
import numpy as np

def act(obs, ctx):
    C, G = ctx.cols("cand"), ctx.cols("global_i")
    END_TURN, PLAY_CARD = ctx.kind("EndTurn"), ctx.kind("PlayCard")
    actions = np.zeros(ctx.num_envs, dtype=np.int32)
    for i in np.flatnonzero(ctx.live):
        n = int(obs["cand_n"][i])
        kinds = obs["cand"][i, :n, C.kind]
        plays = np.flatnonzero(kinds == PLAY_CARD)
        if len(plays) and obs["global_i"][i, G.energy] > 0:
            actions[i] = plays[0]                                   # play the first playable card
        elif (kinds == END_TURN).any():
            actions[i] = int(np.flatnonzero(kinds == END_TURN)[0])
        else:
            actions[i] = int(ctx.rngs[i].integers(0, n))            # anything else: seeded random
    return actions
```

Save it as `my_policy.py` and run `sts2vec eval --policy my_policy.py:act --seedset std-a10-v1`. A class works too
(`module:MyPolicy`, instantiated with no arguments, then called per step); so does an importable
`package.module:callable`. Examples in `examples/`: `policy_template.py` (the code above), `random_policy.py` (raw
`VecEnv` loop with auto-reset), `heuristic_policy.py` (the built-in rules through `sts2vec.evaluate.evaluate`).
Built-ins: `random` (uniform, per-game seeded), `first` (candidate 0), `heuristic` (rules in
`sts2vec/policies.py: HeuristicPolicy`). Reference scores (protocol §8, sim NN track): random 0.087 / 0.045,
first 0.193 / 0.102, heuristic 0.221 / 0.104 on `std-a0-v1` / `std-a10-v1`.

`bench` first warms the pool (`env.pool.warm()`: every fresh template is created before the timed window, and that time
is printed on its own line; `--no-warm` measures cold starts instead, where each entry's first reset pays a `NewRun`
of ≈ 16 ms), then reports env-steps/s (whole loop and native step only), ms per batch, MB of observation buffers per
batch, GC mode, reset time, resets/s, episodes ended / wins, and per-flag status counts (slot-steps). `check` exits 1
on any schema inconsistency or determinism difference. Short JIT runs on small Linux hosts are warm-up dominated: use
≥ 3,000 steps, `DOTNET_TieredCompilation=0` or the NativeAOT library there (`sim/VEC.md`, Bench).

## API

```python
import numpy as np
from sts2vec import VecEnv, PoolSpec, FreshEntry, SaveEntry, Status, STATUS, random_actions

pool = PoolSpec(characters=("IRONCLAD", "SILENT"), count=256)          # config.pool.fresh: seeds "TRAIN-<char>-<i>"
pool = pool.with_entries(FreshEntry("DEFECT", seed="D-1", ascension=5), SaveEntry("run.save"))
with VecEnv(1024, threads=8, step_cap=3000, auto_reset=True, seed_text="VEC", content="ported_only", pool=pool) as env:
    obs = env.reset()                         # dict name -> (N, *shape) array (views of env-owned buffers)
    rng = np.random.default_rng(0)
    for _ in range(100):
        actions = random_actions(rng, obs["cand_n"])        # candidate index per slot; -1 = no-op
        obs, reward, terminated, truncated, status, info = env.step(actions)
        assert not (status & (STATUS["SIM_ERROR"] | STATUS["CONTENT_GAP"])).any(), env.slot_errors()
```

| Member | Meaning |
| --- | --- |
| `VecEnv(num_envs, *, threads=8, step_cap=3000, auto_reset=True, seed_text="VEC", content="ported_only", pool=PoolSpec(), pool_cache=4096, fast_path=True, floor_step_cap=80, dll=None)` | creates the native env (`sts2vec_create`); `pool=None` = empty pool; `floor_step_cap` (= `sts2vec.FLOOR_STEP_CAP`, 0 = off) truncates an episode after that many non-combat decisions on one floor (status `FLOOR_STALL`, `end_kind` FloorStall; combat decisions never count) |
| `.reset(mask=None, start_ids=None, *, copy=False) -> obs` | restart masked slots (None = all) from `start_ids[i]` (None / `< 0` = the slot's start sequence) |
| `.step(actions, *, copy=False) -> (obs, reward, terminated, truncated, status, info)` | `actions` int [N]; `reward` float32, `terminated`/`truncated` uint8, `status` int32 bit flags; `info = {episode_id, step_index, start_entry, last_episode}` |
| `.restore(slot, entry, *, copy=False) -> obs` / `.snapshot(slot) -> entry` | one-slot restart from a pool entry / clone a live slot into the pool |
| `.clone_determinized(slot, seed) -> entry` | determinized clone of a live slot into the pool: same observation, hidden state (draw order, RNG streams, relic-bag / event order) resampled from `seed`; restore it like any entry (sim/VEC.md "Determinized clones") |
| `.branch(src, dsts, determinized=True, seed=0, *, copy=False) -> obs` | ABI 2, search: copy a live slot into every slot of `dsts` in one call, without a pool entry: determinized (destination at call position j gets `clone_determinized(src, branch_seed(seed, j))`: same view, resampled hidden state) or exact (`determinized=False`: same `state_hash`). `src` may also be one source per destination (`sts2vec_branch_many`: many roots in one call, the fast form). Destinations start a new episode (`RESET`, `step_index` 0, `start_entry` -1, `last_episode` untouched) and their observations are written now; sources and other slots are untouched (sim/VEC.md "Search") |
| `.restore_many(slots, entries, *, copy=False) -> obs` | ABI 2: `restore(slots[j], entries[j])` for all j in one parallel call |
| `.abi_version`, `.supports_branch`, `.supports_restore_many`, `.supports_pool_release` | the loaded library's ABI (1 = built before `sts2vec_abi_version`) and whether it has each search feature |
| `.state_hash(slots=None) -> uint64 [n]` | `RunSim.StateHash` (debug/test only; 0 = no run) |
| `.pool.add_fresh(character, *, seed, ascension=0, act1="random", content=None)`, `.pool.add_save(path)`, `len(.pool)` | runtime pool entries (ids in insertion order; `len` = live entries) |
| `.pool.release(ids) -> int` | ABI 2: drop entries for good, all or nothing (a snapshot's state becomes garbage, a cached fresh template frees its slot); `len(pool)` and the start sequence skip them and their ids are never reused (`reset` / `restore` / `release` with one raise) |
| `.pool.warm(max_entries=None) -> int` | create fresh entries' start templates now (None = all, up to `pool_cache`), in id order on the env's threads; returns how many this call created. Starts do not change; only the cold `NewRun` moves out of reset/step |
| `.candidates(i)`, `.legal_mask()`, `.last_episode(i)` | decoded candidate rows (RunActionKind names), bool [N, 192], decoded last-episode row |
| `.status`, `.status_counts()`, `.slot_errors()`, `.last_error(slot=-1)` | status bits of the last call; native exception text per failed slot |
| `.schema` | parsed schema: `schema["hand"].fields`, `schema.cols("hand").card_id`, `schema.col("global_i", "hp")`, `schema.vocab.cards[i]`, `schema.vocab.cards.index_of(id)`, `schema.vocab.enums["RunActionKind"]`, `schema.caps`, `schema.encoder_version`, `schema.verify_version()` |
| `.buffers` | the env-owned arrays (`buffers[name]`), the `void*` table (`buffers.ptr`), `buffers.check()` |
| `.set_threads(n)`, `.close()` (idempotent), context manager | |

Returned arrays are **views valid until the next call** on that env; pass `copy=True` (or copy yourself) to keep them.
`obs` holds every buffer except `reward`, `terminated`, `truncated`, `status`, `episode`, `last_episode`; `cand` / `cand_n`
are in `obs`. Index into tables only with the schema's field names (`schema.cols(...)`), never hard-coded offsets.

Errors: a non-zero native return raises `Sts2VecError` with the export's last_error text; a missing or broken runtime
raises `LoaderError` (hostfxr's own messages included); a CLR not on Server GC makes `VecEnv(...)` raise
`ServerGCError`. Per-slot problems are **status bits, never exceptions** — check `status` every step:
`NOOP 1, ILLEGAL_ACTION 2, CONTENT_GAP 4, SIM_ERROR 8, CANDIDATE_OVERFLOW 16, TABLE_OVERFLOW 32, RESET 64, POOL_EMPTY 128`.
With auto-reset a finished slot already shows its next episode; the finished one is in `info["last_episode"]`.

Lower level: `sts2vec.load()` returns the `Library` (one per process) with every export as a ctypes function
(`lib.sts2vec_step(h, actions_ptr, buffers_ptr)`), `lib.version()`, `lib.schema_json()`, `lib.last_error(h, slot)`,
`lib.gc_server`, `lib.runtime_properties`. Exports added after the ABI freeze (`sts2vec_pool_warm`,
`sts2vec_clone_determinized`, and the ABI 2 set `sts2vec_abi_version`, `sts2vec_branch`, `sts2vec_branch_many`,
`sts2vec_restore_many`, `sts2vec_pool_release`) are optional: a library built before them still loads with the attribute
set to None (`lib.missing_exports`), `lib.abi_version` is 1 and `lib.supports(...)` / `lib.supports_branch` are False, and
the wrapper that needs one raises `Sts2VecError` ("... not supported; rebuild it").

Search (sim/VEC.md "Search"): keep one env per search, roots in some slots, scratch slots for the rollouts, and branch
every root in one call per decision round; nothing accumulates in the pool:

```python
from sts2vec import VecEnv, PoolSpec
with VecEnv(M * (1 + K), threads=8, auto_reset=False, pool=None) as env:
    ...                                                   # roots 0..M-1 track the real games (restore / step them)
    roots = np.repeat(np.arange(M), K)                    # K rollouts per root, slots M..M+M*K-1
    scratch = M + np.arange(M * K)
    obs = env.branch(roots, scratch, seed=round_no)       # determinized copies: same view, resampled hidden state
    first = np.full(env.num_envs, -1, np.int32); first[scratch] = candidate_of_each_rollout
    env.step(first)                                       # then roll out with any policy, score, pick per root
```

## Environment variables

| Variable | Effect |
| --- | --- |
| `STS2VEC_DLL` | managed `Sts2Sim.Vec.dll` (file or build directory) to load instead of the Release build |
| `STS2VEC_AOT_LIB` | NativeAOT `Sts2Sim.Vec.dylib`/`.so` (file or directory) — loaded with `ctypes.CDLL`, same exports |
| `DOTNET_ROOT` | dotnet root holding `host/fxr` and `shared/Microsoft.NETCore.App/9.*` (default probing: `DOTNET_INSTALL_DIR`, macOS Homebrew `dotnet@9`, Linux `$HOME/.dotnet` and distro roots, the `dotnet` on PATH, `dotnet --list-runtimes`) |
| `STS2VEC_ALLOW_WORKSTATION_GC=1` | native: skip the Server GC assert (debugging only; ~5x lower batched throughput) |
| `DOTNET_gcServer=0` | forces Workstation GC (create then fails with `ServerGCError`; used by a test) |
| `STS2VEC_REQUIRE_FULL=1` | tests: the `needs V1/V2` tests fail instead of skipping on a stub build |

## NativeAOT (optional)

```bash
scripts/vec_publish_aot.sh                     # -> sim/Sts2Sim.Vec/bin/aot/Sts2Sim.Vec.dylib (13 MB, ~15 s)
STS2VEC_AOT_LIB=sim/Sts2Sim.Vec/bin/aot/Sts2Sim.Vec.dylib uv run --project sim/tools/vec sts2vec check
```

Same exports and semantics; Server GC is fixed at publish time. The Homebrew ILCompiler links openssl/brotli
(`LIBRARY_PATH=/opt/homebrew/lib` is set by the script; the libraries must exist at run time). On Linux the script
produces `Sts2Sim.Vec.so` (13 MB, ~35 s on Huginn) and links with gcc when clang is missing (`AOT_LINKER` overrides).

## Tests

`tests/` covers the loader (exports, optional exports of older builds, version, Server GC refusal in a subprocess,
hostfxr failure text, discovery),
schema/buffers (order, dtypes, sizes, pointer table, vocab/enum maps, xxh64 / encoder_version), the env API against
the stubs (shapes, dtypes, status bits, partial reset, start sequence, start-id validation, empty pool, snapshot /
restore, copy semantics, close), pools (spec JSON, entries, add_fresh/add_save with the fixture save) and the CLIs;
`test_branch.py` the ABI 2 search calls (exact / determinized / many-root branch, release, restore_many, a second
thread branching while one steps, `branch_seed` vectors).
`test_full.py` and one pool test need the real encoder/candidates: they skip with `needs V1/V2` on a stub build and
run automatically once V1/V2 are merged (cross-process determinism, 1,024 x 300 random soak with 0 SIM_ERROR /
CONTENT_GAP, threads 1 vs 8 byte-identical, step-cap truncation + auto-reset, fixture save -> 3 candidates).
When run from a directory above the project without paths, a bundled pytest plugin limits collection to
`sim/tools/vec` (so the gate command does not pick up other uv projects' tests).
