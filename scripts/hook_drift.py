#!/usr/bin/env python3
"""Check the recorder's hook targets (docs/hook-map.md, and the mod source) against a decompile tree.

    python3 scripts/hook_drift.py [--tree <decompile root>] [--hook-map docs/hook-map.md] [--mod-src mod/src]
                                  [--json out.json] [--md out.md] [--no-mod]

What it reads from the hook map (the newest drift addendum wins, as the map itself says):
  - the "Patched members" table: hook id `patch:Type.Member`, file `<ns>/<File>.cs`, line, and the signature
    column (`ret Name(params)`, `(params)`, `ret (params)`; rows without a parenthesised signature are checked for
    presence only; `Type.Prefix*` (N) rows check that N methods share the prefix and that the listed parameter
    names exist);
  - the "Events:" paragraph (events and static members, `Type.Member` / bare `Member` continuing the last type);
  - the "FieldRefs and reflection:" paragraph (fields / properties, with their type when given).
From mod/src (when present): every `patch:` / `event:` / `fieldref:` hook id string, `PatchSyncLocal(...)`
targets and `AccessTools.FieldRefAccess<T, F>("name")`; each is checked in the tree too, and a mod target the hook
map does not document is reported as `undocumented`.

Per target the tree check resolves the file (doc path, else `<Type>.cs` anywhere), the type (nested included), the
member by name and kind, then compares the signature: return type (async / namespace qualifiers ignored),
parameter types and, where the doc names them, parameter names (Harmony binds by name). Statuses:
  ok | line_moved (info only: the doc line differs) | missing_file | missing_type | missing_member |
  kind_changed | signature_changed | type_changed | count_changed | undocumented
Exit status: 0 no drift, 1 drift (any status other than ok / line_moved), 2 usage error.
"""

import argparse
import collections
import json
import os
import re
import sys
import time

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import version_delta as vd  # noqa: E402

REPO = vd.REPO
SCHEMA = "sts2sim.hook-drift/v1"
DEFAULT_TREE = vd.DEFAULT_CITE_TREE  # STS2_DECOMPILE_ROOT or the main checkout's rule-truth Linux tree
OK = ("ok", "line_moved")
MODIFIERS = {"public", "private", "protected", "internal", "static", "virtual", "override", "abstract", "sealed",
             "async", "new", "extern", "unsafe", "readonly", "partial", "required", "volatile", "event", "const"}
_QUAL = re.compile(r"\b(?:global::)?(?:[A-Za-z_]\w*\.)+(?=[A-Za-z_])")


# ---------------------------------------------------------------------------------------------------------------
# Hook map parsing

def _ticks(s):
    return re.findall(r"`([^`]+)`", s)


def norm_type(t):
    """`System.Threading.Tasks.Task<bool>` -> `Task<bool>`; whitespace and `async` dropped."""
    t = re.sub(r"\basync\b", "", t or "")
    t = _QUAL.sub("", t)
    return re.sub(r"\s+", "", t)


def split_params(s):
    out, depth, cur = [], 0, []
    for ch in s:
        if ch in "<([{":
            depth += 1
        elif ch in ">)]}":
            depth -= 1
        if ch == "," and depth == 0:
            out.append("".join(cur).strip())
            cur = []
        else:
            cur.append(ch)
    if "".join(cur).strip():
        out.append("".join(cur).strip())
    return out


def parse_param(p):
    """`bool ignoreCost = false` -> ('bool', 'ignoreCost'); `MerchantInventory?` -> ('MerchantInventory?', None)."""
    p = re.sub(r"\[[^\]]*\]", "", p).split("=", 1)[0].strip()
    words = [w for w in re.findall(r"[^\s]+", p) if w not in ("this", "ref", "out", "in", "params", "scoped")]
    if not words:
        return None
    # rejoin generic types split by spaces (`Action<A, B> x`)
    joined = " ".join(words)
    m = re.match(r"^(.*?[>\]?\w])\s+([A-Za-z_]\w*)$", joined)
    if m and not joined.endswith(">") and not joined.endswith("?") and not joined.endswith("]"):
        return (norm_type(m.group(1)), m.group(2))
    return (norm_type(joined), None)


def parse_signature(sig, member=None):
    """Hook-map signature text -> {'ret': str|None, 'name': str|None, 'params': [(type, name|None)]} or None."""
    if not sig or "(" not in sig or ")" not in sig:
        return None
    k = sig.index("(")
    close = sig.rfind(")")
    head = sig[:k].strip()
    params = [parse_param(p) for p in split_params(sig[k + 1:close])]
    params = [p for p in params if p]
    words = head.split()
    name = None
    if words and re.match(r"^[A-Za-z_]\w*$", words[-1]) and (len(words) > 1 or words[-1] == member):
        if not (len(words) == 1 and re.match(r"^(void|bool|int|Task|string|long|uint)$", words[-1])):
            name = words[-1]
            words = words[:-1]
    ret = " ".join(w for w in words if w not in MODIFIERS) or None
    return {"ret": norm_type(ret) if ret else None, "name": name, "params": params}


def _section(lines, title_re, start=0):
    """Index of the last heading line matching title_re at or after start (the newest addendum wins)."""
    hit = None
    for i in range(start, len(lines)):
        if lines[i].startswith("#") and re.search(title_re, lines[i]):
            hit = i
    return hit


def parse_hook_map(path):
    """-> list of target dicts (source 'hook-map')."""
    with open(path, encoding="utf-8") as fh:
        text = fh.read()
    lines = text.split("\n")
    targets = []
    i = _section(lines, r"Patched members")
    if i is None:
        raise ValueError("%s: no 'Patched members' table" % path)
    j = i + 1
    while j < len(lines) and not lines[j].startswith("|"):
        j += 1
    while j < len(lines) and lines[j].startswith("|"):
        cells = [c.strip() for c in lines[j].strip().strip("|").split("|")]
        j += 1
        if len(cells) < 5 or set(cells[0]) <= set("-: ") or cells[0].lower().startswith("hook"):
            continue
        hid = (_ticks(cells[0]) or [""])[0]
        m = re.match(r"^patch:([A-Za-z_]\w*)\.([A-Za-z_]\w*\*?)", hid)
        if not m:
            continue
        typ, mem = m.group(1), m.group(2)
        file_key = (_ticks(cells[1]) or [None])[0]
        doc_lines = [int(x) for x in re.findall(r"\d+", cells[3])]
        sig_text = next((t for t in _ticks(cells[4]) if "(" in t and ")" in t), None)
        t = {"id": hid.split(" ")[0], "source": "hook-map", "kind": "method", "type": typ, "member": mem,
             "file": file_key, "line": doc_lines[0] if len(doc_lines) == 1 else None, "lines": doc_lines,
             "signature": parse_signature(sig_text, mem) if sig_text else None, "signature_text": sig_text,
             "doc_line": j}
        if mem.endswith("*"):
            cnt = re.search(r"\((\d+)\)", cells[0])
            t["kind"] = "method_prefix"
            t["count"] = int(cnt.group(1)) if cnt else None
            t["param_names"] = [x for x in _ticks(cells[4]) if re.match(r"^[a-z]\w*$", x)]
        targets.append(t)
    # Events / FieldRefs paragraphs after the table
    for label, kind in (("Events:", "event"), ("FieldRefs and reflection:", "field")):
        k = next((n for n in range(i, len(lines)) if lines[n].startswith(label)), None)
        if k is None:
            continue
        para = []
        n = k
        while n < len(lines) and lines[n].strip():
            para.append(lines[n])
            n += 1
        targets.extend(parse_member_paragraph(" ".join(para)[len(label):], kind, k + 1))
    return targets


def parse_member_paragraph(text, kind, doc_line):
    """Backticked `Type.Member` / bare `Member` at paren depth 0 are targets; a parenthesised group after them gives
    the file (`<ns>/<File>.cs:<line>`) and, for fields, the type (first comma field: `long`, `MapLocation`)."""
    out = []
    depth = 0
    i = 0
    cur_type = None
    pending = []  # targets awaiting their annotation group

    def annotate(group):
        files = re.findall(r"([\w.]+/[\w.]+\.cs):(\d+)", group)
        first = group.split(",", 1)[0].strip().strip("`").strip()
        ftype = None
        if kind == "field" and first and not first.startswith(":") and not first.startswith("v107") and \
                re.match(r"^[A-Za-z_][\w.<>?\[\], ]*$", first) and "/" not in first and " " not in first.strip():
            ftype = first
        for t in pending:
            if files:
                t["file"] = files[0][0]
            if ftype and not t.get("field_type"):
                t["field_type"] = ftype
        pending.clear()

    group_start = None
    while i < len(text):
        ch = text[i]
        if ch == "(":
            if depth == 0:
                group_start = i + 1
            depth += 1
        elif ch == ")":
            depth -= 1
            if depth == 0 and group_start is not None:
                annotate(text[group_start:i])
                group_start = None
        elif ch == "`" and depth == 0:
            close = text.find("`", i + 1)
            if close == -1:
                break
            tok = text[i + 1:close]
            i = close
            tok = re.sub(r"^(?:fieldref|event|patch):", "", tok)
            m = re.match(r"^([A-Z]\w*)\.([A-Za-z_]\w*)$", tok)
            if m:
                cur_type = m.group(1)
                t = {"type": cur_type, "member": m.group(2)}
            elif re.match(r"^[A-Za-z_]\w*$", tok) and cur_type:
                t = {"type": cur_type, "member": tok}
            else:
                t = None
            if t:
                t.update({"id": "%s:%s.%s" % ("event" if kind == "event" else "fieldref", t["type"], t["member"]),
                          "source": "hook-map", "kind": kind, "file": None, "line": None, "signature": None,
                          "doc_line": doc_line})
                out.append(t)
                pending.append(t)
        elif ch in ",;" and depth == 0:
            pass
        i += 1
    # `Launch` (invoked in ...) style references inside groups are not targets; drop duplicates
    seen, uniq = set(), []
    for t in out:
        key = (t["type"], t["member"])
        if key not in seen:
            seen.add(key)
            uniq.append(t)
    return uniq


# ---------------------------------------------------------------------------------------------------------------
# Mod source

_MOD_ID = re.compile(r'"(patch|event|fieldref):([A-Za-z_]\w*)\.([A-Za-z_]\w*)')
_MOD_SYNC = re.compile(r"PatchSyncLocal\(\s*harmony\s*,\s*nameof\(\s*(\w+)\.(\w+)\s*\)")
_MOD_FIELDREF = re.compile(r"FieldRefAccess<\s*([\w.]+)\s*,\s*([\w.<>?\[\]]+)\s*>\s*\(\s*\"(\w+)\"")


def parse_mod(mod_src):
    out = {}
    for dp, dns, fns in os.walk(mod_src):
        dns[:] = sorted(d for d in dns if d not in ("bin", "obj"))
        for fn in sorted(fns):
            if not fn.endswith(".cs"):
                continue
            p = os.path.join(dp, fn)
            rel = os.path.relpath(p, REPO) if p.startswith(REPO) else p
            text = vd.read_text(p)
            for m in _MOD_ID.finditer(text):
                kind = {"patch": "method", "event": "event", "fieldref": "field"}[m.group(1)]
                key = (m.group(2), m.group(3))
                out.setdefault(key, {"id": "%s:%s.%s" % (m.group(1), key[0], key[1]), "source": "mod",
                                     "kind": kind, "type": key[0], "member": key[1], "file": None,
                                     "signature": None, "where": "%s:%d" % (rel, text.count("\n", 0, m.start()) + 1)})
            for m in _MOD_SYNC.finditer(text):
                key = (m.group(1), m.group(2))
                out.setdefault(key, {"id": "patch:%s.%s" % key, "source": "mod", "kind": "method", "type": key[0],
                                     "member": key[1], "file": None, "signature": None,
                                     "where": "%s:%d" % (rel, text.count("\n", 0, m.start()) + 1)})
            for m in _MOD_FIELDREF.finditer(text):
                key = (m.group(1).split(".")[-1], m.group(3))
                t = out.setdefault(key, {"id": "fieldref:%s.%s" % key, "source": "mod", "kind": "field",
                                         "type": key[0], "member": key[1], "file": None, "signature": None,
                                         "where": "%s:%d" % (rel, text.count("\n", 0, m.start()) + 1)})
                t["kind"] = "field"
                t["field_type"] = m.group(2)
    return list(out.values())


# ---------------------------------------------------------------------------------------------------------------
# Tree checks

class Tree:
    def __init__(self, root):
        self.root = root
        self.index = vd.index_tree(root)
        self.by_name = collections.defaultdict(list)
        for key, path in self.index.items():
            self.by_name[os.path.basename(key)[:-3]].append(key)
        self._parsed = {}
        self.cs = vd._cs_source()
        if self.cs is None:
            raise RuntimeError("sim/tools/inventory/cs_source.py not importable")

    def parse(self, key):
        if key not in self._parsed:
            try:
                self._parsed[key] = self.cs.CsFile(self.index[key], os.path.basename(key))
            except Exception:
                self._parsed[key] = None
        return self._parsed[key]

    def find_type(self, typ, file_key=None):
        """-> (key, CsFile, TypeDecl) or (key|None, None, None)."""
        cands = []
        if file_key:
            k = vd.normalise_citation(file_key) if hasattr(vd, "normalise_citation") else file_key
            for kk in (file_key, k):
                if kk in self.index:
                    cands.append(kk)
            if not cands:
                base = os.path.basename(file_key)
                cands.extend(x for x in self.index if x.endswith("/" + base) or x == base)
        cands.extend(x for x in self.by_name.get(typ, []) if x not in cands)
        first = cands[0] if cands else None
        for key in cands:
            cf = self.parse(key)
            if cf is None:
                continue
            for top in cf.types:
                for td in self.cs.iter_types(top):
                    if td.name == typ:
                        return (key, cf, td)
        return (first, None, None)


def member_ret(m):
    head = " ".join(m.head.split())
    k = head.find("(")
    before = head[:k] if k != -1 else head
    before = re.sub(r"\[[^\]]*\]", "", before)
    words = before.split()
    if words and words[-1] == m.name:
        words = words[:-1]
    words = [w for w in words if w not in MODIFIERS]
    return norm_type(" ".join(words))


def member_params(cs, m):
    split = getattr(cs, "split_args", None)
    parts = split(m.params) if split else split_params(m.params or "")
    return [p for p in (parse_param(x) for x in parts) if p]


def sig_text(cs, m):
    return "%s %s(%s)" % (member_ret(m), m.name, ", ".join("%s %s" % (t, n or "") for t, n in member_params(cs, m)))


def sig_matches(want, cs, m):
    if want.get("ret") and member_ret(m) != want["ret"]:
        return False
    have = member_params(cs, m)
    if len(have) != len(want["params"]):
        return False
    for (wt, wn), (ht, hn) in zip(want["params"], have):
        if wt != ht:
            return False
        if wn and hn and wn != hn:
            return False
    return True


def field_type_of(m):
    head = re.sub(r"\[[^\]]*\]", "", " ".join(m.head.split()))
    words = [w for w in head.split() if w not in MODIFIERS]
    if words and words[-1] == m.name:
        words = words[:-1]
    return norm_type(" ".join(words))


def check_target(tree, t):
    r = dict(t)
    r.pop("doc_line", None)
    key, cf, td = tree.find_type(t["type"], t.get("file"))
    r["found_file"] = key
    if key is None:
        r["status"] = "missing_file"
        r["detail"] = "no %s.cs in the tree" % t["type"]
        return r
    if td is None:
        r["status"] = "missing_type"
        r["detail"] = "type %s not declared in %s" % (t["type"], key)
        return r
    cs = tree.cs
    if t["kind"] == "method_prefix":
        prefix = t["member"].rstrip("*")
        ms = [m for m in td.members if m.kind == "method" and m.name and m.name.startswith(prefix)]
        names = {n for m in ms for _, n in member_params(cs, m) if n}
        r["found"] = sorted({m.name for m in ms})
        missing = [n for n in t.get("param_names", []) if n not in names]
        # the doc's (N) counts the patched methods and lists their lines; the class may hold more SyncLocal*
        at_lines = {m.line for m in ms} & set(t.get("lines") or [])
        if t.get("count") is not None and len(r["found"]) < t["count"]:
            r["status"] = "count_changed"
            r["detail"] = "%d methods %s* (doc patches %d)" % (len(r["found"]), prefix, t["count"])
        elif missing:
            r["status"] = "signature_changed"
            r["detail"] = "parameter names not found: %s" % ", ".join(missing)
        elif t.get("lines") and len(at_lines) < len(t["lines"]):
            r["status"] = "line_moved"
            r["detail"] = "%d of %d doc lines hold a %s* method" % (len(at_lines), len(t["lines"]), prefix)
        else:
            r["status"] = "ok"
            r["detail"] = "%d methods %s*, parameter names present" % (len(r["found"]), prefix)
        return r
    ms = [m for m in td.members if m.name == t["member"]]
    if not ms:
        r["status"] = "missing_member"
        r["detail"] = "%s.%s not declared in %s" % (t["type"], t["member"], key)
        return r
    if t["kind"] == "method":
        meth = [m for m in ms if m.kind == "method"]
        if not meth:
            r["status"] = "kind_changed"
            r["detail"] = "%s is a %s now" % (t["member"], ms[0].kind)
            return r
        r["found_line"] = meth[0].line
        r["found_signature"] = [sig_text(cs, m) for m in meth]
        want = t.get("signature")
        if want:
            good = [m for m in meth if sig_matches(want, cs, m)]
            if not good:
                r["status"] = "signature_changed"
                r["detail"] = "doc `%s`; tree %s" % (t.get("signature_text"), "; ".join(r["found_signature"]))
                return r
            r["found_line"] = good[0].line
        r["status"] = "ok"
    elif t["kind"] == "event":
        ev = [m for m in ms if m.kind in ("event", "property", "field")]
        if not ev:
            r["status"] = "kind_changed"
            r["detail"] = "%s is a %s now" % (t["member"], ms[0].kind)
            return r
        r["found_line"] = ev[0].line
        r["found_kind"] = ev[0].kind
        r["status"] = "ok"
    else:  # field / property (FieldRef, reflection)
        fl = [m for m in ms if m.kind in ("field", "property", "const")]
        if not fl:
            r["status"] = "kind_changed"
            r["detail"] = "%s is a %s now" % (t["member"], ms[0].kind)
            return r
        r["found_line"] = fl[0].line
        r["found_kind"] = fl[0].kind
        if t.get("field_type"):
            have = field_type_of(fl[0])
            if norm_type(t["field_type"]) != have:
                r["status"] = "type_changed"
                r["detail"] = "doc type `%s`, tree `%s`" % (t["field_type"], have)
                return r
        r["status"] = "ok"
    if t.get("line") and r.get("found_line") and t["line"] != r["found_line"]:
        r["status"] = "line_moved"
        r["detail"] = "doc line %d, tree line %d" % (t["line"], r["found_line"])
    return r


def run(tree_root, hook_map, mod_src=None):
    t0 = time.time()
    tree = Tree(tree_root)
    doc = parse_hook_map(hook_map)
    results = [check_target(tree, t) for t in doc]
    documented = {(t["type"], t["member"]) for t in doc}
    documented_prefix = [(t["type"], t["member"].rstrip("*")) for t in doc if t["kind"] == "method_prefix"]
    mod = parse_mod(mod_src) if mod_src and os.path.isdir(mod_src) else []
    for t in mod:
        r = check_target(tree, t)
        is_doc = (t["type"], t["member"]) in documented or any(
            t["type"] == ty and t["member"].startswith(p) for ty, p in documented_prefix)
        if r["status"] in OK and not is_doc:
            r["status"] = "undocumented"
            r["detail"] = "the mod targets %s.%s (%s); docs/hook-map.md does not list it" % (
                t["type"], t["member"], t.get("where"))
        results.append(r)
    counts = collections.Counter(r["status"] for r in results)
    drift = [r for r in results if r["status"] not in OK]
    return {"schema": SCHEMA, "tree": tree_root, "hook_map": hook_map, "mod_src": mod_src,
            "targets": {"hook_map": len(doc), "mod": len(mod),
                        "by_kind": dict(collections.Counter(t["kind"] for t in doc))},
            "counts": dict(counts), "drift": len(drift), "results": results,
            "elapsed_s": round(time.time() - t0, 2)}


def render_md(rep):
    L = ["# Hook drift: %s" % rep["tree"], "",
         "Hook map `%s` (%d targets: %s), mod `%s` (%d targets). Drift: **%d**. Status counts: %s." % (
             rep["hook_map"], rep["targets"]["hook_map"],
             ", ".join("%s %d" % kv for kv in sorted(rep["targets"]["by_kind"].items())), rep["mod_src"],
             rep["targets"]["mod"], rep["drift"], ", ".join("%s %d" % kv for kv in sorted(rep["counts"].items()))),
         "", "| status | source | target | file | detail |", "| --- | --- | --- | --- | --- |"]
    order = {"ok": 9, "line_moved": 8}
    for r in sorted(rep["results"], key=lambda r: (order.get(r["status"], 0), r["source"], r["id"])):
        if r["status"] == "ok":
            continue
        L.append("| %s | %s | `%s` | `%s` | %s |" % (r["status"], r["source"], r["id"], r.get("found_file") or
                                                     r.get("file") or "-", (r.get("detail") or "").replace("|", "\\|")))
    ok = [r for r in rep["results"] if r["status"] == "ok"]
    L += ["", "%d targets ok." % len(ok)]
    return "\n".join(L) + "\n"


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    ap.add_argument("--tree", default=DEFAULT_TREE, help="decompile root (default: the rule-truth Linux tree)")
    ap.add_argument("--hook-map", default=os.path.join(REPO, "docs", "hook-map.md"))
    ap.add_argument("--mod-src", default=os.path.join(REPO, "mod", "src"))
    ap.add_argument("--no-mod", action="store_true", help="check the hook map only")
    ap.add_argument("--json", help="write the full report here")
    ap.add_argument("--md", help="write a markdown report here")
    ap.add_argument("-q", "--quiet", action="store_true")
    a = ap.parse_args(argv)
    if not os.path.isdir(a.tree):
        print("hook_drift: tree not found: %s" % a.tree, file=sys.stderr)
        return 2
    rep = run(a.tree, a.hook_map, None if a.no_mod else a.mod_src)
    for path, body in ((a.json, None), (a.md, None)):
        if path:
            os.makedirs(os.path.dirname(os.path.abspath(path)), exist_ok=True)
    if a.json:
        with open(a.json, "w", encoding="utf-8") as fh:
            json.dump(rep, fh, indent=1)
    if a.md:
        with open(a.md, "w", encoding="utf-8") as fh:
            fh.write(render_md(rep))
    c = rep["counts"]
    print("hook-drift: %d hook-map targets (%s) + %d mod targets against %s: drift %d; %s; %.1f s" % (
        rep["targets"]["hook_map"], ", ".join("%s %d" % kv for kv in sorted(rep["targets"]["by_kind"].items())),
        rep["targets"]["mod"], a.tree, rep["drift"], ", ".join("%s %d" % kv for kv in sorted(c.items())),
        rep["elapsed_s"]))
    if not a.quiet:
        for r in rep["results"]:
            if r["status"] not in OK:
                print("  DRIFT %-17s %-6s %s: %s" % (r["status"], r["source"], r["id"], r.get("detail", "")))
    return 1 if rep["drift"] else 0


if __name__ == "__main__":
    sys.exit(main())
