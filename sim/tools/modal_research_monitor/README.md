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
