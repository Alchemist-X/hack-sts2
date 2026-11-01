"""Regenerate every derived inventory manifest: classifier generators, normalisation, version deltas.

    python3 sim/tools/inventory/classify/run_all.py [shared options] [--sim-ref REF] [--raw]
                                                    [--only GROUP,...] [--skip GROUP,...]

Shared options (common.py): --root, --v107-root, --sim-root, --inventory, --work, --out, --corpus, --only-dirs.
--sim-ref REF exports sim/ at git REF (git archive) into <work>/sim-ref/REF/sim and uses it as --sim-root:
the W0 manifests were generated against fe7240c, and sim/ at a later commit flips the ported / in_sim flags.
--raw also runs extract_models.py (into --inventory) first. Groups run in the order of STEPS; each script
gets the shared options. The corpus group is skipped when --corpus does not exist. The docs group rewrites the
generated blocks of docs/sim-port/inventory-*.md only when --out is the repo's sim/specs/inventory.
"""

import os
import subprocess
import sys
import time

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import common  # noqa: E402

STEPS = [
    ("kernel", ["virtuals.py", "hooks.py", "cmds.py", "readiness.py", "annotate_cmds.py", "kindvirt.py", "prompts.py",
                "sites.py", "tables.py", "build_json.py"]),
    ("powers", ["features.py", "vdiff.py", "build.py"]),
    ("cards-ironclad-colorless", ["vdiff.py", "awaits.py", "build.py"]),
    ("cards-necrobinder-regent", ["vdelta2.py", "build.py"]),
    ("cards-silent-defect", ["build.py"]),
    ("relics-potions", [("vdiff.py", "relics"), ("vdiff.py", "potions"), "build.py"]),
    ("monsters", ["extract.py", "enc_extract.py", "vdiff.py", "powerdeps.py", "gen.py"]),
    ("events", ["build.py"]),
    ("minor-kinds", ["vdiff.py", "build.py"]),
    ("run-layer", ["build_gaps.py"]),
    ("subsystems", ["gen_main.py"]),
    ("corpus", ["scan_parts.py", "scan_runs.py", "aggregate.py", "tree_diff.py", "classify_diff.py", "taint.py",
                "replay_stats.py", "build_inventory.py"]),
    ("normalize", ["normalize.py"]),
    ("version-deltas", ["build.py"]),
    ("docs", ["counts.py"]),
]


def own_args(argv):
    out = {"sim_ref": None, "raw": False, "only": None, "skip": set()}
    rest = []
    it = iter(argv)
    for a in it:
        if a == "--sim-ref":
            out["sim_ref"] = next(it)
        elif a == "--raw":
            out["raw"] = True
        elif a == "--only":
            out["only"] = set(next(it).split(","))
        elif a == "--skip":
            out["skip"] = set(next(it).split(","))
        else:
            rest.append(a)
    return out, rest


def export_sim(ref):
    dest = os.path.join(common.WORK, "sim-ref", ref)
    if not os.path.isdir(os.path.join(dest, "sim")):
        os.makedirs(dest, exist_ok=True)
        archive = subprocess.run(["git", "-C", common.REPO_ROOT, "archive", ref, "sim"], check=True, capture_output=True).stdout
        subprocess.run(["tar", "-x", "-C", dest], input=archive, check=True)
    return os.path.join(dest, "sim")


def main():
    opts, _ = own_args(common.REST)
    args = common.forwarded_args()
    if opts["sim_ref"]:
        sim = export_sim(opts["sim_ref"])
        i = args.index("--sim-root")
        args[i + 1] = sim
    groups = [g for g, _ in STEPS if (opts["only"] is None or g in opts["only"]) and g not in opts["skip"]]
    if "corpus" in groups and not os.path.isdir(common.CORPUS):
        sys.stderr.write("corpus not found (%s): skipping the corpus group\n" % common.CORPUS)
        groups.remove("corpus")
    if opts["raw"]:
        sim = args[args.index("--sim-root") + 1]
        cmd = [sys.executable, os.path.join(common.INVENTORY_TOOLS, "extract_models.py"), "--root", common.DECOMPILE_ROOT,
               "--sim-root", sim, "--out", common.INVENTORY]
        print("+ extract_models.py", flush=True)
        subprocess.run(cmd, check=True)
    for group, scripts in STEPS:
        if group not in groups:
            continue
        for s in scripts:
            name, extra = (s, []) if isinstance(s, str) else (s[0], list(s[1:]))
            cmd = [sys.executable, os.path.join(HERE, group, name)] + args + extra
            t0 = time.time()
            res = subprocess.run(cmd, capture_output=True, text=True)
            log = os.path.join(common.work_dir("logs"), "%s__%s%s.log" % (group, name[:-3], "_" + "_".join(extra) if extra else ""))
            with open(log, "w", encoding="utf-8") as fh:
                fh.write(res.stdout)
                fh.write(res.stderr)
            print("+ %s/%s %s(%.1fs)" % (group, name, " ".join(extra) + " " if extra else "", time.time() - t0), flush=True)
            if res.returncode != 0:
                sys.stderr.write(res.stdout[-4000:] + res.stderr[-4000:])
                sys.stderr.write("\nfailed: %s (log %s)\n" % (" ".join(cmd), log))
                sys.exit(res.returncode)
    print("outputs in %s; stage-1 files and logs in %s" % (common.OUT, common.WORK))


if __name__ == "__main__":
    main()
