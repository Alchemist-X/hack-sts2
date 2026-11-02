# E3 — REC recorder install and anchor verification (2026-11-02)

Owner: E3 `recorder`. Branch `e3/recorder` cut from `ebc055f` (contains the REC commit `4094c1e`,
passive anchors R0–R5/R7/R11). Design: `docs/sim-port/design-diff-gate.md` §3/§4.3/§5/§6;
hook map: `docs/hook-map.md` (v0.111.0 drift addendum, R-table).

## 1. Readiness

| Check | Result |
| --- | --- |
| Installed game (`SlayTheSpire2.app/Contents/Resources/release_info.json`) | `v0.111.0`, commit `41cef1ea`, built 2026-08-13 — matches the sim's ported rules |
| Game processes (`pgrep -fl SlayTheSpire2`, `ps … 'Slay the Spire 2'`) | none before, none after |
| `scripts/mcp_mod.sh status` before | `not installed (pure-human recording mode)` |
| Mods dir before | `Sts2Recorder.{conf,dll,json}` only |

## 2. Recorder install (human game mods dir)

Mods dir: `~/Library/Application Support/Steam/steamapps/common/Slay the Spire 2/SlayTheSpire2.app/Contents/MacOS/mods`.

Backup of the previously installed files (byte copies, `cp -p`):
`<session worktree>/tmp/recorder-backup/20261102-012808/` where `<session worktree>` =
`.claude/worktrees/sts2-eval-pipeline-a3aee7` (git-ignored `tmp/`).

| File | Before (backed up) | After (`scripts/install_mod.sh` from `e3/recorder`) |
| --- | --- | --- |
| `Sts2Recorder.dll` | 168 960 B, sha256 `57b92e44…d367` (Jul 17) | 197 632 B, sha256 `0c3c4d37…dc4a` |
| `Sts2Recorder.json` | version `0.2.0`, sha256 `245ea446…ce5` | version `0.3.0`, sha256 `3a96d96d…e0d` |
| `Sts2Recorder.conf` | `snapshot_min_interval_ms: 800`, sha256 `5dd8349d…a963` | unchanged (the script does not touch it) |

Build: 0 warnings, 0 errors. Recorder unit tests (`mod/tests`, includes `NativeAnchorTests`):
**78/78 passed**.

Rollback: copy the three files from the backup dir back into the mods dir.

## 3. MCP state

`/Users/Aincrad/dev-proj/hack-sts2/scripts/mcp_mod.sh on` (the stash `.mcp-stashed/` lives in the main
checkout, so the main checkout's copy of the script must be used; a worktree copy finds no stash).
It moves `STS2_MCP.{dll,json,conf}` into the human mods dir and adds `STS2_MCP` to the
`mod_list` of the human `settings.save` (the script's own `sync_settings`; `off` reverts both).
**Left ON at the end of this step for the farm phase.**

Note for the farm: headless workers do not use the human mods dir. `headless_provision.sh` builds an
overlay runtime whose `Contents/MacOS/mods` is a private directory holding the sandbox MCP (port from
`STS2_MCP_PORT`) and the recorder built from the provisioning worktree's `mod/`. The workers ran with
the human-dir MCP irrelevant to them; turning `mcp_mod.sh off` does not affect a running farm.

### MCP build fix for v0.111.0 (scripts/build_selection_mcp.sh)

`headless_provision.sh` failed: the pinned STS2MCP `55e0648` + selection/rule-evidence patches do
not compile against v0.111.0 (5 errors: `Hook.ModifyDamage` gained a `CardPlay?` parameter,
`StartRunLobby.MaxPlayers` → field `_maxPlayers`, `LoadRunLobby.ConnectedPlayerIds` → `PlayerIds`).
The same three substitutions were already used for the 2026-10-27 macOS training build
(`training/adapt_mcp.py`, main checkout). `build_selection_mcp.sh` now applies them only when the
installed `release_info.json` version is `v0.111.*` (v0.107.x builds unchanged). Result: 0 warnings,
0 errors; sandbox `STS2_MCP.dll` sha256 `2247b47b…6280`. The build also needs
`STS2_MCP_SOURCE_REPO=/Users/Aincrad/dev-proj/STS2MCP` from a worktree (the default resolves to
`.claude/worktrees/STS2MCP`) and `artifacts/mcp/<pinned commit>/` copied from the main checkout to
skip the upstream rebuild.

## 4. Anchor verification run (one headless worker)

```
STS2_MCP_SOURCE_REPO=/Users/Aincrad/dev-proj/STS2MCP STS2_HEADLESS_PORT_BASE=15760 \
  scripts/headless_provision.sh 1 <session worktree>/tmp/farm/verify/worker-1
scripts/headless_launch.sh 1 --base <session worktree>/tmp/farm/verify/worker-1 --record
```

Worker 1: port 15761, isolated HOME, recorder root `tmp/farm/verify/worker-1/inst1/recordings`
(the launcher always uses `<base>/inst<i>/recordings`; the base dir is the isolation knob). The
runtime's recorder DLL is byte-identical to the installed REC build (`0c3c4d37…`). Driver: a
biased-random policy (85 % play a random playable card in combat, 70 % leave shops, otherwise a
uniform legal ref, potions never) over `sts2_live.py` refs via `run_bench.live`; unseeded
menu start (this MCP has no eval API / seeded embark). The lobby inherited the seeded profile's
ascension, so the run is **Ironclad A10**. 48 driver decisions (30 + 18), floors 1–4: Neow →
Monster (`SHRINKER_BEETLE_WEAK`, won) → Unknown/`BRAIN_LEECH` → Monster
(`FUZZY_WURM_CRAWLER_WEAK`, lost; game over). Worker stopped cleanly (SIGTERM).

Session `1793601073-5PVSAJ48Z6DQ`: manifest recorder `0.3.0`, game `v0.111.0`, states 242,
actions 62, events 335, `degraded_hooks: []`, snapshot build avg 6.56 ms / max 32.2 ms.
`sts2rec validate`: **OK, 0 errors, 0 warnings**.

| Anchor | Evidence in the session |
| --- | --- |
| R1 save copies | 6 `run_saved` events ↔ 6 `native/saves/<seq>.save` (`000003` room_entry Ancient, `000010` ancient_exit, `000019` room_entry, `000333` combat_exit, `000342` room_entry, `000357` room_entry). All 6 parse with `sts2rec.run_save.load_run_save`, 0 unsupported reasons |
| R2 replay copies | 1 `replay_archived` → `native/replays/000332.mcr` (win path, floor 2) + `native/replay.mcr`. The loss-path copy (CleanUp) was not exercised: the worker was stopped on the game-over screen |
| R3 deck | `player.deck` in 19 states (non-combat + combat setup) |
| R4 reward offers | `reward_taken` carries `offer` (1/1, a gold reward); 1 state with `rewards.items`. No card reward was picked, so `picked_cards`/reroll were not exercised |
| R5 event option keys | 2/2 `event_option` actions carry `text_key`, `relic_id`, `index` (`NEOW.pages.INITIAL.options.PRECARIOUS_SHEARS`, `BRAIN_LEECH.pages.INITIAL.options.SHARE_KNOWLEDGE`) |
| R7 relic props | `player.relics[].props` in 242/242 states |
| R11 unlock fields | 1 `unlock_profile` event (runs 164, wins 58, discovered acts, wongo points, test-subject kills) |

### Export and gates

`sts2rec export-bundles-v2 <out> --root <farm sessions> --sim-core ../sim/Sts2Sim.Core` (always
pass `--root`: the default is the human corpus):
1 run; segments 8 (combat 2, map 3, room 3); eligible 1 (combat); decisions 47, eligible 20;
reasons `anchor_missing` 7, `content_not_ported:event` 1; bundle anchors 7 (1 `mcr_initial` + 6 save).

`Sts2Sim.Diff -- <out> --v2`: segments 8 — agree 1, hard divergence 0, skipped 7
(`exporter_ineligible`), error 0, sim exception 0; decisions 20/20 agreed; outcome 1/1.

`Sts2Sim.Diff -- g3 <out>`: 1 run, **8/8 segments agree, 45/45 decisions, floors 4/4, reached the
recorded end**; first divergence: none.

So the REC recorder output from an MCP farm worker is valid and replays exactly under G3, but
**the save anchors do not yet make segments eligible under G1/G2**. Exporter gaps
(`sts2rec/sts2rec/diff_export_v2.py`), for the exporter owner:

1. `index_anchors` keys `run_saved` events by `e.data["seq"]`; the recorder writes the sequence as the
   record's top-level `seq` (file name = event seq). Every save anchor is therefore emitted with
   `act/floor = null` and `kind` defaulted to `room_entry`; `anchor_kind`
   (`room_entry`/`ancient_exit`/`combat_exit`) from the event data is ignored. Fix: key by `e.seq`
   and take `kind` from `data["anchor_kind"]`.
2. Run segments (`_finish_run_segment`) always append `anchor_missing`, and later combats only bind
   `native/replay.mcr`; no segment binds a `.save` anchor (design §3 save import is not wired).
3. R2 per-combat copies `native/replays/*.mcr` are not indexed (only `native/replay.mcr`).

## 5. Artifacts (git-ignored, not committed)

- `<session worktree>/tmp/recorder-backup/20261102-012808/` — pre-install recorder files
- `<session worktree>/tmp/farm/verify/worker-1/` — provisioned base (runtime overlay 172 MiB logical, inst1)
- `<session worktree>/tmp/farm/verify/worker-1/inst1/recordings/sessions/1793601073-5PVSAJ48Z6DQ/` — session
- `<session worktree>/tmp/farm/verify/{bundles-v2,report-v2.json,report-g3.json,drive*.log,live-run*}`
