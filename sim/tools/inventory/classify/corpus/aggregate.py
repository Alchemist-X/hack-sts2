import os as _os
import sys as _sys
_sys.path.insert(0, _os.path.join(_os.path.dirname(_os.path.abspath(__file__)), ".."))
import common  # noqa: E402
import json, collections, sys, os

parts = json.load(open(common.work_path("corpus", "runs_parts.json")))
pass1 = {p["dir"]: p for p in json.load(open(common.work_path("corpus", "parts.json")))}
out_path = common.work_path("corpus", "inventory_raw.json")

lineages = collections.OrderedDict()
for p in sorted(parts, key=lambda p: (tuple(p["key"]), p["part"], p["dir"])):
    lineages.setdefault(tuple(p["key"]), []).append(p)

def bucket_floor(f):
    if f is None:
        return "unknown"
    edges = [(1, "1"), (2, "2"), (5, "3-5"), (10, "6-10"), (17, "11-17"), (18, "18"), (25, "19-25"), (34, "26-34"), (35, "35"), (51, "36-51")]
    for e, name in edges:
        if f <= e:
            return name
    return ">51"

runs = []
for key, ps in lineages.items():
    versions = sorted({p["game_version"] for p in ps})
    rec = sorted({p["recorder_version"] for p in ps})
    mods = sorted({m for p in ps for m in p["mods"]})
    max_floor = max([p["max_floor"] for p in ps if p["max_floor"] is not None], default=None)
    max_act = max([p["max_act"] for p in ps if p["max_act"] is not None], default=None)
    last = ps[-1]
    result = last["result"]
    run_ended = None
    for p in ps:
        if pass1[p["dir"]].get("run_ended") is not None:
            run_ended = pass1[p["dir"]]["run_ended"]
    if run_ended is not None:
        outcome = "win" if run_ended.get("win") else ("abandoned" if run_ended.get("abandoned") else "loss")
    else:
        outcome = "incomplete"
    combats = collections.OrderedDict()
    for p in ps:
        for c in p["combat_rooms"]:
            k = (c["act"], c["floor"])
            if k not in combats:
                combats[k] = {"act": c["act"], "floor": c["floor"], "room_type": c["room_type"], "encounter": c["model"],
                              "attempts": 0, "decisions_last_attempt": 0, "parts": []}
            combats[k]["attempts"] += 1
            combats[k]["decisions_last_attempt"] = c["decisions"]
            combats[k]["parts"].append(p["dir"])
    dec = collections.Counter()
    dec_act = collections.Counter()
    ct = collections.Counter()
    for p in ps:
        for k, v in p["decisions"].items():
            dec[k] += v
        for c, a, n in p["decisions_by_act"]:
            dec_act[(c, a)] += n
        for c, x, n in p["choice_types_ctx"]:
            ct[(c, x)] += n
    native = collections.Counter()
    for p in ps:
        for f in p["native"]:
            native[f] += 1
    seen = collections.defaultdict(set)
    for p in ps:
        for k, v in p["seen"].items():
            seen[k].update(v)
    runs.append({
        "key": list(key), "seed": key[1], "character": key[2], "parts": len(ps),
        "game_versions": versions, "recorder_versions": rec, "mods": mods,
        "ascension": ps[0]["ascension"], "game_mode": ps[0]["game_mode"],
        "max_floor": max_floor, "max_act": max_act, "outcome": outcome,
        "combats": list(combats.values()), "decisions": dict(dec),
        "decisions_by_act": [[c, a, n] for (c, a), n in dec_act.items()],
        "choice_types_ctx": [[c, x, n] for (c, x), n in ct.items()],
        "native_files": dict(native),
        "seen": {k: sorted(v) for k, v in seen.items()},
    })

inv = {}
inv["parts_total"] = len(parts)
inv["lineages_total"] = len(runs)

def vca(r):
    v = r["game_versions"][0] if len(r["game_versions"]) == 1 else "+".join(r["game_versions"])
    return v

by_vca = collections.Counter()
by_vca_parts = collections.Counter()
for r in runs:
    by_vca[(vca(r), r["character"], r["ascension"])] += 1
for p in parts:
    by_vca_parts[(p["game_version"], p["character"], p["ascension"])] += 1
inv["by_version_character_ascension"] = [
    {"game_version": v, "character": c, "ascension": a, "lineages": n, "parts": by_vca_parts.get((v, c, a), 0)}
    for (v, c, a), n in sorted(by_vca.items())]

by_v = collections.defaultdict(lambda: collections.Counter())
for r in runs:
    v = vca(r)
    by_v[v]["lineages"] += 1
    by_v[v]["parts"] += r["parts"]
    by_v[v]["outcome:" + r["outcome"]] += 1
    by_v[v]["combats_unique"] += len(r["combats"])
    by_v[v]["combat_attempts"] += sum(c["attempts"] for c in r["combats"])
    for k, n in r["decisions"].items():
        by_v[v]["decisions:" + k] += n
inv["by_version"] = {v: dict(c) for v, c in sorted(by_v.items())}

floors = collections.defaultdict(lambda: collections.Counter())
for r in runs:
    floors[vca(r)][bucket_floor(r["max_floor"])] += 1
    floors["all"][bucket_floor(r["max_floor"])] += 1
inv["max_floor_reached_histogram"] = {v: dict(c) for v, c in floors.items()}
inv["max_act_reached"] = dict(collections.Counter(str(r["max_act"]) for r in runs))

cpa = collections.defaultdict(lambda: collections.Counter())
for r in runs:
    for c in r["combats"]:
        a = "act%s" % ((c["act"] + 1) if isinstance(c["act"], int) else "?")
        cpa[vca(r)][a + ":" + str(c["room_type"])] += 1
        cpa[vca(r)][a + ":attempts"] += c["attempts"]
        cpa[vca(r)][a + ":unique"] += 1
        cpa["all"][a + ":" + str(c["room_type"])] += 1
        cpa["all"][a + ":attempts"] += c["attempts"]
        cpa["all"][a + ":unique"] += 1
inv["combats_per_act"] = {v: dict(sorted(c.items())) for v, c in cpa.items()}

first_combat_only = 0
later = 0
for r in runs:
    cs = r["combats"]
    if cs:
        first_combat_only += 1
        later += len(cs) - 1
inv["combats_first_vs_later"] = {"first_combats": first_combat_only, "later_combats": later}

dec_all = collections.Counter()
dec_by_v = collections.defaultdict(lambda: collections.Counter())
for r in runs:
    for k, n in r["decisions"].items():
        dec_all[k] += n
        dec_by_v[vca(r)][k] += n
inv["decisions_by_screen"] = {"all": dict(dec_all), "by_version": {v: dict(c) for v, c in dec_by_v.items()}}
dba = collections.Counter()
for r in runs:
    for c, a, n in r["decisions_by_act"]:
        dba["%s@act%s" % (c, (a + 1) if isinstance(a, int) else "none")] += n
inv["decisions_by_screen_and_act"] = dict(sorted(dba.items()))
cta = collections.Counter()
for r in runs:
    for c, x, n in r["choice_types_ctx"]:
        cta[c + "@" + x] += n
inv["player_choice_types"] = dict(cta)

nat = collections.Counter()
nat_l = collections.Counter()
for r in runs:
    for f, n in r["native_files"].items():
        nat["parts_with:" + f] += n
        nat_l["lineages_with:" + f] += 1
    if not r["native_files"]:
        nat_l["lineages_without_native"] += 1
inv["native"] = {"parts": dict(nat), "lineages": dict(nat_l)}

recv = collections.Counter()
for p in parts:
    recv[(p["recorder_version"], p["game_version"])] += 1
inv["recorder_versions"] = [{"recorder_version": a, "game_version": b, "parts": n} for (a, b), n in sorted(recv.items())]
modc = collections.Counter()
for r in runs:
    modc["+".join(m for m in r["mods"] if not m.startswith("Sts2Recorder")) or "none"] += 1
inv["lineages_by_extra_mods"] = dict(modc)

seen_all = collections.defaultdict(set)
seen_by_v = collections.defaultdict(lambda: collections.defaultdict(set))
for r in runs:
    for k, v in r["seen"].items():
        seen_all[k].update(v)
        seen_by_v[vca(r)][k].update(v)
inv["content_seen"] = {"all": {k: len(v) for k, v in seen_all.items()},
                       "by_version": {ver: {k: len(v) for k, v in d.items()} for ver, d in seen_by_v.items()}}
inv["_content_seen_ids"] = {k: sorted(v) for k, v in seen_all.items()}
inv["_runs"] = runs
json.dump(inv, open(out_path, "w"), indent=1)
print(json.dumps({k: v for k, v in inv.items() if not k.startswith("_")}, indent=1)[:12000])
