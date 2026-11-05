# scripts/analysis

- `death_profile.py` — death profile of eval dirs (`games/*.summary.json`) and / or `episodes.jsonl` (`@LO-HI` update
  ranges), one label per input, Markdown tables side by side (`--json` for machines): end-location bands, conditional
  death rates, killer ranking (encounter-id suffix kind), and with the wave-6 run-state fields boss-entry HP, deck /
  relics at death, death room kind, per-encounter exposure from eval `fights`; a data-gaps line per label. Stdlib only:
  `python3 -I scripts/analysis/death_profile.py M=<eval dir> T=<episodes.jsonl>@2065-2264`. Method:
  `docs/diag-2026-11-05/death-analysis-m-u2264.md`; tests: `sim/tools/train/tests/test_death_profile.py`.
