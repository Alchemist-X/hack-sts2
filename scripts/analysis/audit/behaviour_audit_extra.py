#!/usr/bin/env python3
"""Follow-up checks for the behaviour audit (stdlib only).

usage: python3 -I behaviour_audit_extra.py <audit_dir> <repo_root> <out.json>
Reuses the grouping of behaviour_audit.py (imported from the same directory via runpy-free copy below).
"""
import collections as C
import json
import re
import sys

AUD, REPO, OUT = sys.argv[1], sys.argv[2], sys.argv[3]
sys.argv = [sys.argv[0], AUD, REPO, "/dev/null"]
# pull helpers / tables from the main script without its prints
src = open(__file__.replace("behaviour_audit_extra.py", "behaviour_audit.py")).read()
src = src.split("# ---------------------------------------------------------------- load")[0]
ns = {}
exec(compile(src, "behaviour_audit.py", "exec"), ns)
base, group, ctype, classify_deck, META, SCALING_POWERS = (ns[k] for k in ("base", "group", "ctype", "classify_deck", "META", "SCALING_POWERS"))
mean = ns["mean"]

GAMES = [json.loads(l) for l in open(f"{AUD}/games.jsonl")]
GBY = {g["g"]: g for g in GAMES}
DEC = C.defaultdict(list)
for l in open(f"{AUD}/decisions.jsonl"):
    d = json.loads(l)
    DEC[d["g"]].append(d)
for k in DEC:
    DEC[k].sort(key=lambda d: d["t"])
for g in GAMES:
    g["og"] = "A1死亡" if g["act"] == 1 else ("到A2Boss" if any(b["act"] == 2 for b in g["boss_entry"]) else "A2途中死亡")
R = {}

# 1. rest at full HP
rest = [d for ds in DEC.values() for d in ds if d["kind"] == "ChooseRestOption"]
R["rest_n"] = len(rest)
R["rest_heal_at_full_hp"] = sum(1 for d in rest if d["hp"] >= d["max_hp"])
R["rest_heal_hp_ge_90"] = sum(1 for d in rest if d["hp"] >= 0.9 * d["max_hp"])
R["rest_p_heal_min"] = min(d["pr"][d["cands"].index("Rest:HEAL")] for d in rest)
R["rest_smith_mass_max"] = max(sum(p for c, p in zip(d["cands"], d["pr"]) if c.startswith("Rest:SMITH")) for d in rest)
R["rest_examples_full_hp"] = [(d["g"], d["t"], d["act"], d["hp"], d["max_hp"], d["chosen"]) for d in rest if d["hp"] >= d["max_hp"]][:4]

# 2. Hand Trick follow-up: the Sly-granted card discarded later in the same turn?
ht = C.Counter()
for k, ds in DEC.items():
    for i, d in enumerate(ds):
        p = d.get("prompt")
        if not (p and p["site"] == "0f3a2f52" and d["kind"] == "PromptSelect"):
            continue
        tgt = base(d["chosen"][7:])
        turn = d["combat"]["turn"]
        ht["n"] += 1
        outcome = "not_discarded"
        for e in ds[i + 1:]:
            if "combat" not in e or e["combat"]["turn"] != turn or e["node"] != d["node"]:
                break
            q = e.get("prompt")
            if q and q["site"] in ns["DISCARD_SITES"] and e["kind"] == "PromptSelect" and base(e["chosen"][7:]) == tgt:
                outcome = "discarded_same_turn"
                break
            if e["kind"] == "PlayCard" and base(e["card"]) == tgt:
                outcome = "played_manually"
                break
        ht[outcome] += 1
R["hand_trick_followup"] = dict(ht)

# 3. archetype at act-2 start vs reaching the A2 boss (controls for deck growth)
a2 = []
for g in GAMES:
    if g["act"] < 2:
        continue
    d0 = next(d for d in DEC[g["g"]] if d["act"] == 2)
    lab, cnt = classify_deck(d0["deck"])
    a2.append((g, "mixed" if lab.startswith("mixed") else lab, cnt, d0))
tab = C.defaultdict(C.Counter)
for g, lab, cnt, d0 in a2:
    tab[lab]["n"] += 1
    tab[lab]["reach"] += g["og"] == "到A2Boss"
R["arch_at_act2_start"] = {k: dict(v) for k, v in tab.items()}
pz = C.defaultdict(C.Counter)
for g, lab, cnt, d0 in a2:
    b = "0-1" if cnt["poison"] <= 1 else ("2-3" if cnt["poison"] <= 3 else "4+")
    pz[b]["n"] += 1
    pz[b]["reach"] += g["og"] == "到A2Boss"
R["poison_at_act2_start_vs_reach"] = {k: dict(v) for k, v in pz.items()}
sc = C.Counter()
for g, lab, cnt, d0 in a2:
    has = bool({base(c) for c in d0["deck"]} & SCALING_POWERS)
    sc[("scaling" if has else "no_scaling") + "_n"] += 1
    sc[("scaling" if has else "no_scaling") + "_reach"] += g["og"] == "到A2Boss"
R["scaling_at_act2_start_vs_reach"] = dict(sc)
R["act2_start_stats"] = {og: {"hpf": round(mean(d0["hp"] / d0["max_hp"] for g, l, c, d0 in a2 if g["og"] == og), 3),
                              "max_hp": round(mean(d0["max_hp"] for g, l, c, d0 in a2 if g["og"] == og), 1),
                              "deck_n": round(mean(len(d0["deck"]) for g, l, c, d0 in a2 if g["og"] == og), 1),
                              "relics": round(mean(len(d0["relics"]) for g, l, c, d0 in a2 if g["og"] == og), 2),
                              "gold": round(mean(d0["gold"] for g, l, c, d0 in a2 if g["og"] == og), 1),
                              "potions": round(mean(sum(1 for p in d0["potions"] if p) for g, l, c, d0 in a2 if g["og"] == og), 2),
                              "n": sum(1 for g, *_ in a2 if g["og"] == og)}
                         for og in ("A2途中死亡", "到A2Boss")}
# a1 boss outcome vs deck at boss entry
a1b = C.Counter()
for g in GAMES:
    rows = [d for d in DEC[g["g"]] if d["node"] == "Boss" and d["act"] == 1]
    if not rows:
        continue
    lab, cnt = classify_deck(rows[0]["deck"])
    lab = "mixed" if lab.startswith("mixed") else lab
    won = any(f["act"] == 1 and f["room"] == "Boss" and f["result"] == "won" for f in g["fights"])
    a1b[lab + "_n"] += 1
    a1b[lab + "_won"] += won
R["a1_boss_by_arch_at_entry"] = dict(a1b)

# 4. travel type preference
TR = re.compile(r"Travel:(\w+)@")
pref = C.defaultdict(lambda: [0, 0.0, 0])  # chosen, uniform expected, decisions where offered
for ds in DEC.values():
    for d in ds:
        if d["kind"] != "Travel":
            continue
        opts = [(i, TR.match(c).group(1)) for i, c in enumerate(d["cands"]) if c.startswith("Travel:")]
        types = [t for i, t in opts]
        if len(set(types)) < 2:
            continue
        ch = [t for i, t in opts if i == d["a"]][0]
        for t in set(types):
            pref[t][1] += types.count(t) / len(types)
            pref[t][2] += 1
        pref[ch][0] += 1
R["travel_type_pref"] = {t: [v[0], round(v[1], 1), v[2]] for t, v in pref.items()}
# forced elites: all options elite
forced = C.Counter()
for ds in DEC.values():
    for d in ds:
        if d["kind"] == "Travel" and d["chosen"].startswith("Travel:Elite"):
            types = {TR.match(c).group(1) for c in d["cands"] if c.startswith("Travel:")}
            forced["elite_travel"] += 1
            forced["all_options_elite"] += types == {"Elite"}
R["elite_travel_forced"] = dict(forced)
# elites from Unknown rooms?
R["elite_fights_by_node"] = dict(C.Counter())
efn = C.Counter()
for k, ds in DEC.items():
    seen = set()
    for d in ds:
        if "combat" in d and d["node"] in ("Elite", "Unknown"):
            key = (d["act"], d["floor"])
            if key in seen:
                continue
            seen.add(key)
            ids = [e["id"] for e in d["combat"]["enemies"]]
            efn[d["node"]] += 1
R["combat_rooms_by_node"] = dict(efn)

# 5. shop removal availability
sh = C.Counter()
for ds in DEC.values():
    for d in ds:
        if d["phase"] == "Shop" and d["dk"] == "Shop":
            rm = [c for c in d["cands"] if c.startswith("Remove:")]
            if rm:
                sh["decisions_with_remove_row"] += 1
                sh["remove_mass_sum"] += sum(p for c, p in zip(d["cands"], d["pr"]) if c.startswith("Remove:"))
                if any(c.startswith("Remove:ASCENDERS_BANE") or base(c.split(":")[1].split("$")[0]) in ("CLUMSY", "INJURY", "SHAME", "DECAY", "REGRET", "DEBT", "NORMALITY", "GREED", "DOUBT", "CURSE_OF_THE_BELL", "POOR_SLEEP", "BAD_LUCK", "MALAISE_X") for c in rm):
                    sh["with_curse_removable"] += 1
R["shop_remove"] = {k: round(v, 2) for k, v in sh.items()}

# 6. Flick Flack manual plays: enabler in hand at the same time?
ff = C.Counter()
for ds in DEC.values():
    for d in ds:
        if d["kind"] == "PlayCard" and base(d["card"]) == "FLICK_FLACK":
            hand = [base(h) for h in d["combat"]["hand"] if "!" not in h]
            ff["manual"] += 1
            if {"SURVIVOR", "DAGGER_THROW", "HIDDEN_DAGGERS", "PREPARED", "ACROBATICS", "CALCULATED_GAMBLE"} & set(hand):
                ff["enabler_playable_in_hand"] += 1
R["flick_flack_manual"] = dict(ff)

# 7. A2 boss detail
detail = []
for g in GAMES:
    if g["og"] != "到A2Boss":
        continue
    rows = [d for d in DEC[g["g"]] if d["node"] == "Boss" and d["act"] == 2 and "combat" in d]
    turns = {}
    for d in rows:
        t = d["combat"]["turn"]
        if t not in turns:
            en = [e for e in d["combat"]["enemies"] if e["max_hp"] < 100000]
            turns[t] = (d["hp"], sum(e["hp"] for e in en), sum(e["max_hp"] for e in en), d["combat"]["incoming"],
                        [e["id"] for e in en], [x for e in en for x in e.get("powers", [])][:6])
    plays = C.Counter(base(d["card"]) for d in rows if d["kind"] == "PlayCard")
    pots = [d["chosen"] for d in rows if d["kind"] == "UsePotion"]
    detail.append({"g": g["g"], "boss": g["boss_entry"][-1]["boss"] if g["act"] == 2 else [b for b in g["boss_entry"] if b["act"] == 2][0]["boss"],
                   "won": g["act"] == 3, "turns": turns, "top_plays": plays.most_common(6), "pots": pots,
                   "deck_n": len(rows[0]["deck"]),
                   "poison_cards": sum(group(base(c)) == "poison" for c in rows[0]["deck"]),
                   "scaling": sorted({base(c) for c in rows[0]["deck"]} & SCALING_POWERS),
                   "last_hand": rows[-1]["combat"]["hand"], "last_powers": rows[-1]["combat"].get("powers")})
R["a2_boss"] = detail
# 8. non-boss damage: hp lost per fight by room type, by act
dm = C.defaultdict(list)
for g in GAMES:
    for f in g["fights"]:
        dm[(f["act"], f["room"])].append((f["hp_before"] - f["hp_after"]) / f["max_hp_before"])
R["hp_loss_frac_per_fight"] = {f"a{a}_{r}": (round(mean(v), 3), len(v)) for (a, r), v in sorted(dm.items())}
# 9. HP at act-1 boss entry by outcome, max-hp, A2 death floor distribution
R["a2_death_act_floor"] = dict(C.Counter(g["floor"] - 17 for g in GAMES if g["og"] == "A2途中死亡"))
R["a2_death_room"] = dict(C.Counter(g["death"]["room_type"] for g in GAMES if g["og"] == "A2途中死亡"))
# 10. card picks: deck size growth; skip by deck size
sk = C.defaultdict(lambda: [0, 0])
for ds in DEC.values():
    for d in ds:
        if d["phase"] == "Rewards" and any(c.startswith("TakeCard:") for c in d["cands"]) and (d["chosen"] == "ProceedRewards" or d["chosen"].startswith("TakeCard:")):
            b = len(d["deck"]) // 5 * 5
            sk[b][1] += 1
            sk[b][0] += d["chosen"] == "ProceedRewards"
R["skip_by_deck_size"] = {k: v for k, v in sorted(sk.items())}
# skip when proceed has what probability; skip when all offered are curses?
# 11. potion use timing within combat: turn of use
pt = C.Counter()
for ds in DEC.values():
    for d in ds:
        if d["kind"] == "UsePotion" and "combat" in d:
            pt[min(d["combat"]["turn"], 6)] += 1
R["potion_use_turn"] = dict(pt)
json.dump(R, open(OUT, "w"), ensure_ascii=False, indent=1)
for k, v in R.items():
    print(k, "=", json.dumps(v, ensure_ascii=False)[:3000])
