# Classifier generators

Python 3 scripts (stdlib only) that regenerate every derived manifest in `sim/specs/inventory/`: the
`*.classified.json` files, `kernel-gaps.json`, `run-layer-gaps.json`, `subsystems.json`,
`corpus-inventory.json`, `requirements.json`, `batches.json` and `version-deltas.json`. The raw manifests
(`cards.json`, `hooks.json`, ...) come from `../extract_models.py`. Run everything from the repo root.

```
python3 sim/tools/inventory/classify/run_all.py --sim-ref fe7240c          # every step, final files into sim/specs/inventory/
python3 sim/tools/inventory/classify/run_all.py --sim-ref fe7240c --out /tmp/inv --skip corpus
python3 sim/tools/inventory/classify/run_all.py --only normalize,version-deltas,docs   # re-normalise from the last stage-1 files
```

A full run takes about 4.5 minutes (`corpus/tree_diff.py` about 3 of them), writes stage-1 files, intermediates
and one log per script under `--work` (default `tmp/inventory-classify/`, git-ignored) and is deterministic: two
runs with different `PYTHONHASHSEED` give the same bytes. Each output carries `generated_by`, the list of the
scripts below that produced it.

## Options

Every script reads the same options (`common.py`); `run_all.py` passes them on.

| Option | Environment | Default | Meaning |
| --- | --- | --- | --- |
| `--root` | `STS2_DECOMPILE_ROOT` | `output/server-bootstrap/linux-decompiled` (main checkout) | v0.111.0 decompile tree, rule truth |
| `--v107-root` | `STS2_V107_ROOT` | `artifacts/knowledge/decompiled-v0.107.1/MegaCrit/sts2/Core` | v0.107.1 tree, version deltas only |
| `--sim-root` | `STS2_SIM_ROOT` | `sim/` of this checkout | sim sources read for ported / in_sim status |
| `--inventory` | `STS2_INVENTORY` | `sim/specs/inventory` | raw manifests written by `extract_models.py` |
| `--work` | `STS2_CLASSIFY_WORK` | `tmp/inventory-classify` | intermediates, stage-1 files, logs |
| `--out` | `STS2_CLASSIFY_OUT` | `sim/specs/inventory` | final manifests |
| `--corpus` | `STS2_CORPUS` | `~/Library/Application Support/Sts2Recorder/sessions` | recorder sessions, read only (corpus group) |
| `--only-dirs` | `STS2_CORPUS_ONLY_DIRS` | all sessions | file with the session dir names to scan |

`run_all.py` adds `--sim-ref REF` (export `sim/` at git `REF` with `git archive` into `<work>/sim-ref/REF/` and
use it as `--sim-root`), `--raw` (run `extract_models.py` into `--inventory` first), `--only` and `--skip`
(comma-separated group names). The corpus group is skipped when `--corpus` does not exist.

**W0 sim root.** The committed manifests were generated with `--sim-ref fe7240c` (the W0 commit). The W1
scaffold commits add stub dispatchers, virtuals and facades to `sim/Sts2Sim.Core`, which flip the `in_sim` /
`ported` flags in `hooks.json`, `summary.json` and `kernel-gaps.json` and from there the tiers. Regenerate
against the current `sim/` only when the flags should follow the kernel (then review the tier moves).

## Pipeline

Stage 1: each group writes `<work>/stage1/<file>`. Stage 2: `normalize/normalize.py` reads every stage-1 file and
writes the final files into `--out`; `version-deltas/build.py` then reads those. Groups depend on earlier groups
only through stage-1 files: `powers`, `relics-potions` and `monsters` read `kernel-gaps.json`; `monsters` reads
`powers.classified.json`.

| Output (`sim/specs/inventory/`) | Group | Scripts, in order |
| --- | --- | --- |
| `kernel-gaps.json` | `kernel` | `virtuals.py`, `hooks.py`, `cmds.py`, `readiness.py`, `annotate_cmds.py`, `kindvirt.py`, `prompts.py`, `sites.py`, `tables.py`, `build_json.py` |
| `powers.classified.json` | `powers` | `features.py`, `vdiff.py`, `build.py` (annotations `ann_00.py` ... `ann_14.py`, `annot_lib.py`) |
| `cards-ironclad-colorless.classified.json` | `cards-ironclad-colorless` | `vdiff.py`, `awaits.py`, `build.py` (records `manual_ironclad.py`, `manual_colorless.py`, `manual_other.py`; `condense.py`) |
| `cards-necrobinder-regent.classified.json` | `cards-necrobinder-regent` | `vdelta2.py`, `build.py` (records `data_necro.py`, `data_regent.py`, `helpers.py`; requirement texts `catalog.py`; version table `vd.py`) |
| `cards-silent-defect.classified.json` | `cards-silent-defect` | `build.py` (records `data_common.py`, `data_silent.py`, `data_defect.py`, `data_powers.py`; version check `vdelta.py`, `view.py`) |
| `relics.classified.json`, `potions.classified.json` | `relics-potions` | `vdiff.py relics`, `vdiff.py potions`, `build.py` (records `annot_common.py`, `annot_relics_1.py` ... `_3.py`, `annot_potions.py`; `condense.py`) |
| `monsters.classified.json`, `encounters.classified.json` | `monsters` | `extract.py`, `enc_extract.py`, `vdiff.py`, `powerdeps.py`, `gen.py` (records `annot_m1.py`, `annot_e.py`; `graphfmt.py`) |
| `events.classified.json` | `events` | `build.py` (records `sem_a.py` ... `sem_d.py`, `semlib.py`, `glossary.py`, `costs.py`; version check `vdelta.py`) |
| `enchantments`, `afflictions`, `orbs`, `modifiers`, `acts`, `characters` `.classified.json` | `minor-kinds` | `vdiff.py`, `build.py` (records `data_<kind>.py`, `lib.py`) |
| `run-layer-gaps.json` | `run-layer` | `build_gaps.py` |
| `subsystems.json` | `subsystems` | `gen_main.py` (data `gen_part1.py` ... `gen_part5.py`, `spec1.json`, `spec2.json`; scan `usage.py`) |
| `corpus-inventory.json` | `corpus` | `scan_parts.py`, `scan_runs.py`, `aggregate.py`, `tree_diff.py`, `classify_diff.py`, `taint.py`, `replay_stats.py`, `build_inventory.py` |
| every `*.classified.json` (final), `requirements.json`, `batches.json`, the four gap manifests (final) | `normalize` | `normalize.py` (catalogue `catalog.py`, `requirements.py`; relations `deps.py`; prompt records `prompts.py`) |
| `version-deltas.json` | `version-deltas` | `build.py` (manual and IL adjudications `triage.py`; evidence finder `evidence.py`) |
| generated count blocks of `docs/sim-port/inventory-*.md`, all of `docs/sim-port/inventory-minor-kinds.md` | `docs` | `counts.py` (writes into `docs/` only when `--out` is the repo's `sim/specs/inventory`, else into `<work>/docs`) |

Exact commands, one per script (shared options omitted; add `--sim-root <dir>` or the other options as needed):

```
# kernel-gaps.json (stage 1)
for s in virtuals hooks cmds readiness annotate_cmds kindvirt prompts sites tables build_json; do
  python3 sim/tools/inventory/classify/kernel/$s.py; done
# powers.classified.json (stage 1)
for s in features vdiff build; do python3 sim/tools/inventory/classify/powers/$s.py; done
# cards-ironclad-colorless.classified.json (stage 1)
for s in vdiff awaits build; do python3 sim/tools/inventory/classify/cards-ironclad-colorless/$s.py; done
# cards-necrobinder-regent.classified.json (stage 1)
for s in vdelta2 build; do python3 sim/tools/inventory/classify/cards-necrobinder-regent/$s.py; done
# cards-silent-defect.classified.json (stage 1)
python3 sim/tools/inventory/classify/cards-silent-defect/build.py
# relics.classified.json, potions.classified.json (stage 1)
python3 sim/tools/inventory/classify/relics-potions/vdiff.py relics
python3 sim/tools/inventory/classify/relics-potions/vdiff.py potions
python3 sim/tools/inventory/classify/relics-potions/build.py
# monsters.classified.json, encounters.classified.json (stage 1)
for s in extract enc_extract vdiff powerdeps gen; do python3 sim/tools/inventory/classify/monsters/$s.py; done
# events.classified.json (stage 1)
python3 sim/tools/inventory/classify/events/build.py
# the six minor kinds (stage 1)
for s in vdiff build; do python3 sim/tools/inventory/classify/minor-kinds/$s.py; done
# run-layer-gaps.json, subsystems.json (stage 1)
python3 sim/tools/inventory/classify/run-layer/build_gaps.py
python3 sim/tools/inventory/classify/subsystems/gen_main.py
# corpus-inventory.json (stage 1)
for s in scan_parts scan_runs aggregate tree_diff classify_diff taint replay_stats build_inventory; do
  python3 sim/tools/inventory/classify/corpus/$s.py; done
# final files into --out
python3 sim/tools/inventory/classify/normalize/normalize.py
python3 sim/tools/inventory/classify/version-deltas/build.py
python3 sim/tools/inventory/classify/docs/counts.py
```

## What is data and what is computed

The `manual_*`, `data_*`, `ann_*`, `annot_*`, `sem_*`, `gen_part*`, `spec*.json`, `catalog.py` and `triage.py`
files hold the reviewed classification (mechanics, prompts, tiers, batches, requirement texts, version-delta
adjudications), each record citing the decompile. The other scripts compute: fingerprints, v0.107.1 diffs,
await orders, move graphs, hook and facade status, requirement keys, tiers with dependencies, batch order,
prompt records, version-delta evidence. Change a classification by editing its record and re-running the group
plus `normalize` and `version-deltas`; never edit the JSON.

Frozen inputs: `corpus/replay_stats.py` carries the W0 replay-coverage reading (`W0_COVERAGE` and three
descriptive strings) as constants, because that count came from a manual decode of archived replays. The corpus
scripts read the recorder sessions read only and write aggregate counts; intermediates stay in `--work`.
`corpus-inventory.json` is a snapshot of the corpus at generation time; `--only-dirs` pins a session list.

## Version deltas

`version-deltas/build.py` takes the union of every classifier `version_delta_v107` flag (status other than
`same`) and the diff gate's mechanical diff (`corpus-inventory.json` `version_diff_v0_107_1_vs_v0_111_0`:
`content_files`, `version_exclusive_content`, `engine_files`) and triages each entry `changed`,
`refactor_only` or `unknown`. Order of rules: a record in `triage.py` (read by hand; `il` records use the
`LdMemberToken` arrays read from `sts2.dll`, `docs/sim-port/w0-critique.md` section 7); card, relic and potion
pools by a membership diff of their generator lists (cards declaring `MultiplayerOnly` are dropped from
single-player pools, `Models/CardPoolModel.cs:77`); a file in one tree only is `changed` unless it is a test mock;
otherwise the classifier status (`rule-change` -> `changed`, `cosmetic` -> `refactor_only`). Engine files have no
classifier and stay `unknown`. `single_player_reachable` is false for test mocks, `MultiplayerOnly` cards (read from
the card source), powers the powers classifier marks `multiplayer_only`, and relics whose `IsAllowed` needs more
than one player. Every entry cites lines in both trees (`evidence.py`: token diff ranked by the
classifier's fingerprint, the diff-gate tokens and the note's `a -> b` values, else difflib, else the matching
member); the build stops if a `triage.py` citation does not resolve.

## After a game update

1. Re-decompile, regenerate the raw manifests (`extract_models.py`, then `--check`).
2. `run_all.py --out <scratch>` and diff against `sim/specs/inventory/`. Several builds stop on a model
   without a record or an unclassified version difference (cards-ironclad-colorless, relics-potions, events,
   minor-kinds); for the others compare the entry counts with the raw manifests.
3. Add or update the records, re-run, review `version-deltas.json`, then publish with the default `--out`.
4. `python3 -m unittest discover -s sim/tools/inventory` and `extract_models.py --check`.
