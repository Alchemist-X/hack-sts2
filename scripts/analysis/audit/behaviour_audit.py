#!/usr/bin/env python3
"""Behaviour audit of pinned-0005064 on silent-a10-dev-v1 (stdlib only).

usage: python3 -I behaviour_audit.py <audit_dir> <repo_root> <out.json>
  audit_dir = dir with decisions.jsonl / games.jsonl
  repo_root = hack-sts2 checkout (card metadata: knowledge/source/v0.111.0/cards.jsonl,
              sim/specs/inventory/cards-silent-defect.classified.json)
Prints tables; writes all aggregates to out.json.
"""
import collections as C
import json
import re
import sys

AUD, REPO, OUT = sys.argv[1], sys.argv[2], sys.argv[3]
R = {}  # results

# ---------------------------------------------------------------- card metadata
META = {}
for l in open(f"{REPO}/knowledge/source/v0.111.0/cards.jsonl"):
    d = json.loads(l)
    bc = d.get("source", {}).get("base_constructor") or ""
    m = re.match(r"\s*(-?\d+),\s*CardType\.(\w+),\s*CardRarity\.(\w+)", bc)
    META[d["id"]] = {"cost": int(m.group(1)) if m else None, "type": m.group(2) if m else None,
                     "rarity": m.group(3) if m else None, "zh": d["localized"].get("zhs", {}).get("title")}
POOL = {e["id"]: e["pool"] for e in json.load(open(f"{REPO}/sim/specs/inventory/cards-silent-defect.classified.json"))["entries"]}

GROUPS = {
    "starter": ["STRIKE_SILENT", "DEFEND_SILENT", "NEUTRALIZE", "SURVIVOR"],
    "shiv": ["ACCURACY", "BLADE_DANCE", "BLADE_OF_INK", "BLADE_SYMPHONY", "CLOAK_AND_DAGGER", "FAN_OF_KNIVES",
             "FINISHER", "HIDDEN_DAGGERS", "INFINITE_BLADES", "KNIFE_TRAP", "LEADING_STRIKE", "PHANTOM_BLADES",
             "STORM_OF_STEEL", "UP_MY_SLEEVE"],
    "poison": ["ACCELERANT", "BOUNCING_FLASK", "BUBBLE_BUBBLE", "CORROSIVE_WAVE", "DEADLY_POISON", "ENVENOM", "HAZE",
               "MIRAGE", "NOXIOUS_FUMES", "OUTBREAK", "POISONED_STAB", "SNAKEBITE"],
    "discard_sly": ["ABRASIVE", "ACROBATICS", "CALCULATED_GAMBLE", "DAGGER_THROW", "FLICK_FLACK", "HAND_TRICK",
                    "MASTER_PLANNER", "MEMENTO_MORI", "PREPARED", "REFLEX", "RICOCHET", "SHADOW_STEP", "TACTICIAN",
                    "TOOLS_OF_THE_TRADE", "UNTOUCHABLE"],
    "block": ["AFTERIMAGE", "ANTICIPATE", "BACKFLIP", "BLUR", "DEFLECT", "DODGE_AND_ROLL", "ESCAPE_PLAN", "FOOTWORK",
              "LEG_SWEEP", "MALAISE", "PIERCING_WAIL", "SHADOWMELD", "WELL_LAID_PLANS", "WRAITH_FORM"],
    "attack_generic": ["ASSASSINATE", "BACKSTAB", "DAGGER_SPRAY", "DASH", "ECHOING_SLASH", "FLECHETTES", "GRAND_FINALE",
                       "MURDER", "PINPOINT", "POUNCE", "PRECISE_CUT", "PREDATOR", "SERPENT_FORM", "SKEWER", "SLICE",
                       "STRANGLE", "SUCKER_PUNCH", "SUPPRESS", "THE_HUNT", "TRACKING"],
    "utility": ["ADRENALINE", "BULLET_TIME", "BURST", "EXPERTISE", "EXPOSE", "NIGHTMARE", "SIDESTEP"],
}
G_OF = {c: g for g, cs in GROUPS.items() for c in cs}
ARCH = ["shiv", "poison", "discard_sly", "block"]
SCALING_POWERS = {"NOXIOUS_FUMES", "INFINITE_BLADES", "ACCURACY", "AFTERIMAGE", "SERPENT_FORM", "FOOTWORK", "ENVENOM",
                  "ACCELERANT", "WRAITH_FORM", "TOOLS_OF_THE_TRADE", "PHANTOM_BLADES", "FAN_OF_KNIVES",
                  "MASTER_PLANNER", "WELL_LAID_PLANS", "TRACKING", "ABRASIVE"}
SLY_STATIC = {"ABRASIVE", "FLICK_FLACK", "REFLEX", "RICOCHET", "SNEAKY", "TACTICIAN", "UNTOUCHABLE"}
DISCARD_SITES = {"d73a63f5": "SURVIVOR", "b9f97c4c": "DAGGER_THROW", "2f54bd40": "HIDDEN_DAGGERS",
                 "7370bfce": "PREPARED", "66cc3de6": "ACROBATICS", "0fb8cdd7": "P:GAMBLERS_BREW",
                 "f9858a16": "turn-start (TOOLS_OF_THE_TRADE?)"}


def base(s):
    """card string -> bare id (strip markers, upgrades, #k)."""
    return s.split("#")[0].rstrip("!*").rstrip("+")


def group(cid):
    if cid in G_OF:
        return G_OF[cid]
    t = META.get(cid, {}).get("type")
    if t in ("Curse", "Status"):
        return "curse_status"
    if POOL.get(cid) == "SILENT":
        return "silent_other"
    return "non_silent"


def ctype(cid):
    t = META.get(cid, {}).get("type")
    return "Curse/Status" if t in ("Curse", "Status") else (t or "?")


def classify_deck(deck):
    cnt = C.Counter(group(base(c)) for c in deck)
    a = sorted(((cnt[k], k) for k in ARCH), reverse=True)
    (n1, k1), (n2, _) = a[0], a[1]
    if n1 >= 4 and n1 >= n2 + 2:
        lab = k1
    elif n1 >= 3:
        lab = "mixed(" + "+".join(k for n, k in a if n >= 3) + ")"
    else:
        lab = "none"
    return lab, cnt


def pct(a, b):
    return f"{100 * a / b:.1f}%" if b else "-"


def mean(xs):
    xs = list(xs)
    return sum(xs) / len(xs) if xs else float("nan")


def med(xs):
    xs = sorted(xs)
    return xs[len(xs) // 2] if xs else float("nan")


# ---------------------------------------------------------------- load
GAMES = [json.loads(l) for l in open(f"{AUD}/games.jsonl")]
GBY = {g["g"]: g for g in GAMES}
DEC = C.defaultdict(list)
for l in open(f"{AUD}/decisions.jsonl"):
    d = json.loads(l)
    DEC[d["g"]].append(d)
for k in DEC:
    DEC[k].sort(key=lambda d: d["t"])


def outcome_group(g):
    acts2boss = any(b["act"] == 2 for b in g["boss_entry"])
    if g["act"] == 1:
        return "A1死亡"
    if acts2boss:
        return "到A2Boss"
    return "A2途中死亡"


for g in GAMES:
    g["og"] = outcome_group(g)
OG = ["A1死亡", "A2途中死亡", "到A2Boss"]
R["outcome_groups"] = dict(C.Counter(g["og"] for g in GAMES))

# ---------------------------------------------------------------- Q1 archetypes
arch_rows = []
fam_total = C.Counter()
for g in GAMES:
    lab, cnt = classify_deck(g["final_deck"])
    g["arch"] = lab
    g["cnt"] = cnt
    g["scaling"] = sorted({base(c) for c in g["final_deck"]} & SCALING_POWERS)
    g["n_power"] = sum(1 for c in g["final_deck"] if META.get(base(c), {}).get("type") == "Power")
    for k, v in cnt.items():
        fam_total[k] += v
arch_tab = C.defaultdict(lambda: C.Counter())
for g in GAMES:
    a = g["arch"] if not g["arch"].startswith("mixed") else "mixed"
    arch_tab[a][g["og"]] += 1
    arch_tab[a]["n"] += 1
    arch_tab[a]["floor_sum"] += g["floor"]
    arch_tab[a]["prog_sum"] += g["progress"]
R["arch_table"] = {k: dict(v) for k, v in arch_tab.items()}
R["arch_labels"] = dict(C.Counter(g["arch"] for g in GAMES))
R["family_cards_in_final_decks"] = dict(fam_total)
# per outcome group mean counts per family
fam_by_og = {}
for og in OG:
    gs = [g for g in GAMES if g["og"] == og]
    fam_by_og[og] = {k: round(mean(g["cnt"][k] for g in gs), 2) for k in
                     ["starter", "shiv", "poison", "discard_sly", "block", "attack_generic", "utility",
                      "silent_other", "non_silent", "curse_status"]}
    fam_by_og[og]["deck_n"] = round(mean(len(g["final_deck"]) for g in gs), 1)
    fam_by_og[og]["has_scaling_power%"] = round(100 * mean(1 if g["scaling"] else 0 for g in gs), 1)
    fam_by_og[og]["n"] = len(gs)
R["family_by_outcome"] = fam_by_og
# key-card presence vs outcome
key_cards = ["NOXIOUS_FUMES", "BOUNCING_FLASK", "DEADLY_POISON", "INFINITE_BLADES", "BLADE_DANCE", "CLOAK_AND_DAGGER",
             "ACCURACY", "FINISHER", "AFTERIMAGE", "FOOTWORK", "LEG_SWEEP", "CALCULATED_GAMBLE", "TACTICIAN", "REFLEX",
             "FLICK_FLACK", "SERPENT_FORM", "WRAITH_FORM", "ADRENALINE", "BACKSTAB", "PREDATOR"]
kc = {}
for c in key_cards:
    has = [g for g in GAMES if any(base(x) == c for x in g["final_deck"])]
    kc[c] = {"games": len(has), "reach_A2boss": sum(g["og"] == "到A2Boss" for g in has),
             "mean_floor": round(mean(g["floor"] for g in has), 1) if has else None}
R["key_cards"] = kc
# deck-poison count vs outcome
R["poison_cards_dist"] = {og: dict(C.Counter(min(g["cnt"]["poison"], 6) for g in GAMES if g["og"] == og)) for og in OG}

# card rewards
offer = C.Counter()
take = C.Counter()
fam_off = C.Counter()
fam_take = C.Counter()
screens = 0
skips = 0
skips_act = C.Counter()
screens_act = C.Counter()
alt_taken = C.Counter()
multi = 0
for k, ds in DEC.items():
    for d in ds:
        tc = [c for c in d["cands"] if c.startswith("TakeCard:")]
        if not tc or d["phase"] != "Rewards":
            continue
        ch = d["chosen"]
        if not (ch.startswith("TakeCard:") or ch == "ProceedRewards" or ch.startswith("TakeAlt:")):
            continue
        if len(tc) > 4:
            multi += 1
        screens += 1
        screens_act[d["act"]] += 1
        for c in tc:
            cid = base(c.split(":", 1)[1])
            offer[cid] += 1
            fam_off[group(cid)] += 1
        if ch == "ProceedRewards":
            skips += 1
            skips_act[d["act"]] += 1
        elif ch.startswith("TakeAlt:"):
            alt_taken[ch] += 1
        else:
            cid = base(ch.split(":", 1)[1])
            take[cid] += 1
            fam_take[group(cid)] += 1
R["card_reward"] = {"resolutions": screens, "skip": skips, "alt": dict(alt_taken), "multi_reward_rows": multi,
                    "skip_by_act": {a: [skips_act[a], screens_act[a]] for a in sorted(screens_act)},
                    "family": {f: [fam_take[f], fam_off[f]] for f in fam_off},
                    "top_taken": take.most_common(25),
                    "most_offered_low_pick": sorted(((c, take[c], n) for c, n in offer.items() if n >= 15),
                                                    key=lambda x: x[1] / x[2])[:15],
                    "most_offered_high_pick": sorted(((c, take[c], n) for c, n in offer.items() if n >= 15),
                                                     key=lambda x: -x[1] / x[2])[:20]}
# ? granular screens / CardReward prompts
R["card_reward"]["prompt_cardreward"] = sum(1 for ds in DEC.values() for d in ds if d.get("prompt", {}).get("kind") == "CardReward")

# ---------------------------------------------------------------- Q2 discard
en_play = C.Counter()
en_avail = C.Counter()
pay_play = C.Counter()
sly_manual = C.Counter()
for ds in DEC.values():
    for d in ds:
        if d["dk"] == "Combat" and d["kind"] == "PlayCard":
            if d.get("discard_enabler"):
                en_play[base(d["card"])] += 1
            if d.get("discard_payoff"):
                pay_play[base(d["card"])] += 1
            if d.get("sly"):
                sly_manual[base(d["card"])] += 1
R["discard_enabler_plays"] = dict(en_play)
R["discard_payoff_plays"] = dict(pay_play)
R["sly_manual_plays"] = dict(sly_manual)

# enabler choice conditioned on Sly in hand
cond = C.Counter()
for ds in DEC.values():
    for d in ds:
        if d["dk"] != "Combat" or "combat" not in d:
            continue
        en_rows = [i for i, c in enumerate(d["cands"]) if c.startswith("Play:") and base(c[5:].split(">")[0]) in
                   ("SURVIVOR", "DAGGER_THROW", "HIDDEN_DAGGERS", "PREPARED", "ACROBATICS", "CALCULATED_GAMBLE")]
        if not en_rows:
            continue
        hand = d["combat"]["hand"]
        has_sly = any("*" in h for h in hand)
        has_junk = any(ctype(base(h)) == "Curse/Status" for h in hand)
        key = ("sly" if has_sly else "nosly") + ("_junk" if has_junk else "")
        cond[key + "_n"] += 1
        if d["a"] in en_rows:
            cond[key + "_chose_enabler"] += 1
        cond[key + "_mass"] += 0  # placeholder
R["enabler_choice_given_hand"] = dict(cond)

# discard picks
picks = []
for ds in DEC.values():
    for d in ds:
        p = d.get("prompt")
        if not p or p["site"] not in DISCARD_SITES or d["kind"] != "PromptSelect":
            continue
        hand = d["combat"]["hand"] if "combat" in d else []
        opts = p["options"]
        aligned = [base(h) for h in hand] == [base(o) for o in opts]
        unpicked = [i for i, o in enumerate(opts) if "#" not in o]
        sel_rows = [i for i, c in enumerate(d["cands"]) if c.startswith("Select:")]
        if len(sel_rows) != len(unpicked):
            continue
        oi = unpicked[sel_rows.index(d["a"])]

        def info(i):
            cid = base(opts[i])
            sly = ("*" in hand[i]) if aligned else (cid in SLY_STATIC)
            unpl = ("!" in hand[i]) if aligned else None
            return {"cid": cid, "t": ctype(cid), "sly": sly, "unpl": unpl,
                    "cost": META.get(cid, {}).get("cost"), "up": opts[i].split("#")[0].rstrip("*").endswith("+")}
        av = [info(i) for i in unpicked]
        ch = info(oi)
        picks.append({"src": DISCARD_SITES[p["site"]], "after": p["after"], "ch": ch, "av": av, "g": d["g"], "t": d["t"],
                      "first": p["picked"] == 0, "energy": d["combat"]["energy"] if "combat" in d else None})
R["discard_picks_n"] = len(picks)
R["discard_picks_by_src"] = dict(C.Counter(x["src"] for x in picks))


def cat(x):
    if x["t"] == "Curse/Status":
        return "诅咒/状态"
    if x["sly"]:
        return "奇巧(Sly)"
    return {"Attack": "攻击", "Skill": "技能", "Power": "能力"}.get(x["t"], x["t"])


R["discard_chosen_cat"] = dict(C.Counter(cat(x["ch"]) for x in picks))
# uniform baseline share per category
base_share = C.Counter()
for x in picks:
    n = len(x["av"])
    for a in x["av"]:
        base_share[cat(a)] += 1 / n
R["discard_uniform_baseline"] = {k: round(v, 1) for k, v in base_share.items()}
R["discard_top_targets"] = C.Counter(x["ch"]["cid"] + ("+" if x["ch"]["up"] else "") for x in picks).most_common(15)
# conditional
condd = {}
for name, pred in [("curse_status", lambda a: a["t"] == "Curse/Status"), ("sly", lambda a: a["sly"]),
                   ("unplayable", lambda a: a["unpl"] is True)]:
    sub = [x for x in picks if any(pred(a) for a in x["av"]) and len(x["av"]) > 1]
    hit = sum(1 for x in sub if pred(x["ch"]))
    bl = sum(sum(1 for a in x["av"] if pred(a)) / len(x["av"]) for x in sub)
    condd[name] = {"n": len(sub), "chose": hit, "uniform_expect": round(bl, 1)}
# upgraded vs non-upgraded same card when both available
R["discard_conditional"] = condd
# Sly by card: discarded vs available
sly_disc = C.Counter(x["ch"]["cid"] for x in picks if x["ch"]["sly"])
sly_av = C.Counter()
for x in picks:
    for cid in {a["cid"] for a in x["av"] if a["sly"]}:
        sly_av[cid] += 1
R["sly_discarded_vs_available"] = {c: [sly_disc[c], sly_av[c]] for c in sly_av}
# no-junk, no-sly picks: chose higher-cost / attack vs skill / basic?
plain = [x for x in picks if not any(a["t"] == "Curse/Status" or a["sly"] for a in x["av"]) and len({a["cid"] for a in x["av"]}) > 1]
R["discard_plain"] = {"n": len(plain),
                      "chose_strike_or_defend": sum(x["ch"]["cid"] in ("STRIKE_SILENT", "DEFEND_SILENT") for x in plain),
                      "uniform_expect_strike_defend": round(sum(sum(a["cid"] in ("STRIKE_SILENT", "DEFEND_SILENT") for a in x["av"]) / len(x["av"]) for x in plain), 1),
                      "chose_max_cost": sum(x["ch"]["cost"] == max(a["cost"] or 0 for a in x["av"]) for x in plain),
                      "chose_neutralize": sum(x["ch"]["cid"] == "NEUTRALIZE" for x in plain),
                      "neutralize_avail": sum(any(a["cid"] == "NEUTRALIZE" for a in x["av"]) for x in plain),
                      "chose_upgraded_when_dupe_unupgraded": 0}
# strike vs defend preference when both present and incoming > block
sd = C.Counter()
for x in plain:
    ids = {a["cid"] for a in x["av"]}
    if {"STRIKE_SILENT", "DEFEND_SILENT"} <= ids and x["ch"]["cid"] in ("STRIKE_SILENT", "DEFEND_SILENT"):
        sd[x["ch"]["cid"]] += 1
R["discard_strike_vs_defend_when_both"] = dict(sd)
# illustrative sly discards
R["sly_examples"] = [(x["g"], x["t"], x["src"], x["ch"]["cid"], [a["cid"] + ("*" if a["sly"] else "") for a in x["av"]])
                     for x in picks if x["ch"]["sly"]][:5]
R["junk_skip_examples"] = [(x["g"], x["t"], x["src"], x["ch"]["cid"], [a["cid"] + ("*" if a["sly"] else "") for a in x["av"]])
                           for x in picks if any(a["t"] == "Curse/Status" for a in x["av"]) and x["ch"]["t"] != "Curse/Status"][:5]
# Hand Trick prompt: which skill got Sly
ht = C.Counter()
for ds in DEC.values():
    for d in ds:
        p = d.get("prompt")
        if p and p["site"] == "0f3a2f52" and d["kind"] == "PromptSelect":
            ht[base(d["chosen"][7:])] += 1
R["hand_trick_targets"] = ht.most_common(8)
# discard-enabler card in hand but not played over a combat? (opportunity) -- plays per draw approx skipped

# ---------------------------------------------------------------- Q3 path / elites / rest / shop
TR = re.compile(r"Travel:(\w+)@r(\d+)c(\d+)\[e(\d+)-(\d+),rest(\d+),shop(\d+)\]")
elite_choice = C.Counter()
route_choice = C.Counter()
first_travel = {}  # (g,act) -> (emin,emax)
visits = C.defaultdict(C.Counter)  # (g,act) -> node type counts
for k, ds in DEC.items():
    for d in ds:
        if d["kind"] != "Travel":
            continue
        opts = [(i, TR.match(c)) for i, c in enumerate(d["cands"]) if c.startswith("Travel:")]
        opts = [(i, m) for i, m in opts if m]
        ch = [m for i, m in opts if i == d["a"]][0]
        visits[(k, d["act"])][ch.group(1)] += 1
        if (k, d["act"]) not in first_travel:
            first_travel[(k, d["act"])] = (int(ch.group(4)), int(ch.group(5)), len(opts))
        if len(opts) < 2:
            continue
        types = [m.group(1) for i, m in opts]
        if "Elite" in types and any(t != "Elite" for t in types):
            elite_choice["n"] += 1
            elite_choice["chose_elite"] += ch.group(1) == "Elite"
            elite_choice["uniform"] += types.count("Elite") / len(types)
        emaxs = [int(m.group(5)) for i, m in opts]
        emins = [int(m.group(4)) for i, m in opts]
        if len(set(emaxs)) > 1:
            route_choice["n_emax"] += 1
            route_choice["chose_min_emax"] += int(ch.group(5)) == min(emaxs)
            route_choice["chose_max_emax"] += int(ch.group(5)) == max(emaxs)
            route_choice["uniform_min"] += emaxs.count(min(emaxs)) / len(emaxs)
            route_choice["uniform_max"] += emaxs.count(max(emaxs)) / len(emaxs)
        if len(set(emins)) > 1:
            route_choice["n_emin"] += 1
            route_choice["chose_min_emin"] += int(ch.group(4)) == min(emins)
            route_choice["uniform_min_emin"] += emins.count(min(emins)) / len(emins)
        rests = [int(m.group(6)) for i, m in opts]
        if len(set(rests)) > 1:
            route_choice["n_rest"] += 1
            route_choice["chose_max_rest"] += int(ch.group(6)) == max(rests)
            route_choice["uniform_max_rest"] += rests.count(max(rests)) / len(rests)
        shops = [int(m.group(7)) for i, m in opts]
        if len(set(shops)) > 1:
            route_choice["n_shop"] += 1
            route_choice["chose_max_shop"] += int(ch.group(7)) == max(shops)
            route_choice["uniform_max_shop"] += shops.count(max(shops)) / len(shops)
R["elite_vs_nonelite_choice"] = {k: round(v, 1) for k, v in elite_choice.items()}
R["route_choice"] = {k: round(v, 1) for k, v in route_choice.items()}

# per act elites fought vs available (acts fully played = boss entered)
per_act = C.defaultdict(list)
for g in GAMES:
    for act in (1, 2):
        ft = first_travel.get((g["g"], act))
        if not ft:
            continue
        completed = any(b["act"] == act for b in g["boss_entry"])
        el = [f for f in g["fights"] if f["act"] == act and f["room"] == "Elite"]
        per_act[act].append({"g": g["g"], "og": g["og"], "emin": ft[0], "emax": ft[1], "fought": len(el),
                             "won": sum(f["result"] == "won" for f in el), "completed": completed,
                             "visits": dict(visits[(g["g"], act)])})
R["elites_per_act"] = {}
for act, rows in per_act.items():
    comp = [r for r in rows if r["completed"]]
    R["elites_per_act"][act] = {
        "games": len(rows), "completed": len(comp),
        "fought_mean_completed": round(mean(r["fought"] for r in comp), 2),
        "emin_mean": round(mean(r["emin"] for r in comp), 2), "emax_mean": round(mean(r["emax"] for r in comp), 2),
        "at_min": sum(r["fought"] == r["emin"] for r in comp), "at_max": sum(r["fought"] == r["emax"] for r in comp),
        "fought_dist": dict(C.Counter(r["fought"] for r in comp)),
        "emax_dist": dict(C.Counter(r["emax"] for r in comp)),
        "fought_all_mean": round(mean(r["fought"] for r in rows), 2),
        "visits_mean": {t: round(mean(r["visits"].get(t, 0) for r in comp), 2)
                        for t in ("Monster", "Elite", "Unknown", "RestSite", "Shop", "Treasure")},
    }
# by outcome group, act 1 and act 2 stats
R["path_by_outcome"] = {}
for og in OG:
    r1 = [r for r in per_act[1] if r["og"] == og and r["completed"]]
    r2 = [r for r in per_act[2] if r["og"] == og]
    R["path_by_outcome"][og] = {
        "a1_n": len(r1), "a1_elites": round(mean(r["fought"] for r in r1), 2) if r1 else None,
        "a1_emax": round(mean(r["emax"] for r in r1), 2) if r1 else None,
        "a1_visits": {t: round(mean(r["visits"].get(t, 0) for r in r1), 2) for t in ("Monster", "Elite", "Unknown", "RestSite", "Shop", "Treasure")} if r1 else None,
        "a2_n": len(r2), "a2_elites": round(mean(r["fought"] for r in r2), 2) if r2 else None,
        "a2_elite_won": round(mean(r["won"] for r in r2), 2) if r2 else None,
        "a2_emin": round(mean(r["emin"] for r in r2), 2) if r2 else None,
        "a2_emax": round(mean(r["emax"] for r in r2), 2) if r2 else None,
    }

# rest sites
rest = []
for k, ds in DEC.items():
    for d in ds:
        if d["kind"] == "ChooseRestOption":
            ch = d["chosen"]
            opt = "HEAL" if ch == "Rest:HEAL" else ("SMITH" if ch.startswith("Rest:SMITH") else ch)
            rest.append({"g": k, "act": d["act"], "hpf": d["hp"] / d["max_hp"], "opt": opt,
                         "card": ch.split(":", 2)[2] if opt == "SMITH" else None,
                         "avail": sorted({c.split(":")[1] for c in d["cands"] if c.startswith("Rest:")}),
                         "p": d["p"], "og": GBY[k]["og"], "floor": d["act_floor"]})
R["rest_options_available"] = dict(C.Counter(o for r in rest for o in r["avail"]))
R["rest_by_act"] = {a: dict(C.Counter(r["opt"] for r in rest if r["act"] == a)) for a in (1, 2, 3)}
bins = [(0, .4), (.4, .6), (.6, .8), (.8, 1.01)]
R["rest_by_hp"] = {f"{lo:.1f}-{min(hi,1):.1f}": dict(C.Counter(r["opt"] for r in rest if lo <= r["hpf"] < hi)) for lo, hi in bins}
R["rest_by_og"] = {og: dict(C.Counter(r["opt"] for r in rest if r["og"] == og)) for og in OG}
# smith targets
smith = [r for r in rest if r["opt"] == "SMITH"]
R["smith_targets"] = C.Counter(base(r["card"]) for r in smith).most_common(20)
first_smith = {}
for r in smith:
    first_smith.setdefault(r["g"], base(r["card"]))
R["first_smith"] = C.Counter(first_smith.values()).most_common(12)
# upgraded already? (card string has +)
R["smith_on_already_upgraded"] = sum(1 for r in smith if r["card"].rstrip("*").endswith("++"))
R["upgraded_final"] = {og: round(mean(g["upgraded"] for g in GAMES if g["og"] == og), 2) for og in OG}
# heal at high hp
R["heal_at_hp_ge_0.8"] = sum(1 for r in rest if r["opt"] == "HEAL" and r["hpf"] >= .8)
R["smith_at_hp_lt_0.4"] = sum(1 for r in rest if r["opt"] == "SMITH" and r["hpf"] < .4)
R["rest_hpf_mean"] = {o: round(mean(r["hpf"] for r in rest if r["opt"] == o), 3) for o in ("HEAL", "SMITH")}

# shop
shop = C.defaultdict(C.Counter)
for k, ds in DEC.items():
    og = GBY[k]["og"]
    vis = set()
    for d in ds:
        if d["phase"] == "Shop" and d["dk"] == "Shop":
            vis.add((d["act"], d["floor"]))
            if d["kind"] == "BuyEntry":
                ch = d["chosen"]
                if ch.startswith("Remove:"):
                    shop[og]["remove"] += 1
                else:
                    shop[og]["buy_" + ch.split(":")[1]] += 1
                m = re.search(r"\$(\d+)", ch)
                shop[og]["gold_spent"] += int(m.group(1)) if m else 0
    shop[og]["visits"] += len(vis)
    shop[og]["games"] += 1
R["shop_by_og"] = {og: dict(v) for og, v in shop.items()}
R["final_gold_by_og"] = {og: round(mean(g["final_gold"] for g in GAMES if g["og"] == og), 1) for og in OG}
R["relics_by_og"] = {og: round(mean(len(g["final_relics"]) for g in GAMES if g["og"] == og), 2) for og in OG}
# relics at end of act 1 (first act-2 decision)
rel_a1 = {}
for k, ds in DEC.items():
    for d in ds:
        if d["act"] == 2:
            rel_a1[k] = len(d["relics"])
            break
R["relics_at_act2_start_by_og"] = {og: round(mean(rel_a1[g["g"]] for g in GAMES if g["og"] == og and g["g"] in rel_a1), 2) for og in OG[1:]}
bought_items = C.Counter()
for ds in DEC.values():
    for d in ds:
        if d["kind"] == "BuyEntry":
            bought_items[re.sub(r"\$\d+", "", d["chosen"])] += 1
R["shop_top_buys"] = bought_items.most_common(15)
# treasure skip
tskip = C.Counter()
for ds in DEC.values():
    for d in ds:
        if d["kind"] == "SkipTreasureRelic":
            tskip[[c for c in d["cands"] if c.startswith("TakeRelic:")][0][10:]] += 1
R["treasure_skipped_relics"] = tskip.most_common()
R["treasure_take"] = sum(1 for ds in DEC.values() for d in ds if d["kind"] == "TakeTreasureRelic")
# relic claims in rewards (elite/boss relic) skipped?
rel_off = C.Counter()
for ds in DEC.values():
    for i, d in enumerate(ds):
        if d["phase"] == "Rewards" and any(c.startswith("Claim:Relic:") for c in d["cands"]):
            rel_off["screens"] += 1
            if d["chosen"] == "ProceedRewards":
                rel_off["left_unclaimed"] += 1
R["reward_relic"] = dict(rel_off)

# ---------------------------------------------------------------- potions
pot = C.Counter()
held_at_death = []
for g in GAMES:
    held_at_death.append(sum(1 for p in g["final_potions"] if p))
for k, ds in DEC.items():
    for d in ds:
        if d["kind"] == "UsePotion":
            ctx = d["node"] if d["dk"] == "Combat" or d["phase"] == "Combat" else "非战斗"
            pot[ctx] += 1
        if d["kind"] == "DiscardPotion":
            pot["discard"] += 1
R["potion_use_ctx"] = dict(pot)
R["potions_held_at_death"] = dict(C.Counter(held_at_death))
# potion slots full share at combat decisions
full = C.Counter()
for ds in DEC.values():
    for d in ds:
        if d["dk"] == "Combat" and d["potions"]:
            full["n"] += 1
            full["full"] += all(d["potions"])
            full["empty"] += not any(d["potions"])
R["potion_belt_state_combat"] = dict(full)
# potion reward claimed when?  claim potion rows chosen vs proceed
pr_ = C.Counter()
for ds in DEC.values():
    for d in ds:
        if d["phase"] == "Rewards":
            for c in d["cands"]:
                if c.startswith("Claim:Potion:"):
                    pr_["offered_rows"] += 1
            if d["chosen"].startswith("Claim:Potion:"):
                pr_["claimed"] += 1
R["potion_reward"] = dict(pr_)

# ---------------------------------------------------------------- Q4 bosses
boss = []
for g in GAMES:
    ds = DEC[g["g"]]
    for bf in g["boss_fights"]:
        act = bf["act"]
        rows = [d for d in ds if d["node"] == "Boss" and d["act"] == act and ("combat" in d)]
        if not rows:
            continue
        first, last = rows[0], rows[-1]
        used = [d["chosen"].split(":")[1].split(">")[0] for d in rows if d["kind"] == "UsePotion"]
        powers_played = [(d["combat"]["turn"], base(d["card"])) for d in rows if d["kind"] == "PlayCard" and META.get(base(d["card"]), {}).get("type") == "Power"]
        en = [e for e in last["combat"]["enemies"] if e["max_hp"] < 100000]
        en_hp = sum(e["hp"] for e in en)
        en_max = sum(e["max_hp"] for e in en) or 1
        boss.append({"g": g["g"], "og": g["og"], "act": act, "boss": bf["encounter"], "result": bf["result"],
                     "hp_in": bf["hp_before"], "hpf_in": bf["hp_before"] / bf["max_hp_before"],
                     "turns": last["combat"]["turn"], "pot_slots_in": sum(1 for p in first["potions"] if p),
                     "pot_used": len(used), "pot_used_ids": used,
                     "pot_left_last": sum(1 for p in last["potions"] if p),
                     "powers_played": powers_played,
                     "deck_scaling": sorted({base(c) for c in first["deck"]} & SCALING_POWERS),
                     "deck_powers": sorted({base(c) for c in first["deck"] if META.get(base(c), {}).get("type") == "Power"}),
                     "enemy_hp_left_frac": en_hp / en_max, "enemies_last": [(e["id"], e["hp"], e["max_hp"], e.get("powers")) for e in last["combat"]["enemies"]],
                     "incoming_last": last["combat"]["incoming"], "block_last": last["combat"]["block"],
                     "hp_last": last["hp"], "deck_n": len(first["deck"]), "arch": g["arch"],
                     "poison_cards": g["cnt"]["poison"]})
R["boss"] = {}
for act in (1, 2):
    for res in ("won", "lost"):
        b = [x for x in boss if x["act"] == act and x["result"] == res]
        if not b:
            continue
        R["boss"][f"a{act}_{res}"] = {
            "n": len(b), "hpf_in_mean": round(mean(x["hpf_in"] for x in b), 3), "hp_in_mean": round(mean(x["hp_in"] for x in b), 1),
            "turns_mean": round(mean(x["turns"] for x in b), 1), "turns_med": med(x["turns"] for x in b),
            "pot_in_mean": round(mean(x["pot_slots_in"] for x in b), 2), "pot_used_mean": round(mean(x["pot_used"] for x in b), 2),
            "pot_left_mean": round(mean(x["pot_left_last"] for x in b), 2),
            "died_with_potion": sum(1 for x in b if x["pot_left_last"] > 0) if res == "lost" else None,
            "deck_has_scaling%": round(100 * mean(1 if x["deck_scaling"] else 0 for x in b), 1),
            "played_a_power%": round(100 * mean(1 if x["powers_played"] else 0 for x in b), 1),
            "enemy_hp_left_mean": round(mean(x["enemy_hp_left_frac"] for x in b), 3) if res == "lost" else None,
            "by_boss": dict(C.Counter(x["boss"] for x in b)),
        }
R["a2_boss_detail"] = [{k: x[k] for k in ("g", "boss", "result", "hp_in", "turns", "pot_slots_in", "pot_used", "pot_left_last",
                                          "deck_scaling", "powers_played", "enemy_hp_left_frac", "enemies_last",
                                          "incoming_last", "block_last", "deck_n", "arch", "poison_cards")}
                       for x in boss if x["act"] == 2]
# act-1 boss winrate by boss
R["a1_boss_by_boss"] = {}
for x in boss:
    if x["act"] == 1:
        r = R["a1_boss_by_boss"].setdefault(x["boss"], [0, 0])
        r[0] += x["result"] == "won"
        r[1] += 1
# a2 boss: per-turn enemy hp & player hp trajectories for good games
traj = {}
for g in GAMES:
    if g["og"] != "到A2Boss":
        continue
    rows = [d for d in DEC[g["g"]] if d["node"] == "Boss" and d["act"] == 2 and "combat" in d]
    seq = []
    lastturn = None
    for d in rows:
        if d["combat"]["turn"] != lastturn:
            lastturn = d["combat"]["turn"]
            en = [e for e in d["combat"]["enemies"] if e["max_hp"] < 100000]
            seq.append((lastturn, d["hp"], sum(e["hp"] for e in en), d["combat"]["incoming"],
                        [p for p in d["combat"].get("powers", [])]))
    traj[g["g"]] = seq
R["a2_boss_traj"] = traj

# ---------------------------------------------------------------- misc verdict numbers
R["neow"] = C.Counter(d["chosen"] for ds in DEC.values() for d in ds if d.get("event") == "NEOW" and d["kind"] == "ChooseEventOption").most_common(10)
R["endturn_with_energy"] = {}
et = C.Counter()
for ds in DEC.values():
    for d in ds:
        if d["kind"] == "EndTurn" and "combat" in d:
            playable = [c for c in d["cands"] if c.startswith("Play:")]
            et["n"] += 1
            if d["combat"]["energy"] > 0 and playable:
                et["energy_left_and_playable"] += 1
R["endturn_with_energy"] = dict(et)
R["win_prob_first_mean"] = mean(g["win_prob_first"] for g in GAMES)
R["remove_targets"] = C.Counter(base(d["chosen"].split(":")[1].split("$")[0]) for ds in DEC.values() for d in ds if d["kind"] == "BuyEntry" and d["chosen"].startswith("Remove:")).most_common(8)
R["deckremove_prompt_targets"] = C.Counter(base(d["chosen"][7:]) for ds in DEC.values() for d in ds if d.get("prompt", {}).get("kind") == "DeckRemove" and d["kind"] == "PromptSelect").most_common(8)
R["deckupgrade_prompt_targets"] = C.Counter(base(d["chosen"][7:]) for ds in DEC.values() for d in ds if d.get("prompt", {}).get("kind") == "DeckUpgrade" and d["kind"] == "PromptSelect").most_common(8)
# low-confidence share of decisions by kind
conf = C.defaultdict(list)
for ds in DEC.values():
    for d in ds:
        if len(d["cands"]) > 1:
            conf[d["kind"]].append(d["p"])
R["mean_p_by_kind"] = {k: (round(mean(v), 3), len(v)) for k, v in conf.items()}

json.dump(R, open(OUT, "w"), ensure_ascii=False, indent=1, default=list)
print(json.dumps({k: v for k, v in R.items() if k not in ("a2_boss_detail", "a2_boss_traj")}, ensure_ascii=False, default=list)[:200000])
