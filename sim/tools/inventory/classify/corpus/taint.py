import os as _os
import sys as _sys
_sys.path.insert(0, _os.path.join(_os.path.dirname(_os.path.abspath(__file__)), ".."))
import common  # noqa: E402
import gzip, json, os, re, sys, collections
ROOT = common.CORPUS
vd = json.load(open(common.work_path("corpus", "version_diff.json")))
def slug(n):
    n = n.split("#")[0]
    if n.endswith(".cs"):
        n = n[:-3]
    s = re.sub(r"(?<=[a-z0-9])(?=[A-Z])", "_", n)
    s = re.sub(r"(?<=[A-Z])(?=[A-Z][a-z])", "_", s)
    return s.upper()
changed = {a: {slug(e["file"]) for e in v} for a, v in vd["content_files"].items()}
only_new = {a: {slug(x) for x in v} for a, v in vd["only_v0.111.0"].items()}
only_old = {a: {slug(x) for x in v} for a, v in vd["only_v0.107.1"].items()}
def open_stream(d, name):
    gz = os.path.join(d, name + ".jsonl.gz"); pl = os.path.join(d, name + ".jsonl")
    if os.path.isfile(gz): return gzip.open(gz, "rt", encoding="utf-8")
    if os.path.isfile(pl): return open(pl, encoding="utf-8")
    return None
def recs(d):
    out = []
    for name in ("states", "actions", "events"):
        h = open_stream(d, name)
        if not h: continue
        with h:
            for line in h:
                line = line.strip()
                if not line: continue
                try: out.append(json.loads(line))
                except json.JSONDecodeError: pass
    out.sort(key=lambda r: r.get("seq", 0))
    return out
stats = collections.Counter()
reasons = collections.Counter()
first_taint_dec = []
for dname in common.corpus_dirs():
    d = os.path.join(ROOT, dname)
    m = json.load(open(os.path.join(d, "manifest.json")))
    if m["game"]["version"] != "v0.107.1": continue
    cur = None
    combats = []
    for r in recs(d):
        t = r.get("type")
        if t == "event" and r.get("entry") == "room_entered":
            mid = (r["data"] or {}).get("room_model_id") or ""
            cur = {"enc": mid, "cards": set(), "relics": set(), "monsters": set(), "powers": set(), "potions": set(), "orbs": set(), "decisions": 0, "taint_at": None} if mid.startswith("ENCOUNTER.") else None
            if cur: combats.append(cur)
            continue
        if cur is None: continue
        def taint_check():
            hit = []
            for area, ids in (("cards", cur["cards"]), ("relics", cur["relics"]), ("monsters", cur["monsters"]), ("powers", cur["powers"]), ("potions", cur["potions"]), ("orbs", cur["orbs"])):
                for i in ids:
                    if i in changed.get(area, set()) or i in only_old.get(area, set()):
                        hit.append(area + ":" + i)
            return hit
        if t == "state":
            s = r.get("state") or {}
            b = s.get("battle")
            if isinstance(b, dict):
                p = s.get("player") or {}
                for rl in p.get("relics") or []:
                    if isinstance(rl, dict) and rl.get("id"): cur["relics"].add(rl["id"])
                for en in b.get("enemies") or []:
                    cur["monsters"].add(re.sub(r"_\d+$", "", en.get("entity_id") or ""))
                    for pw in en.get("status") or []:
                        if isinstance(pw, dict) and pw.get("id"): cur["powers"].add(pw["id"].replace("_POWER", "") + "_POWER" if not pw["id"].endswith("_POWER") else pw["id"])
                for pw in p.get("status") or []:
                    if isinstance(pw, dict) and pw.get("id"): cur["powers"].add(pw["id"])
                for o in p.get("orbs") or []:
                    if isinstance(o, dict) and o.get("id"): cur["orbs"].add(o["id"])
        elif t == "action":
            a = r.get("action") or {}
            k = a.get("kind"); pr = a.get("params") or {}
            if r.get("status") == "cancelled": continue
            if k in ("play_card", "end_turn"):
                if cur["taint_at"] is None and taint_check():
                    cur["taint_at"] = cur["decisions"]
                    cur["taint_hit"] = taint_check()
                cur["decisions"] += 1
                if k == "play_card" and pr.get("card_name"): cur["cards"].add(pr["card_name"])
            if k == "use_potion" and pr.get("potion_id"): cur["potions"].add(pr["potion_id"].split(".", 1)[-1])
    for c in combats:
        if c["decisions"] == 0: continue
        stats["combats"] += 1
        stats["decisions"] += c["decisions"]
        hit = c.get("taint_hit") if c["taint_at"] is not None else None
        if c["taint_at"] is None:
            stats["untainted_combats"] += 1
            stats["untainted_decisions"] += c["decisions"]
        else:
            stats["decisions_before_taint"] += c["taint_at"]
            for h in hit: reasons[h.split(":")[0]] += 1
            for h in hit: reasons["id:" + h] += 1
res = {"definition": "v0.107.1 combat windows (room_entered ENCOUNTER.* with >= 1 play_card/end_turn) replayed under v0.111.0 rules; a combat is tainted from the first decision at which any card played so far, relic owned, monster present, power seen, orb channeled or potion used is in the rule_diverging or v0.107.1-only list (conservative: every flag counts, no triage)",
       "combats": stats["combats"], "decisions": stats["decisions"],
       "untainted_combats": stats["untainted_combats"], "untainted_combat_decisions": stats["untainted_decisions"],
       "decisions_before_first_taint_in_tainted_combats": stats["decisions_before_taint"],
       "comparable_decisions_upper_bound": stats["untainted_decisions"] + stats["decisions_before_taint"],
       "taint_sources_combats": {k: v for k, v in reasons.items() if ":" not in k},
       "top_taint_ids": [[k[3:], v] for k, v in reasons.most_common(60) if k.startswith("id:")][:30],
       "known_false_positive": "monsters:NIBBIT (call-set flag only) although the 4 agreeing v0.107.1 bundles include NIBBITS_WEAK under v0.111.0 rules"}
json.dump(res, open(common.work_path("corpus", "taint_stats.json"), "w"), indent=1)
print(json.dumps(res)[:600])
