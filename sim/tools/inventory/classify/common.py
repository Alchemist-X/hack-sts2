"""Shared configuration for the classifier generators (stdlib only).

Every generator under sim/tools/inventory/classify/<kind>/ imports this module. Options are read
from the command line (unknown options are left for the script) or from the environment:

  --root        STS2_DECOMPILE_ROOT   v0.111.0 decompile tree (one dir per namespace)
  --v107-root   STS2_V107_ROOT        v0.107.1 decompile tree (cross-reference only)
  --sim-root    STS2_SIM_ROOT         sim/ directory (ported status of hooks, Cmds, defs)
  --inventory   STS2_INVENTORY        raw manifests written by extract_models.py
  --work        STS2_CLASSIFY_WORK    scratch for intermediates and stage-1 outputs
  --out         STS2_CLASSIFY_OUT     where publish/normalize write the final manifests
  --corpus      STS2_CORPUS           recorder sessions root (corpus generator only, read-only)
  --only-dirs   STS2_CORPUS_ONLY_DIRS file listing the session dirs to scan (corpus generator; default all)
"""

import argparse
import json
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
REPO_ROOT = os.path.abspath(os.path.join(HERE, "..", "..", "..", ".."))
DEFAULT_DECOMPILE_ROOT = "/Users/Aincrad/dev-proj/hack-sts2/output/server-bootstrap/linux-decompiled"
DEFAULT_V107_ROOT = "/Users/Aincrad/dev-proj/hack-sts2/artifacts/knowledge/decompiled-v0.107.1/MegaCrit/sts2/Core"
DEFAULT_CORPUS = os.path.expanduser("~/Library/Application Support/Sts2Recorder/sessions")
GENERATOR_DIR = "sim/tools/inventory/classify"


def _parse(argv):
    ap = argparse.ArgumentParser(add_help=False, allow_abbrev=False)
    env = os.environ.get
    ap.add_argument("--root", default=env("STS2_DECOMPILE_ROOT", DEFAULT_DECOMPILE_ROOT))
    ap.add_argument("--v107-root", default=env("STS2_V107_ROOT", DEFAULT_V107_ROOT))
    ap.add_argument("--sim-root", default=env("STS2_SIM_ROOT", os.path.join(REPO_ROOT, "sim")))
    ap.add_argument("--inventory", default=env("STS2_INVENTORY", os.path.join(REPO_ROOT, "sim", "specs", "inventory")))
    ap.add_argument("--work", default=env("STS2_CLASSIFY_WORK", os.path.join(REPO_ROOT, "tmp", "inventory-classify")))
    ap.add_argument("--out", default=env("STS2_CLASSIFY_OUT", os.path.join(REPO_ROOT, "sim", "specs", "inventory")))
    ap.add_argument("--corpus", default=env("STS2_CORPUS", DEFAULT_CORPUS))
    ap.add_argument("--only-dirs", default=env("STS2_CORPUS_ONLY_DIRS"))
    return ap.parse_known_args(argv)


OPTS, REST = _parse(sys.argv[1:])
DECOMPILE_ROOT = os.path.abspath(OPTS.root)
V107_ROOT = os.path.abspath(OPTS.v107_root)
SIM_ROOT = os.path.abspath(OPTS.sim_root)
INVENTORY = os.path.abspath(OPTS.inventory)
WORK = os.path.abspath(OPTS.work)
OUT = os.path.abspath(OPTS.out)
CORPUS = OPTS.corpus
ONLY_DIRS = OPTS.only_dirs
_V107_SUFFIX = os.path.join("MegaCrit", "sts2", "Core")
V107_TREE = V107_ROOT[:-len(_V107_SUFFIX) - 1] if V107_ROOT.endswith(os.sep + _V107_SUFFIX) else V107_ROOT
STAGE1 = os.path.join(WORK, "stage1")
INVENTORY_TOOLS = os.path.abspath(os.path.join(HERE, ".."))


def forwarded_args():
    """The shared options as a list, to pass on to a sub-step."""
    out = ["--root", DECOMPILE_ROOT, "--v107-root", V107_ROOT, "--sim-root", SIM_ROOT, "--inventory", INVENTORY,
           "--work", WORK, "--out", OUT, "--corpus", CORPUS]
    if ONLY_DIRS:
        out += ["--only-dirs", ONLY_DIRS]
    return out


def corpus_dirs():
    """Session directory names under CORPUS (sorted), restricted to the --only-dirs list when given."""
    dirs = sorted(x for x in os.listdir(CORPUS) if os.path.isdir(os.path.join(CORPUS, x)))
    if ONLY_DIRS:
        with open(ONLY_DIRS, encoding="utf-8") as fh:
            keep = set(line.strip() for line in fh if line.strip())
        dirs = [d for d in dirs if d in keep]
    return dirs


def work_dir(*parts):
    p = os.path.join(WORK, *parts)
    os.makedirs(p, exist_ok=True)
    return p


def work_path(kind, name):
    return os.path.join(work_dir(kind), name)


def stage1_path(name):
    os.makedirs(STAGE1, exist_ok=True)
    return os.path.join(STAGE1, name)


def inventory_path(name):
    return os.path.join(INVENTORY, name)


def sim_path(rel):
    return os.path.join(SIM_ROOT, rel)


def generator(kind, *files):
    """`generated_by` value: the committed generator paths, repo-relative."""
    return [GENERATOR_DIR + "/" + kind + "/" + f for f in files]


def load_json(path):
    with open(path, encoding="utf-8") as fh:
        return json.load(fh)


def require_dir(path, what):
    if not os.path.isdir(path):
        sys.stderr.write("%s not found: %s\n" % (what, path))
        sys.exit(2)
