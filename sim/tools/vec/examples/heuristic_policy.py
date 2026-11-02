"""Run the built-in rule policy on a protocol seed set and print the protocol metric.

    uv run --project sim/tools/vec python sim/tools/vec/examples/heuristic_policy.py [std-a0-v1]

Equivalent CLI: `sts2vec eval --policy heuristic --seedset std-a0-v1`. The rules are documented on
sts2vec.policies.HeuristicPolicy; subclass it and override `choose(obs, i)` to try a variant.
"""

import sys

from sts2vec.evaluate import evaluate
from sts2vec.policies import HeuristicPolicy

seedset = sys.argv[1] if len(sys.argv) > 1 else "std-a0-v1"
report = evaluate(seedset, HeuristicPolicy, f"tmp/eval/example-heuristic-{seedset}", threads=8)
h = report["headline"]
print(f"{seedset}: progress {h['progress_mean']:.3f} {h['progress_ci95']}, wins {h['wins']}/{h['games']}")
