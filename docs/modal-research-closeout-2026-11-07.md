# STS2 Modal research closeout — 2026-11-07 UTC

The existing S115 and M116 runs, both original dev256 evaluations, and the prescribed paired comparison are complete. The last production quiescence check found all 12 known Modal functions quiet, the old timers disabled, and the monitor bridge inactive and unloaded. Further dispatch has stopped. The final static monitor publication succeeded at 05:41:29 UTC on November 7.

## Accepted results

Both evaluations used the same 256 development seeds under the greedy FP32 Silent A10 protocol with search and the breaker off.

| Endpoint | Cumulative decisions | Mean protocol progress | Wins | Act 2 or later | Act 3 | Max floor |
|---|---:|---:|---:|---:|---:|---:|
| S115 | 695,861,248 | 0.46938174609375 | 0/256 | 181 | 14 | 48 |
| M116 | 100,007,936 | 0.24007618359375 | 0/256 | 6 | 0 | 24 |

S115 remains the highest accepted development result. The study goal of mean progress ≥0.6 and win rate >10% remains unmet. The blind1000 set remains unused. Both evaluations had 256 replayable games and zero exported illegal, crash, error, abort, stuck or capped episodes. Transient native flags remain unknown.

M minus S averaged **−0.2293055625** progress, with a paired 95% interval of [−0.25126917675781246, −0.20693053447265627]. M was ahead on 10 seeds, behind on 223 and tied on 23. Task121 ran the fixed PCG64 bootstrap once with 20,000 draws and seed 121116115. S115 continued an existing lineage and M116 started from random initialization. Their training histories and decision counts differ. This comparison describes the observed endpoints. The interval measures variation across evaluation seeds and does not estimate variation across independent training runs.

S115 versus its own 114C ancestor gained +0.03831094921875 mean progress, with a paired 95% interval of [0.0157630759765625, 0.061222021484374986]. It improved on 119 seeds, worsened on 74 and tied on 63.

M116 trained from fresh initialization for 763 updates. Its iterations took 14,170.225 seconds (3h56m10.225s). S115 spent 16,697.626 seconds (4h38m17.626s) across its two continuation segments. These figures cover the training iterations.

## Evidence and code boundaries

All following evidence paths are relative to ignored `artifacts/modal-research/2026-11-04/research/`. They identify local records, not files distributed in this branch. The live authority is Raven `/var/lib/sts2-modal-production/operation.sqlite3`; local databases must never restore or overwrite it. Git HEAD does not represent all deployed accepted overlays.

| Evidence | Path | SHA-256 |
|---|---|---|
| Accepted S originaldev | `task115/actual-finaldev/root-actual-acceptance.json` | `0d001b2f609f20386a96f56fbed4fe0b0d9e76b76491ef0ec3fc1832cf95097f` |
| Accepted S-v114C comparison | `task115/analysis-actual/root-acceptance.json` | `cbf25b2bd779b8d222c9a2671815b7349ff3f9ccf161116e83b74745b368f15b` |
| Accepted M116 training endpoint | `task116/terminal-root-readback/root-actual-endpoint-acceptance.json` | `5ba9aa7d27a41a35ee30a7b538b53c705102fb7d5b260566b3b8ba6e6b912ec7` |
| Accepted M116 original dev256 | `task116/actual-finaldev/root-actual-acceptance.json` | `3f6b11fac065cbe55cc38cf28c65eca19fdca4dd8c35a63fbbe654f1adb8a2c8` |
| Task121 comparison result | `task121-current-comparison-actual/analysis-result.json` | `e11a200956b8cbc6289e6310c3b11fca12b5c777d9b25a98df0e3b47285100fb` |
| Independent Task121 review | `task121-current-comparison-actual/independent-review.json` | `6e3c2794f25bcf7e74c5e05e254ac602891631562f51b28d860c795edd807d5f` |
| Accepted Task121 result | `task121-current-comparison-actual/root-acceptance.json` | `aadee00451127c04ba572b3d698a1192f07ea9aad0a3a29d5b1c49892ce52e72` |
| M training reserve retirement | `task116/terminal-root-readback/financial-retirement/execution-and-readback.json` | `41a5a58723703eeaf2e1bb94b591762af2edc722bf573d8ca65d3c8a1a278cbe` |
| M dev reserve retirement | `task116/actual-finaldev/financial-closeout/execution-and-readback.json` | `abc4a13168340a150192be57e54365995aa86bcf062904655f1bb1fe5fae019c` |
| Production quiescence | `task116/finaldev-execution-preparation/final-quiescence-20261107T053244Z.json` | `8bb4ab54db4497c7d8fa7921fb93fef0345f423ab2a1aca37c370a9599b90846` |
| Accepted current comparison preparation | `task121-current-comparison-root-acceptance.json` | `4b93e25247d8809258571c29edfbc04c983406c34404fe0e4611f96016b17216` |
| Final static monitor publication | `task119-monitor/current-comparison-update/v2-final-closeout/publication-receipt.json` | `3df7e82cb3da7f292581bb35ebeda6583d5a915be833f65b9956b0a7b51dbaf0` |
| Accepted final monitor release | `task119-monitor/current-comparison-update/v2-final-closeout/root-release-acceptance.json` | `c42bc216a236f148911ea32f828e2c843c5f01082d8bd3c606944d0cdbcc402a` |
| Historical S monitor update | `task119-monitor/current-comparison-update/v1-s-final-dev/root-release-acceptance.json` | `cf0288a08681e1229469a4b12480c29c819786ce0f5f040151c30d4b2cd7c6ef` |

[Read-only snapshots](research-snapshots/2026-11-07/README.md) preserve the accepted comparison implementation, authored history, architecture/performance and budget reports, bridge runbook and operator audit with byte hashes. Snapshot fixture tests exercise validation boundaries; they are not actual comparison execution or scientific evidence. Historical reports retain their timestamps and uncertainty.

## Monitoring, throughput and budget

The [owner-private monitor](https://sts2-modal-research-console.crazy-kfc.chatgpt.site) has its own repository, published source `08f6519a79bc53bae02fa5d57218f7bd04a8f687`. The bounded read-only bridge reads saved observations from the existing observer; writer metadata is distinct from accepted checkpoint/evaluation evidence. Historical archive and budget are timestamped snapshots. The final quiescence check found the bridge inactive and unloaded. The final static publication is pending.

The archived operator audit distinguishes implementation from activation and measurement. S115 segment2 recorded 763 updates/100,007,936 added decisions and 8405.512 iteration seconds; these host elapsed times omit portions of the provider lifecycle. Prefetch overlaps learner work and must not be added again. Historical Function32/Sandbox16 timing ratio1.280 is descriptive with multiple changed factors, not a CPU-only causal result. No new performance experiment was run for closeout.

The authorized project budget was $600. The M training and dev reservations of $39.92 and $7.44 were retired. The $21.01 protection for a possible future blind evaluation remains unused. Those amounts describe reservation accounting. Actual call fees, cash attribution, and aggregate root-chat tokens, cached input and context compression remain unknown. The archived budget report retains its original observation time and scope.

## Source coverage gaps

The repository controller is an earlier baseline. Deployed accepted controller054–056 patches and later task-specific runtime/learner/profile overlays remain outside the tracked production entry points. The archive now preserves the available current source layers separately from those entry points. Do not deploy this branch over Raven. Relevant frozen source families include task104 base `candidate-worker/sts2train/{model,features}.py`, task115/116 `ops`, `learning-overlay`, `profile-overlay`, originaldev/terminal collection and accounting adapters, and task119 bridge integration. Task121 comparison is archived as source only. Earlier failed Task057 and deferred117/118/120 preparations are not certified production replacements.

A pre-existing tracked change to `sim/tools/modal_research/artifact_adapter.py` expands metric-field allowlisting. The exact author/deployment provenance is unknown. Closeout reviewed it as an additive local projection change: five existing metric-history tests and a direct expanded-field projection check pass; broader adapter tests pass17/19, with two blocked by missing local NumPy. The branch includes this reviewed local change in a separate commit. Its deployment status is unverified. No ignored artifact tree is added wholesale.
