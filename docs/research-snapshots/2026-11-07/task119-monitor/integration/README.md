# Research dashboard data integration

The owner-private Site serves the research dashboard at `/` and the previous dashboard at `/legacy`. The old `/api/snapshot` schema, validation and R2 keys are retained. `GET /api/research` returns the reviewed archive and the latest bounded observer projection. The reviewed archive is compiled into the Worker with a content hash and its own generation time. `POST /api/research/live` can update only `research/v1/live/latest.json` through the existing private service boundary. Browser visitors receive 403 for this write. The live key uses an R2 conditional write against the observed ETag. [Cloudflare R2 Workers API](https://developers.cloudflare.com/r2/api/workers/workers-api-reference/#conditional-operations)

The live projection covers the two explicitly configured calls. `decisions` and `updates` are cumulative. `new_decisions` is relative to the run's fixed starting checkpoint. `started_at` comes from the submission receipt. Resource numbers describe the submitted resource request. `observed_at` is the time recorded by the existing observer. Training progress and episode counts have a separate worker status time. Missing observations remain null. A returned call and writer checkpoint metadata are distinct from independently verified checkpoint bodies and accepted research results. The history carries those reviewed results separately. This projector emits `writer_metadata` for checkpoint pointers. Each run is stale once its observation exceeds 900 seconds, even when its peer is current.

The archive consumes only the four curated JSON files named in `assemble.py`, verifies their bytes against `curated-input-pins.json`, and reduces filesystem references to short names. Evidence references are text. The assembler never opens the referenced paths. The input pins match the content independent review. The released research data preserve the authored distinction between shared App metering, exclusive App metering, known payments and unknown actual costs.

Run the assembly from `research/task119-monitor`:

```sh
python3 integration/assemble.py --pins integration/curated-input-pins.json \
  --live integration/latest-live.json --output ../../monitor/site/worker/research-archive.js
```

`project_live.py` accepts two explicit immutable observations or two existing observer directories. Directory mode reads at most six recent candidates per call plus its terminal observation, under a 4096-entry listing bound. It skips partially written JSON and rejects foreign identities. It does not poll Modal. The initial archive observations were projected from the existing M116 and S115-S2 observer files. Refreshing the Site does not start a producer.

`push_live.py` makes one bounded HTTPS request and checks the returned version. Use only after the owner has deployed and authorized the endpoint, with the existing approved service credential supplied through an inherited file descriptor. The script neither requests nor copies a credential. It rejects redirects and prints only the result version or error class. The preparation checks used no credential and made no live write.

From the Site directory:

```sh
bash scripts/build.sh
node scripts/validate-artifact.mjs
node scripts/test-research-api.mjs
STS2_MONITOR_PREVIEW_PORT=8879 node scripts/preview.mjs
```

The Node tests use a memory R2 fixture and the self-contained build output. They exercise route coexistence, visitor rejection, oversize and invalid bodies, stale versions and per-run freshness, compare-and-swap conflicts, and legacy GET/POST. Three Python tests cover bounded projection, identity and path handling. The owner performs final deployment and native private access verification.
