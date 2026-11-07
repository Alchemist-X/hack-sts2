"""Twin lockstep runner: a trained sts2train checkpoint plays the real Slay the Spire 2 through a simulator twin.

Entry point: ``scripts/live/nn_twin_play.py``; user guide: ``scripts/live/README-nn-twin.md``.
"""

import sys
from pathlib import Path

# The live harness layer this runner reuses: scripts/sts2_action_executor.py and the sts2rec package
# (legal_actions, run_save) -- both stdlib-only.
ROOT = Path(__file__).resolve().parents[3]
for _p in (ROOT / "scripts", ROOT / "sts2rec"):
    if str(_p) not in sys.path:
        sys.path.insert(0, str(_p))
