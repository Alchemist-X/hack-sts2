import os as _os
import sys as _sys
_sys.path.insert(0, _os.path.join(_os.path.dirname(_os.path.abspath(__file__)), ".."))
import common  # noqa: E402
import json
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import annot_e
import annot_m1
import graphfmt
import extract as X

INV = common.INVENTORY
NEW = X.NEW

raw_m = json.load(open(os.path.join(INV, "monsters.json")))
raw_e = json.load(open(os.path.join(INV, "encounters.json")))
raw_a = json.load(open(os.path.join(INV, "acts.json")))
auto = json.load(open(common.work_path("monsters", "auto.json")))
enc_auto = json.load(open(common.work_path("monsters", "enc_auto.json")))
vdiff = json.load(open(common.work_path("monsters", "vdiff.json")))
pdeps = json.load(open(common.work_path("monsters", "powerdeps.json")))
PCLS = {e["class"]: e for e in json.load(open(common.stage1_path("powers.classified.json")))["entries"]}
ported = json.load(open(os.path.join(INV, "ported.json")))

ASC = {"ToughEnemies": 8, "DeadlyEnemies": 9}
VISUAL_KERN = re.compile(r"ShouldDisappearFromDoom|HpDisplay")

CODE_ENTERED = {
    "Queen": {"ENRAGE_MOVE": "SetMoveImmediate from AfterDeath when the Amalgam dies during BURN_BRIGHT_FOR_ME"},
    "TestSubject": {"RESPAWN_MOVE": "SetMoveImmediate(force) from TriggerDeadState (AdaptablePower.AfterDeath)"},
    "WaterfallGiant": {"ABOUT_TO_BLOW_MOVE": "SetMoveImmediate(force) from TriggerAboutToBlowState (SteamEruptionPower.AfterDeath)"},
    "DecimillipedeSegment": {"DEAD_MOVE": "SetMoveImmediate from ReattachPower.AfterDeath"},
    "DecimillipedeSegmentBack": {"DEAD_MOVE": "SetMoveImmediate from ReattachPower.AfterDeath"},
    "DecimillipedeSegmentFront": {"DEAD_MOVE": "SetMoveImmediate from ReattachPower.AfterDeath"},
    "DecimillipedeSegmentMiddle": {"DEAD_MOVE": "SetMoveImmediate from ReattachPower.AfterDeath"},
    "MockReattachMonster": {"DEAD_MOVE": "SetMoveImmediate from ReattachPower.AfterDeath"},
    "ToughEgg": {"NIBBLE_MOVE": "ForceCurrentState(AfterHatchedState) when spawned already hatched"},
    "TerrorEel": {"TERROR_MOVE": "follow-up id of the Stun issued by ShriekPower"},
    "LagavulinMatriarch": {"SLASH_MOVE": "follow-up id of the Stun issued by AsleepPower"},
    "Tunneler": {"BITE_MOVE": "follow-up id of the Stun issued by BurrowedPower"},
    "CeremonialBeast": {"BEAST_CRY_MOVE": "follow-up id of the Stun issued by PlowPower"},
}

LISTENER_RELEVANT = {"Aeonglass": ["AfterCardGeneratedForCombat"], "Queen": ["AfterDeath"]}

SPAWNED_BY = {
    "Axebot": ["StockPower (Axebot respawn on death)"],
    "EyeWithTeeth": ["Fogmog ILLUSION_MOVE"],
    "GasBomb": ["LivingFog BLOAT_MOVE"],
    "Guardbot": ["Fabricator (defense spawn)"], "Noisebot": ["Fabricator (defense spawn)"],
    "Zapbot": ["Fabricator (aggro spawn)"], "Stabbot": ["Fabricator (aggro spawn)"],
    "Parafright": ["TheObscura ILLUSION_MOVE"],
    "ToughEgg": ["Ovicopter LAY_EGGS_MOVE"],
    "TwoTailedRat": ["TwoTailedRat CALL_FOR_BACKUP_MOVE"],
    "Wriggler": ["InfestedPower (PhrogParasite death)"],
    "SneakyGremlin": ["SurprisePower (GremlinMerc death)"], "FatGremlin": ["SurprisePower (GremlinMerc death)"],
    "BigDummy": ["MockAttackAndSummonMinionMonster"],
    "Osty": ["OstyCmd.Summon / PlayerCmd.AddPet<Osty> (Necrobinder)"],
    "Byrdpip": ["relic Byrdpip SummonPet (PlayerCmd.AddPet)"],
    "PaelsLegion": ["relic PaelsLegion SummonPet (PlayerCmd.AddPet)"],
}
PETS = {"Osty", "Byrdpip", "PaelsLegion"}
SPAWNER_ENCS = {"Guardbot": ["FabricatorNormal"], "Noisebot": ["FabricatorNormal"], "Zapbot": ["FabricatorNormal"], "Stabbot": ["FabricatorNormal"],
                "FatGremlin": ["GremlinMercNormal"], "SneakyGremlin": ["GremlinMercNormal"], "Wriggler": ["PhrogParasiteElite"]}
TEST = {"BigDummy", "MultiAttackMoveMonster", "OneHpMonster", "SingleAttackMoveMonster", "TenHpMonster"}
UNUSED = {"TheAdversaryMkOne", "TheAdversaryMkTwo", "TheAdversaryMkThree"}


def fmt_num(v):
    try:
        f = float(v)
        return int(f) if f == int(f) else f
    except Exception:
        return v


def val_entry(prop, line):
    v = X.eval_expr(prop["expr"], auto_props_cache[0])
    if v is None:
        return None
    d = {"a0": fmt_num(v["a0"]), "line": line}
    if v["asc"]:
        d["asc"] = {"level": v["asc"]["level"], "a": ASC.get(v["asc"]["level"]), "value": fmt_num(v["asc"]["value"])}
        if str(v["asc"]["value"]) == str(v["a0"]):
            d["asc"]["noop"] = True
    return d


auto_props_cache = [None]


def gvia_scan(path):
    t = X.strip_comments(open(path, encoding="utf-8").read())
    out = []
    for m in re.finditer(r"AscensionHelper\.GetValueIfAscension\(AscensionLevel\.(\w+),\s*([^,]+?),\s*([^,()]+?)\)", t):
        out.append({"line": X.line_of(t, m.start()), "level": m.group(1), "a": ASC.get(m.group(1)),
                    "value": fmt_num(m.group(2).strip().rstrip("mf")), "a0": fmt_num(m.group(3).strip().rstrip("mf"))})
    for m in re.finditer(r"AscensionHelper\.HasAscension\(AscensionLevel\.(\w+)\)|HasAscension\(AscensionLevel\.(\w+)\)", t):
        out.append({"line": X.line_of(t, m.start()), "level": m.group(1) or m.group(2), "has_ascension_check": True})
    return out


def reach(graph):
    if not graph:
        return set()
    states = {s["id"]: s for s in graph["states"]}
    seen = set()
    todo = [i["id"] for i in graph["initial"]] + [s["id"] for s in graph["states"] if s["kind"] == "MoveState"]
    while todo:
        x = todo.pop()
        if x in seen or x not in states:
            if x not in states:
                seen.add(x)
            continue
        seen.add(x)
        st = states[x]
        for f in st.get("follow_up", []):
            todo.append(f["to"])
        for b in st.get("branches", []):
            todo.append(b["to"])
    return seen


def graph_json(name, g, file):
    if not g:
        return None
    reachable = reach(g)
    regs = [r["id"] for r in g["registered"]]
    states = []
    for st in g["states"]:
        d = {"id": st["id"], "kind": {"MoveState": "move", "RandomBranchState": "random", "ConditionalBranchState": "conditional"}[st["kind"]],
             "line": st["line"]}
        if st["kind"] == "MoveState":
            perf = st.get("perform")
            d["perform"] = "(no-op lambda)" if perf and "Task.CompletedTask" in perf else perf
            d["intents"] = st.get("intents_resolved", [])
            fus = st.get("follow_up", [])
            if fus and all(not f["conds"] for f in fus):
                fus = [fus[-1]]
            if len(fus) == 1 and not fus[0]["conds"]:
                d["follow_up"] = fus[0]["to"]
            elif fus:
                d["follow_up"] = [{"to": f["to"], "when_built": " && ".join(graphfmt.short_cond(c) for c in f["conds"])} for f in fus]
            else:
                d["follow_up"] = None
            if st.get("init", {}).get("MustPerformOnceBeforeTransitioning") == "true":
                d["must_perform_once"] = True
        else:
            brs = []
            for b in st.get("branches", []):
                bd = {"to": b["to"]}
                if "condition" in b:
                    bd["condition"] = graphfmt.short_cond(b["condition"])
                if "repeat" in b:
                    bd["repeat"] = b["repeat"]
                    if b.get("max_times"):
                        bd["max_times"] = fmt_num(b["max_times"])
                    w = re.sub(r"^\(\)\s*=>\s*", "", b.get("weight", "1f"))
                    if re.match(r"^-?[\d.]+f$", w):
                        bd["weight"] = fmt_num(w.rstrip("f"))
                    else:
                        bd["weight_expr"] = graphfmt.short_cond(w)
                    if b.get("cooldown") not in (None, "0"):
                        bd["cooldown"] = fmt_num(b["cooldown"])
                if b.get("conds"):
                    bd["when_built"] = " && ".join(graphfmt.short_cond(c) for c in b["conds"])
                brs.append(bd)
            d["branches"] = brs
        if st["id"] not in regs:
            d["registered"] = False
        if st["id"] not in reachable:
            d["reachable"] = False
        if st.get("build_conditions"):
            d["built_when"] = sorted(set(graphfmt.short_cond(c) for c in st["build_conditions"]))
        states.append(d)
    initial = [{"state": i["id"], "when": " && ".join(graphfmt.short_cond(c) for c in i["conds"]) or None} for i in g["initial"]]
    incoming = set(i["id"] for i in g["initial"])
    for s0 in g["states"]:
        for f in s0.get("follow_up", []):
            incoming.add(f["to"])
        for b in s0.get("branches", []):
            incoming.add(b["to"])
    code_entered = CODE_ENTERED.get(name, {})
    orphans = [s0["id"] for s0 in g["states"] if s0["id"] not in incoming and s0["id"] not in code_entered]
    incoming0 = set(i["id"] for i in g["initial"])
    for s0 in g["states"]:
        for f in s0.get("follow_up", []):
            incoming0.add(f["to"])
        for b in s0.get("branches", []):
            incoming0.add(b["to"])
    rnd = [s["id"] for s in g["states"] if s["kind"] == "RandomBranchState" and s["id"] in incoming0]
    return {"citation": "%s:%d-%d" % (file, g["line"], g["end_line"]), "initial": initial, "states": states,
            "registration_order": regs, "shape": graphfmt.render(g), "random_branches_reachable": rnd,
            "entered_by_code": code_entered, "orphan_states": orphans,
            "uses_monster_ai_rng": bool(rnd)}


def special_intents(intents):
    sp = []
    for it in intents:
        k = it.replace("Intent", "")
        if k in ("SingleAttack", "MultiAttack", "Defend", "Buff", "Debuff"):
            continue
        sp.append(k)
    return sorted(set(sp))


def asc_summary(name, file, values, hp, props=None):
    scans = gvia_scan(os.path.join(NEW, "MegaCrit.Sts2.Core." + file))
    line2prop = {p["line"]: k for k, p in (props or {}).items()}
    by = {"ToughEnemies": [], "DeadlyEnemies": []}
    noop = []
    for s in scans:
        if s.get("has_ascension_check"):
            by.setdefault(s["level"], []).append("HasAscension check @%d" % s["line"])
            continue
        prop = line2prop.get(s["line"])
        label = prop or "inline@%d" % s["line"]
        if str(s["value"]) == str(s["a0"]):
            noop.append("%s (%s %s=%s)" % (label, s["level"], s["a0"], s["value"]))
            continue
        by.setdefault(s["level"], []).append("%s %s->%s" % (label, s["a0"], s["value"]))
    out = {"levels_used": sorted(k for k, v in by.items() if v)}
    for lvl in ("ToughEnemies", "DeadlyEnemies"):
        if by.get(lvl):
            out["A%d_%s" % (ASC[lvl], lvl)] = by[lvl]
    if noop:
        out["noop_branches"] = noop
    out["A18_plus"] = "none (maximum ascension is 10: Entities.Ascension/AscensionManager.cs:8)"
    if not by.get("ToughEnemies") and not by.get("DeadlyEnemies"):
        out["summary"] = "ascension-insensitive"
    else:
        parts = []
        if by.get("ToughEnemies"):
            parts.append("A8 changes %d value(s)" % len(by["ToughEnemies"]))
        if by.get("DeadlyEnemies"):
            parts.append("A9 changes %d value(s)" % len(by["DeadlyEnemies"]))
        out["summary"] = "; ".join(parts)
    return out


def vdelta_monster(name, ann):
    v = vdiff["monsters"].get(name)
    if ann.get("vnote"):
        cls = ann["vnote"].split(":", 1)[0].strip()
        note = ann["vnote"].split(":", 1)[1].strip() if ":" in ann["vnote"] else ann["vnote"]
        return {"class": cls, "note": note}
    if v is None or v["status"] == "only_v0.111.0":
        return {"class": "new", "note": "file only in v0.111.0"}
    if v["status"] == "same_tokens":
        return {"class": "same", "note": "normalized numbers, rule enums, Cmd/model calls, move graph and per-move Cmd sequence equal (raw text differs only by decompiler settings)"}
    return {"class": "cosmetic", "note": "diff limited to decompiler naming / state-machine layout (closure locals, awaiter fields); values, graph and Cmd sequence equal after manual check"}


def build_monsters():
    pinfo = {p: v for p, v in pdeps.items()}
    enc_by_monster = {}
    enc_kind = {e["class"]: e for e in raw_e["entries"]}
    for e in raw_e["entries"]:
        for mname in set(e.get("monsters", [])):
            enc_by_monster.setdefault(mname, []).append(e["class"])
    entries = []
    for r in raw_m["entries"]:
        name = r["class"]
        ann = annot_m1.M[name]
        a = auto[name]
        auto_props_cache[0] = a["props"]
        file = r["file"]
        base = None
        g = a["graph"]
        if g is None and name.startswith("DecimillipedeSegment") and name != "DecimillipedeSegment":
            base = "DecimillipedeSegment"
        if g is None and name == "MysteriousKnight":
            base = "FlailKnight"
        gfile = file
        if base:
            g = auto[base]["graph"]
            gfile = auto[base]["file"]
            auto_props_cache[0] = dict(auto[base]["props"], **a["props"])
        values = {}
        props = dict(auto[base]["props"]) if base else {}
        props.update(a["props"])
        for k, p in props.items():
            if k in ("MinInitialHp", "MaxInitialHp"):
                continue
            if p["type"] not in ("int", "decimal", "float"):
                continue
            if re.search(r"HpBarSize|DeathAnim|HurtAnimation|Padding|Scale|Offset|Delay|Duration|Sfx|Vfx", k):
                continue
            ve = val_entry(p, p["line"])
            if ve is not None:
                values[k] = ve
        if name == "KnowledgeDemon":
            values["_disintegrationDamageValues"] = {"a0": [6, 7, 8], "line": 771, "indexed_by": "CurseOfKnowledgeCounter",
                                                     "source": "unreadable in the rule-truth tree (LdMemberToken); recovered from the macOS v0.111.0 decompile with identical IL (see notes)"}
        hp = {}
        for k in ("MinInitialHp", "MaxInitialHp"):
            p = props.get(k)
            if p:
                ve = val_entry(p, p["line"])
                key = "min" if k.startswith("Min") else "max"
                if ve:
                    d = {"a0": ve["a0"]}
                    if "asc" in ve:
                        d["a%d" % ve["asc"]["a"]] = ve["asc"]["value"]
                    hp[key] = d
                else:
                    hp[key] = {"expr": p["expr"]}
        hp["citation"] = r.get("hp_citation")
        if name == "Axebot":
            hp["rule"] = "base GetValueIfAscension(ToughEnemies, 76/86, 70/78) + RespawnMaxHpBonus (RespawnCount * 10, RespawnCount = 2 - StockAmount)"
            hp["min"] = {"a0": 70, "a8": 76, "plus": "RespawnMaxHpBonus"}
            hp["max"] = {"a0": 78, "a8": 86, "plus": "RespawnMaxHpBonus"}
        if name == "TestSubject":
            hp["rule"] = "form 1 = FirstFormHp; RESPAWN_MOVE revives with SecondFormHp 200 (A8 212) then ThirdFormHp 300 (A8 313)"
        if name == "ToughEgg":
            hp["rule"] = "egg HP as above; HATCH sets max/current HP to RunRng.Niche.NextInt(HatchlingMinHp 19 (A8 20), HatchlingMaxHp 22 (A8 23) + 1)"
        if name.startswith("DecimillipedeSegment"):
            hp["rule"] = "rolled HP, then made even and unique among segments in AfterAddedToRoom (SetMaxAndCurrentHp)"
        if name == "WaterfallGiant":
            hp["rule"] = "first death with SteamEruption re-arms HP to 999999999 until EXPLODE kills it"
        mg = graph_json(name, g, gfile)
        move_bodies = {}
        src_moves = auto[base]["moves"] if base else a["moves"]
        for mv, info in sorted(src_moves.items()):
            move_bodies[mv] = "%s:%d-%d" % (gfile, info["line"], info["end_line"])
        hooks = {}
        for h, info in sorted(a["hooks"].items()):
            hooks[h] = "%s:%d" % (file, info["line"])
        deps = ann.get("deps", {})
        dep_pow = []
        for p in deps.get("powers", []):
            pi = pinfo.get(p)
            pc = PCLS.get(p)
            dep_pow.append({"power": p, "ported": bool(pi and pi["ported"]), "tier_hint": pi["tier"] if pi else None,
                            "features": pi["features"] if pi else [],
                            "powers_classified": ({"tier": pc.get("tier"), "port_batch": pc.get("port_batch"), "batch_label": pc.get("batch_label")} if pc else None)})
        eff = ann["tier"]
        for d in dep_pow:
            if d["ported"]:
                continue
            t = d["powers_classified"]["tier"] if d["powers_classified"] and d["powers_classified"].get("tier") else d["tier_hint"]
            if t:
                eff = max(eff, t)
        if deps.get("afflictions") or deps.get("enchantments"):
            eff = max(eff, 3)
        eff_from = []
        for d in dep_pow:
            if d["ported"]:
                continue
            t = d["powers_classified"]["tier"] if d["powers_classified"] and d["powers_classified"].get("tier") else d["tier_hint"]
            if t and t > ann["tier"]:
                eff_from.append("%s (powers.classified T%d)" % (d["power"], t) if d["powers_classified"] else "%s (tier_hint T%d)" % (d["power"], t))
        for af in deps.get("afflictions", []):
            eff_from.append("affliction %s (>= T3)" % af)
        for en in deps.get("enchantments", []):
            eff_from.append("enchantment %s (>= T3)" % en)
        kern = [k for k in ann.get("kern", []) if not VISUAL_KERN.search(k)]
        lr = LISTENER_RELEVANT.get(name, [])
        vis_hooks = [h for h in r["abstract_model_hooks"] if h not in lr]
        if lr:
            kern = ["monster as hook listener: " + ", ".join(lr)] + [k for k in kern if not k.startswith("monster as hook listener")]
        rng = []
        if name not in PETS and not r["is_abstract"]:
            rng.append("RunRng.Niche: 1 draw at spawn (unique HP roll, Combat/CombatState.cs:717, Entities.Creatures/Creature.cs:972-984)")
        if mg and mg["uses_monster_ai_rng"]:
            rng.append("RunRng.MonsterAi: NextFloat per evaluated random branch (%s)" % ", ".join(mg["random_branches_reachable"]))
        rng += ann.get("rng", [])
        if name == "Fabricator":
            rng.append("RunRng.MonsterAi: NextItem for each bot pick (SpawnBot)")

        if name == "Entomancer":
            rng.append("RunRng.Shuffle via PersonalHivePower (Dazed at random draw-pile positions when hit)")
        rng = list(dict.fromkeys(rng))
        priv = {"used": False}
        if r.get("uses_private_rng"):
            priv = {"used": True, "members": r.get("private_rng_members"), "rule_relevant": False,
                    "note": "MonsterModel.Rng read only in SetupSkins (visual)"}
        sp = ann.get("special", {})
        role = None
        encs = list(r.get("encounters", []))
        if name in SPAWNER_ENCS:
            for x in SPAWNER_ENCS[name]:
                if x not in encs:
                    encs.append(x)
        kinds = sorted(set(enc_kind[e]["kind"] for e in encs if e in enc_kind and enc_kind[e]["kind"] in ("weak", "normal", "elite", "boss", "event")))
        if r["is_mock"]:
            role = "mock"
        elif r["is_deprecated"]:
            role = "deprecated"
        elif r["is_abstract"]:
            role = "abstract base"
        elif name in PETS:
            role = "pet"
        elif name in TEST:
            role = "test"
        elif name in UNUSED:
            role = "unused"
        else:
            role = "/".join(kinds) if kinds else "unlisted"
            if name in SPAWNED_BY and name not in ("Axebot", "TwoTailedRat"):
                role += " (summoned)"
        reach_via = []
        act_encs = [e for e in encs if e in enc_kind and enc_kind[e]["kind"] in ("weak", "normal", "elite", "boss")]
        if act_encs:
            reach_via.append("act pool: " + ", ".join(act_encs) + (" (spawned mid-combat)" if name in SPAWNED_BY and name not in ("Axebot", "TwoTailedRat") else ""))
        ev_encs = [e for e in encs if e in enc_kind and enc_kind[e]["kind"] == "event"]
        if ev_encs:
            reach_via.append("event combat: " + ", ".join(ev_encs))
        if name in SPAWNED_BY:
            reach_via.append("spawned: " + "; ".join(SPAWNED_BY[name]))
        if r["is_abstract"]:
            reach_via.append("abstract: reached through its concrete subclasses")
        elif not reach_via and not r["is_mock"]:
            reach_via.append("unreachable in a standard run")
        batch = compute_batch(name, ann, r, dep_pow, deps, kern)
        notes = ann.get("notes", "")
        visual_notes = []
        if sp.get("death") and re.search(r"visual|music|only plays|subscription|phobia", sp.get("death")):
            visual_notes.append(sp["death"])
        if vis_hooks:
            visual_notes.append("visual-only AbstractModel overrides: " + ", ".join(vis_hooks))
        vis_mm = [k for k in ("ShouldDisappearFromDoom", "OnDieToDoom", "BeforeRemovedFromRoom", "ShouldFadeAfterDeath", "DeathAnimLengthOverride", "IsHealthBarVisible") if k in r["hooks_overridden"]]
        if vis_mm:
            visual_notes.append("visual-only MonsterModel overrides: " + ", ".join(vis_mm) + " (ShouldDisappearFromDoom/OnDieToDoom are read only by DoomPower VFX, Models.Powers/DoomPower.cs:377, :612)")
        e = {
            "id": r["id"], "model_id": r["model_id"], "class": name, "file": file,
            "is_mock": r["is_mock"], "is_abstract": r["is_abstract"], "is_deprecated": r["is_deprecated"], "ported": r["ported"],
            "role": role, "reachable_via": reach_via, "encounters": encs, "acts": sorted(set(r.get("acts", [])) | set(a for x in act_encs for a in enc_kind[x].get("acts", []))),
            "spawned_by": SPAWNED_BY.get(name, []),
            "hp": hp, "values": values,
            "move_graph": mg, "move_graph_inherited_from": base, "move_bodies": move_bodies, "hook_overrides": hooks,
            "intents_used": r.get("intents", []), "special_intents": special_intents(r.get("intents", [])),
            "mechanics": ann.get("mech", []),
            "powers_applied": sorted(set(deps.get("powers", []))),
            "deps": {"powers": dep_pow, "cards": deps.get("cards", []), "monsters": deps.get("monsters", []),
                     "afflictions": deps.get("afflictions", []), "enchantments": deps.get("enchantments", [])},
            "summons": sp.get("summon"), "multipart": sp.get("multipart"), "phases": sp.get("phases"),
            "death_rules": (sp.get("death") if sp.get("death") and not re.search(r"visual|music|only plays|subscription|phobia", sp.get("death")) else None),
            "role_detail": sp.get("role"),
            "escapes": any("CreatureCmd.Escape" in m or "escapes" in m for m in ann.get("mech", []) + ann.get("kern", [])) or "Escape" in special_intents(r.get("intents", [])),
            "prompts": ann.get("prompts", []),
            "kernel_requirements": kern,
            "kernel_features": kernel_features(kern),
            "kernel_features_via_powers": sorted(set(f for d in dep_pow if d["tier_hint"] and d["tier_hint"] >= 2 for f in d["features"])),
            "listener_hooks": {"rule_relevant": lr, "visual_only": vis_hooks},
            "rng_streams": rng, "private_rng": priv,
            "ascension_sensitivity": asc_summary(name, file, values, hp, a["props"]),
            "version_delta_v107": vdelta_monster(name, ann),
            "tier": ann["tier"], "effective_tier": eff, "effective_tier_from": eff_from, "port_batch": batch,
            "decompile_unreadable": bool(ann.get("decompile_broken")),
            "notes": "; ".join(x.rstrip(".") for x in [notes] + visual_notes if x),
            "open_questions": ann.get("oq", []),
        }
        entries.append(e)
    return entries


def compute_batch(name, ann, r, dep_pow, deps, kern):
    if r["ported"]:
        return 0
    if r["is_mock"] or r["is_deprecated"] or name in TEST or name in UNUSED or r["is_abstract"]:
        return 9 if not r["is_abstract"] else 5
    if name in PETS:
        return 8
    if ann.get("prompts") or ann.get("decompile_broken"):
        return 7
    feats = set()
    for d in dep_pow:
        feats.update(d["features"])
    text = " ".join(kern + ann.get("mech", []))
    if name == "Architect":
        return 1
    if "run_layer" in feats or re.search(r"\(run layer\)|run deck mutation|RemoveFromDeck|LoseGold|encounter custom", text):
        return 6
    if "revive" in feats or re.search(r"revive|dead-but-present|REVIVE_MOVE|TriggerAboutToBlowState|ReattachPower", text):
        return 5
    if "summon" in feats or re.search(r"CreatureCmd\.Add", text):
        return 4
    if "afflictions" in feats or deps.get("afflictions"):
        return 3
    if ann["tier"] >= 2:
        return 2
    return 1


BATCH_LEGEND = {
    "0": "ported in P0 (sim/Sts2Sim.Core/Monsters/Defs, Monsters/Encounters)",
    "1": "plain: own code uses ported facades only; power deps need at most W1 hook work (side-turn participants, power internal data)",
    "2": "kernel facades: monster-as-listener hooks, generic CardPileCmd.AddToCombatAndPreview<T>, CreatureCmd.Escape, raw HP writes",
    "3": "afflictions: depends on affliction-applying powers (Galvanized, Tainted, Smog, Hexed, Entangled, Ringing, Bound)",
    "4": "summons: mid-combat CreatureCmd.Add into encounter slots (own move or a death power)",
    "5": "revive / dead-but-present creatures and multi-phase bosses (Reattach, Illusion, Adaptable, SteamEruption)",
    "6": "run-layer coupled: gold theft, deck theft, extra combat rewards, event custom state",
    "7": "player prompt during the enemy turn and/or decompile-broken data (KnowledgeDemon)",
    "8": "player-side pets (Osty, relic pets): ship with the Osty/pet subsystem and their cards/relics",
    "9": "not content: test monsters, mocks, deprecated or unused models (port only as test seams)",
}
TIER_LEGEND = {
    "1": "pure numbers through facades the sim already has (DamageCmd.Attack, CreatureCmd.GainBlock/Heal/Kill/Stun, PowerCmd.Apply/Remove, move state machine)",
    "2": "needs facades or hooks that exist in the game but not in the sim",
    "3": "needs a player prompt or a new subsystem (summons, revive, pets, afflictions, run-layer effects)",
    "4": "decompile-unreadable data or cross-cutting",
}


def main():
    mon = build_monsters()
    env = {
        "schema": "sts2sim.inventory.classified/v1", "kind": "monsters", "game_version": raw_m["game_version"],
        "raw_manifest": "sim/specs/inventory/monsters.json", "raw_source_digest": raw_m["source_digest"],
        "generated": "2026-11-01", "generated_by": common.generator("monsters", "extract.py", "enc_extract.py", "vdiff.py", "powerdeps.py", "gen.py"),
        "decompile_root": common.DECOMPILE_ROOT,
        "cross_reference_tree": common.V107_ROOT,
        "citation_format": "<ns-dir>/<File>.cs:<line>, MegaCrit.Sts2.Core. prefix dropped",
        "companion_doc": "docs/sim-port/inventory-monsters.md",
        "tier_legend": TIER_LEGEND, "batch_legend": BATCH_LEGEND,
        "count": len(mon), "entries": mon,
    }
    json.dump(env, open(common.stage1_path("monsters.classified.json"), "w"), indent=1, ensure_ascii=False)
    print("monsters", len(mon))




KFEAT = [
    ("monster_hook_listener", r"monster as hook listener"),
    ("add_to_combat_and_preview", r"AddToCombatAndPreview<T>"),
    ("escape", r"CreatureCmd\.Escape"),
    ("mid_combat_summon", r"CreatureCmd\.Add|mid-combat summons"),
    ("encounter_slots", r"GetNextSlot|Slots|SortEnemiesBySlotName|last free slot"),
    ("dead_but_present", r"dead-but-present"),
    ("runtime_move_state", r"runtime-created MoveState"),
    ("set_move_immediate_from_hook", r"SetMoveImmediate"),
    ("stun_with_move", r"CreatureCmd\.Stun"),
    ("set_hp_direct", r"SetMaxAndCurrentHp|SetMaxHp|SetCurrentHpInternal"),
    ("slot_name_branch", r"SlotName"),
    ("starter_move_index", r"StarterMoveIdx"),
    ("side_turn_participants", r"participants"),
    ("encounter_custom_state", r"custom state|custom flag"),
    ("player_side_pets", r"pets on the player side|Osty/pets"),
    ("generated_cards", r"CreateCard|AddGeneratedCardToCombat"),
    ("random_pile_position", r"CardPilePosition\.Random"),
    ("run_layer_effects", r"run layer|run deck mutation|LoseGold|RemoveFromDeck|AddExtraReward|SpecialCardReward|CalculateGoldProportion"),
    ("player_prompt", r"CardSelectCmd"),
    ("instanced_power_target", r"Target"),
    ("power_internal_data", r"internal data"),
    ("live_combat_check", r"IsLiveCombat"),
    ("monster_ai_rng_extra", r"MonsterAi draw|RunRng\.\w+ draw inside|RunRng\.Niche draw"),
    ("card_upgrade_ops", r"FakeUpgrade|CardCmd\.Downgrade|CardCmd\.Upgrade"),
    ("combat_end_blockers", r"ShouldStopCombatFromEnding"),
    ("power_monster_coupling", r"power calls a monster method|power writes monster field|from a power"),
    ("multipart_boss", r"multi-part"),
    ("encounter_rng", r"encounter-private Rng"),
    ("hand_overflow", r"into the hand"),
]


def kernel_features(kern):
    out = []
    for key, rx in KFEAT:
        if any(re.search(rx, k) for k in kern):
            out.append(key)
    return out


GOLD_DEFAULT = {"Monster": [10, 20], "Elite": [35, 45], "Boss": [100, 100]}


def build_encounters(mon_entries):
    mon_by_class = {m["class"]: m for m in mon_entries}
    out = []
    spawned_into = {}
    for m in mon_entries:
        for sb in m["spawned_by"]:
            pass
    SUMMONS_IN = {
        "FogmogNormal": ["EyeWithTeeth"], "LivingFogNormal": ["GasBomb"], "FabricatorNormal": ["Guardbot", "Noisebot", "Zapbot", "Stabbot"],
        "TheObscuraNormal": ["Parafright"], "OvicopterNormal": ["ToughEgg"], "TwoTailedRatsNormal": ["TwoTailedRat"],
        "PhrogParasiteElite": ["Wriggler"], "GremlinMercNormal": ["SneakyGremlin", "FatGremlin"], "AxebotsNormal": ["Axebot"],
        "MockAttackAndSummonEncounter": ["BigDummy"],
    }
    EVENT_ENTRY = {
        "BattlewornDummyEventV1Encounter": "Models.Events/BattlewornDummy.cs:183", "BattlewornDummyEventV2Encounter": "Models.Events/BattlewornDummy.cs:189",
        "BattlewornDummyEventV3Encounter": "Models.Events/BattlewornDummy.cs:195", "DenseVegetationEventEncounter": "Models.Events/DenseVegetation.cs:367",
        "FakeMerchantEventEncounter": "Models.Events/FakeMerchant.cs:101", "MysteriousKnightEventEncounter": "Models.Events/TheLanternKey.cs:126",
        "PunchOffEventEncounter": "Models.Events/PunchOff.cs:533", "TheArchitectEventEncounter": "Models.Events/TheArchitect.cs:785 (CanonicalEncounter, layout display only; no combat)",
    }
    for r in raw_e["entries"]:
        name = r["class"]
        ann = annot_e.E[name]
        ea = enc_auto.get(name, {})
        mons = set(r.get("monsters", []))
        comp_mons = []
        for c in ann["composition"]:
            for mm in re.findall(r"\b[A-Z]\w+", c):
                if mm in mon_by_class and mm not in comp_mons:
                    comp_mons.append(mm)
        for mm in r.get("monsters", []):
            if mm not in comp_mons:
                comp_mons.append(mm)
        summoned = SUMMONS_IN.get(name, [])
        allm = list(dict.fromkeys(comp_mons + summoned))
        mb = {mm: mon_by_class[mm]["port_batch"] for mm in allm if mm in mon_by_class}
        mt = {mm: mon_by_class[mm]["effective_tier"] for mm in allm if mm in mon_by_class}
        own_tier = 1
        kern = list(ann.get("kern", []))
        if r.get("uses_encounter_rng"):
            kern.append("encounter-private Rng (EncounterModel.Rng, seeded Models/EncounterModel.cs:202-216; ported, sim/specs/rng-encounter-private.md)")
        if r.get("slots"):
            kern.append("encounter Slots + CombatState.SortEnemiesBySlotName (Combat/CombatState.cs:958-964) after each slotted AddCreature (Combat/CombatManager.cs:6999-7002)")
        if summoned and name != "MockAttackAndSummonEncounter":
            kern.append("mid-combat summons into encounter slots (EncounterModel.GetNextSlot, Models/EncounterModel.cs:195-198, or a last-free-slot scan)")
        if "SaveCustomState" in ea.get("override_lines", {}) or name.startswith("BattlewornDummyEventV"):
            kern.append("encounter custom state RanOutOfTime (SaveCustomState/LoadCustomState) read back by the event")
            own_tier = max(own_tier, 2)
        if "CalculateGoldProportion" in ea.get("override_lines", {}):
            own_tier = max(own_tier, 3)
        rt = r.get("room_type")
        gold = None
        if r.get("min_gold_reward") is not None:
            gold = {"min": r["min_gold_reward"], "max": r["max_gold_reward"], "source": "override"}
        elif rt in GOLD_DEFAULT:
            gold = {"min": GOLD_DEFAULT[rt][0], "max": GOLD_DEFAULT[rt][1], "source": "EncounterModel default by RoomType (Models/EncounterModel.cs:45-83); A3 Poverty x0.75 then (int)"}
        tier = max([own_tier] + list(mt.values())) if mt else own_tier
        batch = max(mb.values()) if mb else 9
        if r["is_mock"]:
            batch = 9
        if name in ("DeprecatedEncounter", "BattlewornDummyEventEncounter"):
            batch = 9 if name == "DeprecatedEncounter" else 6
        if r.get("ported"):
            batch = 0
        elif batch == 0:
            batch = 1
        v = vdiff["encounters"].get(name)
        if v is None or v["status"] == "only_v0.111.0":
            vd = {"class": "new", "note": "file only in v0.111.0: the v0.107.1 BattlewornDummyEventEncounter (one class with a DummySetting enum) was split into V1/V2/V3 subclasses; encounter ids changed"}
        elif v["status"] == "same_tokens":
            vd = {"class": "same", "note": "normalized numbers, enums and model calls equal"}
        elif name == "BattlewornDummyEventEncounter":
            vd = {"class": "rule-change", "note": "now abstract (custom state only); v0.107.1 picked BattleFriendV1/V2/V3 from a Setting enum"}
        elif name == "SlitheringStranglerNormal":
            vd = {"class": "rule-change", "note": "Tags {Jaxfruit, Slimes} added (none in v0.107.1): changes AddWithoutRepeatingTags during room generation"}
        elif name == "SnappingJaxfruitNormal":
            vd = {"class": "rule-change", "note": "Tags {Mushroom} -> {Mushroom, Jaxfruit}: changes AddWithoutRepeatingTags during room generation"}
        elif name == "PhrogParasiteElite":
            vd = {"class": "cosmetic", "note": "camera scaling 0.95 added"}
        else:
            vd = {"class": "cosmetic", "note": "see vdiff"}
        reach = None
        if r["kind"] in ("weak", "normal", "elite", "boss"):
            reach = "act pool: %s %s" % (r.get("act"), r["kind"])
        elif r["kind"] == "event":
            reach = "event: " + ", ".join(r.get("referenced_by_events") or []) + ((" (" + EVENT_ENTRY[name] + ")") if name in EVENT_ENTRY else "")
        elif r["kind"] == "unlisted":
            reach = "listed in no act: unreachable in a standard run"
        elif r["kind"] == "mock":
            reach = "mock (test seam)"
        else:
            reach = r["kind"]
        e = {
            "id": r["id"], "model_id": r["model_id"], "class": name, "file": r["file"],
            "is_mock": r["is_mock"], "is_abstract": r["is_abstract"], "ported": r["ported"],
            "kind": r["kind"], "room_type": rt, "is_weak": r.get("is_weak"), "act": r.get("act"), "act_index": r.get("act_index"),
            "reachable_via": reach,
            "tags": r.get("tags", []), "slots": r.get("slots", []),
            "composition": ann["composition"],
            "generate_monsters": ("%s:%s-%s" % (r["file"], ea.get("generate_line"), ea.get("generate_end_line"))) if ea.get("generate_line") else None,
            "composition_rng": ann.get("rng", []),
            "uses_encounter_rng": r.get("uses_encounter_rng"), "encounter_rng_calls": r.get("encounter_rng_calls", []),
            "monsters": allm, "summoned_during_combat": summoned,
            "monster_port_batches": mb, "monster_effective_tiers": mt,
            "rewards": {"should_give_rewards": r.get("should_give_rewards"), "gold": gold,
                        "gold_proportion_override": "CalculateGoldProportion" in ea.get("override_lines", {})},
            "event_entry": EVENT_ENTRY.get(name),
            "kernel_requirements": kern,
            "kernel_features": kernel_features(kern),
            "prompts": [p for mm in allm if mm in mon_by_class for p in mon_by_class[mm]["prompts"]],
            "mechanics": enc_mechanics(name, r, ann, allm, summoned, gold, rt, ea),
            "rng_streams": (["EncounterModel.Rng (private, per encounter instance): " + "; ".join(ann.get("rng", []))] if r.get("uses_encounter_rng") else []),
            "ascension_sensitivity": enc_ascension(r, rt, allm, mon_by_class),
            "version_delta_v107": vd,
            "tier": tier, "port_batch": batch,
            "notes": ann.get("notes", ""),
            "open_questions": ann.get("oq", []),
        }
        out.append(e)
    return out



def enc_mechanics(name, r, ann, allm, summoned, gold, rt, ea):
    out = ["spawn order: " + "; ".join(ann["composition"])] if ann["composition"] else []
    if ann.get("rng"):
        out.append("composition/starter draws on EncounterModel.Rng: " + "; ".join(ann["rng"]))
    if r.get("slots"):
        out.append("Slots " + ", ".join(r["slots"]) + " fix turn order (enemy list re-sorted by slot index after each slotted add)")
    if summoned:
        out.append("mid-combat spawns: " + ", ".join(summoned))
    if r.get("tags"):
        out.append("EncounterTags " + ", ".join(r["tags"]) + " (room generation: AddWithoutRepeatingTags)")
    if r.get("should_give_rewards") is False:
        out.append("no combat rewards (ShouldGiveRewards false)")
    if gold and gold.get("source") == "override":
        out.append("gold reward override %s-%s" % (gold["min"], gold["max"]))
    if "CalculateGoldProportion" in ea.get("override_lines", {}):
        out.append("CalculateGoldProportion override (gold depends on escapes/theft)")
    if "SaveCustomState" in ea.get("override_lines", {}) or name.startswith("BattlewornDummyEventV"):
        out.append("custom state saved for the parent event")
    return out


def enc_ascension(r, rt, allm, mon_by_class):
    via = {}
    for mm in allm:
        m = mon_by_class.get(mm)
        if not m:
            continue
        a = m["ascension_sensitivity"]
        if a.get("A8_ToughEnemies") or a.get("A9_DeadlyEnemies"):
            via[mm] = {"A8": len(a.get("A8_ToughEnemies") or []), "A9": len(a.get("A9_DeadlyEnemies") or [])}
    engine = []
    if rt in ("Monster", "Elite", "Boss") and r.get("should_give_rewards") is not False:
        engine.append("Poverty (A3): gold reward x0.75 then (int) (Models/EncounterModel.cs:56-58, :75-77)")
    if rt == "Boss":
        engine.append("DoubleBoss (A10): the last act also draws a SecondBoss from its boss pool (Runs/RunManager.cs:4082-4086)")
    return {"levels_used_in_file": [], "engine": engine, "via_monsters": via,
            "A18_plus": "none (maximum ascension is 10: Entities.Ascension/AscensionManager.cs:8)",
            "summary": "encounter file has no ascension branch; %d spawned monster(s) scale at A8/A9" % len(via)}


GAUSS_CIT = "Random/Rng.cs:188-211"
UNKNOWN_CIT = "Map/MapPointTypeCounts.cs:57-60"


def unlock_rule(raw):
    raw = (raw or "").strip()
    if re.fullmatch(r"\{\s*return true;\s*\}", raw):
        return "always unlocked"
    m = re.search(r"IsEpochRevealed<(\w+)>", raw)
    if m:
        return "unlocked once epoch %s is revealed (UnlockState)" % m.group(1)
    return "unparsed (see citation)"


def map_point_rule(stmts):
    text = " ".join(stmts or [])
    out = {"draw_order": "rest-site count first, then unknown-room count (same mapRng)"}
    m = re.search(r"restCount = mapRng\.NextGaussianInt\((\d+), (\d+), (\d+), (\d+)\)", text)
    if m:
        out["rest_sites"] = {"draw": "NextGaussianInt", "mean": int(m.group(1)), "std_dev": int(m.group(2)), "min": int(m.group(3)), "max": int(m.group(4)),
                             "semantics": "rejection loop, 2 NextDouble per attempt, Math.Round, bounds inclusive", "citation": GAUSS_CIT}
    m = re.search(r"restCount = mapRng\.NextInt\((\d+), (\d+)\)", text)
    if m:
        out["rest_sites"] = {"draw": "NextInt", "min_inclusive": int(m.group(1)), "max_exclusive": int(m.group(2))}
    m = re.search(r"unknownCount = MapPointTypeCounts\.StandardRandomUnknownCount\(mapRng\)( - (\d+))?", text)
    if m:
        out["unknown_rooms"] = {"draw": "StandardRandomUnknownCount = NextGaussianInt(mean 12, std_dev 1, min 10, max 14)",
                                "offset": -int(m.group(2)) if m.group(2) else 0, "citation": UNKNOWN_CIT}
    m = re.search(r"return new MapPointTypeCounts\((\d+), (\d+)\)", text)
    if m:
        out = {"constant": {"unknown_rooms": int(m.group(1)), "rest_sites": int(m.group(2))}}
    return out

def build_acts():
    acts = []
    for r in raw_a["entries"]:
        ol = r["override_lines"]
        f = r["file"]
        acts.append({
            "id": r["id"], "model_id": r["model_id"], "class": r["class"], "file": f,
            "index": r.get("index"), "is_default": r.get("is_default"),
            "unlock": {"rule": unlock_rule(r.get("is_unlocked_raw")), "citation": "%s:%s" % (f, ol.get("IsUnlocked"))},
            "base_number_of_rooms": r.get("base_number_of_rooms"), "number_of_weak_encounters": r.get("number_of_weak_encounters"),
            "pools": {"weak": r.get("encounters_weak", []), "normal": r.get("encounters_normal", []),
                      "elite": r.get("encounters_elite", []), "boss": r.get("encounters_boss", [])},
            "pools_citation": "%s:%s" % (f, ol.get("GenerateAllEncounters")),
            "boss_discovery_order": r.get("boss_discovery_order", []), "boss_discovery_order_citation": "%s:%s" % (f, ol.get("BossDiscoveryOrder")),
            "ancients": r.get("ancients", []), "ancient_unlock_gates": r.get("ancient_unlock_gates", []),
            "ancients_citation": "%s:%s" % (f, ol.get("GetUnlockedAncients")),
            "events_count": len(r.get("events") or []), "events_citation": "%s:%s" % (f, ol.get("AllEvents")),
            "map_point_types": dict(map_point_rule((r.get("map_params") or {}).get("get_map_point_types")),
                                    elites={"default": 5, "A1_SwarmingElites": 8, "rule": "round(5 x 1.6) at A1+", "citation": "Map/MapPointTypeCounts.cs:23-29"},
                                    shops={"default": 3, "citation": "Map/MapPointTypeCounts.cs:32-36"},
                                    citation="%s:%s" % (f, (r.get("map_params") or {}).get("get_map_point_types_line"))),
            "ascension_sensitivity": {"levels_used_in_file": [], "engine": ["SwarmingElites (A1): NumOfElites 5 -> 8 (Map/MapPointTypeCounts.cs:29)",
                                                                           "DoubleBoss (A10): the last act gets SecondBoss = UpFront.NextItem(boss pool minus Boss) (Runs/RunManager.cs:4082-4086)"],
                                      "A18_plus": "none (maximum ascension is 10)"},
            "first_run_order": r.get("first_run_order", []),
            "first_run_citation": "%s:%s" % (f, ol.get("ApplyActDiscoveryOrderModifications")),
        })
    return acts


ACT_RULES = [
    {"rule": "pool filters: weak = RoomType.Monster && IsWeak; regular = RoomType.Monster && !IsWeak; elite = RoomType.Elite; boss = RoomType.Boss (all over GenerateAllEncounters order)", "citation": "Models/ActModel.cs:123-129"},
    {"rule": "rooms per act = BaseNumberOfRooms (-1 in multiplayer); floors = rooms + 2", "citation": "Models/ActModel.cs:185-198"},
    {"rule": "GenerateRooms(rng = RunRng.UpFront): events (act + shared, epoch-filtered) UnstableShuffle; normalEncounters = NumberOfWeakEncounters draws from a weak GrabBag, then draws from a regular GrabBag up to GetNumberOfRooms; eliteEncounters = 15 draws from an elite GrabBag; each bag refills (all weight 1.0) when empty; Boss = rng.NextItem(boss pool); Ancient = rng.NextItem(unlocked ancients ++ shared ancient subset)", "citation": "Models/ActModel.cs:241-323; Runs/RunManager.cs:4077"},
    {"rule": "AddWithoutRepeatingTags: GrabAndRemove with predicate 'shares no EncounterTag with the previous list entry and is not that entry'; rejection sampling (one draw per attempt); unconstrained GrabAndRemove when no element qualifies", "citation": "Models/ActModel.cs:362-373; Helpers/GrabBag.cs:34-82"},
    {"rule": "ApplyDiscoveryOrderModifications (Standard mode, TestMode off): Boss := first BossDiscoveryOrder entry the profile has not seen; then the act's own first-run hook (Overgrowth only, NumberOfRuns == 0)", "citation": "Models/ActModel.cs:338-358; Runs/RunManager.cs:4078-4081, :4088-4108; Models.Acts/Overgrowth.cs:110-127"},
    {"rule": "A10 DoubleBoss: last act gets SecondBoss = UpFront.NextItem(boss pool minus Boss)", "citation": "Runs/RunManager.cs:4082-4086"},
    {"rule": "map point counts (act GetMapPointTypes on the map Rng): rest-site count drawn first, then unknown-room count; NextGaussianInt(mean, stdDev, min, max) = Box-Muller on 2 NextDouble per attempt, Math.Round, retried until inside [min, max]; StandardRandomUnknownCount = NextGaussianInt(12, 1, 10, 14)", "citation": "Random/Rng.cs:188-211; Map/MapPointTypeCounts.cs:57-60"},
    {"rule": "shared ancients: Darv subset handed to acts 2..n via UpFront.NextInt(count + 1) takes from an UnstableShuffle", "citation": "Runs/RunManager.cs:4055-4072"},
    {"rule": "room pulls: NextNormalEncounter = normalEncounters[visited % count] (the first NumberOfWeakEncounters monster rooms therefore get weak encounters); NextEliteEncounter likewise; NextBossEncounter returns SecondBoss once a boss was visited", "citation": "Rooms/RoomSet.cs:74-104"},
    {"rule": "act choice per index (StartRunLobby.BeginRunLocally): rng = Rng(GetDeterministicHashCode(seed) + GetDeterministicHashCode(\"act_selection\")); for each ModelDb.ActsByIndex slot take the first unlocked non-default act not yet in Progress.DiscoveredActs (single player, TestMode off, profile-dependent), else rng.NextItem(unlocked acts of that slot); then act 0 := GetAct(lobby Act1), a no-op in v0.111.0 (the act dropdown is hidden, defaults to \"random\" and is copied into Act1 for single player and host; GetAct(\"random\") is null); the same rng then resolves RandomCharacter picks", "citation": "Models/ActModel.cs:470-505; Models/ModelDb.cs:244-275; Random/Rng.cs:42-45; Multiplayer.Game.Lobby/StartRunLobby.cs:236-242, :703-715, :737-748; Nodes.Screens.CharacterSelect/NCharacterSelectScreen.cs:1116, :1436-1439; Nodes.Screens.CharacterSelect/NActDropdown.cs:45-49"},
    {"rule": "encounter Rng seed (v0.111.0) = (ulong)((long)RunRng.Seed + (long)TotalFloor) + GetDeterministicHashCode(Id.Entry); v0.107.1 used (uint)((int)Seed + TotalFloor + old hash)", "citation": "Models/EncounterModel.cs:202-216; v0.107.1 Models/EncounterModel.cs:259-270"},
    {"rule": "enemy order: after each AddCreature with a SlotName, List.Sort by Slots.IndexOf(SlotName) (introsort; insertion sort below 17 elements is stable, unknown slots compare as -1)", "citation": "Combat/CombatState.cs:958-964; Combat/CombatManager.cs:6990-7005"},
]


def main2():
    mon = json.load(open(common.stage1_path("monsters.classified.json")))["entries"]
    enc = build_encounters(mon)
    acts = build_acts()
    env = {
        "schema": "sts2sim.inventory.classified/v1", "kind": "encounters", "game_version": raw_e["game_version"],
        "raw_manifest": "sim/specs/inventory/encounters.json", "raw_source_digest": raw_e["source_digest"],
        "raw_acts_manifest": "sim/specs/inventory/acts.json", "raw_acts_source_digest": raw_a["source_digest"],
        "generated": "2026-11-01", "generated_by": common.generator("monsters", "extract.py", "enc_extract.py", "vdiff.py", "powerdeps.py", "gen.py"),
        "decompile_root": common.DECOMPILE_ROOT,
        "citation_format": "<ns-dir>/<File>.cs:<line>, MegaCrit.Sts2.Core. prefix dropped",
        "companion_doc": "docs/sim-port/inventory-encounters.md",
        "tier_legend": TIER_LEGEND, "batch_legend": BATCH_LEGEND,
        "batch_rule": "encounter port_batch = max port_batch of every monster it spawns at start or mid-combat (monsters.classified.json)",
        "count": len(enc), "entries": enc,
        "acts": acts, "act_selection_rules": ACT_RULES,
        "event_combat_rules": [
            {"rule": "events with LayoutType Combat (PunchOff, TheLanternKey, TheArchitect) build their CanonicalEncounter at event entry, after BeginEvent: GenerateMonstersWithSlots (encounter Rng), new CombatState, AddPlayer, then CreateCreature + AddCreature per monster (one RunRng.Niche HP draw each); entering combat later reuses that CombatState (ShouldCreateCombat false)",
             "citation": "Rooms/EventRoom.cs:161-183; Multiplayer.Game/EventSynchronizer.cs:470-473; Multiplayer.Game/EventCombatSynchronizer.cs:83-128, :175-183"},
            {"rule": "other event combats (BattlewornDummy, DenseVegetation, FakeMerchant) create the CombatState only when EnterCombatWithoutExitingEvent fires (CreateCombatState(canonicalEncounter.ToMutable()))",
             "citation": "Models/EventModel.cs:812-838; Multiplayer.Game/EventCombatSynchronizer.cs:175-178"},
            {"rule": "TheArchitect never calls EnterCombatWithoutExitingEvent: its Architect creature is display-only (animations only)",
             "citation": "Models.Events/TheArchitect.cs:162, :322, :783-785, :1115"},
        ],
        "reward_rules": [
            {"rule": "gold by RoomType: Monster 10-20, Elite 35-45, Boss 100-100; A3 Poverty multiplies by 0.75 then (int)", "citation": "Models/EncounterModel.cs:45-83; Helpers/AscensionHelper.cs:12"},
            {"rule": "gold proportion = CalculateGoldProportion(combatState): default 1 - EscapedCreatures.Count / SpawnedEnemies.Count, SpawnedEnemies = distinct canonical monster models", "citation": "Rooms/CombatRoom.cs:831; Models/EncounterModel.cs:324-327, :343-360; Combat/CombatState.cs:722"},
            {"rule": "ShouldGiveRewards false: BattlewornDummyEventV1/V2/V3 (and MockNoRewardsEncounter)", "citation": "Models.Encounters/BattlewornDummyEventV1Encounter.cs:12"},
        ],
    }
    json.dump(env, open(common.stage1_path("encounters.classified.json"), "w"), indent=1, ensure_ascii=False)
    print("encounters", len(enc), "acts", len(acts))


if __name__ == "__main__":
    main()
    main2()
