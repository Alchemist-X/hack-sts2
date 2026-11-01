"""Merge raw manifest + mechanical features + semantic annotations into powers.classified.json (scratch builder)."""

import os as _os
import sys as _sys
_sys.path.insert(0, _os.path.join(_os.path.dirname(_os.path.abspath(__file__)), ".."))
import common  # noqa: E402
import importlib
import json
import os
import re
import sys
from collections import Counter, OrderedDict, defaultdict

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
sys.path.insert(0, common.INVENTORY_TOOLS)

import annot_lib  # noqa: E402
import cs_source  # noqa: E402

DECOMP = common.DECOMPILE_ROOT
INV = common.INVENTORY
OUT_JSON = common.stage1_path("powers.classified.json")

for i in range(15):
    importlib.import_module("ann_%02d" % i)
ANN = annot_lib.ANN

BATCH_OF_LABEL = OrderedDict([
    ("core1", 1), ("core2", 2), ("temp", 2), ("doom", 2), ("cardops", 3), ("history", 4), ("cardgen", 5),
    ("afflict", 5), ("summon", 5), ("orbs", 6), ("osty", 6), ("stars", 6), ("forge", 6), ("prompt", 7),
    ("run", 8), ("monster", 9), ("unreferenced", 10), ("crosscut", 10), ("mock", None),
])
BATCH_TITLES = OrderedDict([
    (1, "existing kernel: pure numbers/hooks the sim already has"),
    (2, "combat-kernel surface: side-turn participants, missing combat hooks/Cmd overloads, temporary-stat family, Doom"),
    (3, "card-model operations: keywords, upgrade/downgrade, clones, fixed-card creation, auto-play, result-location hook"),
    (4, "combat history readers"),
    (5, "pool card generation, afflictions, mid-combat monster summons"),
    (6, "character subsystems: orbs (Defect), Osty (Necrobinder), stars and forge (Regent)"),
    (7, "player-choice prompts"),
    (8, "run-layer coupled (combat-end rewards, gold, deck)"),
    (9, "monster-coupled: port together with the named monster"),
    (10, "out of single-player scope or deferred: multiplayer-only powers, unreferenced content, the UI-ordered RollingBoulder"),
])

PRESENT_MARK = "(present"
PRESENT_FEATURES = [
    "CardInstance.HasTag", "PowerModel.AmountOnTurnStart", "CardPlay.IsLastInSeries", "CardPlay.Resources.StarsSpent",
    "CardPlay.Resources.EnergySpent", "CardPlay.IsAutoPlay", "CardInstance.IsDupe", "CombatState.RoundNumber",
    "PlayerCombatState.TurnNumber", "RngCollectionExtensions.UnstableShuffle", "CreatureCmd.Stun(creature, stunMove, nextMoveId)",
    "PowerInstanceType.InstancedPerApplier", "PowerModel.Target", "PowerInstanceType.Instanced stacking",
    "CardEnergyCost.CostsX", "CardPileCmd.AddGeneratedCardToCombat", "CreatureCmd.Damage (single-target per hit)",
    "CardCmd.Exhaust",
]
SUBSYSTEM_T3 = [
    "choice API", "CardSelectCmd", "Stars subsystem", "PlayerCmd.GainStars", "Hook.AfterStars", "OrbQueue", "OrbCmd",
    "Hook.ModifyOrbValue", "Hook.AfterOrbEvoked", "FocusPower (orbs)", "OrbModel", "Osty / pets", "OstyCmd", "ForgeCmd",
    "Hook.AfterForge", "CombatHistory", "Afflictions", "CardFactory", "CreatureCmd.Add", "CombatState.CreateCreature",
    "run layer", "run effects", "run deck", "run gold", "run history", "MapPointHistory", "Hook.AfterCombatEnd", "Relic query",
    "potions runtime", "Hook.BeforePotionUsed",
]
SURFACE_T2 = [
    "HookParam", "Hook.", "CardCmd.", "CardPileCmd.Add", "CardPileCmd.AutoPlayFromDrawPile", "CardPileCmd.AddToCombatAndPreview",
    "CardPileCmd.RemoveFromDeck", "CreatureCmd.Escape", "BlockVar overload", "DamageVar", "PetOwner", "Pets", "IsPet", "HpDisplay",
    "SetMoveImmediate", "StateLog", "CreateClone", "CreateCard", "CreateInHand", "Soul.Create", "ITemporaryPower",
    "TemporaryStrengthPower base", "TemporaryDexterityPower base", "CardLocation", "PowerModel.IsVisible", "PlayersTakingExtraTurn",
    "EncounterDef state", "auto pre-play phase", "auto post-play phase", "AllCards", "IsDupe", "IsClone", "Allies",
    "CardModel.", "GetPowerInstances", "card replay", "StackType computed", "Type computed",
]


def load(name):
    with open(os.path.join(common.STAGE1 if name == "kernel-gaps.json" else INV, name)) as fh:
        return json.load(fh)


def cmd_status():
    k = load("kernel-gaps.json")
    st = defaultdict(set)
    for cls, lst in k["cmd_facades"].items():
        for m in lst:
            st[cls + "." + m["name"]].add(m["status"])
    return {a: sorted(b) for a, b in st.items()}


def model_class_index():
    idx = {}
    for kind, fn in (("cards", "cards.json"), ("monsters", "monsters.json"), ("relics", "relics.json"),
                     ("potions", "potions.json"), ("events", "events.json"), ("encounters", "encounters.json"),
                     ("afflictions", "afflictions.json"), ("orbs", "orbs.json"), ("enchantments", "enchantments.json"),
                     ("powers", "powers.json")):
        for e in load(fn)["entries"]:
            idx.setdefault(e["class"], set()).add(kind)
    return idx


UI_MEMBERS = {"ExtraHoverTips", "Title", "Description", "SmartDescriptionLocKey", "RemoteDescriptionLocKey", "OriginModel",
              "AmountLabelColor", "DisplayAmount", "IsVisibleInternal", "ShouldPlayVfx", "PlayVfx", "StartDoomAnim",
              "WaitForTween", "Vfx", "RevealWrigglersAfterDeathAnim", "RevealReplacementAfterDeathAnim", "DoFadeOutOnAllSegments",
              "PlayVfxAndThenRemoveNodes", "UpdateCreaturePositions", "FlipScale", "CalculateTotalDamageNextTurn"}


def outgoing_refs(entry, idx):
    path = os.path.join(DECOMP, "MegaCrit.Sts2.Core." + entry["file"])
    cf = cs_source.CsFile(path, entry["file"])
    td = cf.main_type()
    text = cf.masked
    spans = []
    for m in td.members:
        nm = cs_source.generated_owner(m.name) or m.name
        if m.kind == "type" and m.type_decl is not None:
            nm2 = cs_source.generated_owner(m.name)
            if nm2 in UI_MEMBERS:
                spans.append((m.start, m.end))
            continue
        if nm in UI_MEMBERS:
            spans.append((m.start, m.end))
    buf = list(text)
    for a, b in spans:
        for i in range(a, b):
            if buf[i] != "\n":
                buf[i] = " "
    logic = "".join(buf)
    head_end = td.body_start
    strong = defaultdict(lambda: defaultdict(set))
    weak = defaultdict(set)
    for m in re.finditer(r"\b([A-Z]\w+)\b", logic[head_end:]):
        name = m.group(1)
        if name == entry["class"] or name not in idx:
            continue
        start = head_end + m.start()
        pre = logic[max(0, start - 40):start]
        gm = re.search(r"(\w+)\s*<\s*(?:[\w.]+\.)?$", pre)
        if gm:
            meth = gm.group(1)
            if meth in ("TaskAwaiter", "IReadOnlyList", "IEnumerable", "List", "Enumerator", "IEnumerator"):
                continue
            mode = {"Apply": "apply", "HasPower": "read", "GetPower": "read", "GetPowerAmount": "read",
                    "GetPowerInstances": "read", "Remove": "remove", "PowerVar": "var", "FromPower": "tip",
                    "Afflict": "afflict", "AfflictAndPreview": "afflict", "Channel": "channel", "Monster": "modeldb",
                    "Card": "modeldb", "Power": "modeldb", "Potion": "modeldb", "Relic": "modeldb", "Affliction": "modeldb",
                    "CreateCard": "create", "AddToCombatAndPreview": "create", "Add": "spawn", "GetRelic": "read",
                    "FromCard": "tip", "FromOrb": "tip", "FromAffliction": "tip", "CardPool": "modeldb",
                    "FromCardWithCardHoverTips": "tip", "Encounter": "modeldb"}.get(meth, "generic:" + meth)
        elif re.search(r"\bis\s+$", pre):
            mode = "is"
        elif re.search(r"\bas\s+$", pre):
            mode = "as"
        elif re.search(r"\(\s*$", pre) and logic[start + len(name):start + len(name) + 1] == ")":
            mode = "cast"
        elif logic[start + len(name):start + len(name) + 1] == ".":
            mode = "static"
        else:
            mode = "ident"
        kinds = idx[name]
        kind = sorted(kinds)[0] if len(kinds) == 1 else ("powers" if "powers" in kinds else sorted(kinds)[0])
        if mode in ("tip", "var"):
            weak[kind].add(name)
        else:
            strong[kind][name].add(mode)
    for kind in list(weak):
        weak[kind] = set(n for n in weak[kind] if n not in strong.get(kind, {}))
        if not weak[kind]:
            del weak[kind]
    return strong, weak


STRONG_IN = {"apply", "read", "remove", "modeldb", "is", "as", "cast", "static", "new", "inherit", "typeof"}


NON_RULE_KINDS = {"achievements", "badges", "models_core", "pools"}


def referrer_table(feat):
    out = OrderedDict()
    dep_out = OrderedDict()
    for key, modes in feat["referrers"].items():
        kind, cls = key.split("/", 1)
        if cls == "AbstractModelSubtypes" or kind in NON_RULE_KINDS:
            continue
        noise = ("generic:TaskAwaiter", "generic:IReadOnlyList", "generic:IEnumerable", "generic:List",
                 "generic:IEnumerator", "generic:Enumerator")
        ms = sorted(set(m for m in modes if m in STRONG_IN or (m.startswith("generic:") and m not in noise)))
        if not ms:
            ms = ["hover-tip/var only"]
        out.setdefault(kind, []).append({"class": cls, "modes": ms})
        if ms != ["hover-tip/var only"] and not kind.endswith("(mock)"):
            dep_out.setdefault(kind, []).append(cls)
    return out, dep_out


def derive_tier(reqs, prompts):
    t = 1
    for r in reqs:
        if PRESENT_MARK in r:
            continue
        if any(k in r for k in SUBSYSTEM_T3):
            t = max(t, 3)
        elif any(k in r for k in SURFACE_T2):
            t = max(t, 2)
    if prompts:
        t = max(t, 3)
    return t


def main():
    raw = load("powers.json")
    feats = json.load(open(common.work_path("powers", "features.json")))
    vdiff = json.load(open(common.work_path("powers", "vdiff.json")))
    hooks = {h["name"]: h for h in load("hooks.json")["hooks"]}
    disp = {d["name"]: d for d in load("hooks.json")["dispatchers"]}
    cst = cmd_status()
    idx = model_class_index()
    ported = {e["id"] for e in load("ported.json")["entries"]["powers"]}
    entries = []
    warnings = []
    by_class = {}
    for e in raw["entries"]:
        c = e["class"]
        a = ANN[c]
        f = feats[c]
        reqs = list(a["req"])
        reqs = [r for r in reqs if "HookParam.ShouldPlay.AutoPlayType" not in r]
        drop_ui_hooks = {("IllusionPower", "AfterCombatEnd"), ("SandpitPower", "AfterOstyRevived")}
        for h in f["hooks"]:
            if not h["in_sim"] and (c, h["hook"]) not in drop_ui_hooks:
                tag = "Hook." + h["hook"]
                if not any(r.startswith(tag) for r in reqs):
                    reqs.append(tag)
        for cm in f["cmds"]:
            call = cm["call"]
            if call.startswith("Hook.") or call.split(".")[0] in ("Cmd", "SfxCmd", "VfxCmd", "TalkCmd", "ThinkCmd"):
                continue
            stt = cm["status"]
            if stt in (["missing"], ["partial"]):
                if not any(r.startswith(call) for r in reqs):
                    reqs.append(call + (" (partial in sim)" if stt == ["partial"] else ""))
        for h in f["hooks"]:
            pass
        normed = []
        for r in reqs:
            if "(present" not in r and any(r.startswith(pf) for pf in PRESENT_FEATURES):
                r = r + " (present)"
            normed.append(r)
        reqs = normed
        vd = a["vd"]
        vstat = vdiff.get(c, {}).get("status")
        if vd is None:
            if vstat == "same_tokens":
                vd = ("same", "")
            elif vstat == "new_in_v111":
                vd = ("new", "not in v0.107.1")
            else:
                warnings.append("no version note for differing %s" % c)
                vd = ("unreviewed", json.dumps(vdiff.get(c, {}).get("diff"))[:200])
        if vstat == "same_tokens" and vd[0] not in ("same", "cosmetic"):
            warnings.append("vd %s for same_tokens %s" % (vd[0], c))
        derived = derive_tier(reqs, a["prompts"])
        tier = a["tier"]
        if not e["is_mock"] and derived != tier and tier != 4:
            warnings.append("tier mismatch %s manual=%s derived=%s reqs=%s" % (c, tier, derived, reqs))
        label = a["batch"]
        if label == "core":
            label = "core1" if tier == 1 else "core2"
        base_label = label.split(":")[0]
        if base_label not in BATCH_OF_LABEL:
            warnings.append("unknown batch label %s for %s" % (label, c))
        batch = BATCH_OF_LABEL.get(base_label)
        strong, weak = outgoing_refs(e, idx)
        referrers, dep_referrers = referrer_table(f)
        hook_rows = []
        for h in f["hooks"]:
            hook_rows.append(OrderedDict([("hook", h["hook"]), ("line", h["line"]), ("scope", h["scope"]),
                                          ("in_sim", h["in_sim"]),
                                          ("dropped_ui_only", (c, h["hook"]) in drop_ui_hooks)]))
        rec = OrderedDict()
        for k in ("id", "model_id", "class", "file", "class_line", "base_class", "is_mock", "is_abstract", "registered",
                  "power_type", "stack_type", "instance_type", "allow_negative", "is_temporary", "dynamic_fields",
                  "dynamic_vars", "constants", "lines_logic"):
            rec[k] = e.get(k)
        rec["ported"] = e["id"] in ported
        rec["mechanics"] = a["mechanics"]
        rec["prompts"] = a["prompts"]
        rec["kernel_requirements"] = reqs
        rec["rng_streams"] = a["rng"]
        rec["ascension_sensitivity"] = a["asc"]
        rec["version_delta_v107"] = OrderedDict([("status", vd[0]), ("note", vd[1])])
        rec["tier"] = tier
        rec["tier_derived_from_requirements"] = derived
        rec["batch_label"] = a["batch"]
        rec["port_batch"] = batch
        rec["hooks"] = hook_rows
        rec["non_hook_overrides"] = f["non_hook_overrides"]
        rec["override_lines"] = e["override_lines"]
        rec["cmd_calls"] = [OrderedDict([("call", cm["call"]), ("sim_status", cm["status"])]) for cm in f["cmds"]]
        rec["depends_on"] = OrderedDict((k, OrderedDict(sorted((n, sorted(m - {"ident"}) or ["ident"]) for n, m in v.items())))
                                        for k, v in sorted(strong.items()))
        rec["weak_refs"] = OrderedDict((k, sorted(v)) for k, v in sorted(weak.items()))
        rec["manual_deps"] = a["deps"]
        rec["referrers"] = referrers
        rec["referrer_counts"] = OrderedDict((k, len(v)) for k, v in referrers.items())
        rec["dependent_referrers"] = dep_referrers
        rec["non_rule_referrers"] = sorted(k for k in f["referrers"] if k.split("/", 1)[0] in NON_RULE_KINDS
                                           and not k.endswith("/AbstractModelSubtypes"))
        rec["core_referrers"] = f["core_referrers"]
        rec["notes"] = a["notes"]
        rec["open_questions"] = a["oq"]
        entries.append(rec)
        by_class[c] = rec

    # asc sensitivity default
    for rec in entries:
        if rec["ascension_sensitivity"] is None:
            path = os.path.join(DECOMP, "MegaCrit.Sts2.Core." + rec["file"])
            txt = open(path, encoding="utf-8-sig").read()
            if re.search(r"AscensionLevel\.|HasAscension|GetValueIfAscension|AscensionHelper", txt):
                warnings.append("ascension token in %s" % rec["class"])
                rec["ascension_sensitivity"] = "see file"
            else:
                rec["ascension_sensitivity"] = "none"

    # multiplayer-only reachability: every applier is a MultiplayerOnly card (or such a power)
    mp_cards = set()
    cards_dir = os.path.join(DECOMP, "MegaCrit.Sts2.Core.Models.Cards")
    for fn in sorted(os.listdir(cards_dir)):
        if fn.endswith(".cs") and "CardMultiplayerConstraint.MultiplayerOnly" in open(os.path.join(cards_dir, fn), encoding="utf-8-sig").read():
            mp_cards.add(fn[:-3])
    appliers = {}
    for rec in entries:
        ap = []
        for kind, lst in rec["referrers"].items():
            for r in lst:
                if any(m in ("apply", "modeldb", "cast") or m.startswith("generic:") for m in r["modes"]):
                    ap.append((kind, r["class"]))
        appliers[rec["class"]] = ap
    mp_only = set()
    for _ in range(6):
        for rec in entries:
            ap = appliers[rec["class"]]
            if rec["is_mock"] or not ap:
                continue
            if all((k == "cards" and n in mp_cards) or (k == "powers" and n in mp_only) for k, n in ap):
                mp_only.add(rec["class"])
    for rec in entries:
        rec["multiplayer_only"] = rec["class"] in mp_only
        rec["appliers"] = ["%s/%s" % x for x in appliers[rec["class"]]]
        if rec["multiplayer_only"]:
            in_scope_readers = [
                "%s/%s" % (k, n) for k, lst in rec["dependent_referrers"].items() for n in lst
                if not ((k == "cards" and n in mp_cards) or (k == "powers" and n in mp_only))]
            if in_scope_readers:
                rec["batch_raised_by"] = ["multiplayer-only applier, but read by in-scope models (%s): kept in its natural batch"
                                          % ", ".join(in_scope_readers)]
            else:
                rec["batch_raised_by"] = ["multiplayer-only: every applier is a MultiplayerOnly card (filtered out of single-player pools)"]
                rec["port_batch"] = 10
    envelope_mp_cards = sorted(mp_cards)

    # dependency closure on port batches (power -> power)
    def power_deps(rec):
        ds = set(rec["manual_deps"])
        for n in rec["depends_on"].get("powers", {}):
            ds.add(n)
        if rec["base_class"] in by_class:
            ds.add(rec["base_class"])
        return ds
    changed = True
    rounds = 0
    while changed and rounds < 20:
        changed = False
        rounds += 1
        for rec in entries:
            if rec["port_batch"] is None:
                continue
            for d in sorted(power_deps(rec)):
                dr = by_class.get(d)
                if dr is None or dr["ported"] or dr["port_batch"] is None:
                    continue
                if dr["port_batch"] > rec["port_batch"] and not (rec["port_batch"] == 9 and dr["port_batch"] <= 9):
                    if rec["port_batch"] != 9:
                        rec.setdefault("batch_raised_by", []).append("%s (batch %s)" % (d, dr["port_batch"]))
                        rec["port_batch"] = dr["port_batch"]
                        changed = True
    for rec in entries:
        for d in sorted(power_deps(rec)):
            dr = by_class.get(d)
            if dr and not dr["ported"] and dr["port_batch"] and rec["port_batch"] and dr["port_batch"] > rec["port_batch"] and rec["port_batch"] != 9:
                warnings.append("dep order violation %s(%s) -> %s(%s)" % (rec["class"], rec["port_batch"], d, dr["port_batch"]))

    # hook usage table
    hook_use = defaultdict(lambda: {"non_mock": [], "mock": []})
    for rec in entries:
        for h in rec["hooks"]:
            hook_use[h["hook"]]["mock" if rec["is_mock"] else "non_mock"].append(rec["class"])
    hook_table = []
    for name in sorted(hook_use, key=lambda n: (-len(hook_use[n]["non_mock"]), n)):
        hinfo = hooks.get(name, {})
        hook_table.append(OrderedDict([
            ("hook", name), ("abstract_model_line", hinfo.get("line")), ("scope", hinfo.get("scope")),
            ("in_sim", hinfo.get("in_sim")), ("dispatchers", hinfo.get("dispatchers")),
            ("dispatcher_in_sim", all(disp.get(d, {}).get("in_sim") for d in hinfo.get("dispatchers", []) or []) if hinfo.get("dispatchers") else None),
            ("powers_non_mock", len(hook_use[name]["non_mock"])), ("powers_mock", len(hook_use[name]["mock"])),
            ("classes", hook_use[name]["non_mock"]), ("mock_classes", hook_use[name]["mock"]),
        ]))

    envelope = OrderedDict()
    envelope["schema"] = "sts2sim.inventory.classified/v1"
    envelope["kind"] = "powers"
    envelope["game_version"] = raw["game_version"]
    envelope["base_manifest"] = "sim/specs/inventory/powers.json"
    envelope["base_manifest_source_digest"] = raw["source_digest"]
    envelope["join_key"] = "id"
    envelope["cross_reference_tree"] = "artifacts/knowledge/decompiled-v0.107.1/MegaCrit/sts2/Core/Models/Powers (v0.107.1, version_delta_v107 only)"
    envelope["companion_doc"] = "docs/sim-port/inventory-powers.md"
    envelope["generated_by"] = common.generator("powers", "features.py", "vdiff.py", "build.py")
    envelope["count"] = len(entries)
    envelope["field_legend"] = OrderedDict([
        ("mechanics", "effect primitives in game order, owner/target/timing stated"),
        ("prompts", "player prompts the power itself triggers: type, cmd, source pile, min/max select (expressions), hook site, follow-up"),
        ("kernel_requirements", "Cmd/Hook/subsystem features the sim must have first; '(present)' marks features the sim already has; Hook.X = virtual and/or dispatcher missing in sim; HookParam.SideTurnParticipants = the participants list dropped from the sim's side-turn hooks"),
        ("rng_streams", "RunRng/PlayerRng streams read, with the call and order"),
        ("ascension_sensitivity", "AscensionLevel use in the power file"),
        ("version_delta_v107", "same (identical token fingerprint), cosmetic (decompiler or UI/VFX-only differences, or hook signature gained an unused CardPlay parameter), rule-change, new (not in v0.107.1)"),
        ("tier", "1 pure numbers through existing sim facades; 2 needs game facades/hooks/params missing in the sim; 3 needs a prompt or a new subsystem; 4 unreadable or cross-cutting"),
        ("tier_derived_from_requirements", "mechanical tier from kernel_requirements keywords (cross-check)"),
        ("batch_label", "semantic grouping; monster:<Name> = port together with that monster"),
        ("port_batch", "proposed batch number (see batch_titles); raised to respect power->power dependencies; null for mocks"),
        ("depends_on", "outgoing references from the power's rule code (UI members excluded) by kind and mode"),
        ("weak_refs", "hover-tip / DynamicVar-only references (no rule dependency)"),
        ("referrers", "models that reference this power, by kind, with reference modes from their code (apply/read/remove/is/…); 'hover-tip/var only' carries no rule dependency"),
        ("dependent_referrers", "referrers whose port depends on this power (non-hover-tip modes)"),
        ("core_referrers", "non-model code referencing the power (UI, AutoSlay, DevConsole, DynamicVarSet)"),
    ])
    envelope["batch_titles"] = OrderedDict((str(k), v) for k, v in BATCH_TITLES.items())
    envelope["multiplayer_only_cards_v0_111_0"] = envelope_mp_cards
    envelope["hook_usage"] = hook_table
    envelope["entries"] = entries
    with open(OUT_JSON, "w") as fh:
        json.dump(envelope, fh, indent=1, ensure_ascii=False)
        fh.write("\n")
    with open(common.work_path("powers", "build_warnings.txt"), "w") as fh:
        fh.write("\n".join(warnings) + "\n")
    print("entries", len(entries), "warnings", len(warnings))
    print("\n".join(warnings[:200]))


if __name__ == "__main__":
    main()
