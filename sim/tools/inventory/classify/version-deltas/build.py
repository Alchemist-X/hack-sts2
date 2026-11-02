"""Reconciled v0.107.1 -> v0.111.0 version-delta list (w0-critique section 5.4, C20, G20).

Union of
  (a) the classifiers' version_delta_v107 flags: every entry of the normalised *.classified.json whose status
      is not `same`;
  (b) the diff gate's mechanical diff in corpus-inventory.json version_diff_v0_107_1_vs_v0_111_0:
      content_files (rule-diverging estimate), version_exclusive_content (only in one tree) and engine_files;
  (c) triage.py MANUAL_VD: content files with a unique edit that scripts/version_delta.py found and (a) and (b)
      missed (classifier `same`, not gate-flagged), each read by hand;
then one triage per entry: changed | refactor_only | unknown, with evidence lines in both trees.

Triage order: a manual or IL record in triage.py; pools by a mechanical membership diff of their generator
lists; files present in one tree only are `changed` (added / removed content); otherwise the classifier status
(rule-change -> changed, cosmetic -> refactor_only); anything else -> unknown. Engine files are outside every
classifier and stay `unknown` with their mechanical evidence.

Input: --out (normalised classified files, corpus-inventory.json), --inventory (raw manifests, pools.json),
--root (v0.111.0 decompile), --v107-root. Output: <out>/version-deltas.json.
"""

import collections
import json
import os
import re
import sys
from collections import OrderedDict

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
sys.path.insert(0, os.path.join(HERE, ".."))
sys.path.insert(0, os.path.join(HERE, "..", "normalize"))
import common  # noqa: E402
import deps as D  # noqa: E402
import evidence as EV  # noqa: E402
from triage import MANUAL, MANUAL_VD, STATUS_TRIAGE, V107, V111  # noqa: E402

SCHEMA = "sts2sim.inventory.version-deltas/v1"
GENERATED_BY = common.generator("version-deltas", "build.py", "triage.py", "evidence.py")
KIND_NS = OrderedDict([
    ("cards", "Cards"), ("powers", "Powers"), ("relics", "Relics"), ("potions", "Potions"), ("monsters", "Monsters"),
    ("encounters", "Encounters"), ("events", "Events"), ("enchantments", "Enchantments"), ("afflictions", "Afflictions"),
    ("orbs", "Orbs"), ("modifiers", "Modifiers"), ("acts", "Acts"), ("characters", "Characters"),
    ("card_pools", "CardPools"), ("relic_pools", "RelicPools"), ("potion_pools", "PotionPools"),
])
POOL_KINDS = ("card_pools", "relic_pools", "potion_pools")
MEMBER_RE = re.compile(r'(?:ModelDb\.(?:Card|Relic|Potion)|MockCard)<([A-Z]\w+)>(?:\((CardRarity\.\w+)\))?')
GENERATOR_RE = re.compile(r'\bGenerateAll(?:Cards|Relics|Potions)\s*\(')
MP_ONLY_RE = re.compile(r'MultiplayerConstraint\s*=>\s*CardMultiplayerConstraint\.MultiplayerOnly')
MP_FILTER_CITE = "Models/CardPoolModel.cs:77, Factories/CardFactory.cs:31"  # single-player filters of MultiplayerOnly cards
TRIAGE_LEGEND = OrderedDict([
    ("changed", "a single-player run can observe the difference (values, effects, prompts, pool members, odds), or a game "
                "model (not a test mock) exists in one tree only; models no single-player run can meet keep their classifier's "
                "verdict and carry single_player_reachable false"),
    ("refactor_only", "no single-player run can observe the difference: decompiler style, renames, UI/VFX, history logging, "
                      "branches that run only with other players, pool members filtered out in single player, test mocks, "
                      "arrays recovered from IL that equal v0.107.1"),
    ("unknown", "flagged but not adjudicated (engine files, unreviewed classifier rows)"),
])
DELTA_TYPES = OrderedDict([
    ("modified", "the file exists in both trees"),
    ("added", "only in v0.111.0"),
    ("removed", "only in v0.107.1"),
])
BASIS = OrderedDict([
    ("classifier", "the classifier's version_delta_v107 status (its own mechanical diff, then manual reading)"),
    ("manual", "read by hand in both trees (triage.py)"),
    ("il", "v0.111.0 LdMemberToken array read from sts2.dll (docs/sim-port/w0-critique.md section 7, CONTRACTS D9)"),
    ("membership", "generator-list membership diff of the pool file in both trees"),
    ("presence", "the model file exists in one tree only"),
    ("unadjudicated", "no classifier covers it and no manual record exists"),
])


def read_lines(path):
    with open(path, encoding="utf-8-sig") as fh:
        return fh.read().split("\n")


def v111_abs(rel):
    d, f = rel.split("/", 1)
    return os.path.join(common.DECOMPILE_ROOT, "MegaCrit.Sts2.Core." + d, f)


def v107_abs(rel):
    return os.path.join(common.V107_ROOT, rel)


def to_v107(rel111):
    d, f = rel111.split("/", 1)
    return d.replace(".", "/") + "/" + f


class V107Index(object):
    def __init__(self):
        self.by_name = collections.defaultdict(list)
        for dp, dn, fn in os.walk(common.V107_ROOT):
            dn.sort()
            for f in sorted(fn):
                if f.endswith(".cs"):
                    self.by_name[f].append(os.path.relpath(os.path.join(dp, f), common.V107_ROOT).replace(os.sep, "/"))

    def locate(self, rel111, hint=None):
        for cand in ([hint] if hint else []) + [to_v107(rel111)]:
            if cand and os.path.exists(v107_abs(cand)):
                return cand
        hits = self.by_name.get(rel111.rsplit("/", 1)[1], [])
        return hits[0] if len(hits) == 1 else None


def cite(rel, lines):
    return ["%s:%d" % (rel, n) for n in lines]


def check_citation(c, tree):
    if c.startswith("IL:"):
        return
    rel, ln = c.rsplit(":", 1)
    path = v111_abs(rel) if tree == V111 else v107_abs(rel)
    lines = read_lines(path)
    n = int(ln)
    if not (1 <= n <= len(lines)) or not lines[n - 1].strip():
        raise SystemExit("triage.py citation does not resolve: %s %s" % (tree, c))


def pool_members(lines):
    out = OrderedDict()
    for i, raw in enumerate(lines, 1):
        for m in MEMBER_RE.finditer(raw):
            name = m.group(1) + ("(%s)" % m.group(2).split(".")[1] if m.group(2) else "")
            out.setdefault(name, i)
    return out


def card_mp_only(tree, cls):
    """True when the card declares CardMultiplayerConstraint.MultiplayerOnly in that tree."""
    path = v111_abs("Models.Cards/%s.cs" % cls) if tree == V111 else v107_abs("Models/Cards/%s.cs" % cls)
    if not os.path.exists(path):
        return False
    return bool(MP_ONLY_RE.search(open(path, encoding="utf-8-sig").read()))


def sp_reachable(kind, cls, c, rel111, rel107, pool_row):
    """(single_player_reachable, why not): test mocks, MultiplayerOnly cards, powers only those cards apply,
    relics whose IsAllowed needs more than one player."""
    e = c["entry"] if c else {}
    first_dir = (rel111 or rel107 or "").split("/")[0]
    if e.get("is_mock") or e.get("scope") == "test_only" or (pool_row or {}).get("is_mock") or ".Mock" in first_dir \
            or "/Mock" in (rel107 or ""):
        return False, "test mock"
    if kind == "cards" and card_mp_only(V111 if rel111 else V107, cls):
        return False, "MultiplayerOnly card: dropped from single-player pools (%s)" % MP_FILTER_CITE
    if kind == "powers" and e.get("multiplayer_only") is True:
        return False, "applied only by MultiplayerOnly content (powers.classified.json multiplayer_only)"
    if kind == "relics" and rel111:
        text = open(v111_abs(rel111), encoding="utf-8-sig").read()
        m = re.search(r"bool IsAllowed\(IRunState runState\)\s*\{\s*return [^;]*Players\)\.Count > 1;", text)
        if m:
            return False, "IsAllowed needs more than one player (%s:%d)" % (rel111, text[:m.start()].count("\n") + 3)
    return True, None


def generator_line(lines, cls):
    for i, raw in enumerate(lines, 1):
        if GENERATOR_RE.search(raw):
            return i
    return EV.class_line(lines, cls)


def model_refs_v107(cls, own_rel, index):
    pat = re.compile(r'<%s>|typeof\(%s\)' % (re.escape(cls), re.escape(cls)))
    out = []
    for name in sorted(index.by_name):
        for rel in index.by_name[name]:
            if rel == own_rel:
                continue
            try:
                text = open(v107_abs(rel), encoding="utf-8-sig").read()
            except OSError:
                continue
            if pat.search(text):
                out.append(rel)
    return sorted(out)


def gate_record(x):
    if x is None:
        return OrderedDict([("flagged", False)])
    rec = OrderedDict([("flagged", True)])
    for k in ("values", "enums", "calls", "decompile_gap"):
        if k in x:
            rec[k] = x[k]
    return rec


def gate_hints(h, x):
    if not x:
        return
    for side_key, side in (("v0.107.1", V107), ("v0.111.0", V111)):
        for v in (x.get("values") or {}).get(side_key, []):
            h.add(side, v.split("=>")[-1])
        for v in (x.get("enums") or {}).get(side_key, []):
            h.add(side, v.split(".")[-1])
            h.add(EV.BOTH, v.split(".")[0])
    for side_key, side in (("v0.107.1_only", V107), ("v0.111.0_only", V111)):
        for v in (x.get("calls") or {}).get(side_key, []):
            h.add(side, v)


def classifier_hints(h, vd):
    if not vd:
        return
    h.add_note(vd.get("note") or "")
    for k in ("fingerprint_diff", "identifier_delta", "dependency_deltas"):
        if vd.get(k):
            h.add_struct(vd[k])
    if vd.get("fingerprint_only_v107"):
        h.add_struct(vd["fingerprint_only_v107"], V107)
    if vd.get("fingerprint_only_v111"):
        h.add_struct(vd["fingerprint_only_v111"], V111)


def main():
    out_dir = common.OUT
    raw = dict((k, common.load_json(common.inventory_path(k + ".json"))) for k in D.CONTENT_KINDS)
    raw_by_file = {}
    raw_by_class = {}
    for kind in D.CONTENT_KINDS:
        for e in raw[kind]["entries"]:
            raw_by_file[e["file"]] = (kind, e)
            raw_by_class.setdefault((kind, e["class"]), e)
    pools = common.load_json(common.inventory_path("pools.json"))
    pool_rows = {}
    for kind in POOL_KINDS:
        for cls, row in pools[kind].items():
            pool_rows[(kind, cls)] = row

    classified = OrderedDict()
    for f in D.KIND_OF_FILE:
        doc = common.load_json(os.path.join(out_dir, f + ".classified.json"))
        kind = D.KIND_OF_FILE[f]
        for e in doc["entries"]:
            rawe = raw_by_class.get((kind, e["class"]))
            key = (kind, e["class"])
            if key in classified:
                raise SystemExit("duplicate classified class %s:%s" % key)
            classified[key] = {"file": f, "entry": e, "raw": rawe}

    inv_path = os.path.join(out_dir, "corpus-inventory.json")
    if not os.path.exists(inv_path):
        inv_path = common.stage1_path("corpus-inventory.json")
    gate = common.load_json(inv_path)["version_diff_v0_107_1_vs_v0_111_0"]
    gate_files = {}
    for area, lst in gate["content_files"].items():
        for x in lst:
            gate_files[(area, x["file"][:-3])] = x
    exclusive = {}
    for side, areas in gate["version_exclusive_content"].items():
        for area, names in areas.items():
            for n in names:
                exclusive[(area, n)] = side

    index = V107Index()
    keys = set(k for k, c in classified.items() if ((c["entry"].get("version_delta_v107") or {}).get("status") or "same") != "same")
    keys |= set(gate_files) | set(exclusive)
    vd_keys = set(tuple(k.split(":", 1)) for k in MANUAL_VD)
    keys |= vd_keys
    kind_rank = dict((k, i) for i, k in enumerate(KIND_NS))
    entries = []
    for key in sorted(keys, key=lambda k: (kind_rank[k[0]], k[1])):
        kind, cls = key
        c = classified.get(key)
        vd = (c["entry"].get("version_delta_v107") or {}) if c else None
        status = (vd or {}).get("status")
        gx = gate_files.get(key)
        rel111 = None
        ident = model_id = None
        if c:
            rel111 = c["raw"]["file"] if c["raw"] else None
            ident, model_id = c["entry"]["id"], c["entry"].get("model_id") or (c["raw"] or {}).get("model_id")
        elif kind in POOL_KINDS and key in pool_rows:
            row = pool_rows[key]
            rel111, ident, model_id = row["file"], row.get("id"), row.get("model_id")
        elif key in raw_by_class:
            r = raw_by_class[key]
            rel111, ident, model_id = r["file"], r["id"], r["model_id"]
        if rel111 is None and exclusive.get(key) != "only_v0.107.1":
            rel111 = "Models.%s/%s.cs" % (KIND_NS[kind], cls)
        if rel111 and not os.path.exists(v111_abs(rel111)):
            rel111 = None
        hint107 = (vd or {}).get("v107_file") or (vd or {}).get("old_file")
        rel107 = index.locate(rel111 or "Models.%s/%s.cs" % (KIND_NS[kind], cls), hint107)
        if rel111 and rel107:
            dtype = "modified"
        elif rel111:
            dtype = "added"
        elif rel107:
            dtype = "removed"
        else:
            raise SystemExit("no file in either tree for %s:%s" % key)

        rec = OrderedDict()
        rec["key"] = "%s:%s" % key
        rec["kind"] = kind
        rec["class"] = cls
        rec["id"] = ident
        rec["model_id"] = model_id
        rec["delta_type"] = dtype
        reach, why = sp_reachable(kind, cls, c, rel111, rel107, pool_rows.get(key))
        rec["single_player_reachable"] = reach
        if why:
            rec["single_player_note"] = why
        rec["files"] = OrderedDict([(V111, rel111), (V107, rel107)])
        rec["classifier"] = (OrderedDict([("file", c["file"] + ".classified.json"), ("status", status),
                                          ("note", (vd or {}).get("note", ""))]) if c else None)
        rec["diff_gate"] = gate_record(gx)
        if key in exclusive:
            rec["diff_gate"]["exclusive"] = exclusive[key]
        rec["sources"] = ("both" if (c and status not in (None, "same")) and (gx or key in exclusive)
                          else "classifier" if (c and status not in (None, "same"))
                          else "diff_gate" if (gx or key in exclusive) else "version_delta")

        lines111 = read_lines(v111_abs(rel111)) if rel111 else None
        lines107 = read_lines(v107_abs(rel107)) if rel107 else None
        man = MANUAL.get(rec["key"]) or MANUAL_VD.get(rec["key"])
        extra = OrderedDict()
        if man:
            triage, basis, note, ev = man
            for tree in (V111, V107):
                for x in ev.get(tree, []):
                    check_citation(x, tree)
            evidence = OrderedDict([(V111, list(ev.get(V111, []))), (V107, list(ev.get(V107, [])))])
            method = basis
        elif kind in POOL_KINDS and dtype == "modified":
            m111, m107 = pool_members(lines111), pool_members(lines107)
            added = [n for n in m111 if n not in m107]
            removed = [n for n in m107 if n not in m111]
            basis = "membership"
            triage = "changed" if (added or removed) else "unknown"
            mock = bool((pool_rows.get(key) or {}).get("is_mock"))
            note = "members %d -> %d; added %s; removed %s" % (
                len(m107), len(m111), ", ".join(added) or "none", ", ".join(removed) or "none")
            if mock:
                note += " (test pool: no single-player effect)"
                if triage == "changed":
                    triage = "refactor_only"
            elif kind == "card_pools" and triage == "changed":
                mp = sorted([n for n in added if card_mp_only(V111, n)] + [n for n in removed if card_mp_only(V107, n)])
                extra["members_multiplayer_only"] = mp
                if len(mp) == len(added) + len(removed):
                    triage = "refactor_only"
                    note += ("; every added or removed card is MultiplayerOnly, which GetUnlockedCards drops in "
                             "single player (%s): the single-player pool is unchanged" % MP_FILTER_CITE)
                elif mp:
                    note += "; MultiplayerOnly (no single-player effect): %s" % ", ".join(mp)
            e111 = [m111[n] for n in added][:EV.MAX_LINES] or [generator_line(lines111, cls)]
            e107 = [m107[n] for n in removed][:EV.MAX_LINES] or [generator_line(lines107, cls)]
            evidence = OrderedDict([(V111, cite(rel111, e111)), (V107, cite(rel107, e107))])
            method = "membership"
            extra["members_added"] = added
            extra["members_removed"] = removed
        elif dtype in ("added", "removed"):
            basis = "presence"
            triage = "changed"
            mock = bool(c and (c["entry"].get("is_mock") or c["entry"].get("scope") == "test_only")) or \
                bool((pool_rows.get(key) or {}).get("is_mock"))
            if mock:
                triage = "refactor_only"
            note = (vd or {}).get("note") or ("only in v0.111.0" if dtype == "added" else "only in v0.107.1")
            if mock and "mock" not in note.lower():
                note += " (test mock: no single-player effect)"
            if dtype == "added":
                ln = (c["raw"] or {}).get("class_line") if c and c["raw"] else None
                evidence = OrderedDict([(V111, cite(rel111, [ln or EV.class_line(lines111, cls)])), (V107, [])])
            else:
                evidence = OrderedDict([(V111, []), (V107, cite(rel107, [EV.class_line(lines107, cls)]))])
                extra["referenced_by_v0.107.1"] = model_refs_v107(cls, rel107, index)
            method = "class-declaration"
            if status == "new" and dtype != "added":
                triage, note = "unknown", "classifier says new but a v0.107.1 file exists: " + note
        else:
            t = STATUS_TRIAGE.get(status) if c else None
            if t is None:
                triage, basis = "unknown", "unadjudicated"
                note = ((vd or {}).get("note") or "") if c else "flagged by the diff gate; no classifier covers this file"
                if c and status in ("same", None):
                    note = "flagged by the diff gate, classifier status %s; no manual record" % status
            else:
                triage, basis, note = t, "classifier", (vd or {}).get("note", "")
            h = EV.Hints()
            classifier_hints(h, vd)
            gate_hints(h, gx)
            ev, method = EV.find(lines111, lines107, h, cls)
            evidence = OrderedDict([(V111, cite(rel111, ev[V111])), (V107, cite(rel107, ev[V107]))])
        rec["triage"] = triage
        rec["basis"] = basis
        rec["note"] = note
        rec["evidence"] = evidence
        rec["evidence_method"] = method
        for k, v in extra.items():
            rec[k] = v
        entries.append(rec)

    engine = []
    for x in sorted(gate["engine_files"], key=lambda x: (x["namespace"], x["file"])):
        rel111 = "%s/%s" % (x["namespace"], x["file"])
        rel107 = index.locate(rel111)
        if not (rel107 and os.path.exists(v111_abs(rel111))):
            raise SystemExit("engine file missing in a tree: %s" % rel111)
        h = EV.Hints()
        gate_hints(h, x)
        ev, method = EV.find(read_lines(v111_abs(rel111)), read_lines(v107_abs(rel107)), h, x["file"][:-3])
        rec = OrderedDict([
            ("key", "engine:%s" % rel111), ("namespace", x["namespace"]), ("file", x["file"]),
            ("delta_type", "modified"), ("files", OrderedDict([(V111, rel111), (V107, rel107)])),
            ("diff_gate", gate_record(x)), ("triage", "unknown"), ("basis", "unadjudicated"),
            ("note", "engine code: no classifier reviews it; the kernel and run-layer ports read v0.111.0 only"),
            ("evidence", OrderedDict([(V111, cite(rel111, ev[V111])), (V107, cite(rel107, ev[V107]))])),
            ("evidence_method", method),
        ])
        engine.append(rec)

    counts = OrderedDict()
    counts["content_entries"] = len(entries)
    counts["by_triage"] = OrderedDict((t, sum(1 for e in entries if e["triage"] == t)) for t in TRIAGE_LEGEND)
    counts["by_delta_type"] = OrderedDict((t, sum(1 for e in entries if e["delta_type"] == t)) for t in DELTA_TYPES)
    counts["by_basis"] = OrderedDict((b, sum(1 for e in entries if e["basis"] == b)) for b in BASIS)
    counts["by_source"] = OrderedDict((s, sum(1 for e in entries if e["sources"] == s)) for s in ("both", "classifier", "diff_gate", "version_delta"))
    by_kind = OrderedDict()
    for kind in KIND_NS:
        row = OrderedDict((t, sum(1 for e in entries if e["kind"] == kind and e["triage"] == t)) for t in TRIAGE_LEGEND)
        if sum(row.values()):
            by_kind[kind] = row
    counts["by_kind"] = by_kind
    counts["single_player_reachable"] = OrderedDict(
        (t, sum(1 for e in entries if e["triage"] == t and e["single_player_reachable"])) for t in TRIAGE_LEGEND)
    counts["not_single_player_reachable"] = OrderedDict(sorted(collections.Counter(
        e["single_player_note"].split(":")[0].split(" (")[0] for e in entries if not e["single_player_reachable"]).items()))
    dis = [e for e in entries if e["classifier"] and e["classifier"]["status"] == "same" and e["diff_gate"]["flagged"]]
    counts["gate_flagged_classifier_same"] = OrderedDict((t, sum(1 for e in dis if e["triage"] == t)) for t in TRIAGE_LEGEND)
    counts["gate_flagged_classifier_same_keys"] = [e["key"] for e in dis]
    fp = [e for e in entries if e["diff_gate"]["flagged"] and e["triage"] == "refactor_only"]
    counts["gate_flagged_refactor_only"] = len(fp)
    counts["engine_entries"] = len(engine)
    counts["engine_by_triage"] = OrderedDict((t, sum(1 for e in engine if e["triage"] == t)) for t in TRIAGE_LEGEND)
    counts["evidence_methods"] = OrderedDict(sorted(collections.Counter(e["evidence_method"] for e in entries + engine).items()))

    doc = OrderedDict()
    doc["schema"] = SCHEMA
    doc["generated_by"] = GENERATED_BY
    doc["game_version"] = "0.111.0"
    doc["baseline_version"] = "0.107.1"
    doc["trees"] = OrderedDict([
        (V111, "<decompile root>/MegaCrit.Sts2.Core.<namespace>/<file> (output/server-bootstrap/linux-decompiled); "
               "cited as <namespace>/<file>:<line>"),
        (V107, "artifacts/knowledge/decompiled-v0.107.1/MegaCrit/sts2/Core/<path>:<line>"),
    ])
    doc["sources"] = OrderedDict([
        ("classifiers", "version_delta_v107 of the *.classified.json in this directory (every status but same)"),
        ("diff_gate", "corpus-inventory.json version_diff_v0_107_1_vs_v0_111_0: content_files, version_exclusive_content, engine_files"),
        ("version_delta", "scripts/version_delta.py: content files with a unique edit that the classifiers read as same and the "
                          "diff gate did not flag (triage.py MANUAL_VD, docs/version-delta-demo-2026-11-01.md)"),
    ])
    doc["triage_legend"] = TRIAGE_LEGEND
    doc["delta_types"] = DELTA_TYPES
    doc["basis_legend"] = BASIS
    doc["evidence_note"] = ("up to %d lines per tree; manual / il evidence was read by hand, the rest is mechanical "
                            "(evidence.py: token-diff, difflib, anchor, membership, class-declaration)" % EV.MAX_LINES)
    doc["taint_filter_rule"] = ("a v0.107.1 trajectory is comparable with v0.111.0 only while it touches no content entry "
                                "triaged changed or unknown with single_player_reachable true; engine entries are unknown "
                                "until adjudicated")
    doc["counts"] = counts
    doc["entries"] = entries
    doc["engine_entries"] = engine
    os.makedirs(out_dir, exist_ok=True)
    with open(os.path.join(out_dir, "version-deltas.json"), "w", encoding="utf-8") as fh:
        json.dump(doc, fh, indent=1, ensure_ascii=False)
        fh.write("\n")
    print(json.dumps(counts, indent=1))


if __name__ == "__main__":
    main()
