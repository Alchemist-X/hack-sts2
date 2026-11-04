# STS2 Modal monitor projection

This directory contains only monitor projection and push code. It never writes the research ledger or downloads weights. The Sites Worker source is in `artifacts/modal-research/2026-11-04/monitor/site` (ignored by the repository).

## Snapshot schema

`schema_version: 1`; monotonic `version` and `generated_at`; `data_at` is the newest source observation time. `streams.formal` and `streams.engineering` each contain S/M/L arms with `stage` (`plan`, `code`, `deployed`, `real`), actual decision count, target, and a bounded series of allowlisted metric points. Missing metrics are JSON `null`. `budget.actual_usd` is `null` with `actual_status: unknown` until settlement is complete; platform gross observation and reservations are distinct fields. The Worker rejects extra fields, NaN, timestamps that move backward, and payloads over 1 MiB. It keeps latest plus at most 288 history keys in R2. The browser defaults to formal and fetches `/api/snapshot` every 45 seconds. Server export and push run every 5 minutes. The UI calls data stale after 15 minutes.

## Trusted projection

```sh
python3 sim/tools/modal_research_monitor/export_snapshot.py \
  --root /path/to/research-artifacts \
  --telemetry-input /path/to/protected/checkpoint.json \
  --active-grant /path/to/protected/active-grant.json \
  --output /path/to/protected/monitor_input.json
```

The artifact root contains `status.json` and `research/plan.json`. Production reads those files and the protected telemetry/grant projections. Optional offline engineering and curated metric inputs are separate. It never opens the production SQLite database. The optional telemetry input is a separately verified projection from the restricted Modal Volume collector. If absent, the dashboard uses accepted local status and optional engineering task023 metric only. Malformed telemetry aborts before writing and preserves the previous snapshot. The exporter retains at most 2,000 actual points per arm from its own previous snapshot. No token, raw log, shell command, full path, source document, or model reasoning is copied to the output.

The telemetry projection must have exactly these top-level fields: `job_key`, `experiment_id`, `config_hash`, `grant_fence`, `receipt_fence`, `receipt_status`, `observed_at`, `checkpoint_at`, `manifest_hash`, `identity`, `metadata`, `metrics`. The collector must verify the receipt, ready manifest hash, file length and SHA-256, and the frozen artifact identity before creating this projection. The exporter additionally checks the two fences, active train job, experiment/config hash in status.json and the exact protected active-grant projection, `capacity` result label, and checkpoint termination. It displays such data as **运行中遥测，未终态验收**. In-flight decision counts use the checkpoint telemetry label. The authoritative ledger advances accepted progress only after terminal artifact reconciliation.

The optional `--metrics-manifest` is for already curated local JSON metric sources and requires each formal item to carry the native `capacity` result label. Do not point it at arbitrary external data.

## Push-only unit

```sh
python3 sim/tools/modal_research_monitor/push_snapshot.py \
  --source /run/credentials/sts2-monitor-push.service/monitor_input \
  --token-file /run/credentials/sts2-monitor-push.service/site_access
```

The push helper is standard library only, has a fixed owner-private Sites URL, disables redirects and proxy inheritance, posts `Content-Type: application/json` with the platform `OAI-Sites-Authorization` service header, then GET-verifies version and generation time. It logs neither token nor response body. Keep service token in a private systemd credential, never in Git, shell arguments, or logs. The Worker requires no visitor identity for POST; Sites owner-private dispatch checks service access before the Worker. An identical retry is idempotent.

Initial deployment will show a true empty state until the first successful push. Engineering smoke numbers are separated from formal training; one task023 mini batch is not an end-to-end training rate. Production S first milestone is 10,092,544 new decisions; full common target is 50,069,504 per arm.

## Local checks

`python3 -m unittest sim.tools.modal_research_monitor.test_export_snapshot`

From the Site directory: `bash scripts/build.sh && node scripts/validate-artifact.mjs`. The Worker API was smoke tested with an in-memory R2 stub for GET, POST, duplicate retry, stale version, extra field, and visitor rejection.

## Parallel capacity monitoring

The Site schema already has S, M, and L arms. Monitor grant parsing accepts the current v1 inactive/singleton handoff and v2 `{schema_version:2,grants:[...]}` with at most ten entries, matching the observed workspace GPU quota. This bound applies only to the read-only monitor input; it never limits the controller or GPU concurrency. The collector reads remote JSON only for train grants whose experiment IDs match `silent-a10-capacity-[sml]-formal-v1`; engineering and evaluation grants are ignored for this dashboard. For v2 it writes `{schema_version:2,items:[<verified checkpoint projection>...]}` with up to three capacity-arm records. Each remote read remains bounded to receipt, ready, metrics, and exact ready file metadata; it does not read weights.

The renderer accepts both v1 and v2 telemetry. It matches every arm to its active job, grant fence, experiment ID, and config hash. It keeps up to 512 actual metric points per arm in a private version-fenced sidecar. The card decision count comes from the checkpoint metadata, while each metric point uses only its own `last_completed_update.env_steps` on the x-axis. Missing rows remain empty. Across arms, `data_at` is the latest verified checkpoint time and never moves backward. With no active work and `await_decision`, the formal stream says `等待研究决策` instead of running.

For a complete terminal train metric after its grant becomes inactive, the root controller supplies `monitor/accepted-metrics.json` containing only the last accepted metric summary. The renderer's optional `--accepted-metrics` path reads this bounded projection and verifies experiment/config, the ledger-accepted checkpoint job/manifest hash, metadata decision count, and metric row count before adding its point. It does not open SQLite or the accepted artifact mirror. The projection does not change the controller's scientific state and must not contain raw paths, weights, credentials, logs, or reasoning text.

## Update-level curves (045)

New training generations may include `ready.files.history` pointing to a SHA-256 verified `history.json` of at most 128 KiB. The file schema is `{version:1,series:"train",omitted_rows:<nonnegative>,rows:[...]}`. Each row has a real trainer `update`, cumulative `env_steps`, optional real UTC `at` from the trainer's own `time` field, and finite allowlisted metrics. The collector checks the file hash, byte size, job-bound ready identity, increasing update/steps, and checkpoint bounds, then projects only allowed fields into `metrics.update_history`. The renderer adds all true rows seen during a five-minute interval. It never assigns a checkpoint timestamp to individual older updates; a missing row time remains null. The public Site's existing `points` shape needs no schema change for this train history.

The completed first S run's full `metrics.jsonl` was not persisted in the accepted artifact. A separate read-only audit recovered eight rounded, genuinely logged stdout updates plus the exact terminal metrics row. The private sparse history input is `formal-s-metrics-history-045.json` under the Raven monitor history directory, with a separate private provenance record. The renderer's `--history` input verifies the exact accepted checkpoint job/generation/manifest and experiment config before drawing these nine points. It rejects extra fields and interpolation; all nine old points have `at:null` because their per-update absolute times were not retained. Development evaluation metrics require a separate `evaluations` series and must not be placed in training `points`.
