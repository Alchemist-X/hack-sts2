# Inventory tooling

Python 3 (stdlib only, 3.8+) scanners that turn the decompiled game tree into the JSON manifests in
`sim/specs/inventory/` (field contract and limits: `sim/specs/inventory/README.md`). Run from the
repo root.

```
python3 sim/tools/inventory/extract_models.py                  # write every manifest
python3 sim/tools/inventory/extract_models.py --check          # validate only; exit 1 on problems
python3 sim/tools/inventory/extract_models.py --kind relics    # rewrite one kind (context still loaded)
python3 sim/tools/inventory/extract_models.py --root <tree> --out <dir> --sim-root <sim/>
python3 sim/tools/inventory/spotcheck.py --kind cards -n 10 [--seed N] [--class Bash] [--no-source]
python3 sim/tools/inventory/model_ids.py StrikeIronclad LeafSlimeM   # print ModelDb entries
python3 -m unittest discover -s sim/tools/inventory -p 'test_*.py'
```

The decompile root defaults to
`/Users/Aincrad/dev-proj/hack-sts2/output/server-bootstrap/linux-decompiled` (override with `--root` or
`STS2_DECOMPILE_ROOT`). The tree is read-only input; nothing outside `--out` is written. A full run
takes 10-40 s and is deterministic. `--check` and every run print one accounting line per kind:
files scanned, entries, non-model files, whether every file is accounted for exactly once, entries with
no pool/act/encounter link, mocks, abstract classes.

| File | Role |
| --- | --- |
| `cs_source.py` | comment/literal masking (string literals become `"STR<k>"` placeholders, line numbers kept), top-level type and member splitting, compiler-generated name mapping (`_003COnPlay_003Ed__5` → `OnPlay`) |
| `model_ids.py` | `StringHelper.Slugify` / `SnakeCase` with .NET `\G` semantics, `ModelId.SlugifyCategory`, `model_id()` |
| `extract_models.py` | context loading (pools, `ModelDb` lists, `AbstractModelSubtypes`, `AbstractModel` hooks, `Hook.cs` dispatchers, Node class names, sim defs), per-kind extractors, cross-kind resolution, validation, JSON writer |
| `spotcheck.py` | prints random entries next to the source lines they cite, for eyeball review |
| `test_inventory.py` | unit tests (slug vectors verified against .NET 9, masker, member splitter, logic-line counting, dynamic vars, RNG tokens) plus tree-backed checks skipped when the tree is absent |

After a game update: regenerate, compare `source_digest` per manifest, then rerun `--check` and the
tests. `summary.json` `validation._problems` must be empty.
