# scripts/colab/lab — line specs, the line agent, keepalive v3, colab_guard (audit OPS2)

A training line used to exist only as environment variables in a hand-written `launch_*.sh`. Its lifecycle was a
free-text last line ending in `: done`, printed on success and on failure alike, and read by five components with
five regexes. This directory replaces that with three things:

- a **line spec** (`ops/lines/<line>.json` in the bucket);
- one **line agent** per runtime (`line_agent.py`). It reads the runtime's queue `ops/queues/<session>.json`, runs
  the lines through `w3m_line_v2.sh`, and writes `runs/<run>/line_status.json` plus its own heartbeat
  `ops/agents/<session>.json`;
- **keepalive_v3.sh**, which stops a runtime only on two consecutive fresh `drained` heartbeats.

`colab_guard.sh` sits in front of every ssh / scp / `colab exec`, so that a dead session name can never spawn a ghost
runtime.

Nothing here is deployed. The running lines (C3 / S / C4) and `scripts/colab/w3m_line.sh` are untouched.

## Line spec schema

| key | type | meaning |
| --- | --- | --- |
| `line` | name | the line's id, which is also its file name `ops/lines/<line>.json`. Names match `[A-Za-z0-9][A-Za-z0-9_.-]*`. |
| `run` | name | the run dir `$STS/runs/<run>` and the bucket prefix `runs/<run>/` |
| `bundle` | path | the unpacked training bundle (absolute, or relative to the agent's `--work`). A symlink such as `bundle-w17` is resolved. The agent waits up to `bundle_wait_s` (default 43200) for `<bundle>/run_train.sh`. |
| `fork` | `{run, update, from, wait_s?}` | the checkpoint the line starts from, always `pinned-<update>.pt` of `fork.run`. `from: local` reads `$STS/runs/<run>/checkpoints/`. `from: bucket` reads `runs/<run>/checkpoints/` from the store into `$STS/fork/pinned-<update>-<run>.pt`, fetched only once its size is stable across two ticks. Both wait up to `wait_s` (default 21600). A local fork that is missing locally but present in the store is fetched from the store. |
| `env` | object | the driver's knobs. Required: `TAG ENT0 ENT1 MACROS FORK_U LAST ENDS OVERLAP letter`. Optional: `EXTRA WAIT_RUN WAIT_MAX_S DEV AB DEV512`. `letter` (`a`/`b`) and `OVERLAP` (`off`/`double`) become the driver's 3rd and 5th arguments. Checks: `FORK_U == fork.update`; `ENDS` is strictly increasing and above `FORK_U`; `LAST` is the last of `ENDS`; `b` requires `off`. None of these knobs leak in from the agent's own environment. |
| `after` | list | dependencies; all must hold. `{line, need: done\|terminal}` reads that line's `line_status.json`. `{run, need: update, update: N, file?}` is a predecessor run by the old driver, which has no line status: it holds once the trainer's `runs/<run>/status.json` shows `update >= N` with state `finished`/`stopped`, and, if given, once `runs/<run>/<file>` exists in the store. `need: done` on a line that ended `failed`/`killed` fails this line ("dependency"). |
| `control` | `{run}` or null | the paired control run. It is recorded in `line_status.json` for DECIDE / the ledger. |
| `eval_plan` | `legacy` \| `legacy+dev512` \| `dev512` | becomes `EVAL_PLAN` for the driver (see below). Default `legacy`. |
| `kill` | list | GUARD rules. The agent carries them but does not evaluate them (`line_guard.py` is a separate item). |
| `pair_rule` | object | DECIDE's rule. Carried, not evaluated. |
| `on_fail` | `next` \| `hold` | after a failure, `next` takes the next runnable line. `hold` blocks the queue (heartbeat `blocked`) until the failed line is removed from the queue. |
| `replicas` | int | must be 1. The agent refuses more: queue each replica as its own line. |
| `preflight` | `{paths: [...]}` | paths that must exist before the line starts, for example a save pool. A missing path fails the line. |
| `hypothesis`, `note` | text | free text |

Examples: `example-c4.json` (C4, local fork from C3, depending on the old-driver C3 run) and `example-s2.json` (S2,
bucket fork from S, `after c4 terminal`, `on_fail hold`). Validate them with
`python3 -I line_agent.py validate specs/*.json`. Show what one would run with
`python3 -I line_agent.py plan specs/example-s2.json`.

### Eval plans (w3m_line_v2.sh `EVAL_PLAN`)

- `legacy`: trainer flags byte-identical to `w3m_line.sh`. That is per-100 dev-128 greedy-on and greedy-off, plus the
  20-game `silent-a10-v1` every 100. The endpoint evals are unchanged.
- `legacy+dev512`: as `legacy`, except that the periodic `--eval-every 100` eval runs on `DEV512` instead of
  `silent-a10-v1`. This is EVAL-C step 4, for lines that must pair with a legacy line.
- `dev512`: EVAL-C. Flags `--eval-every 0` and `--eval-at-seedset DEV512 --eval-at-modes greedy-off`. Every segment
  end also gets a dev-128 greedy-off endpoint eval, so the line stays comparable to legacy lines.

`DEV512` defaults to `silent-a10-dev-s512`. It has to be in the bundle's `seedsets/`, which `bundle-w17` does not
have. Both non-legacy plans therefore need a bundle built after EVAL-C.

## Store layout and states

```
ops/lines/<line>.json            spec            (written by the orchestrator: line_agent.py publish)
ops/queues/<session>.json        {"session", "lines": [...]}   (line_agent.py queue --session S c4 s2)
ops/agents/<session>.json        heartbeat {state: busy|drained|blocked, reason, active_line, queue:[{line,run,state}],
                                            heartbeat_at, heartbeat_unix, pid, host, interval_s}
runs/<run>/line_status.json      {state, reason, update, segment_end, next_action, heartbeat_at, heartbeat_unix,
                                  session, line, run, control, driver_rc}
runs/<run>/line_driver.log       the driver log (it lives outside the run dir, so gcs_sync never mirrors it)
runs/<run>/STOP | KILL           orchestrator requests: STOP = exit before the next session (rc 3, killed);
                                 KILL = SIGTERM to the driver's process group now (SIGKILL after 60 s)
```

Line states: `waiting` (dependencies or bundle), `forking` (restore / fork fetch / the driver waiting for its fork),
`training`, `evaluating`, `pairing` (reserved for DECIDE), and the terminal states `done`, `failed`, `killed`. One
non-terminal state, `held`, means the line needs a human (an incomplete restore) and blocks the queue.

Heartbeat:

- `busy`: a line is active.
- `blocked`: no line is runnable but some are not terminal (waiting on dependencies, held, an on_fail hold, a missing
  spec, or no queue file at all).
- `drained`: every queued line is terminal.

Driver rc → state: `0` gives `done`, `3` gives `killed` (STOP), and any other rc gives `failed`. The reason for
`failed` is `2` bad arguments, `4` endpoint reports incomplete, `1` anything else, followed by the last log line.

Agent rules:

- **Order.** The agent takes the first queued line whose dependencies hold, one line at a time.
- **No double runs.** A line whose `line_status.json` is non-terminal and fresh (younger than `--stale-s`) under
  another session is not taken.
- **Agent restart.** One agent per session is enforced by a `flock` on `$STS/.line_agent-<session>.lock`. A driver
  that outlives the agent is adopted through `runs/<run>.driver.pid` and `.driver.rc`.
- **Re-running a terminal line.** Delete its `line_status.json` in the store and restart the agent.
- **Restore.** A run the bucket knows (it has `runs/<run>/status.json`) but the runtime does not is restored before the
  driver starts. The gs:// store uses `gcs_pull.py --newest-ckpt-only --skip-existing`. After that, `status.json`,
  `metrics.jsonl` and `evals.jsonl` (if the store has it) must match the store's md5, with one retry. Otherwise the
  line is `held` and no trainer starts. This prevents the 11-05 line-B overwrite of the bucket's metrics history.

## w3m_line_v2.sh: the four changes against w3m_line.sh

Each change is marked `v2 (n)` in the script, and `diff -u ../w3m_line.sh w3m_line_v2.sh` shows all of them.

1. **STOP.** `RUN/STOP` is checked before every training session and before the final evals. If it exists, the
   driver logs `... STOP ...: killed` and exits 3.
2. **Compile fallback for one segment.** A compile-gate trip adds `--compile off` to the current segment only. In v1
   it stayed on for the rest of the driver process, which made C2/D eager for about 5,000 updates.
3. **Before `done`.** The driver makes one more pass over the endpoint evals, which redoes only missing or interrupted
   ones. With `SYNC=1` it then runs `gcs_sync.py --once` and checks that every expected endpoint report exists
   locally and in the bucket. If any is missing, it exits 4 (`endpoint reports incomplete: stop`).
4. **`EVAL_PLAN`.** The eval plan comes from the spec, as described above.

There is also a `-h/--help` usage path. v1 called with no arguments died on `set -u`.

## keepalive_v3.sh and colab_guard.sh

- `keepalive_v3.sh [INTERVAL]` defaults to `DRY_RUN=1`. In dry-run it pings nothing and stops nothing: it prints
  `DRY: …` lines and logs what it would do. The reads still happen (`colab sessions`, `gcloud storage cat` of the
  heartbeats), so a dry instance can run as a shadow next to the live `colab_keepalive.sh`.
  - Two consecutive fresh `drained` reads stop the runtime.
  - A stale heartbeat only raises an alert.
  - A runtime with no heartbeat is only pinged.
  - It alerts once for each failed line, once for a CPU ghost, and once for a session that has been `blocked` longer
    than `BLOCKED_ALERT_S`.
  - It writes its own heartbeat to `$KA_DIR/keepalive_heartbeat`.
- `colab_guard.sh ssh|scp|exec|check|sessions …` refuses (exit 3, plus a line in `$GUARD_ALERT`) in these cases:
  - a session that `colab sessions` does not list;
  - a session listed with Hardware CPU;
  - `colab sessions` itself failing.

  `DRY_RUN=1` still runs the check, then prints the command instead of running it.

## Deployment (not done; proposed order)

1. Upload `scripts/colab/lab/` to the next fresh runtime. Do not use one that runs C3 / S / C4.
2. Publish the line's spec and queue with `line_agent.py publish` / `queue`, using `--store gs://<bucket>` and the
   key.
3. Start the agent with
   `setsid nohup python3 -I line_agent.py run --store gs://<bucket> --key $STS/gcs-sts2-ckpt.json --gcs-pull $STS/gcs_pull.py --session <S> --work /content/sts2 >> /content/sts2/line_agent.log 2>&1 < /dev/null &`.
   The agent is stdlib-only. Its gs:// helper and `gcs_pull.py` run under the VM's `python3`, which has
   google-cloud-storage.
4. Shadow mode: keep the old Tokyo keepalive pointed at the agent's driver log (`runs/<run>.driver.log` still ends in
   `w3m-<tag>: done`), and run `keepalive_v3.sh` with the default `DRY_RUN=1` beside it.
5. After one segment end has been verified (`line_status.json`, the heartbeat, and the endpoint report in the bucket),
   switch to `DRY_RUN=0` and retire the sessions.txt entry.

## Tests

Run `python3 -m pytest -q scripts/colab/lab/tests/test_lab.py`. Without pytest, run
`python3 -I scripts/colab/lab/tests/test_lab.py`.

The tests use stubs for the trainer and evaluator (through the drivers' `TRAIN` / `EV` knobs), JSON checkpoints read
by a fake `torch`, a no-op `sleep` on PATH, a local-directory store, a `colab` CLI stub and a fake
`google.cloud.storage`. They never touch Colab, GCS or the Tokyo VM.

Not covered by the tests: the `SYNC=1` path of `w3m_line_v2.sh` (the background loop needs `setsid`, which macOS
lacks), the real `gcs_pull.py` restore, and `gcloud storage cat` in keepalive_v3.
