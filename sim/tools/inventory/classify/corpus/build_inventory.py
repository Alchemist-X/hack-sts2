import os as _os
import sys as _sys
_sys.path.insert(0, _os.path.join(_os.path.dirname(_os.path.abspath(__file__)), ".."))
import common  # noqa: E402
import json, collections, sys, os
B = common.work_dir("corpus") + "/"
inv = json.load(open(B + "inventory_raw.json"))
vd = json.load(open(B + "version_diff.json"))
taint = json.load(open(B + "taint_stats.json"))
replay = json.load(open(B + "replay_stats.json"))
runs = inv["_runs"]
IN_COMBAT = ("play_card", "end_turn", "choice_in_combat", "potion_in_combat")
RUN_LEVEL = ("map", "reward", "choice_out_of_combat", "event", "rest", "shop", "potion_out_of_combat")
def split(d):
    return {"in_combat": sum(d.get(k, 0) for k in IN_COMBAT), "run_level": sum(d.get(k, 0) for k in RUN_LEVEL)}
by_version = {}
for v, c in inv["by_version"].items():
    d = {k.split(":", 1)[1]: n for k, n in c.items() if k.startswith("decisions:")}
    by_version[v] = {
        "lineages": c["lineages"], "parts": c["parts"],
        "outcomes": {k.split(":", 1)[1]: n for k, n in c.items() if k.startswith("outcome:")},
        "combats_unique": c["combats_unique"], "combat_attempts": c["combat_attempts"],
        "decisions": {k: d.get(k, 0) for k in IN_COMBAT + RUN_LEVEL}, "decisions_totals": split(d),
        "follow_up_actions_not_decisions": d.get("follow_up", 0),
        "seeding": {"v0.107.1": "legacy_v18", "v0.110.1": "xxh64", "v0.111.0": "xxh64"}[v],
    }
rt = collections.Counter()
att = collections.Counter()
for r in runs:
    v = r["game_versions"][0]
    for c in r["combats"]:
        rt[(v, c["room_type"])] += 1
        att[min(c["attempts"], 5)] += 1
for v in by_version:
    by_version[v]["combats_unique_by_room_type"] = {t: rt[(v, t)] for t in ("Monster", "Elite", "Boss")}
lineage_rows = []
for r in runs:
    d = r["decisions"]
    lineage_rows.append({
        "start_time": r["key"][0], "seed": r["seed"], "character": r["character"], "ascension": r["ascension"],
        "game_version": "+".join(r["game_versions"]), "game_mode": r["game_mode"], "parts": r["parts"],
        "extra_mods": [m for m in r["mods"] if not m.startswith("Sts2Recorder")],
        "max_floor": r["max_floor"], "max_act": r["max_act"], "outcome": r["outcome"],
        "combats_unique": len(r["combats"]), "combat_attempts": sum(c["attempts"] for c in r["combats"]),
        "decisions": split(d),
        "native": {"replay_mcr_parts": r["native_files"].get("replay.mcr", 0), "run_history": bool(r["native_files"].get("run_history.run"))},
    })
_summary = json.load(open(common.inventory_path("summary.json")))["stats"]
game_totals = {k: v["non_mock_non_deprecated_concrete"] for k, v in _summary.items() if "non_mock_non_deprecated_concrete" in v}
game_totals["_source"] = "sim/specs/inventory/summary.json stats.*.non_mock_non_deprecated_concrete"
out = {
    "schema": "sts2-sim/corpus-inventory@1",
    "generated": "2026-11-01",
    "corpus_root": "~/Library/Application Support/Sts2Recorder/sessions (read-only scan; the newest part was still being written)",
    "rule_truth": "output/server-bootstrap/linux-decompiled (v0.111.0, commit of recorded v0.111.0 parts: 41cef1ea)",
    "method": {
        "scripts": common.generator("corpus", "scan_parts.py", "scan_runs.py", "aggregate.py", "tree_diff.py", "classify_diff.py",
                                    "taint.py", "replay_stats.py", "build_inventory.py"),
        "scripts_note": "committed generators, Python 3 stdlib; run from the repo root in the order listed (sim/tools/inventory/classify/README.md)",
        "lineage": "parts with equal (manifest.run.start_time, seed, character), as sts2rec.lineage.group_lineages",
        "combat": "room_entered event whose room_model_id starts with ENCOUNTER.; unique = (lineage, act, floor); attempts = re-entries of that room (save&quit or QuickSL reloads)",
        "decision": "actions.jsonl record (no cancelled records exist); in_combat = the state at state_seq has a battle block, or is a card/hand/bundle select or map view with a later battle snapshot in the same room; move_to_map_coord and ready_to_begin_enemy_turn are follow-ups, not decisions",
        "categories": {"play_card": ["play_card"], "end_turn": ["end_turn", "undo_end_turn"], "choice_in_combat|choice_out_of_combat": ["player_choice"],
                       "map": ["vote_for_map_coord", "vote_to_move_to_next_act"], "reward": ["reward_taken", "rewards_skipped", "pick_relic", "open_chest", "card_reward_obtained"],
                       "shop": ["shop_purchase", "shop_card_removal"], "rest": ["rest_site_option"], "event": ["event_option", "event_proceed"],
                       "potion_in_combat|potion_out_of_combat": ["use_potion", "discard_potion"]},
    },
    "totals": {"parts": inv["parts_total"], "lineages": inv["lineages_total"],
               "outcomes": dict(collections.Counter(r["outcome"] for r in runs)),
               "combats_unique": sum(len(r["combats"]) for r in runs),
               "combat_attempts": sum(sum(c["attempts"] for c in r["combats"]) for r in runs),
               "first_combats": inv["combats_first_vs_later"]["first_combats"], "later_combats": inv["combats_first_vs_later"]["later_combats"],
               "decisions": split(inv["decisions_by_screen"]["all"])},
    "by_version": by_version,
    "by_version_character_ascension": inv["by_version_character_ascension"],
    "max_floor_reached_histogram": inv["max_floor_reached_histogram"],
    "max_act_reached_lineages": inv["max_act_reached"],
    "combats_per_act": inv["combats_per_act"],
    "combat_attempt_distribution": {("5+" if k == 5 else str(k)): n for k, n in sorted(att.items())},
    "decisions_by_screen": inv["decisions_by_screen"],
    "decisions_by_screen_and_act": inv["decisions_by_screen_and_act"],
    "player_choice_types": inv["player_choice_types"],
    "native": {
        "parts_with_native_dir": 769,
        "files": inv["native"],
        "current_run_save_copies": 0,
        "replay_mcr": replay,
        "run_history_run": {"lineages": 127, "schema_versions": {"9 (v0.107.1)": 111, "10 (v0.110.1)": 7, "10 (v0.111.0)": 9},
                            "outcomes": {"win": 38, "loss": 50, "abandoned": 39},
                            "contents": "game JSON at run end: seed, build_id, acts, modifiers, players[] (final deck with floor_added_to_deck, relics, potions, max_potion_slot_count), map_point_history[act][floor] (map_point_type, rooms[] with model_id/monster_ids/turns_taken, player_stats[] with card_choices, relic_choices, potion_choices, event_choices, ancient_choice, rest_site_choices, cards_gained/removed/transformed/enchanted, upgraded/downgraded_cards, bought_*, gold and hp flows)",
                            "note": "sts2rec/native_run.py accepts schema 8 and 9 only; the 16 schema-10 files are read raw by the exporter"},
    },
    "recorder": {"versions": inv["recorder_versions"], "known_good_versions_in_recorder": ["v0.107.1"],
                 "untested_flag": {"v0.107.1": False, "v0.110.1": True, "v0.111.0": True},
                 "game_commits": {"v0.107.1": "59260271", "v0.110.1": "db5d3552", "v0.111.0": "41cef1ea"},
                 "lineages_by_extra_mods": inv["lineages_by_extra_mods"], "parts_gzip_compressed": 127, "degraded_hooks": 0,
                 "checksum_events": {"parts": 13, "events": 780, "versions": ["v0.107.1"]}},
    "content_seen": {"definition": "ids appearing in hands, played cards, card/hand/reward select lists, relic and potion lists, enemy entity ids, power ids (statuses, power_received), room_entered encounter/event ids",
                     "by_version": inv["content_seen"]["by_version"], "all_versions": inv["content_seen"]["all"],
                     "game_totals_v0_111_0": game_totals},
    "version_diff_v0_107_1_vs_v0_111_0": {
        "trees": {"v0.111.0": "output/server-bootstrap/linux-decompiled (state-machine bodies)",
                  "v0.107.1": "artifacts/knowledge/decompiled-v0.107.1/MegaCrit/sts2/Core (async bodies restored)"},
        "files": {"v0.111.0": 3537, "v0.107.1": 3424, "common_by_namespace_and_name": 3405, "only_v0.111.0": 132, "only_v0.107.1": 19},
        "files_per_namespace": {ns.replace("MegaCrit.Sts2.Core.", "") or "(global)": {"common": c.get("common", 0), "only_v0.111.0": c.get("added", 0), "only_v0.107.1": c.get("removed", 0)}
                                for ns, c in sorted(json.load(open(B + "tree_diff.json"))["per_namespace"].items())},
        "method": "files paired by (namespace, file name); both sides cleaned of comments, attributes, using lines and async/iterator state-machine plumbing; compared per file: (a) multiset of numeric literals outside presentation contexts with 0/1/-1 dropped, plus `Name => constant` properties; (b) multiset of rule enums (CardType, CardRarity, TargetType, CardKeyword, PowerStackType, AscensionLevel, ValueProp, PileType, ...); (c) set of called members with presentation/API-noise names removed; rule_diverging_estimate = any of the three. A raw text diff is useless because the two trees were decompiled with different settings",
        "precision_note": "manual review of all 86 flagged card files: about 85-90 % are real balance or behaviour changes; residual noise = lambda/display-class rendering of 0-valued constants, renamed APIs (ModifyCardPlayResultPileTypeAndPosition -> ModifyCardPlayResultLocation, FromCard(card) -> FromCard(card, play)), presentation-only calls. Potions: 23 flags are TargetType.Self -> TargetType.AnyPlayer (likely inert in single player). Characters: 6 flags are animator-graph noise. Value lists omit 0, 1 and -1, so a change 1 -> 2 reads [] -> [2]",
        "content_summary": vd["content_summary"],
        "engine_summary": vd["engine_summary"],
        "hook_surface": {"Hook dispatchers": {"v0.111.0": 148, "v0.107.1": 146, "only_v0.111.0": ["AfterSideTurnEnd", "BeforeSideTurnEnd", "BeforeCombatRewardOffered", "ModifyCardPlayResultLocation"], "only_v0.107.1": ["AfterTurnEnd", "BeforeTurnEnd"]},
                         "AbstractModel virtuals": {"v0.111.0": 183, "v0.107.1": 181, "only_v0.111.0": ["AfterModifyingCardPlayResultLocation", "BeforeCombatRewardOffered", "ModifyCardPlayResultLocation"], "only_v0.107.1": ["AfterModifyingCardPlayResultPileOrPosition"]},
                         "counting_note": "regex count over Hooks/Hook.cs and Models/AbstractModel.cs public static / public virtual members; the sim's own count (149 / 185) uses its scaffold's definition"},
        "save_schema": {"SerializableRun": {"v0.107.1": "migrations up to V15->V16", "v0.111.0": "schema 20 (V16->V17 ... V19->V20 added; seed hashing change in V18->V19)"}},
        "version_exclusive_content": {"only_v0.111.0": vd["only_v0.111.0"], "only_v0.107.1": vd["only_v0.107.1"]},
        "decompile_gaps_v0_111_0": {"pattern": "RuntimeHelpers.InitializeArray(.../*OpCode not supported: LdMemberToken*/): static array initializer values unreadable",
                                    "rule_relevant_files": ["Entities.Merchant/MerchantInventory.cs (static CardType[5] layout)", "Map/GoldenPathActMap.cs (MapPointType[16])",
                                                            "Models.Cards/AscendersBane.cs (keywords)", "Models.Cards/Folly.cs (keywords)", "Models.Events/ColossalFlower.cs (_prizeCosts and a second int[3])",
                                                            "Models.Events/TinkerTime.cs (RiderEffect[3] x3)", "Models.Monsters/KnowledgeDemon.cs (int[3])", "Models.Relics/GlassEye.cs (CardRarity[5])",
                                                            "Entities.Cards/CardKeywordOrder.cs", "Helpers/HandPosHelper.cs"],
                                    "files_total": 13, "v0_107_1_tree": "renders all of them (e.g. ColossalFlower _prizeCosts = {35, 75, 135}); usable as a cross-reference only, v0.111.0 values need IL or a re-decompile"},
        "content_files": vd["content_files"],
        "engine_files": vd["engine_files"],
        "legacy_corpus_taint_estimate": taint,
    },
    "lineages": lineage_rows,
}
OUT = common.stage1_path("corpus-inventory.json")
json.dump(out, open(OUT, "w"), indent=1, ensure_ascii=False)
print("bytes", os.path.getsize(OUT))
