# Publication validation

This publication copies accepted worker sources and aggregate results; it does not
change the training algorithm or run a new GPU experiment.

## Checks performed for this share

- Original Git-object bytes compared with all manifested immutable-source copies
  and Task104 package mirrors. Source SHA-256 and byte counts verified.
- Python syntax parsed without importing or executing the training/Modal runtime.
- Five accepted curve rows cross-checked between CSV and JSON; batching arithmetic,
  inactive acquisition shaping, pending Task104 quality and unused blind status checked.
- Existing scientific plot visually checked and copied unchanged.
- Archived CPU tests retained; portable schema/reference paths checked, including
  the Task099 logging-equivalence test's historical PPO source.
- New publication material and unpublished base changes scanned for common private
  key/API-token/Bearer-literal patterns; no matches in those scans. This is a bounded
  pattern check, not a universal proof that arbitrary source contains no sensitive data.
- Git whitespace/diff checks and Markdown local-link checks performed before push.

Re-run the read-only checks with:

```bash
python3 research/gpt-training/verify_snapshot.py
```

## What was not re-run

The local publication interpreter has no PyTorch, NumPy or pytest. No dependencies
were installed and the dynamic CPU suites were **not re-run here**. The archived
candidate reports record Task099's 38 passing CPU tests and Task104's 30 passing
CPU tests in their original environment. Those historical results do not certify
the newly relocated test harness or a different runtime. The guide provides exact
commands to run the portable suites on a prepared machine.

No new simulator games, checkpoint loads, Modal calls, GPU training/evaluations,
production deployments or service changes occurred. Production native binaries,
vocabulary assets, credentials, signed release descriptors and weights are not in
this publication; full end-to-end reproduction requires separate provisioning.
The current production host has later accepted patches than the inherited host
tooling in this branch. Publishing source is not production deployment approval.
