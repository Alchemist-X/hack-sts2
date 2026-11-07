# Research closeout snapshots

Read-only archive of authored summaries and the accepted Task121 comparison implementation. These files do not replace the production controller or constitute a deployable release. Task121 execution requires separately accepted original endpoints and one-shot authority; the snapshot includes no endpoint data or authority. Its copied README describes the preparation state at capture.

`manifest.json` pins every original byte. Source references within reports point to ignored local evidence under `artifacts/modal-research/2026-11-04/`; those artifacts are deliberately excluded from Git. No weights, trajectories, generated logs, database, credentials or runtime environment are included. The independent monitor site repository is published separately at commit `08f6519a79bc53bae02fa5d57218f7bd04a8f687`.

Historical reports retain their observation times. Current results and pending completion are summarized in `docs/modal-research-closeout-2026-11-07.md`.

## Coverage and exclusions

The archive additionally retains current S/M runtime/ops, full42-file M worker base, learning/profile overlays, accepted originaldev and terminal collection/inspection/accounting source, bridge integration, and published monitor source. Folder names containing candidate describe their historical development location; individual manifest status distinguishes accepted deployed work from accepted disabled preparation. Copying source does not grant permission to launch it. Scripts are mode0644 and no package/install/build hook is added to the main repository.

Apply source layers conceptually in order: `task116/ops/frozen_source` is the worker base; `learning-overlay` supplies PPO/strategic helper; `profile-overlay` supplies M-specific transition contract and GPU gate. Those two profile files intentionally differ from the frozen base and match the private-runtime source-proof fields. Ops wrappers bind image/release paths and require separately retained authority/data; the snapshot is not a self-contained runtime.

Native audit excerpts (task112 encoder/schema/VecEnv/status and task105 RunLoop) are preserved. The worker base commit53dd1d9 contains Python only and reuses native binaries. Complete mapping from those reused binaries to a full C# build tree was not independently verified for closeout. The existing tracked `sim/` tree supplies the repository simulator source; no binary or unverified native build identity is invented.

Excluded: intermediate history/preimage variants, independent-review duplicate trees, failed Task057 draft, deferred117/118/120 experiments, raw trajectories/logs/evaluation payloads, model/checkpoints, seed bodies, runtime authority/bindings, SQLite, hosting connection settings, credentials, and site-generated data/build output. These are not silently certified as accepted production code. Some accepted ops files retain commands or paths for historical review; no command was executed from this archive.

Historical accepted controller054–056 patches and targeted tests are included as unapplied patches, with authored acceptance reports. Native read-only source-path proof `task112/readonly-source-path-proof.json` SHA `31fdb8d009f9feb900945cbcd6c110ef6f54d41b3e8d30ee22bac914bdd208a5` records engine SHA `2d035a9be243fd40b9cef407d31fb46c12b6ca42ff8e39a6f95f15042fa984ce`. This is existing evidence, not a new build verification.

The monitor source imports generated `worker/research-archive.js`; that generated data package is deliberately absent from this archive. Its published SHA and size are indexed in `manifest.json` under `excluded_generated_site_payload`. No stub or replacement is provided. Consequently this main-branch archive cannot directly build or deploy the monitor. The complete previously published site remains in its separate repository at commit `08f6519a79bc53bae02fa5d57218f7bd04a8f687`.
