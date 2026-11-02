import os as _os
import sys as _sys
_sys.path.insert(0, _os.path.join(_os.path.dirname(_os.path.abspath(__file__)), ".."))
import common  # noqa: E402
import collections
import json
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)

import semlib  # noqa: E402
from glossary import GLOSSARY  # noqa: E402
import sem_a  # noqa: E402
import sem_b  # noqa: E402
import sem_c  # noqa: E402
import sem_d  # noqa: E402
import vdelta  # noqa: E402
from costs import COSTS  # noqa: E402

RAW = common.inventory_path("events.json")
RELICS = common.inventory_path("relics.json")
OUT_JSON = common.stage1_path("events.classified.json")

CMD_IGNORE = {"Cmd.Wait", "Cmd.CustomScaledWait", "CardCmd.PreviewCardPileAdd", "CardCmd.Preview",
              "CreatureCmd.TriggerAnim"}
ACT_INDEX = {"Overgrowth": 0, "Underdocks": 0, "Hive": 1, "Glory": 2}
VERSION_CLASSES = ("same", "cosmetic", "rule-change")
CATEGORIES = ("act_event", "shared_event", "start_ancient", "act_ancient", "shared_ancient", "final_event",
              "hook_injected_event", "deprecated", "mock")

TIER_DEF = {
    "basis": "Events cannot run in the sim today (no run layer). Tiers are therefore measured against the sim plus the "
             "W2 event core of docs/sim-port/design-run-layer.md 3.10-3.11: EventModel/AncientEventModel lifecycle, "
             "EventOption legality, the event Rng, and the basic run-state facades (HP, max HP, gold, fixed card "
             "add/remove, curse add, fixed relic obtain).",
    "1": "only the event core and basic run-state facades (pure numbers, fixed cards/relics, event Rng)",
    "2": "needs game facades that are not in the sim and not part of the core: relic grab bag, card reward "
         "generation, random upgrade/downgrade/clone, potion discard, relic removal, ancient option logic and "
         "unlock/profile queries, combat layout, run end",
    "3": "needs a player prompt (CardSelectCmd deck/grid prompt, rewards screen, abandon modal) or a new subsystem "
         "(event combat, merchant UI, Crystal Sphere minigame)",
    "4": "decompile-broken (lost static arrays) or cross-cutting content state that must be resolved first",
}

BATCH_DEF = {
    "0": "not ported (deprecated save placeholders, test mock)",
    "1": "W2 gate set: Neow and the act-2/3 ancients (option generation is a W2 oracle), TheArchitect (terminal "
         "victory) and the tier-1 events",
    "2": "tier-2 events without prompts (grab bag, card generation, random upgrades, potion/relic trades, profile input)",
    "3": "prompt events: deck prompts, grid picks and the rewards screen (needs the W1 resumable prompt API)",
    "4": "event combats and combat-layout events (needs event encounters/monsters and EventCombatSynchronizer)",
    "5": "special subsystems: Crystal Sphere minigame, FakeMerchant shop + FoulPotion rule, LanternKey -> "
         "WarHistorianRepy chain",
    "6": "blocked on IL re-verification of lost static arrays (ColossalFlower, TinkerTime)",
}

VERSION_DEF = {
    "same": "no source change visible: token fingerprints equal or only decompiler style differs",
    "cosmetic": "source changed without a single-player rule effect (renames, history logging, multiplayer-only "
                "branches, owner-only reward lists)",
    "rule-change": "single-player rules differ between v0.107.1 and v0.111.0",
}


def load_data():
    data = {}
    for mod in (sem_a, sem_b, sem_c, sem_d):
        for k, v in mod.DATA.items():
            if k in data:
                raise SystemExit("duplicate " + k)
            data[k] = v
    return data


def relic_pickup_prompts(relics_by_class, names):
    out = []
    for r in names:
        x = relics_by_class.get(r)
        if x is None:
            raise SystemExit("unknown relic " + r)
        toks = list(x.get("choice_tokens") or []) + list(x.get("offers_rewards") or [])
        if toks:
            out.append({"relic": r, "prompts": sorted(set(toks)), "file": x["file"]})
    return out


def resolve_list(cls, items, key="cite"):
    res = []
    for it in items:
        it = dict(it)
        if it.get(key) is not None:
            it[key] = semlib.resolve(cls, it[key])
        res.append(it)
    return res


def build():
    raw = json.load(open(RAW))
    relics = {e["class"]: e for e in json.load(open(RELICS))["entries"]}
    data = load_data()
    raw_classes = [e["class"] for e in raw["entries"]]
    missing = [c for c in raw_classes if c not in data]
    extra = [c for c in data if c not in raw_classes]
    if missing or extra:
        raise SystemExit("coverage: missing %s extra %s" % (missing, extra))
    entries = []
    for e in raw["entries"]:
        cls = e["class"]
        d = data[cls]
        cat = d["category"]
        if cat not in CATEGORIES:
            raise SystemExit("bad category %s %s" % (cls, cat))
        if d["version"]["class"] not in VERSION_CLASSES:
            raise SystemExit("bad version class " + cls)
        if d["tier"] not in (1, 2, 3, 4):
            raise SystemExit("bad tier " + cls)
        app = dict(d["appearance"])
        if app.get("is_allowed_cite"):
            app["is_allowed_cite"] = semlib.resolve(cls, app["is_allowed_cite"])
        app_out = collections.OrderedDict()
        app_out["pool"] = app.pop("pool")
        app_out["acts"] = e.get("acts") or []
        app_out["shared_pool"] = e.get("shared_pool")
        app_out["pool_status"] = e.get("pool_status")
        app_out["allowed_act_indices"] = app.pop("allowed_act_indices")
        app_out["is_allowed"] = app.pop("is_allowed")
        app_out["is_allowed_cite"] = app.pop("is_allowed_cite", None)
        app_out["unlock_gate"] = app.pop("unlock_gate")
        app_out["entry"] = app.pop("entry")
        app_out["one_per_run"] = cat in ("act_event", "shared_event", "hook_injected_event")
        if app:
            raise SystemExit("unknown appearance keys %s %s" % (cls, list(app)))
        for a in app_out["acts"]:
            if ACT_INDEX[a] not in app_out["allowed_act_indices"] and cat in ("act_event", "act_ancient", "start_ancient"):
                raise SystemExit("act index mismatch " + cls)
        streams = resolve_list(cls, d["rng_streams"])
        auto = []
        if cat not in ("mock", "deprecated"):
            auto.append("EventFramework")
            if cat in ("start_ancient", "act_ancient", "shared_ancient"):
                auto.append("AncientFramework")
            if any(s["stream"] == "EventRng" for s in streams):
                auto.append("EventRng")
            if d.get("layout") == "Combat":
                auto.append("CombatLayout")
            if d.get("combats"):
                auto.append("EventCombat")
            if any("Resume" in (o.get("next") or "") for o in d["options"]):
                auto.append("EventResume")
        explicit = list(d["kernel_requirements"])
        kr = [a for a in auto if not any(x == a or x.startswith(a + " ") for x in explicit)]
        for k in explicit:
            if k not in kr:
                kr.append(k)
        if cat not in ("mock", "deprecated"):
            src = open(semlib.event_path(cls)[0], encoding="utf-8-sig").read()
            seen = sorted(set(m.group(0) for m in re.finditer(r"\b\w*Cmd\.\w+", src)))
            covered = json.dumps(kr) + json.dumps(d["prompts"]) + json.dumps(d["options"])
            for t in seen:
                if t in CMD_IGNORE or t.startswith(("VfxCmd", "SfxCmd", "TalkCmd", "ThinkCmd")):
                    continue
                if t not in covered and t.split(".")[1] not in covered:
                    kr.append(t)
        cdeps = d.get("content_deps") or {}
        indirect = relic_pickup_prompts(relics, cdeps.get("relics", []))
        out = collections.OrderedDict()
        for k, v in e.items():
            out[k] = v
        out["category"] = cat
        out["port_scope"] = d.get("port_scope", "port")
        out["layout"] = d["layout"]
        out["appearance"] = app_out
        opts = resolve_list(cls, d["options"])
        for o in opts:
            c = COSTS.get((cls, o["page"], o["key"]))
            if c is not None:
                if o.get("cost"):
                    raise SystemExit("cost twice %s %s" % (cls, o["key"]))
                o["cost"] = c
        out["options"] = opts
        out["outcomes"] = d.get("outcomes") or {}
        out["combats"] = resolve_list(cls, d.get("combats") or [])
        out["prompts"] = resolve_list(cls, d["prompts"])
        out["indirect_prompts"] = indirect
        out["rng_streams"] = streams
        out["mechanics"] = d["mechanics"]
        out["kernel_requirements"] = kr
        out["content_deps"] = cdeps
        out["ascension_sensitivity"] = d["ascension"]
        out["unlock_progress_dependence"] = d["unlock_progress"]
        fp = vdelta.compare(cls)
        o107 = [t for t in fp.get("only_v107", []) if t != "STRUI"]
        o111 = [t for t in fp.get("only_v111", []) if t != "STRUI"]
        ver = collections.OrderedDict()
        ver["status"] = d["version"]["class"]
        ver["note"] = d["version"]["note"]
        ver["fingerprint"] = "equal" if not o107 and not o111 else "differs"
        if o107 or o111:
            ver["fingerprint_only_v107"] = sorted(set(o107))[:12]
            ver["fingerprint_only_v111"] = sorted(set(o111))[:12]
        if d["version"].get("mechanical") == "identifier-rename":
            ver["identifier_delta"] = "Player.CanRemovePotions -> Player.CanUseOrRemovePotions"
        out["version_delta_v107"] = ver
        out["tier"] = d["tier"]
        out["tier_reason"] = d["tier_reason"]
        out["port_batch"] = d["port_batch"]
        out["notes"] = d["notes"]
        out["open_questions"] = d["open_questions"]
        entries.append(out)
    used = set()
    for e in entries:
        for o in e["options"]:
            if o.get("cost") is not None:
                used.add((e["class"], o["page"], o["key"]))
    unused = [k for k in COSTS if k not in used]
    if unused:
        raise SystemExit("unused cost keys %s" % unused)
    env = collections.OrderedDict()
    env["schema"] = "sts2sim.inventory.classified/v1"
    env["kind"] = "events"
    env["game_version"] = raw["game_version"]
    env["raw_manifest"] = "sim/specs/inventory/events.json"
    env["raw_source_digest"] = raw["source_digest"]
    env["cross_reference_tree"] = "artifacts/knowledge/decompiled-v0.107.1/MegaCrit/sts2/Core (v0.107.1)"
    env["generated"] = "2026-11-01"
    env["companion_doc"] = "docs/sim-port/inventory-events.md"
    env["method"] = ("Every file read in full (v0.111.0 condensed state machines; v0.107.1 async source as a reading aid "
                     "only, every rule re-checked in v0.111.0). Citations resolve against v0.111.0 unless prefixed "
                     "[v107]. Version deltas: token fingerprint (strings, Cmd/ModelDb/enum/var/Rng tokens, comparison "
                     "literals) and identifier-set comparison of both trees, then manual reading of every difference.")
    env["target_configuration"] = "single player, GameMode.Standard, no modifiers, TestMode.IsOff, veteran unlock profile"
    env["added_fields"] = ["category", "port_scope", "layout", "appearance", "options", "outcomes", "combats", "prompts",
                           "indirect_prompts", "rng_streams", "mechanics", "kernel_requirements", "content_deps",
                           "ascension_sensitivity", "unlock_progress_dependence", "version_delta_v107", "tier",
                           "tier_reason", "port_batch", "notes", "open_questions"]
    env["raw_fields"] = "every field of the raw events.json entry is kept unchanged before the added fields"
    env["tier_definition"] = TIER_DEF
    env["port_batch_definition"] = BATCH_DEF
    env["version_delta_definition"] = VERSION_DEF
    env["glossary"] = collections.OrderedDict(GLOSSARY)
    env["count"] = len(entries)
    env["entries"] = entries
    return env


def main():
    env = build()
    text = json.dumps(env, indent=1, ensure_ascii=False) + "\n"
    if "--check" in common.REST:
        print("ok", env["count"])
        return
    with open(OUT_JSON, "w", encoding="utf-8") as f:
        f.write(text)
    print("wrote", OUT_JSON, env["count"])


if __name__ == "__main__":
    main()
