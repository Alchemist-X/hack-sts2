# STS2 Modal research loop bootstrap

The local controller persists evidence, job intents, attempts, reservations, accepted decisions and experiment progress in a SQLite database on local durable storage. A tick has no sleeps. Research starts only for a new event. A normal segment below the next milestone schedules another segment with the identical frozen specification. At a milestone, evaluation must finish and publish checksummed evidence before a research decision. A decision selects a pre-registered immutable experiment or an allowlisted engineering task. It cannot change approvals or run shell commands.

```bash
OMP_NUM_THREADS=1 MKL_NUM_THREADS=1 OPENBLAS_NUM_THREADS=1 PYTHONDONTWRITEBYTECODE=1 \
  PYTHONPATH=sim/tools python3 -m unittest discover -s sim/tools/modal_research/tests -v
PYTHONPATH=sim/tools python3 -m modal_research \
  --state artifacts/modal-research/2026-11-04/operation.sqlite3 status
```

Use a separate database for fixtures. The operation database records total approval, the current execution cap, the observed platform limits, gross spending, unsettled reserves and persistent pause/stop state. Credit is the currently available platform credit. Snapshot headroom can become stale when unrelated work consumes the same workspace quota. Verify the live ceiling and other activity before production admission. The internal ledger uses integer cents and requires settled actual costs or a retained reservation for unresolved costs. Failed attempts count. Operator approvals and external probe reservations use stable references, and retries never renew a job deadline. A confirmed unsubmitted attempt and an ambiguous receipt take separate paths. A timed-out ambiguous job retains its reserve pending reconciliation and stops new launches.

Ready manifests are published with data checksums and identities. The publisher commits all payload files before atomically creating the immutable marker, then commits the marker. Train checkpoints require model, optimizer, scheduler state, RNG, seed cursor and metrics roles. Exact recovery additionally requires serialized environment state. Fresh-episode recovery advances the seed cursor and is labeled accordingly. Reconciliation rehashes resumed artifacts before launching. The fixture tests verify the protocol with synthetic payloads.

## Deployment boundary

`modal_app.declare_app` declares an H100 training function, a checkpoint evaluation function and a CPU tick with a five-minute schedule when supplied a verified runtime and Modal bindings. It makes no deployment or secret lookup. The tick requires a transactional database shared by deployments. `Store` provides the local-disk SQLite implementation only. A Modal Volume v2 is the artifact mount because immutable marker creation uses a hard link. Worker generations need unique directories, and the runtime reloads Volume state before a newly admitted invocation. The provider-side claim must enforce attempt fencing, deadlines and remaining gross spend even on Modal's automatic restart. The local controller rechecks eligibility immediately before dispatch. An external admission contract is required to close the cross-service race after that check.

`gateway.GatewayBackend` is an authenticated HTTPS transport contract for a verified service with durable job IDs and idempotent submission. That service must implement authoritative lookup, cost settlement, and scoped access to tools for SOL. It needs separate verified research and engineering bindings. It is not a standalone service or an authentication check.

For the first deployment, use the Raven single-host route in [the deployment plan](RAVEN_DEPLOYMENT.md).

The autonomous worker entry is `native_worker.run`. It accepts only a frozen worker request and deployment-owned installation paths. It resolves a resumed checkpoint from the current accepted ledger lineage, verifies its checksum before loading, and gives the trainer the exact protected dev/blind holdouts even when periodic evaluation is disabled. The general-purpose local trainer CLI is outside this guarded entry point.

The deployment remains gated until the shared store, service bindings, actual trainer/evaluator runtime and image, platform enforcement and Volume behavior are integrated and tested. Run the separately budgeted real GPU acceptance before enabling the schedule. Fixture results provide no GPU, gameplay, model API, or deployed availability evidence.

Modal reference material for [schedules](https://modal.com/docs/guide/cron), [preemption](https://modal.com/docs/guide/preemption), [timeouts](https://modal.com/docs/guide/timeouts), and [Volume v2](https://modal.com/docs/guide/volumes).
