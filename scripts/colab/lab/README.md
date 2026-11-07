# scripts/colab/lab: line runner v2, guard, keepalive v3 (retro batch 1, 2026-11-06)

Audit items OPS2 (line spec, line agent, driver v2, keepalive v3, colab_guard) and GUARD (line_guard, preflight).
**Nothing here is deployed.** The live lines, `scripts/colab/w3m_line.sh`, `scripts/colab/ops/launch_*.sh` and
the running keepalive are unchanged. Everything is stdlib-only Python (`python3 -I`) or bash 3.2. Every tool has `--help`.

## Pieces

| file | what it does |
| --- | --- |
| `specs/README.md`, `specs/example-*.json` | Line spec schema: one JSON per training line, replacing the env vars in a hand-written `launch_*.sh`. Examples: C4, and S2 (S2 waits until C4 is terminal). |
| `line_agent.py` | One agent per runtime. It reads `ops/queues/<session>.json` and takes the first line whose `after` dependencies hold. It prepares the line (preflight paths, bundle wait, md5-checked restore, fork fetch) and runs it through `w3m_line_v2.sh` in its own process group. It writes `runs/<run>/line_status.json` and the heartbeat `ops/agents/<session>.json` (`busy`/`blocked`/`drained`). It is restart-safe: a lock is held and a surviving driver is adopted. |
| `w3m_line_v2.sh` | `w3m_line.sh` plus four changes, each marked `v2 (n)` in the script. (1) A `RUN/STOP` check between sessions exits 3. (2) The `--compile off` fallback applies to one segment only. (3) A final report check runs before `done`, exiting 4 if reports are missing; with `SYNC=1` it also checks the bucket. (4) `EVAL_PLAN` is `legacy`, `legacy+dev512` or `dev512`, and `legacy` gives flags byte-identical to v1. |
| `keepalive_v3.sh` | Lists live runtimes with `colab sessions`, pings the GPU runtimes and reads the agent heartbeats. It stops a runtime only on the 2nd consecutive fresh `drained` heartbeat. A stale heartbeat or a CPU ghost raises an alert and is never stopped. `DRY_RUN=1` is the default. |
| `colab_guard.sh` | Wrapper for every ssh / scp / `colab exec`. It refuses dead or CPU session names, so a typo cannot spawn a billing ghost runtime. |
| `line_guard.py` + `guard-rules-default.json` | Kill rules evaluated on `metrics.jsonl` / `evals.jsonl`: approx_kl, push_clamped, macro pmass collapse, sps vs control, non-finite loss, dev-128 window vs fork, 3 evals < 0.42. `--mode alert` writes `ALERT` and merges a reason into `line_status.json`. `--mode kill` writes STOP, sends SIGTERM to the trainer (same host and RUN_DIR in its cmdline only) and to the driver's process group, then sets `state: killed`. Exit codes: 0 / 10 alert / 11 kill. |
| `preflight.sh` | The gate before a new line: premise (target macro pmass >= 1e-6 fails), what-if (prints the commands; a delta < 0 needs a justification) and smoke (prints the fork+10 driver command; it runs only with `RUN=1` and is judged by `line_guard --tags smoke`). Exit codes: 0 PASS / 1 FAIL / 3 INCOMPLETE. |
| `tests/` | `test_lab.py` (14) and `test_line_guard.py` (16). Both run under `python3 -I` and under pytest. The stubs (fake trainer, eval, colab, torch, GCS) live in `tests/stubs/`. The mirror-based guard tests read `$STS2_GUARD_MIRROR` and skip when it is missing. |

Readouts and verdicts (`evalstats.py`, `decide.py`, `eval_summary.py`) live in `scripts/analysis/` (see `README-eval.md` there).

## Shadow-mode rollout

1. **Tomorrow, the first new line runs under v2 in shadow.** Publish its spec (`line_agent.py publish`) and queue it.
   Start `line_agent.py run` on that runtime. The agent writes `line_status.json` and its heartbeat. The **old
   keepalive stays live and authoritative**. It tails the log listed in its `sessions.txt` and stops the runtime when the
   last line ends in `: done`, and v2 still logs `done` as v1 does. So during shadow mode, queue **one line per
   runtime**, or the old keepalive stops the runtime after the first line and the queue never runs.
   Alongside it, run `keepalive_v3.sh` with `DRY_RUN=1` (the default) as a shadow, and `line_guard.py --mode alert` once
   per mirror tick. Neither can stop or kill anything.
2. **Compare at the first segment end.** Check that `line_status.json` agrees with `train.log` / `status.json`.
   Check every `DRY: colab stop` that keepalive_v3 printed against what the old keepalive did. Every guard ALERT must be a true positive or understood.
3. **Cut over after one endpoint.** Once the line has reached one endpoint with the reports complete (driver
   rc 0 and line state `done`), the shadow decisions must have matched. Then stop the old keepalive and run `keepalive_v3.sh` with
   `DRY_RUN=0`. From then on, route ssh / scp through `colab_guard.sh`.
4. **Kill mode last.** Switch `line_guard.py` to `--mode kill` only after one full line under alert mode with no false
   positive. Under kill mode, the driver group kill also takes the launcher with it, so v1-style `: done` never prints. This is why
   kill mode requires keepalive_v3, which reads `line_status`, to already be the live keepalive.

Rollback at any step: stop the agent and keep the old launcher path. `w3m_line.sh` is untouched.

## Known gaps

- The following were not exercised on stubs: v2 with `SYNC=1` (macOS has no `setsid`), the real `gcs_pull.py` restore, and `gcloud storage cat` reading heartbeats from Tokyo.
- `legacy+dev512` assumes the periodic `--eval-every` eval accepts the dev-512 seedset. This is unverified.
- The S2 example stays on `legacy` because `bundle-w17` has no dev-512 seedset.
- `kill` and `pair_rule` in a spec are carried by the agent but not evaluated. `line_guard.py` and `decide.py` evaluate them.
- The guard's eval window combines independent-sample intervals, which is conservative for paired seeds. The paired window is `scripts/analysis/evalstats.py`.
- On a gs:// store, every call starts one Python process that imports google-cloud-storage, about 10 per 60 s tick. The cost has not been measured on a VM.
