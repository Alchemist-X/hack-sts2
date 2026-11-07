# Task056 production outcome

Task056 extends the user-opted-in Task054 terminal Modal reserve view to nine independently verified historical access probes. The change affects only effective future held budget and its status/research evidence; it leaves each original `jobs.reserve`, `jobs.actual=NULL`, billing binding, event, and decision in place. The S worker was not stopped, retried, or contacted.

## Source and verification

- Active project `store.py` old SHA-256 `f59d66fae6d43240c67324bdba6ad9fb587dd30f19841d26b87273f739f5947e`; new `ddaba51e930326a696d3ce69431caffe40be853e7d44a9e28e18880f09e4ed2e`.
- Patch `056-live-store.patch` SHA-256 `bca54d4aa2fdac3b71ceac875b9c562f36aad910ae1c1db8480f9c50fe75d1ab`; `git apply --check -p1` passed against the 054b package. Only `store.py` changed. The active Task055 controller remained SHA-256 `0367dec5a58d633b166d0c4ff1cdf46bac47ff0aedcec79c7e823e850a58dfbf`.
- Four focused tests passed, covering exact nine terminal proofs, default policy, tampered and active rows, shared status/research budget view, active S reserve, and repeat reads with unchanged jobs/billing. Independent acceptance: `acceptance-056.md`.
- A candidate compiled in memory against the production SQLite opened `mode=ro` with `query_only=ON` returned exactly nine IDs / 1420 cents; `db_total_changes=0`. Before/after projected total held 3320→1900 cents, Modal held 2150→730 cents, historical display 2102→3522 cents, spent unchanged at 745 cents. See `live-ro-dryrun.json`.
- Pinned one-file deployment script SHA-256 `3fa1127134db34dce4d5e6100ac214da0c97ce982ffab63a0aaec3dfe5492bdb` ran once via root-only project staging. It checked old/new store and Task055 controller hashes, saved a root0600 backup, atomically replaced only `store.py`, and read back the new SHA. No timer/service stop or restart, manual tick, Modal, model, or ledger-write command was used.

## Natural runtime postcheck

At the later ordinary status update, the S continuation reached **25,034,752 decisions** and published checkpoint generation `b26b7e492c9742e1ab0c325c55258b7c`, manifest SHA `2785cfba94de391c0ed6e377cb401f8a410cb16a2d78fb9244350df9204da014`. The scheduler then submitted the planned dev256 eval `eval-1b3462b7f4d08a751308a362a164cf94:1`, provider Sandbox `sb-Q4GYqw9qoivXPXiEjDjIU7`, which was running in the captured status. This state transition was produced by the existing timer and is independent of the one-file budget deployment.

The safe `status.json` mirror SHA-256 is `486a00b3ddc95072a6ae2ffdf2239705bf3af661b40ee141cc072af9f1397c5d` (17,126 bytes, server mtime epoch 1793840255). It reports effective total held **$14.50** = active eval $2.80 + non-Modal unknown $11.70, effective Modal held $2.80, historical terminal Modal original reserves $42.52, workspace gross used $7.45, platform gross room after active hold $32.25, and study room $78.05. The approved $100 study cap and $42.50 gross/$30 net platform limits are unchanged. Latest old decision remains `decision-d63c1f6d5211cf42cb85fa7d6e01a226` with action PAUSE; the operational stop is clear under the recorded user override. Timer remains active.

The platform `metered_observed_at=1793840052.9125218` is the **billing snapshot time**, distinct from the later status-file mtime and the S model checkpoint. It does not provide per-job actual costs; all nine historical probe `actual` fields remain NULL. Read-only postcheck compared all 18 probe rows before/after exactly equal; nine Modal rows retain original reserve 1420 cents and the same billing and outcome proofs. The authority is still `/var/lib/sts2-modal-production/operation.sqlite3`; `authority.json` SHA-256 `e3858302b38f7d07a25ce9c1504577992b0993cadfdde3d5e4d307ee0b70ce15` is distinct from the SQLite file hash.
