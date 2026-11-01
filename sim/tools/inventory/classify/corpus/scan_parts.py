import os as _os
import sys as _sys
_sys.path.insert(0, _os.path.join(_os.path.dirname(_os.path.abspath(__file__)), ".."))
import common  # noqa: E402
import gzip, json, os, sys, collections, time

ROOT = common.CORPUS
OUT = common.work_path("corpus", "parts.json")

def open_stream(d, name):
    gz = os.path.join(d, name + ".jsonl.gz")
    pl = os.path.join(d, name + ".jsonl")
    if os.path.isfile(gz):
        return gzip.open(gz, "rt", encoding="utf-8")
    if os.path.isfile(pl):
        return open(pl, "r", encoding="utf-8")
    return None

def iter_records(d, name):
    h = open_stream(d, name)
    if h is None:
        return
    with h:
        for line in h:
            line = line.strip()
            if not line:
                continue
            try:
                yield json.loads(line)
            except json.JSONDecodeError:
                yield {"_torn": True}

def scan(d):
    m = json.load(open(os.path.join(d, "manifest.json")))
    r = {"dir": os.path.basename(d), "manifest": {
        "recorder_version": m.get("recorder_version"),
        "game_version": m["game"]["version"], "commit": m["game"].get("commit"),
        "untested": m["game"].get("untested"),
        "seed": m["run"]["seed"], "character": m["run"]["character"],
        "ascension": m["run"]["ascension"], "game_mode": m["run"]["game_mode"],
        "start_time": m["run"]["start_time"], "part": m.get("part", 1),
        "incomplete": m.get("incomplete"), "result": m.get("result"),
        "counts": m.get("counts"), "mods": m.get("mods", []),
        "compression": m.get("compression"), "degraded_hooks": m.get("degraded_hooks", []),
    }}
    nd = os.path.join(d, "native")
    native = {}
    if os.path.isdir(nd):
        for f in sorted(os.listdir(nd)):
            p = os.path.join(nd, f)
            native[f] = os.path.getsize(p)
    r["native"] = native
    actions = collections.Counter()
    actions_human = collections.Counter()
    choice_types = collections.Counter()
    action_seq = []
    torn = 0
    for rec in iter_records(d, "actions"):
        if rec.get("_torn"):
            torn += 1
            continue
        a = rec.get("action", {})
        k = a.get("kind")
        st = rec.get("status")
        p = a.get("params", {}) or {}
        key = k if st != "cancelled" else k + ":cancelled"
        actions[key] += 1
        if p.get("human") is True:
            actions_human[key] += 1
        if k == "player_choice":
            choice_types[str(p.get("choice_type"))] += 1
        action_seq.append([rec.get("seq"), k, st, rec.get("state_seq")])
    r["actions"] = dict(actions)
    r["actions_human"] = dict(actions_human)
    r["choice_types"] = dict(choice_types)
    events = collections.Counter()
    rooms = []
    floor_summaries = []
    for rec in iter_records(d, "events"):
        if rec.get("_torn"):
            torn += 1
            continue
        e = rec.get("entry")
        events[e] += 1
        data = rec.get("data") or {}
        if e == "room_entered":
            rooms.append({"seq": rec.get("seq"), "room_type": data.get("room_type"),
                          "room_model_id": data.get("room_model_id"),
                          "act": data.get("act"), "floor": data.get("floor")})
        elif e == "floor_summary":
            fs = {"seq": rec.get("seq"), "map_point_type": data.get("map_point_type")}
            rr = data.get("rooms")
            if isinstance(rr, list):
                fs["rooms"] = [ {"model_id": x.get("model_id"), "room_type": x.get("room_type"),
                                 "turns_taken": x.get("turns_taken")} for x in rr if isinstance(x, dict)]
            ps = data.get("player_stats")
            if isinstance(ps, list) and ps:
                fs["player_stats_keys"] = sorted(ps[0].keys()) if isinstance(ps[0], dict) else None
            floor_summaries.append(fs)
        elif e == "run_ended":
            r["run_ended"] = data
    r["events"] = dict(events)
    r["rooms"] = rooms
    r["floor_summaries"] = floor_summaries
    screens = collections.Counter()
    triggers = collections.Counter()
    max_floor = None
    max_act = None
    first_state_type = None
    last_state_type = None
    state_keys_by_type = {}
    for rec in iter_records(d, "states"):
        if rec.get("_torn"):
            torn += 1
            continue
        s = rec.get("state") or {}
        stype = s.get("state_type", rec.get("screen"))
        screens[stype] += 1
        triggers[rec.get("trigger")] += 1
        if first_state_type is None:
            first_state_type = stype
        last_state_type = stype
        run = s.get("run") or {}
        f = run.get("floor")
        a = run.get("act")
        if isinstance(f, int):
            max_floor = f if max_floor is None else max(max_floor, f)
        if isinstance(a, int):
            max_act = a if max_act is None else max(max_act, a)
        if stype not in state_keys_by_type:
            state_keys_by_type[stype] = sorted(s.keys())
    r["screens"] = dict(screens)
    r["triggers"] = dict(triggers)
    r["max_floor"] = max_floor
    r["max_act"] = max_act
    r["first_state_type"] = first_state_type
    r["last_state_type"] = last_state_type
    r["state_keys_by_type"] = state_keys_by_type
    r["torn_lines"] = torn
    return r

def main():
    dirs = common.corpus_dirs()
    out = []
    t0 = time.time()
    for i, x in enumerate(dirs):
        try:
            out.append(scan(os.path.join(ROOT, x)))
        except Exception as ex:
            out.append({"dir": x, "error": repr(ex)})
        if i % 200 == 0:
            print(i, round(time.time() - t0, 1), file=sys.stderr)
    with open(OUT, "w") as f:
        json.dump(out, f)
    print("done", len(out), round(time.time() - t0, 1), file=sys.stderr)

main()
