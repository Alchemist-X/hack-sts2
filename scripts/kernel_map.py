#!/usr/bin/env python3
"""Build sim/specs/kernel-map.json: every kernel file of the rule-truth decompile -> the sim files that port it.

  python3 scripts/kernel_map.py [--tree <decompile root>] [--tree-version 0.111.0] [--unmapped] [--check]

The map is what scripts/version_delta.py reads to say which sim files a changed kernel file touches (content files
map through sim/specs/inventory and Defs/<Class>.cs; the kernel has no such 1:1 layout). Keys are decompile paths
relative to the tree root (`MegaCrit.Sts2.Core.Commands/PowerCmd.cs`); version_delta normalises them to its
`Commands/PowerCmd.cs` key, so a nested-layout tree resolves as well.

Three derived sources, each recorded per file under `via` so a reviewer can see why a sim file is listed:
  type   the decompiled file's top-level type name is declared in sim Core (class / struct / enum / interface /
         record, partials included), or a fixed renaming of it: <T>State (Creature -> CreatureState), <X>Model ->
         <X>Def (CardModel -> CardDef). Content Defs directories are excluded.
  cite   a sim Core file cites the decompiled path as `file:line` in a comment (bare `Hook.cs:12` resolves when the
         file name is unique among kernel files).
  spec   sim/specs/**/*.md and docs/sim-port/*.md: markdown lines (table rows, sentences) that cite the
         decompiled file (`file:line`) and name a sim symbol on the same line (`CombatLoop.BaseHandDrawCount`,
         `CreatureState`: a type declared in sim Core outside content Defs) or a sim Core path. A symbol that is also
         a game type counts only for its own file. A pair needs SPEC_MIN_LINES such lines; `via` lists the docs.
plus the hand-curated part, which this script never rewrites:
  curated   {path: {sim: [...], status, reason}}: replaces (`replace: true`) or extends the derived sim files.
  patterns  [{glob, status, sim?, reason}]: for whole namespaces (save migrations, multiplayer queues, ...).
            A file entry in `curated` wins over a pattern; the first matching pattern wins.
Status per file: `mapped` (sim files listed), `no_port` (curated: deliberately not ported, with the reason), or
`unmapped` (nothing found: the review list). `--check` writes nothing and exits 1 when the committed map differs
from a regeneration, a curated key or pattern matches nothing, or a kernel file of >= 50 lines is unmapped.

Stdlib only.
"""

import argparse
import collections
import fnmatch
import json
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import version_delta as V  # noqa: E402

REPO = V.REPO
MAP_PATH = os.path.join(REPO, "sim", "specs", "kernel-map.json")
SCHEMA = "sts2sim.kernel-map/v1"
SPEC_MIN_LINES = 2         # a spec pair needs this many co-citing lines (all docs together)
CHECK_MIN_LINES = 50
_TICK_SYM = re.compile(r"`([A-Za-z_]\w*)(?:\.[\w<>]+)*(?:\([^`]*\))?`")

_TYPE_DECL = re.compile(
    r"^[ \t]*(?:\[[^\]\n]*\][ \t]*)*(?:(?:public|internal|private|protected|static|sealed|abstract|partial|readonly|"
    r"unsafe|file|ref|new)\s+)*(?:class|struct|enum|interface|record(?:\s+(?:struct|class))?)\s+([A-Za-z_]\w*)",
    re.M)
_SIM_PATH = re.compile(r"(?<![\w./-])((?:sim/)?(?:Sts2Sim\.Core/)?(?:[A-Z][\w]*/)+[A-Z][\w.]*\.cs)(?!:\d)")


def top_level_types(text):
    """Type names declared at brace depth 0 or 1 (namespace block or file-scoped namespace)."""
    code = V.strip_comments(text)
    out = []
    for m in _TYPE_DECL.finditer(code):
        depth = code.count("{", 0, m.start()) - code.count("}", 0, m.start())
        if depth <= 1:
            out.append(m.group(1))
    return out


def kernel_files(tree):
    """{rel path: {key, area, lines, types}} for every kernel-scoped .cs file in the tree."""
    out = {}
    for dp, dns, fns in os.walk(tree):
        dns.sort()
        for fn in sorted(fns):
            if not fn.endswith(".cs"):
                continue
            ab = os.path.join(dp, fn)
            rel = os.path.relpath(ab, tree).replace(os.sep, "/")
            key = V.canonical_key(rel)
            scope, area, _ = V.classify(key)
            if scope != "kernel":
                continue
            with open(ab, encoding="utf-8", errors="replace") as fh:
                text = fh.read()
            out[rel] = {"key": key, "area": area, "lines": text.count("\n") + 1, "types": top_level_types(text)}
    return out


def sim_index(repo):
    """sim Core: declared type -> files (content Defs excluded); all rel paths; file:line citations per file."""
    core = os.path.join(repo, "sim", "Sts2Sim.Core")
    types = collections.defaultdict(set)
    paths = set()
    cites = collections.defaultdict(set)   # citation key -> sim files
    for dp, dns, fns in os.walk(core):
        dns[:] = sorted(d for d in dns if d not in ("bin", "obj"))
        for fn in sorted(fns):
            if not fn.endswith(".cs"):
                continue
            ab = os.path.join(dp, fn)
            rel = os.path.relpath(ab, repo).replace(os.sep, "/")
            paths.add(rel)
            text = V.read_text(ab)
            content = any(s in rel for s in ("/Defs/", "/Monsters/Encounters/", "/Events/Ancients/", "/Run/Acts/"))
            if not content:
                for t in top_level_types(text):
                    types[t].add(rel)
            for m in V._CITE.finditer(text):
                cites[V.normalise_citation(m.group(1))].add(rel)
    return types, paths, cites


def resolve_sim_path(mention, sim_paths):
    p = mention
    if p.startswith("sim/"):
        p = p[4:]
    if not p.startswith("Sts2Sim.Core/"):
        p = "Sts2Sim.Core/" + p
    p = "sim/" + p
    return p if p in sim_paths else None


def spec_pairs(repo, sim_paths, key_to_rel, sim_types, game_types):
    """(rel, sim file) -> Counter of docs, from markdown lines that cite the decompiled file (`file:line`) and, on
    the same line, name a sim symbol (`SimType` / `SimType.Member`, SimType declared in sim Core outside content
    Defs) or a sim Core path. A symbol whose name is also a game type is skipped unless it is the cited file's own
    type: `Hook.AfterX` next to a CombatManager citation names the game's Hook, not a sim file."""
    pairs = collections.defaultdict(collections.Counter)
    roots = [os.path.join(repo, "sim", "specs"), os.path.join(repo, "docs", "sim-port")]
    for root in roots:
        for dp, dns, fns in os.walk(root):
            dns.sort()
            for fn in sorted(fns):
                if not fn.endswith(".md"):
                    continue
                ab = os.path.join(dp, fn)
                doc = os.path.relpath(ab, repo).replace(os.sep, "/")
                for line in V.read_text(ab).split("\n"):
                    keys = set()
                    for m in V._CITE.finditer(line):
                        k = V.normalise_citation(m.group(1))
                        if k in key_to_rel:
                            keys.add(k)
                    if not keys:
                        continue
                    syms = {m.group(1) for m in _TICK_SYM.finditer(line) if m.group(1) in sim_types}
                    files = {s for s in (resolve_sim_path(m.group(1), sim_paths) for m in _SIM_PATH.finditer(line))
                             if s}
                    for k in keys:
                        stem = k.rsplit("/", 1)[-1][:-3]
                        hit = set(files)
                        for t in syms:
                            if t in game_types and t != stem:
                                continue
                            hit |= sim_types[t]
                        for s in hit:
                            pairs[(key_to_rel[k], s)][doc] += 1
    return pairs


def type_candidates(t):
    yield t, "type"
    yield t + "State", "type:%sState" % t
    if t.endswith("Model") and len(t) > 5:
        yield t[:-5] + "Def", "type:%sDef" % t[:-5]


def load_map(path):
    if os.path.exists(path):
        with open(path, encoding="utf-8") as fh:
            return json.load(fh)
    return {}


def _tree_label(tree):
    tail = "output/server-bootstrap/linux-decompiled"
    norm = tree.replace(os.sep, "/").rstrip("/")
    return tail if norm.endswith("/" + tail) else norm


def build(tree, repo=REPO, old=None, tree_version="0.111.0"):
    old = old or {}
    curated = old.get("curated", {})
    patterns = old.get("patterns", [])
    kf = kernel_files(tree)
    key_to_rel = {v["key"]: rel for rel, v in kf.items()}
    by_name = collections.defaultdict(list)
    for rel, v in kf.items():
        by_name[rel.rsplit("/", 1)[-1]].append(rel)
    types, sim_paths, sim_cites = sim_index(repo)
    game_types = {fn[:-3] for _, _, fns in os.walk(tree) for fn in fns if fn.endswith(".cs")}
    pairs = spec_pairs(repo, sim_paths, key_to_rel, types, game_types)
    spec_by_rel = collections.defaultdict(dict)
    for (rel, s), docs in pairs.items():
        if sum(docs.values()) >= SPEC_MIN_LINES:
            spec_by_rel[rel][s] = ["%s x%d" % (d.rsplit("/", 1)[-1], n) for d, n in docs.most_common()]
    files = collections.OrderedDict()
    for rel in sorted(kf):
        v = kf[rel]
        via = collections.OrderedDict()
        sims = collections.OrderedDict()
        stem = rel.rsplit("/", 1)[-1][:-3]
        names = [stem] + [t for t in v["types"] if t != stem and len(t) >= 8]
        for t in names:
            for cand, tag in type_candidates(t):
                for s in sorted(types.get(cand, ())):
                    sims.setdefault(s, []).append(tag if cand == t and t == stem else "%s(%s)" % (tag, t)
                                                  if cand == t else tag)
        cited = set(sim_cites.get(v["key"], ()))
        fname = rel.rsplit("/", 1)[-1]
        if len(by_name[fname]) == 1:
            cited |= sim_cites.get(fname, set())
        for s in sorted(cited):
            sims.setdefault(s, []).append("cite")
        for s, ev in sorted(spec_by_rel.get(rel, {}).items()):
            sims.setdefault(s, []).append("spec:" + ", ".join(ev[:3]) + (" +%d" % (len(ev) - 3) if len(ev) > 3 else ""))
        entry = collections.OrderedDict([("key", v["key"]), ("area", v["area"]), ("lines", v["lines"])])
        cur = curated.get(rel) or curated.get(v["key"])
        pat = None
        if cur is None:
            for p in patterns:
                if fnmatch.fnmatchcase(rel, p["glob"]) or fnmatch.fnmatchcase(v["key"], p["glob"]):
                    pat = p
                    break
        src = cur or pat
        if src is not None:
            if src.get("replace") or src.get("status") == "no_port":
                sims = collections.OrderedDict()
            for s in src.get("sim", []):
                sims.setdefault(s, []).append("curated" if cur else "pattern:" + src["glob"])
        status = (src or {}).get("status")
        if status != "no_port":
            status = "mapped" if sims else "unmapped"
        entry["status"] = status
        entry["sim"] = list(sims)
        if sims:
            entry["via"] = collections.OrderedDict((s, tags) for s, tags in sims.items())
        if src is not None and src.get("reason"):
            entry["reason"] = src["reason"]
        if pat is not None:
            entry["pattern"] = pat["glob"]
        files[rel] = entry
    missing = sorted({s for e in files.values() for s in e["sim"] if not os.path.exists(os.path.join(repo, s))})
    known = set(files) | {e["key"] for e in files.values()}
    missing += ["curated key not in the tree: %s" % k for k in curated if k not in known]
    used = {e.get("pattern") for e in files.values()}
    missing += ["pattern matches nothing: %s" % p["glob"] for p in patterns if p["glob"] not in used]
    stats = summarise(files)
    out = collections.OrderedDict([
        ("schema", SCHEMA),
        ("tree", _tree_label(tree)),
        ("tree_version", tree_version),
        ("generated_by", "python3 scripts/kernel_map.py (regenerates `stats` and `files`; `patterns` and `curated` "
                         "are hand-edited and kept)"),
        ("legend", collections.OrderedDict([
            ("keys", "decompile path relative to the tree root; `key` is the namespace form specs cite and "
                     "scripts/version_delta.py uses (curated keys and pattern globs may use either form)"),
            ("status", "mapped: sim files listed; no_port: deliberately not ported (see reason); unmapped: nothing "
                       "found, to review"),
            ("via", "per sim file: type / type:<renamed>(<declared type>) (sim declares the type), cite (a sim "
                    "comment cites the file), spec:<doc> xN (N spec lines cite the file and name a sim symbol of "
                    "this file), curated, pattern:<glob>"),
        ])),
        ("stats", stats),
        ("patterns", patterns),
        ("curated", curated),
        ("files", files),
    ])
    return out, missing


def summarise(files):
    by_status = collections.Counter(e["status"] for e in files.values())
    lines = collections.Counter()
    by_area = collections.defaultdict(collections.Counter)
    via_kinds = collections.Counter()
    for e in files.values():
        lines[e["status"]] += e["lines"]
        by_area[e["area"]][e["status"]] += 1
        kinds = set()
        for tags in e.get("via", {}).values():
            for t in tags:
                kinds.add(t.split(":", 1)[0].split("(", 1)[0])
        for k in kinds:
            via_kinds[k] += 1
    return collections.OrderedDict([
        ("kernel_files", len(files)),
        ("by_status", dict(sorted(by_status.items()))),
        ("lines_by_status", dict(sorted(lines.items()))),
        ("files_with_source", dict(sorted(via_kinds.items()))),
        ("by_area", {a: dict(sorted(c.items())) for a, c in sorted(by_area.items())}),
    ])


def dumps(m):
    """Indented JSON, but one line per entry of `patterns`, `curated` and `files`: a map change is a one-line diff."""
    one = lambda v: json.dumps(v, ensure_ascii=False)  # noqa: E731
    parts = []
    for k, v in m.items():
        if k == "patterns":
            body = ",\n".join("  " + one(p) for p in v)
            parts.append('%s: [\n%s\n ]' % (one(k), body) if v else '%s: []' % one(k))
        elif k in ("curated", "files"):
            body = ",\n".join("  %s: %s" % (one(rel), one(e)) for rel, e in v.items())
            parts.append('%s: {\n%s\n }' % (one(k), body) if v else '%s: {}' % one(k))
        else:
            parts.append("%s: %s" % (one(k), json.dumps(v, indent=1, ensure_ascii=False).replace("\n", "\n ")))
    return "{\n " + ",\n ".join(parts) + "\n}\n"


def _show(path):
    rel = os.path.relpath(path, REPO)
    return path if rel.startswith("..") else rel


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--tree", default=V.DEFAULT_CITE_TREE, help="rule-truth decompile root")
    ap.add_argument("--tree-version", default="0.111.0", help="game version of --tree (default 0.111.0)")
    ap.add_argument("--out", default=MAP_PATH)
    ap.add_argument("--check", action="store_true",
                    help="write nothing; exit 1 when the committed map is stale (sim files, specs or the tree "
                         "moved), a curated key or pattern is stale, or a kernel file of >= %d lines is unmapped"
                         % CHECK_MIN_LINES)
    ap.add_argument("--unmapped", action="store_true", help="print the unmapped files with line counts")
    ap.add_argument("--repo", default=REPO, help=argparse.SUPPRESS)
    a = ap.parse_args(argv)
    if not os.path.isdir(a.tree):
        ap.error("decompile tree not found: %s" % a.tree)
    old = load_map(a.out)
    m, stale = build(os.path.abspath(a.tree), os.path.abspath(a.repo), old, a.tree_version)
    text = dumps(m)
    s = m["stats"]
    print("kernel-map: %d kernel files; %s; lines %s" % (
        s["kernel_files"], ", ".join("%s %d" % kv for kv in s["by_status"].items()),
        ", ".join("%s %d" % kv for kv in s["lines_by_status"].items())))
    print("sources (files): %s" % ", ".join("%s %d" % kv for kv in s["files_with_source"].items()))
    big = sorted(((e["lines"], rel) for rel, e in m["files"].items() if e["status"] == "unmapped"), reverse=True)
    if a.unmapped or a.check:
        for n, rel in big:
            print("  unmapped %5d  %s" % (n, rel))
    for s_ in stale:
        print("stale: %s" % s_)
    if a.check:
        old_text = ""
        if os.path.exists(a.out):
            with open(a.out, encoding="utf-8") as fh:
                old_text = fh.read()
        bad = [(n, rel) for n, rel in big if n >= CHECK_MIN_LINES]
        if old_text != text:
            print("check: %s is out of date; run python3 scripts/kernel_map.py" % _show(a.out))
        print("check: %d unmapped files of >= %d lines" % (len(bad), CHECK_MIN_LINES))
        return 1 if bad or stale or old_text != text else 0
    with open(a.out, "w", encoding="utf-8") as fh:
        fh.write(text)
    print("wrote %s" % _show(a.out))
    return 0


if __name__ == "__main__":
    sys.exit(main())
