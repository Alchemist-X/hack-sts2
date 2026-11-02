"""Normalise the stage-1 classified manifests onto one schema; write requirements.json and batches.json.

Input: <work>/stage1/*.classified.json (the per-kind generators), the raw manifests (--inventory) and
kernel-gaps.json (stage 1). Output (--out): the normalised *.classified.json, requirements.json,
batches.json, and the gap manifests (kernel-gaps, run-layer-gaps, subsystems, corpus-inventory)
with their `generated_by`. The schema is documented in sim/specs/inventory/README.md.
"""

import collections
import json
import os
import sys
from collections import OrderedDict

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
sys.path.insert(0, os.path.join(HERE, ".."))
import common  # noqa: E402
import catalog  # noqa: E402
import deps as D  # noqa: E402
import prompts as P  # noqa: E402
import requirements as R  # noqa: E402

SCHEMA = "sts2sim.inventory.classified/v2"
FILES = list(D.KIND_OF_FILE)
MINOR = ["enchantments", "afflictions", "orbs", "modifiers", "acts", "characters"]
GENERATORS = {
    "cards-ironclad-colorless": common.generator("cards-ironclad-colorless", "vdiff.py", "awaits.py", "build.py"),
    "cards-necrobinder-regent": common.generator("cards-necrobinder-regent", "vdelta2.py", "build.py"),
    "cards-silent-defect": common.generator("cards-silent-defect", "build.py"),
    "powers": common.generator("powers", "features.py", "vdiff.py", "build.py"),
    "relics": common.generator("relics-potions", "vdiff.py", "build.py"),
    "potions": common.generator("relics-potions", "vdiff.py", "build.py"),
    "monsters": common.generator("monsters", "extract.py", "enc_extract.py", "vdiff.py", "powerdeps.py", "gen.py"),
    "encounters": common.generator("monsters", "extract.py", "enc_extract.py", "vdiff.py", "powerdeps.py", "gen.py"),
    "events": common.generator("events", "build.py"),
}
for _k in MINOR:
    GENERATORS[_k] = common.generator("minor-kinds", "vdiff.py", "build.py", "data_%s.py" % _k)
NORMALIZER = common.generator("normalize", "normalize.py")[0]
PASS_THROUGH = OrderedDict([
    ("kernel-gaps.json", common.generator("kernel", "virtuals.py", "hooks.py", "cmds.py", "readiness.py", "annotate_cmds.py",
                                          "kindvirt.py", "prompts.py", "sites.py", "tables.py", "build_json.py")),
    ("run-layer-gaps.json", common.generator("run-layer", "build_gaps.py")),
    ("subsystems.json", common.generator("subsystems", "gen_main.py")),
    ("corpus-inventory.json", common.generator("corpus", "scan_parts.py", "scan_runs.py", "aggregate.py", "tree_diff.py",
                                               "classify_diff.py", "taint.py", "replay_stats.py", "build_inventory.py")),
])
READINESS_KIND = {"cards": "Cards", "powers": "Powers", "relics": "Relics", "potions": "Potions", "monsters": "Monsters",
                  "encounters": "Encounters", "enchantments": "Enchantments", "afflictions": "Afflictions", "orbs": "Orbs",
                  "modifiers": "Modifiers", "events": "Events"}
PARTIAL_FACADES = ("CreatureCmd.Damage", "DamageCmd.Attack", "CardCmd.Discard", "CardPileCmd.Add")
READINESS_EXCLUDE = {("powers", "ILLUSION_POWER"): {"AfterCombatEnd"}, ("powers", "SANDPIT_POWER"): {"AfterOstyRevived"}}
D9_CLOSED = {("cards", "ASCENDERS_BANE"), ("cards", "FOLLY"), ("events", "COLOSSAL_FLOWER"), ("relics", "GLASS_EYE"),
             ("relics", "GOLDEN_COMPASS")}
VD_STATUS = {"new_in_v111": "new", "refactor_only": "cosmetic", "unreviewed": "unknown"}
CARD_BATCHES = OrderedDict([
    ("0", "ported in P0: re-verify against the expanded kernel"),
    ("1", "basics on the current kernel: existing facades, every dependency ported"),
    ("2", "needs unported powers or token cards: with power batches 1-2"),
    ("3", "card-kernel facades and hooks (card listeners, CardCmd ops, clone/create, calculated vars, auto-play, Sly, attack context)"),
    ("4", "combat-history readers"),
    ("5", "player prompts (choice runtime, combat prompt kinds)"),
    ("6", "card generation from unlock-filtered pools (CardFactory + UnlockProfile)"),
    ("7", "orb core (OrbQueue, the five orbs, Channel/Evoke, Focus): Defect starters"),
    ("8", "orb advanced (evoke loops, passive counts, slots, random orbs, orb history)"),
    ("9", "character subsystems Osty, Stars, Forge"),
    ("10", "other subsystems: potions runtime, gold in combat, enchantments, monster-driven prompts"),
    ("11", "run layer and cross-cutting: deck-resident hooks, saved properties, deck persistence, combat-end rewards"),
    ("12", "multiplayer-only: unreachable in single-player pools"),
])
POWER_TO_CARD_BATCH = {1: 2, 2: 2, 3: 3, 4: 4, 5: 6, 7: 5, 8: 11}
CARD_PREREQ = {"0": [], "1": [], "2": ["1"], "3": ["1", "2"], "4": ["3"], "5": ["3"], "6": ["3"], "7": ["3"], "8": ["4", "7"],
               "9": ["3", "4", "5"], "10": ["3", "5"], "11": ["3", "5", "6", "9", "10"], "12": []}
CARD_MAP = {
    "cards-ironclad-colorless": {0: 0, 1: 1, 2: 2, 3: 3, 4: 4, 5: 5, 6: 6, 7: "split7", 8: 11, 9: 12},
    "cards-necrobinder-regent": {1: 1, 2: 2, 3: 3, 4: 4, 5: 5, 6: 6, 7: 9, 8: 11, 9: 12},
    "cards-silent-defect": {1: 1, 2: 2, 3: 3, 4: 4, 5: 5, 6: 7, 7: 8, 8: "split8", 99: 12},
}
TIER_LEGEND = OrderedDict([
    ("1", "own code needs only facades and hooks the P0 sim has, or its kind's runtime (kind_baselines)"),
    ("2", "needs game facades, hooks, hook parameters or model APIs the P0 sim lacks"),
    ("3", "needs a player prompt or a new subsystem (choice runtime, orbs, Osty, stars, forge, card generation, combat history, potions, enchantments, run layer)"),
    ("4", "unreadable, cross-cutting or test-only"),
])

SCHEMA_FIELDS = OrderedDict([
    ("kernel_requirements", "requirement-catalogue keys (requirements.json) that the entry's own code needs and the P0 sim lacks"),
    ("kernel_requirement_notes", "the classifier's free-text requirements, kept for reading (mapped to keys by normalize/catalog.py)"),
    ("dependencies", "{requires, reads, co_port}: {content kind: [id]}; requires = applied, created, summoned or offered models, "
                     "reads = only read or type-tested, co_port = definitions that call each other"),
    ("prompts", "ChoicePromptKind records (sim/CONTRACTS.md B1): kind, entry, site, opened_by, via, pile, min, max, require_confirm, "
                "can_skip, cancelable, context, filter, cite, notes; via names the applied power, spawned monster or offered relic "
                "whose own prompt this is (null for the entry's own prompts)"),
    ("phases", "run-scope decision phases the entry opens (Rewards, RestSite, EventCombat, CrystalSphere, Shop, PotionUse, AbandonRun): "
               "phase, role, entry, site, opened_by, via, detail, cite"),
    ("nested_prompts", "prompts of the cards the entry auto-plays"),
    ("version_delta_v107", "the classifier's v0.107.1 comparison: status same | cosmetic | rule-change | new | decompile-gap | unknown, "
                           "note and its evidence fields; the reconciled, triaged list is version-deltas.json"),
    ("tier", "own code only, 1-4 (tier_legend): max of the classifier's tier and the levels of kernel_requirements; test mocks 4"),
    ("tier_with_deps", "max tier over the entry and every entry it depends on (all three relations), transitively"),
    ("tier_classifier", "the classifier's own-code tier before normalisation"),
    ("tier_from_requirements", "max requirements.json level of kernel_requirements (1 when empty)"),
    ("tier_adjustments", "why tier differs from tier_classifier"),
    ("port_batch", "batch in this kind's scheme (cards: the unified card scheme port_batch_legend); the cross-kind order is batches.json"),
    ("port_batch_legacy", "cards: the slice classifier's own batch number"),
    ("card_batch_raised_by", "cards: required powers or tokens whose card batch is later than the mapped one"),
])
LEGEND_KEYS = ("fields", "field_docs", "field_notes", "field_legend")
DROPPED_FIELDS = ("tier_own", "tier_derived_from_requirements", "effective_tier", "effective_tier_from", "indirect_prompts")


def legacy_tier(fname, e):
    if fname == "cards-silent-defect":
        return e["tier_own"]
    if fname == "encounters":
        return None
    return e["tier"]


def raw_index(raw):
    return dict((k, dict((e["id"], e) for e in raw[k]["entries"])) for k in raw)


def own_keys(cat, kind, entry, fname, texts, readiness):
    keys = []
    for t in texts:
        ks = [t] if cat.key_kind(t) is not None else cat.keys_for(t)
        for k in ks:
            if k not in keys:
                keys.append(k)
    rk = READINESS_KIND.get(kind)
    rrow = readiness.get("%s/%s" % (rk, entry["class"])) if rk else None
    if rrow:
        skip = READINESS_EXCLUDE.get((kind, entry["id"]), set())
        for h in rrow.get("missing_hooks", []):
            if h not in keys and h not in skip and cat.key_kind(h) == "hook_virtual":
                keys.append(h)
        for c in rrow.get("missing_cmds", []):
            c2 = c.rstrip("~")
            if c2 not in keys and cat.key_kind(c2) == "cmd_facade":
                keys.append(c2)
    variants = rrow.get("missing_cmds", []) if rrow else []
    out = []
    for k in keys:
        if k in PARTIAL_FACADES and rrow is not None:
            vs = [v for v in variants if v.startswith(k + "(") and cat.key_kind(v) == "cmd_facade"]
            if vs:
                for v in vs:
                    if v not in out:
                        out.append(v)
                continue
            if k != "CardPileCmd.Add":
                continue
        d = cat.describe(k)
        if d["kind"] in ("cmd_facade", "hook_virtual") and d["status"] in ("ported", "strip"):
            continue
        if k not in out:
            out.append(k)
    if (kind, entry["id"]) in D9_CLOSED and "decompile:ldmembertoken" not in out:
        out.append("decompile:ldmembertoken")
    return sorted(out)


def level_of(cat, kind, key):
    if key in catalog.KIND_BASELINES.get(kind, ()):
        return 1
    if key in catalog.KIND_LEVELS.get(kind, {}):
        return catalog.KIND_LEVELS[kind][key]
    if key.startswith("EnchantmentModel.") and kind == "enchantments":
        return 1
    if key.startswith("OrbModel.") and kind == "orbs":
        return 1
    if key.startswith("AfflictionModel.") and kind == "afflictions":
        return 1
    if key.startswith("ModifierModel.") and kind == "modifiers":
        return 1
    if key.startswith("CharacterModel.") and kind == "characters":
        return 1
    if key.startswith("EventModel.") and kind == "events":
        return 1
    return cat.describe(key)["level"] or 1


KIND_ORDER = ["powers", "cards", "orbs", "enchantments", "afflictions", "potions", "relics", "monsters", "encounters", "events",
              "modifiers", "characters", "acts"]


def node_key(n):
    kind, b = n.split(":", 1)
    return (int(b) if b.isdigit() else 99, KIND_ORDER.index(kind) if kind in KIND_ORDER else 99, n)


def topo(nodes, edges):
    """Kahn order of nodes (ties: lower batch number, then KIND_ORDER) and the strongly connected groups of size > 1."""
    succ = collections.defaultdict(set)
    for a, b in edges:
        if a != b:
            succ[a].add(b)
    index, low, on, stack, sccs, counter = {}, {}, set(), [], [], [0]

    def strong(v):
        index[v] = low[v] = counter[0]
        counter[0] += 1
        stack.append(v)
        on.add(v)
        for w in sorted(succ[v]):
            if w not in index:
                strong(w)
                low[v] = min(low[v], low[w])
            elif w in on:
                low[v] = min(low[v], index[w])
        if low[v] == index[v]:
            comp = []
            while True:
                w = stack.pop()
                on.discard(w)
                comp.append(w)
                if w == v:
                    break
            sccs.append(sorted(comp))

    sys.setrecursionlimit(10000)
    for v in sorted(nodes):
        if v not in index:
            strong(v)
    comp_of = {}
    for i, c in enumerate(sccs):
        for v in c:
            comp_of[v] = i
    cedges = collections.defaultdict(set)
    indeg = collections.Counter()
    for a in succ:
        for b in succ[a]:
            ca, cb = comp_of[a], comp_of[b]
            if ca != cb and cb not in cedges[ca]:
                cedges[ca].add(cb)
                indeg[cb] += 1
    ready = sorted((min(node_key(v) for v in sccs[i]), i) for i in range(len(sccs)) if indeg[i] == 0)
    order = []
    import heapq
    heap = list(ready)
    heapq.heapify(heap)
    while heap:
        _, i = heapq.heappop(heap)
        order.extend(sorted(sccs[i], key=node_key))
        for j in sorted(cedges[i]):
            indeg[j] -= 1
            if indeg[j] == 0:
                heapq.heappush(heap, (min(node_key(v) for v in sccs[j]), j))
    return order, [c for c in sccs if len(c) > 1]


def insert_after(od, anchor, key, value):
    out = OrderedDict()
    done = False
    for k, v in od.items():
        if k == key:
            continue
        out[k] = v
        if k == anchor and not done:
            out[key] = value
            done = True
    if not done:
        out[key] = value
    return out


def main():
    raw = dict((k, common.load_json(common.inventory_path(k + ".json"))) for k in D.CONTENT_KINDS)
    rawi = raw_index(raw)
    index = D.Index(raw)
    kg = common.load_json(common.stage1_path("kernel-gaps.json"))
    cat = R.Catalog(kg)
    readiness = kg["content_readiness"]["files"]
    stage = OrderedDict((f, common.load_json(common.stage1_path(f + ".classified.json"))) for f in FILES)
    power_ids = dict((e["class"], e["id"]) for e in raw["powers"]["entries"])

    ents = OrderedDict()
    for f in FILES:
        kind = D.KIND_OF_FILE[f]
        for e in stage[f]["entries"]:
            ents[(kind, e["id"])] = {"file": f, "entry": e, "raw": rawi[kind].get(e["id"])}

    text_use = collections.defaultdict(lambda: collections.Counter())
    key_use = collections.defaultdict(lambda: collections.Counter())
    key_texts = collections.defaultdict(set)
    for (kind, ident), x in ents.items():
        e, f = x["entry"], x["file"]
        texts = list(e.get("kernel_requirements") or [])
        x["texts"] = texts
        x["keys"] = own_keys(cat, kind, e, f, texts, readiness)
        for t in texts:
            text_use[t][kind] += 1
            for k in (([t] if cat.key_kind(t) is not None else cat.keys_for(t))):
                key_texts[k].add(t)
        for k in x["keys"]:
            key_use[k][kind] += 1
        x["deps3"] = D.dependencies(f, e, index)
        x["deps"] = [(k2, i2) for k2, i2, _ in x["deps3"]]
        x["requires"] = [(k2, i2) for k2, i2, rel in x["deps3"] if rel == D.REQUIRES]
        x["tier_req"] = max([1] + [level_of(cat, kind, k) for k in x["keys"] if k != "decompile:ldmembertoken"])

    adjust = collections.Counter()
    for key, x in ents.items():
        e, f = x["entry"], x["file"]
        t0 = legacy_tier(f, e)
        x["tier_classifier"] = t0 if f != "encounters" else e["tier"]
        reasons = []
        t = t0 if t0 is not None else x["tier_req"]
        if f == "encounters":
            reasons.append("encounter tier is now own code only (the W0 value included the monsters)")
        if key in D9_CLOSED and t == 4:
            t = x["tier_req"]
            reasons.append("D9: the lost static array is recovered from IL (w0-critique section 7)")
        scope = e.get("scope")
        if t < x["tier_req"] and not (t == 4):
            reasons.append("raised to the level of its own requirements (%s)" % ", ".join(
                k for k in x["keys"] if level_of(cat, key[0], k) == x["tier_req"]))
            t = x["tier_req"]
        if e.get("is_mock") or scope == "test_only":
            if t != 4:
                reasons.append("test-only mock")
            t = 4
        x["tier"] = t
        x["tier_adjustments"] = reasons
        if reasons and t != x["tier_classifier"]:
            adjust[x["file"]] += 1

    twd = dict((k, x["tier"]) for k, x in ents.items())
    for _ in range(100):
        changed = False
        for k, x in ents.items():
            v = max([twd[k]] + [twd[d] for d in x["deps"] if d in twd])
            if v != twd[k]:
                twd[k] = v
                changed = True
        if not changed:
            break

    own_prompts = {}
    for k, x in ents.items():
        f, e = x["file"], x["entry"]
        if f in MINOR:
            pr, ph, ne = list(e.get("prompts") or []), list(e.get("phases") or []), []
        else:
            pr, ph, ne = P.unify(f, e, power_ids, x["raw"])
        x["prompts"], x["phases"], x["nested"] = pr, ph, ne
        own_prompts[k] = [p for p in pr if p["opened_by"] == "%s:%s" % k]

    def power_closure(k):
        seen, stack, first = [], [(d, d) for d in ents[k]["requires"] if d[0] == "powers"], {}
        while stack:
            d, via = stack.pop(0)
            if d in first or d not in ents:
                continue
            first[d] = via
            seen.append(d)
            stack.extend((dd, via) for dd in ents[d]["requires"] if dd[0] == "powers")
        return [(d, first[d]) for d in seen]

    indirect_count = collections.Counter()
    for k, x in ents.items():
        have = set((p["kind"], p["site"], p["entry"]) for p in x["prompts"])
        reach = power_closure(k)
        if k[0] == "encounters":
            reach += [(d, d) for d in x["requires"] if d[0] == "monsters"]
        if k[0] == "events":
            for d in x["requires"]:
                if d[0] == "relics" and d in ents:
                    reach.append((d, d))
                    for ph in ents[d]["phases"]:
                        q = dict(ph)
                        q["via"] = "%s:%s" % d
                        if (q["phase"], q["site"]) not in set((a["phase"], a["site"]) for a in x["phases"]):
                            x["phases"].append(q)
        for d, via in reach:
            for p in own_prompts.get(d, []):
                sig = (p["kind"], p["site"], p["entry"])
                if sig in have:
                    continue
                q = dict(p)
                q["via"] = "%s:%s" % via
                x["prompts"].append(q)
                have.add(sig)
                indirect_count[x["file"]] += 1

    card_batch_changes = collections.Counter()
    for k, x in ents.items():
        f, e = x["file"], x["entry"]
        if f not in CARD_MAP:
            x["port_batch"] = e.get("port_batch")
            continue
        old = e.get("port_batch")
        if old is None:
            new = None
        elif e.get("ported"):
            new = 0
        else:
            new = CARD_MAP[f][old]
            if new == "split7":
                new = 9 if set(x["keys"]) & {"kernel:osty", "kernel:forge", "kernel:stars"} else 10
            elif new == "split8":
                if "kernel:enchantments" in x["keys"]:
                    new = 10
                elif "kernel:card_generation" in x["keys"] and not any(z.startswith("run:") or z == "kernel:run_effects" for z in x["keys"]):
                    new = 6
                else:
                    new = 11
        x["port_batch"] = new
        x["port_batch_legacy"] = old
        if str(new) != str(old):
            card_batch_changes[f] += 1

    def power_card_batch(pk):
        pb = ents[pk]["port_batch"]
        if pb == 6:
            return 7 if "kernel:orbs" in ents[pk]["keys"] else 9
        return POWER_TO_CARD_BATCH.get(pb)

    for _ in range(30):
        moved = False
        for k, x in ents.items():
            if x["file"] not in CARD_MAP or x["port_batch"] in (None, 0, 12):
                continue
            for d in x["requires"]:
                if d not in ents or ents[d]["entry"].get("ported"):
                    continue
                if d[0] == "powers":
                    t = power_card_batch(d)
                elif d[0] == "cards" and ents[d]["file"] in CARD_MAP and ents[d]["port_batch"] not in (None, 0, 12):
                    t = ents[d]["port_batch"]
                else:
                    t = None
                if t is not None and t > x["port_batch"]:
                    x.setdefault("batch_raised_by", []).append("%s:%s (card batch %d)" % (d[0], d[1], t))
                    x["port_batch"] = t
                    moved = True
        if not moved:
            break
    for k, x in ents.items():
        if x.get("batch_raised_by"):
            card_batch_changes["raised by a required power or token"] += 1

    p_by_class = dict((ents[k]["entry"]["class"], k) for k in ents if k[0] == "powers")

    def ptier(cls):
        k = p_by_class.get(cls)
        return (ents[k]["tier"], twd[k], ents[k]["port_batch"]) if k else (None, None, None)

    dep_changes = collections.Counter()
    for f in ("cards-silent-defect", "cards-necrobinder-regent"):
        env = stage[f]
        if f == "cards-silent-defect":
            for cls, rec in env["dependency_powers"].items():
                t, tw, b = ptier(cls)
                if t is not None and rec.get("tier") != t:
                    dep_changes["cards-silent-defect.dependency_powers"] += 1
                rec["tier"], rec["tier_with_deps"], rec["batch"] = t, tw, b
            for e in env["entries"]:
                for rec in e["depends_on"]["powers"]:
                    t, tw, b = ptier(rec["power"])
                    if t is not None and rec.get("tier") != t:
                        dep_changes["cards-silent-defect.entries.depends_on.powers"] += 1
                    rec["tier"], rec["tier_with_deps"], rec["batch"] = t, tw, b
        else:
            newt = OrderedDict()
            for cls, rec in env["power_dependencies"].items():
                t, tw, b = ptier(cls)
                if t is not None and rec.get("provisional_tier") != t:
                    dep_changes["cards-necrobinder-regent.power_dependencies"] += 1
                r2 = OrderedDict()
                for kk, vv in rec.items():
                    if kk == "provisional_tier":
                        r2["tier"] = t
                    elif kk == "provisional_tier_with_deps":
                        r2["tier_with_deps"] = tw
                    elif kk == "provisional_batch":
                        r2["port_batch"] = b
                    else:
                        r2[kk] = vv
                newt[cls] = r2
            env["power_dependencies"] = newt
    for e in stage["monsters"]["entries"]:
        for rec in e["deps"]["powers"]:
            t, tw, b = ptier(rec["power"])
            if t is None:
                continue
            if rec.get("tier_hint") != t:
                dep_changes["monsters.entries.deps.powers.tier_hint"] += 1
                if not (rec.get("tier_hint") == 0 and rec.get("ported")):
                    dep_changes["monsters.entries.deps.powers.tier_hint (excluding ported 0)"] += 1
            rec["tier_hint"] = t
            k = p_by_class[rec["power"]]
            rec["powers_classified"] = OrderedDict([("tier", t), ("tier_with_deps", tw), ("port_batch", b),
                                                    ("batch_label", ents[k]["entry"].get("batch_label"))])

    os.makedirs(common.OUT, exist_ok=True)
    summary = OrderedDict()
    for f in FILES:
        env0 = stage[f]
        kind = D.KIND_OF_FILE[f]
        entries = []
        for e in env0["entries"]:
            k = (kind, e["id"])
            x = ents[k]
            out = OrderedDict()
            for kk, vv in e.items():
                if kk in ("tier_own", "tier_derived_from_requirements", "effective_tier", "effective_tier_from", "indirect_prompts",
                          "tier_with_deps", "kernel_requirement_notes", "phases", "dependencies"):
                    continue
                if f == "cards-silent-defect" and kk == "tier":
                    continue
                out[kk] = vv
            out["kernel_requirements"] = x["keys"]
            out = insert_after(out, "kernel_requirements", "kernel_requirement_notes",
                               list(e.get("kernel_requirement_notes") or []) if f in MINOR else x["texts"])
            out["prompts"] = x["prompts"]
            out = insert_after(out, "prompts", "phases", x["phases"])
            if x["nested"]:
                out = insert_after(out, "phases", "nested_prompts", x["nested"])
            deps_out = OrderedDict((rel, OrderedDict()) for rel in (D.REQUIRES, D.READS, D.CO_PORT))
            for dk, di, rel in x["deps3"]:
                deps_out[rel].setdefault(dk, []).append(di)
            out = insert_after(out, "kernel_requirement_notes", "dependencies", deps_out)
            vd = e.get("version_delta_v107")
            if isinstance(vd, dict):
                vd = OrderedDict(vd)
                if "class" in vd and "status" not in vd:
                    vd = OrderedDict([("status", vd["class"])] + [(a, b) for a, b in vd.items() if a != "class"])
                vd["status"] = VD_STATUS.get(vd.get("status"), vd.get("status"))
                out["version_delta_v107"] = vd
            out["tier"] = x["tier"]
            out = insert_after(out, "tier", "tier_with_deps", twd[k])
            out = insert_after(out, "tier_with_deps", "tier_classifier", x["tier_classifier"])
            out = insert_after(out, "tier_classifier", "tier_from_requirements", x["tier_req"])
            out = insert_after(out, "tier_from_requirements", "tier_adjustments", x["tier_adjustments"])
            out["port_batch"] = x["port_batch"]
            if "port_batch_legacy" in x:
                out = insert_after(out, "port_batch", "port_batch_legacy", x["port_batch_legacy"])
                out = insert_after(out, "port_batch_legacy", "card_batch_raised_by", x.get("batch_raised_by", []))
            entries.append(out)
        env = OrderedDict()
        env["schema"] = SCHEMA
        env["kind"] = env0["kind"]
        env["content_kind"] = kind
        env["game_version"] = env0["game_version"]
        env["generated_by"] = GENERATORS[f] + [NORMALIZER]
        env["requirements_catalogue"] = "sim/specs/inventory/requirements.json"
        env["batches"] = "sim/specs/inventory/batches.json"
        env["tier_legend"] = TIER_LEGEND
        env["schema_fields"] = SCHEMA_FIELDS
        env["kind_baseline"] = catalog.KIND_BASELINES.get(kind, [])
        for kk, vv in env0.items():
            if kk in ("schema", "kind", "game_version", "generated_by", "entries", "count", "tier_legend", "tier_rule",
                      "tier_definition", "requirement_levels", "requirement_catalog"):
                continue
            if kk == "prompt_type_legend":
                env["legacy_prompt_type_legend"] = vv
                continue
            if kk in LEGEND_KEYS and isinstance(vv, dict):
                vv = OrderedDict((a, SCHEMA_FIELDS[a] if a in SCHEMA_FIELDS else b) for a, b in vv.items()
                                 if a not in DROPPED_FIELDS)
            if kk == "added_fields" and isinstance(vv, list):
                vv = [a for a in vv if a not in DROPPED_FIELDS]
            if f in CARD_MAP and kk in ("port_batch_legend", "port_batch_prerequisites", "batch_labels", "batch_prerequisites"):
                env["legacy_" + kk] = vv
                continue
            if f in CARD_MAP and kk == "fields":
                vv = OrderedDict((a, b) for a, b in vv.items() if a not in ("tier", "port_batch"))
                env["legacy_port_batch_legend"] = env0["fields"]["port_batch"]
            env[kk] = vv
        if f in CARD_MAP:
            env["port_batch_legend"] = CARD_BATCHES
            env["port_batch_prerequisites"] = CARD_PREREQ
        env["count"] = len(entries)
        env["entries"] = entries
        path = os.path.join(common.OUT, f + ".classified.json")
        with open(path, "w", encoding="utf-8") as fh:
            json.dump(env, fh, indent=1, ensure_ascii=False)
            fh.write("\n")
        summary[f] = len(entries)

    write_requirements(cat, key_use, key_texts, text_use)
    write_batches(ents, twd, stage)
    for name, gens in PASS_THROUGH.items():
        src = common.stage1_path(name)
        if not os.path.exists(src):
            continue
        doc = common.load_json(src)
        out = OrderedDict()
        for kk, vv in doc.items():
            out[kk] = vv
            if kk in ("schema", "meta"):
                out["generated_by"] = gens
        if "generated_by" not in out:
            out = OrderedDict([("generated_by", gens)] + list(out.items()))
        out["generated_by"] = gens
        with open(os.path.join(common.OUT, name), "w", encoding="utf-8") as fh:
            json.dump(out, fh, indent=1, ensure_ascii=False)
            fh.write("\n")
    report = OrderedDict([
        ("entries", summary), ("tier_adjusted_entries", dict(adjust)), ("dependency_tier_changes", dict(dep_changes)),
        ("indirect_prompts_added", dict(indirect_count)), ("card_batch_renumbered", dict(card_batch_changes)),
    ])
    with open(common.work_path("normalize", "report.json"), "w") as fh:
        json.dump(report, fh, indent=1)
    print(json.dumps(report, indent=1))


def write_requirements(cat, key_use, key_texts, text_use):
    keys = OrderedDict()
    for k in sorted(set(key_use) | set(catalog.FEATURES)):
        d = cat.describe(k)
        row = OrderedDict([("kind", d["kind"]), ("level", d["level"]), ("status", d["status"]), ("title", d["title"])])
        if d.get("scope"):
            row["scope"] = d["scope"]
        row["rows"] = d["rows"]
        row["used_by"] = OrderedDict(sorted(key_use.get(k, {}).items()))
        row["w0_texts"] = sorted(key_texts.get(k, ()))
        keys[k] = row
    unmapped = sorted(t for t in text_use if not ([t] if cat.key_kind(t) else cat.keys_for(t)))
    doc = OrderedDict([
        ("schema", "sts2sim.inventory.requirements/v1"),
        ("game_version", "0.111.0"),
        ("generated_by", [NORMALIZER, common.GENERATOR_DIR + "/normalize/catalog.py", common.GENERATOR_DIR + "/normalize/requirements.py"]),
        ("key_forms", OrderedDict([
            ("<Cmd>.<Method>", "kernel-gaps.json cmd_facades[<Cmd>] rows named <Method>"),
            ("<Cmd>.<Method>(<variant>)", "the overload family of that facade that content_readiness reports as a gap"),
            ("<Hook>", "kernel-gaps.json hook_virtuals row"),
            ("<Base>.<Virtual>", "kernel-gaps.json model_kind_virtuals[<Base>] row"),
            ("<area>:<id>", "a feature without such a row; `rows` joins it to kernel-gaps.json, run-layer-gaps.json or subsystems.json"),
        ])),
        ("levels", TIER_LEGEND),
        ("status", "P0 sim status from kernel-gaps.json (ported, partial, missing, strip) for facades and hooks; features: missing unless level 1"),
        ("selection_rule", "an entry lists the keys its own code needs: W0 texts mapped by normalize/catalog.py plus the missing hooks and Cmd overloads of its file in kernel-gaps.json content_readiness; keys whose facade or hook is already ported are left out"),
        ("kind_baselines", catalog.KIND_BASELINES),
        ("count", len(keys)),
        ("keys", keys),
        ("w0_texts_without_a_key", OrderedDict([("reason", "the feature exists in the P0 sim or has no single-player rule effect"),
                                                 ("texts", unmapped)])),
    ])
    with open(os.path.join(common.OUT, "requirements.json"), "w", encoding="utf-8") as fh:
        json.dump(doc, fh, indent=1, ensure_ascii=False)
        fh.write("\n")


def write_batches(ents, twd, stage):
    schemes = OrderedDict()
    legends = OrderedDict()
    for f in FILES:
        kind = D.KIND_OF_FILE[f]
        env = stage[f]
        if kind == "cards":
            legends["cards"] = CARD_BATCHES
        elif kind == "powers":
            legends[kind] = env["batch_titles"]
        elif kind in ("monsters", "encounters"):
            legends[kind] = env["batch_legend"]
        elif kind == "events":
            legends[kind] = env["port_batch_definition"]
        else:
            legends.setdefault(kind, env.get("port_batch_legend"))
    members = collections.defaultdict(list)
    for k, x in ents.items():
        members["%s:%s" % (k[0], x["port_batch"])].append(k[1])
    edges = collections.defaultdict(list)
    co_pairs = collections.defaultdict(list)
    for k, x in ents.items():
        if x["port_batch"] is None:
            continue
        to = "%s:%s" % (k[0], x["port_batch"])
        for d in x["requires"]:
            if d not in ents or ents[d]["port_batch"] is None or ents[d].get("entry", {}).get("ported"):
                continue
            frm = "%s:%s" % (d[0], ents[d]["port_batch"])
            if frm != to:
                edges[(frm, to)].append("%s:%s <- %s:%s" % (k[0], k[1], d[0], d[1]))
        for dk, di, rel in x["deps3"]:
            d = (dk, di)
            if rel != D.CO_PORT or d not in ents or ents[d]["port_batch"] is None:
                continue
            a, b = sorted(["%s:%s" % (k[0], x["port_batch"]), "%s:%s" % (dk, ents[d]["port_batch"])])
            if a != b:
                co_pairs[(a, b)].append("%s:%s ~ %s:%s" % (k[0], k[1], dk, di))
    nodes = set(n for n in members if not n.endswith(":None"))
    for a, b in edges:
        nodes.add(a)
        nodes.add(b)
    order, cycles = topo(nodes, list(edges))
    for kind, leg in legends.items():
        rows = OrderedDict()
        for b, title in (leg or {}).items():
            n = "%s:%s" % (kind, b)
            rows[b] = OrderedDict([("title", title), ("members", len(members.get(n, [])))])
        n_none = len(members.get("%s:None" % kind, []))
        if n_none:
            rows["null"] = OrderedDict([("title", "not scheduled (mock, deprecated, unreferenced)"), ("members", n_none)])
        schemes[kind] = rows
    edge_rows = [OrderedDict([("from", a), ("to", b), ("dependencies", len(v)), ("examples", sorted(v)[:6])])
                 for (a, b), v in sorted(edges.items())]
    doc = OrderedDict([
        ("schema", "sts2sim.inventory.batches/v1"),
        ("game_version", "0.111.0"),
        ("generated_by", [NORMALIZER]),
        ("node_form", "<content kind>:<port_batch> (cards use the unified card scheme; the other kinds keep their classifier's scheme)"),
        ("edge_rule", "from -> to when an entry of batch `to` requires an unported entry of batch `from` (dependencies.requires of the classified manifests); P0-ported dependencies are dropped; reads do not order batches; co_port pairs (definitions that call each other) are listed separately"),
        ("card_batch_mapping", OrderedDict((f, dict((str(a), b) for a, b in m.items())) for f, m in CARD_MAP.items())),
        ("card_batch_prerequisites", CARD_PREREQ),
        ("schemes", schemes),
        ("edges", edge_rows),
        ("order", order),
        ("requires_cycles", OrderedDict([("note", "batch nodes whose requires-edges form a cycle: port them in the same wave"), ("groups", cycles)])),
        ("co_port_pairs", [OrderedDict([("batches", [a, b]), ("pairs", len(v)), ("examples", sorted(v)[:6])])
                           for (a, b), v in sorted(co_pairs.items())]),
    ])
    with open(os.path.join(common.OUT, "batches.json"), "w", encoding="utf-8") as fh:
        json.dump(doc, fh, indent=1, ensure_ascii=False)
        fh.write("\n")


if __name__ == "__main__":
    main()
