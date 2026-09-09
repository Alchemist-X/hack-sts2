#!/usr/bin/env python3
"""Reproducible run statistics. Never reads message/analysis bodies for usage."""
from __future__ import annotations

import argparse
import collections
import json
from pathlib import Path


def rows(path):
    with Path(path).open(encoding="utf-8") as handle:
        for line in handle:
            if line.strip():
                yield json.loads(line)


def usage_report(path, cutoff=None, start=None):
    seen, totals, turns, compactions = set(), collections.Counter(), {}, []
    for row in rows(path):
        stamp = row.get("timestamp", "")
        if (cutoff and stamp > cutoff) or (start and stamp < start):
            continue
        if row.get("type") == "compacted":
            compactions.append(stamp)
        if row.get("type") != "token_usage_record":
            continue
        payload = row["payload"]
        response_id = payload.get("response_id")
        if not response_id:
            raise ValueError("usage record lacks response_id; cannot deduplicate safely")
        if response_id in seen:
            continue
        seen.add(response_id)
        usage = payload["usage"]  # Per request, NOT resetting turn totals or repeated token_count events.
        totals.update(usage)
        turn = turns.setdefault(payload.get("turn_id", "unknown"), {"requests": 0, "usage": collections.Counter()})
        turn["requests"] += 1
        turn["usage"].update(usage)
    return {"source": str(Path(path).resolve()), "start": start, "cutoff": cutoff,
            "scope": "thread requests within the specified timestamps; includes setup/research/reporting",
            "available": bool(seen), "requests": len(seen), "usage": dict(totals),
            "uncached_input_tokens": totals["input_tokens"] - totals["cached_input_tokens"],
            "compaction_count": len(compactions), "compaction_timestamps": compactions,
            "turns": turns, "account_bill": "unknown; cached input is a subset of input; reasoning is a subset of output"}


def run_report(path):
    records = list(rows(path))
    intents = {r["decision_id"]: r for r in records if r.get("type") == "decision_intent"}
    outcomes = {r["decision_id"]: r for r in records if r.get("type") in {"decision_outcome", "decision_outcome_recovered"}}
    reward_choices, potion_uses, shops, encounters, claims = [], [], {}, {}, []
    for key, r in intents.items():
        s, a = r["state"], r["action"]
        run = s.get("run", {})
        location = {"act": run.get("act"), "floor": run.get("floor"), "round": s.get("battle", {}).get("round")}
        base = {"decision_id": key, **location, "reason": r.get("decision_reason"), "outcome_recorded": key in outcomes}
        if a["action"] in {"select_card_reward", "skip_card_reward"}:
            offered = s.get("card_reward", {}).get("cards", [])
            picked = next((c for c in offered if c.get("index") == a.get("card_index")), None)
            reward_choices.append({**base, "choice": "take" if picked else "skip", "picked": picked, "offered": offered})
        if a["action"] in {"use_potion", "discard_potion"}:
            potion = next((p for p in s.get("player", {}).get("potions", []) if p.get("slot") == a.get("slot")), None)
            potion_uses.append({**base, "action": a, "potion": potion, "hp_before": s.get("player", {}).get("hp")})
        if a["action"] == "claim_reward":
            item = next((x for x in s.get("rewards", {}).get("items", []) if x.get("index") == a.get("index")), {})
            claims.append({**base, "reward": item})
        if s.get("shop"):
            shop = shops.setdefault(str((run.get("act"), run.get("floor"))), {**location, "initial_items": s["shop"].get("items", []), "purchases": []})
            if a["action"] == "shop_purchase":
                item = next((x for x in s["shop"].get("items", []) if x.get("index") == a.get("index")), None)
                shop["purchases"].append({**base, "item": item})
        if s.get("battle"):
            for enemy in s["battle"].get("enemies", []):
                identity = enemy.get("id") or str(enemy.get("entity_id", "")).rsplit("_", 1)[0]
                encounter = encounters.setdefault(str((run.get("act"), run.get("floor"))), {**location, "room_type": s.get("state_type"), "monsters": {}})
                encounter["monsters"].setdefault(identity, enemy)
    counts = collections.Counter(r["choice"] for r in reward_choices)
    result = {"source": str(Path(path).resolve()), "intent_count": len(intents), "outcome_count": len(outcomes),
            "unmatched_intents": sorted(set(intents) - set(outcomes)),
            "actions": dict(collections.Counter(r["action"]["action"] for r in intents.values())),
            "card_reward_summary": {"offers": len(reward_choices), **counts, "take_rate": counts["take"] / len(reward_choices) if reward_choices else None},
            "card_rewards": reward_choices, "reward_claim_types": dict(collections.Counter(x["reward"].get("type", "unknown") for x in claims)),
            "reward_claims": claims, "shops": list(shops.values()), "encounters": list(encounters.values()),
            "potion_actions": potion_uses,
            "counting_note": "Physical action counts include resumed room attempts. Cross-check native history for committed resource consumption; card rewards exclude event transforms, shops and starting bundles."}
    directory = Path(path).parent
    native = directory / "recorder-part2/native/run_history.run"
    if not native.exists():
        native = directory / "recorder/native/run_history.run"
    if native.exists():
        history = json.loads(native.read_text())
        points = [p for act in history.get("map_point_history", []) for p in act]
        used = [v for p in points for stat in p.get("player_stats", []) for v in stat.get("potion_used", [])]
        rooms = [r for p in points for r in p.get("rooms", [])]
        result["native_history"] = {"source": str(native.resolve()), "win": history.get("win"),
            "was_abandoned": history.get("was_abandoned"), "run_time": history.get("run_time"),
            "potion_used_count": len(used), "potion_used_types": dict(collections.Counter(used)),
            "room_types": dict(collections.Counter(r.get("room_type") for r in rooms)),
            "monster_type_count": len({m for r in rooms for m in r.get("monster_ids", [])})}
    annotations = list(rows(directory / "annotations.jsonl")) if (directory / "annotations.jsonl").exists() else []
    result["annotations"] = annotations
    result["sl"] = {"annotated_sl_count": sum(r.get("kind") == "sl" for r in annotations),
                    "annotated_checkpoint_resumes": sum(r.get("kind") == "checkpoint_resume" for r in annotations),
                    "legacy_user_requested_pauses": sum(r.get("type") == "user_requested_pause" for r in records),
                    "legacy_user_requested_resumes": sum(r.get("type") == "user_requested_resume" for r in records),
                    "coverage": "annotation counts; absence is not evidence of zero UI reloads in legacy runs"}
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("run_dir", type=Path)
    parser.add_argument("--codex-session", type=Path)
    parser.add_argument("--cutoff")
    parser.add_argument("--start")
    parser.add_argument("--output", type=Path)
    a = parser.parse_args()
    report = run_report(a.run_dir / "decisions.jsonl")
    report["cost"] = usage_report(a.codex_session, a.cutoff, a.start) if a.codex_session else {"available": False, "reason": "No Codex session supplied; unknown, not zero"}
    out = a.output or a.run_dir / "audit.json"
    out.parent.mkdir(parents=True, exist_ok=True)
    out.write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({"report": str(out.resolve()), "card_rewards": report["card_reward_summary"], "cost": report["cost"]}, ensure_ascii=False))


if __name__ == "__main__":
    main()
