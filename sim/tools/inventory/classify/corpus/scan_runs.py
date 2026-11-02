import os as _os
import sys as _sys
_sys.path.insert(0, _os.path.join(_os.path.dirname(_os.path.abspath(__file__)), ".."))
import common  # noqa: E402
import gzip, json, os, sys, collections, time, re

ROOT = common.CORPUS
OUT = common.work_path("corpus", "runs_parts.json")

COMBAT_ROOM_TYPES = {"Monster", "Elite", "Boss"}

def open_stream(d, name):
    gz = os.path.join(d, name + ".jsonl.gz")
    pl = os.path.join(d, name + ".jsonl")
    if os.path.isfile(gz):
        return gzip.open(gz, "rt", encoding="utf-8")
    if os.path.isfile(pl):
        return open(pl, "r", encoding="utf-8")
    return None

def read_stream(d, name):
    h = open_stream(d, name)
    out = []
    if h is None:
        return out
    with h:
        for line in h:
            line = line.strip()
            if not line:
                continue
            try:
                out.append(json.loads(line))
            except json.JSONDecodeError:
                pass
    return out

def category(kind, in_combat):
    if kind == "play_card":
        return "play_card"
    if kind in ("end_turn", "undo_end_turn"):
        return "end_turn"
    if kind == "player_choice":
        return "choice_in_combat" if in_combat else "choice_out_of_combat"
    if kind in ("vote_for_map_coord", "vote_to_move_to_next_act"):
        return "map"
    if kind in ("reward_taken", "rewards_skipped", "pick_relic", "open_chest", "card_reward_obtained"):
        return "reward"
    if kind in ("shop_purchase", "shop_card_removal"):
        return "shop"
    if kind == "rest_site_option":
        return "rest"
    if kind in ("event_option", "event_proceed"):
        return "event"
    if kind in ("use_potion", "discard_potion"):
        return "potion_in_combat" if in_combat else "potion_out_of_combat"
    if kind in ("move_to_map_coord", "ready_to_begin_enemy_turn"):
        return "follow_up"
    return "other:" + kind

def scan_part(d):
    m = json.load(open(os.path.join(d, "manifest.json")))
    recs = []
    for name in ("states", "actions", "events"):
        for r in read_stream(d, name):
            recs.append(r)
    recs.sort(key=lambda r: (r.get("seq", 0), {"state": 1, "action": 0, "event": 2}.get(r.get("type"), 3)))
    room = None
    combat_rooms = []
    decisions = collections.Counter()
    decisions_by_act = collections.Counter()
    choice_types_ctx = collections.Counter()
    cancelled = collections.Counter()
    human = collections.Counter()
    nonhuman = collections.Counter()
    max_floor = None
    max_act = None
    seen = {"cards": set(), "relics": set(), "potions": set(), "monsters": set(), "encounters": set(),
            "events": set(), "powers": set(), "rest_options": set(), "shop_items": set()}
    screens = collections.Counter()
    battle_states = 0
    last_battle_seq_in_room = None
    state_by_seq = {}
    battle_seqs_by_room = collections.defaultdict(list)
    cur_room_seq = None
    for r in recs:
        if r.get("type") == "event" and r.get("entry") == "room_entered":
            cur_room_seq = r["seq"]
        elif r.get("type") == "state":
            s = r.get("state") or {}
            has_battle = isinstance(s.get("battle"), dict) and "enemies" in s.get("battle")
            state_by_seq[r["seq"]] = (s.get("state_type"), has_battle, cur_room_seq)
            if has_battle:
                battle_seqs_by_room[cur_room_seq].append(r["seq"])
    def action_in_combat(r, room_seq):
        ss = r.get("state_seq")
        info = state_by_seq.get(ss)
        if info is None:
            return False
        stype, has_battle, rseq = info
        if has_battle:
            return True
        if stype in ("card_select", "hand_select", "bundle_select", "map", "popup", "overlay", "relic_select"):
            later = [x for x in battle_seqs_by_room.get(rseq, []) if x > ss]
            return bool(later)
        return False
    for r in recs:
        t = r.get("type")
        if t == "event":
            e = r.get("entry")
            data = r.get("data") or {}
            if e == "room_entered":
                room = {"seq": r["seq"], "room_type": data.get("room_type"), "model": data.get("room_model_id"),
                        "act": data.get("act"), "floor": data.get("floor")}
                mid = room["model"] or ""
                if room["room_type"] in COMBAT_ROOM_TYPES or mid.startswith("ENCOUNTER."):
                    combat_rooms.append(dict(room, decisions=0, battle_states=0))
                    if mid.startswith("ENCOUNTER."):
                        seen["encounters"].add(mid[len("ENCOUNTER."):])
                if mid.startswith("EVENT."):
                    seen["events"].add(mid[len("EVENT."):])
            elif e == "power_received":
                pw = data.get("power")
                if isinstance(pw, str):
                    seen["powers"].add(pw.split(".", 1)[-1])
            continue
        in_combat = bool(combat_rooms) and room is not None and combat_rooms[-1]["seq"] == room["seq"]
        if t == "state":
            s = r.get("state") or {}
            st = s.get("state_type")
            screens[st] += 1
            run = s.get("run") or {}
            f = run.get("floor"); a = run.get("act")
            if isinstance(f, int):
                max_floor = f if max_floor is None else max(max_floor, f)
            if isinstance(a, int):
                max_act = a if max_act is None else max(max_act, a)
            p = s.get("player") or {}
            for rl in p.get("relics") or []:
                if isinstance(rl, dict) and rl.get("id"):
                    seen["relics"].add(rl["id"])
            for pt in p.get("potions") or []:
                if isinstance(pt, dict) and pt.get("id"):
                    seen["potions"].add(pt["id"])
            for c in p.get("hand") or []:
                if isinstance(c, dict) and c.get("id"):
                    seen["cards"].add(c["id"])
            for pw in p.get("status") or []:
                if isinstance(pw, dict) and pw.get("id"):
                    seen["powers"].add(pw["id"])
            b = s.get("battle")
            if isinstance(b, dict) and "enemies" in b:
                battle_states += 1
                if in_combat:
                    combat_rooms[-1]["battle_states"] += 1
                for en in b.get("enemies") or []:
                    eid = en.get("entity_id") or ""
                    seen["monsters"].add(re.sub(r"_\d+$", "", eid))
                    for pw in en.get("status") or []:
                        if isinstance(pw, dict) and pw.get("id"):
                            seen["powers"].add(pw["id"])
            if st == "rest_site":
                for o in (s.get("rest_site") or {}).get("options") or []:
                    if o.get("id"):
                        seen["rest_options"].add(o["id"])
            if st == "shop":
                for it in (s.get("shop") or {}).get("items") or []:
                    for k in ("card_id", "relic_id", "potion_id"):
                        if it.get(k):
                            seen["shop_items"].add(k[:-3] + ":" + it[k])
            for key in ("card_reward", "card_select", "hand_select"):
                blk = s.get(key)
                if isinstance(blk, dict):
                    for c in blk.get("cards") or []:
                        if isinstance(c, dict) and c.get("id"):
                            seen["cards"].add(c["id"])
            continue
        if t == "action":
            in_combat = action_in_combat(r, room["seq"] if room else None)
            a = r.get("action") or {}
            k = a.get("kind")
            st = r.get("status")
            p = a.get("params") or {}
            if st == "cancelled":
                cancelled[k] += 1
                continue
            cat = category(k, in_combat)
            decisions[cat] += 1
            act_idx = room["act"] if room else None
            decisions_by_act[(cat, act_idx)] += 1
            if p.get("human") is True:
                human[cat] += 1
            elif p.get("human") is False:
                nonhuman[cat] += 1
            if k == "player_choice":
                choice_types_ctx[(str(p.get("choice_type")), "combat" if in_combat else "out")] += 1
            if in_combat and cat in ("play_card", "end_turn"):
                combat_rooms[-1]["decisions"] += 1
            if k == "play_card":
                cid = p.get("card_name")
                if cid:
                    seen["cards"].add(cid)
            if k == "use_potion" and p.get("potion_id"):
                seen["potions"].add(p["potion_id"].split(".", 1)[-1])
    return {
        "dir": os.path.basename(d),
        "key": [m["run"]["start_time"], m["run"]["seed"], m["run"]["character"]],
        "part": m.get("part", 1),
        "recorder_version": m.get("recorder_version"),
        "game_version": m["game"]["version"],
        "character": m["run"]["character"],
        "ascension": m["run"]["ascension"],
        "game_mode": m["run"]["game_mode"],
        "seed": m["run"]["seed"],
        "mods": m.get("mods", []),
        "result": m.get("result"),
        "incomplete": m.get("incomplete"),
        "native": sorted(os.listdir(os.path.join(d, "native"))) if os.path.isdir(os.path.join(d, "native")) else [],
        "max_floor": max_floor, "max_act": max_act,
        "combat_rooms": combat_rooms,
        "decisions": dict(decisions),
        "decisions_by_act": [[c, a, n] for (c, a), n in decisions_by_act.items()],
        "choice_types_ctx": [[c, x, n] for (c, x), n in choice_types_ctx.items()],
        "cancelled": dict(cancelled),
        "human": dict(human), "nonhuman": dict(nonhuman),
        "screens": dict(screens),
        "battle_states": battle_states,
        "seen": {k: sorted(v) for k, v in seen.items()},
    }

def main():
    dirs = common.corpus_dirs()
    out = []
    t0 = time.time()
    for i, x in enumerate(dirs):
        try:
            out.append(scan_part(os.path.join(ROOT, x)))
        except Exception as ex:
            out.append({"dir": x, "error": repr(ex)})
        if i % 300 == 0:
            print(i, round(time.time() - t0, 1), file=sys.stderr)
    json.dump(out, open(OUT, "w"))
    print("done", len(out), round(time.time() - t0, 1), file=sys.stderr)

main()
