#!/usr/bin/env python3
"""eval_summary.py: write the flat ``sts2.eval.summary/1`` summary.json of protocol eval directories.

    python3 -I scripts/analysis/eval_summary.py EVAL_DIR [EVAL_DIR ...] [--run RUN] [--update N] [--force] [--stdout]
    python3 -I scripts/analysis/eval_summary.py --backfill ROOT [ROOT ...] [--force] [--dry-run]
    python3 -I scripts/analysis/eval_summary.py --print-gcloud [--bucket gs://BUCKET/runs]

An eval directory is a protocol run directory (``sts2vec eval`` / ``sts2train eval``): config.json + games/
(*.summary.json), usually report.json and run.json. A repeat parent (r0/, r1/, ...) is not one; each r<k>/ child is.
summary.json is written next to report.json (one additive file; nothing else is touched):

- identity: run (from the checkpoint path ``.../runs/<run>/checkpoints/...``, report out_dir or a ``runs/<run>/`` path
  component; --run overrides), update (checkpoint name ``*-<N>.pt``, else a ``u<N>`` path component; --update
  overrides), checkpoint, weights_hash (config, else report prompt_hash ``weights:<h>``),
  seedset {id, name, role, n, sha256 (protocol checksum of the config's seed set)}, mode {name, policy, breaker,
  sample_seed, repeat, macros}, games_found / complete;
- headline: progress_mean / progress_sd / progress_ci95 (as sts2vec.protocol.aggregate: normal 95%, clipped to [0, 1]),
  clear_rate and Wilson clear_ci95, results, act_reached, floor_mean;
- stage funnel (counts over games; same definitions as sts2train.reportcard.stages, ported here so nothing imports
  sts2train): act1_boss, act1_boss_beaten, act2, act2_boss, act2_boss_beaten, act3, cleared; act2_boss_pass =
  act2_boss_beaten / act2_boss (None when nobody reached it);
- per_seed {game_key: progress} and per_seed_stages {game_key: bitmask over ``stage_bits``} (paired leading
  indicators without games/), wall_s (run.json);
- report_check: whether report.json's headline progress_mean / games equal the recomputed ones (exactly).

Progress is recomputed per game exactly as sts2vec.protocol.load_summaries does (protocol v0.2 ``compute``), so
progress_mean matches report.json bit for bit; ``progress_stored_differs`` counts games whose stored ``progress``
differs (``sts2train compare`` reads the stored value; 0 for every modern eval).

--backfill walks the trees and writes summary.json into every eval directory that lacks one (--force rewrites).
--print-gcloud prints (does not run) the bash loop that does the same over the bucket's runs/*/evals/**.
Standard library only; meant to run as ``python3 -I`` on downloaded data.
"""

from __future__ import annotations

import argparse
import glob
import hashlib
import json
import math
import os
import re
import statistics
import sys
import time
from collections import Counter

SCHEMA = "sts2.eval.summary/1"
PROTOCOL_VERSION = "0.2"
ACTS = 3
DEFAULT_ACT_ROOMS = {1: 17, 2: 16, 3: 15}          # sts2vec.protocol
DOUBLE_BOSS_ASCENSION = 10
RESULTS = ("win", "loss", "capped", "stuck", "crashed", "aborted", "error")
STAGE_KEYS = ("act1_boss", "act1_boss_beaten", "act2", "act2_boss", "act2_boss_beaten", "act3", "cleared")
DEFAULT_BUCKET = "gs://gomoku-zero-20260816-ai-sts2train/runs"   # scripts/colab/ops/mirror_loop.sh

# ------------------------------------------------------------------------------------------------ protocol ports


def default_act_rooms(act: int, ascension: int) -> int:
    n = DEFAULT_ACT_ROOMS.get(act, DEFAULT_ACT_ROOMS[ACTS])
    if act == ACTS and ascension >= DOUBLE_BOSS_ASCENSION:
        n += 1
    return n


def _int_keys(d) -> dict:
    return {int(k): v for k, v in (d or {}).items()}


def compute_progress(result, act, floor, ascension: int, act_start_floor=None, act_rooms=None):
    """sts2vec.protocol.compute's progress (v0.2): ((act - 1) + min(rooms cleared, L - 1) / L) / 3, 1.0 on a win."""
    if result == "win":
        return 1.0
    if act is None or floor is None:
        return None
    starts, rooms = _int_keys(act_start_floor), _int_keys(act_rooms)
    act = max(1, min(int(act), ACTS))
    L = rooms[act] if act in rooms else default_act_rooms(act, ascension)
    if act == 1:
        base = 0
    elif act in starts:
        base = starts[act]
    else:
        base = sum(rooms.get(b) or default_act_rooms(b, ascension) for b in range(1, act))
    k = max(0, int(floor) - base)
    c = max(0, k - 1)
    return round(((act - 1) + min(c, L - 1) / L) / ACTS, 6)


def act_frame(s: dict, act: int, ascension: int) -> tuple[int, int]:
    """(base, L) of `act` in a game summary (sts2train.reportcard.act_frame)."""
    starts, rooms = _int_keys(s.get("act_start_floor")), _int_keys(s.get("act_rooms"))
    L = rooms.get(act) or default_act_rooms(act, ascension)
    if act == 1:
        base = 0
    elif act in starts:
        base = starts[act]
    else:
        base = sum(rooms.get(b) or default_act_rooms(b, ascension) for b in range(1, act))
    return int(base), int(L)


def stages(s: dict, ascension=None) -> dict:
    """Staged milestones of one game summary (sts2train.reportcard.stages, same keys and definitions)."""
    asc = int(s.get("ascension") or 0) if ascension is None else int(ascension)
    res = s.get("result")
    won = res == "win"
    act, floor = int(s.get("act") or 0), int(s.get("floor") or 0)

    def boss_room(a: int) -> bool:
        if won or act > a:
            return True
        if act < a:
            return False
        base, L = act_frame(s, a, asc)
        return floor - base >= L

    def beaten(a: int) -> bool:
        return won or act > a or (act == a and res == "stuck" and boss_room(a))

    return {"act1_boss": boss_room(1), "act1_boss_beaten": beaten(1), "act2": won or act >= 2,
            "act2_boss": boss_room(2), "act2_boss_beaten": beaten(2), "act3": won or act >= 3, "cleared": won}


def stage_mask(flags: dict) -> int:
    return sum(1 << i for i, k in enumerate(STAGE_KEYS) if flags[k])


def mask_flags(mask: int) -> dict:
    return {k: bool(int(mask) >> i & 1) for i, k in enumerate(STAGE_KEYS)}


def wilson(wins: int, n: int, z: float = 1.96):
    if n == 0:
        return [0.0, 0.0]
    p = wins / n
    denom = 1 + z * z / n
    centre = (p + z * z / (2 * n)) / denom
    half = z * math.sqrt(p * (1 - p) / n + z * z / (4 * n * n)) / denom
    return [max(0.0, centre - half), min(1.0, centre + half)]


def mean_ci95(xs, z: float = 1.96):
    if not xs:
        return [None, None]
    m = statistics.fmean(xs)
    if len(xs) < 2:
        return [m, m]
    half = z * statistics.stdev(xs) / math.sqrt(len(xs))
    return [max(0.0, m - half), min(1.0, m + half)]


def checksum(seedset: dict) -> str:
    """sts2vec.protocol.checksum: sha256 of {ascension, content, games: [{character, seed}]} (sorted-key compact)."""
    canon = {"ascension": seedset["ascension"], "content": seedset["content"],
             "games": [{"character": g["character"], "seed": g["seed"]} for g in seedset["games"]]}
    return hashlib.sha256(json.dumps(canon, sort_keys=True, separators=(",", ":")).encode("utf-8")).hexdigest()


def game_key(game: dict) -> str:
    return f"{game['character']}-{game['seed']}"


# ------------------------------------------------------------------------------------------------ identity


def read_json(path: str):
    try:
        with open(path, encoding="utf-8") as f:
            return json.load(f)
    except (OSError, ValueError):
        return None


_RUN_RE = re.compile(r"(?:^|/)runs/([^/]+)/")
_CKPT_RE = re.compile(r"-(\d+)\.pt$")
_UDIR_RE = re.compile(r"^u(\d+)(?:-|$)")


def parse_run(path: str, cfg: dict, report: dict | None) -> tuple[str | None, str | None]:
    for src, s in (("checkpoint", cfg.get("checkpoint")), ("report.out_dir", (report or {}).get("out_dir")),
                   ("path", os.path.abspath(path) + "/")):
        m = _RUN_RE.search(str(s or ""))
        if m:
            return m.group(1), src
    return None, None


def parse_update(path: str, cfg: dict) -> tuple[int | None, str | None]:
    m = _CKPT_RE.search(os.path.basename(str(cfg.get("checkpoint") or "")))
    if m:
        return int(m.group(1)), "checkpoint"
    parts = os.path.abspath(path).split(os.sep)
    for p in reversed(parts):
        m = _UDIR_RE.match(p)
        if m:
            return int(m.group(1)), "path"
    return None, None


def mode_of(cfg: dict) -> dict:
    m = cfg.get("eval_mode") or {}
    lb = (cfg.get("loop_breaker") or {}).get("mode")
    name = m.get("name") or (f"greedy-{lb or 'off'}" if cfg.get("greedy") else str(cfg.get("policy")))
    return {"name": name, "policy": m.get("policy") or ("greedy" if cfg.get("greedy") else cfg.get("policy")),
            "breaker": m.get("breaker") or lb, "sample_seed": m.get("sample_seed"), "repeat": m.get("repeat"),
            "macros": (cfg.get("limits") or {}).get("macros")}


def is_eval_dir(d: str) -> bool:
    return os.path.isfile(os.path.join(d, "config.json")) and os.path.isdir(os.path.join(d, "games"))


# ------------------------------------------------------------------------------------------------ summary


def load_games(d: str, ascension: int) -> tuple[list[dict], int]:
    """Game summaries in sts2vec.protocol.load_summaries order with progress recomputed; (games, stored differs)."""
    out, differs = [], 0
    for p in sorted(glob.glob(os.path.join(d, "games", "*.summary.json"))):
        s = read_json(p)
        if not isinstance(s, dict):
            raise ValueError(f"{p}: unreadable game summary")
        prog = compute_progress(s.get("result"), s.get("act"), s.get("floor"), int(s.get("ascension") or ascension or 0),
                                s.get("act_start_floor"), s.get("act_rooms"))
        if s.get("progress") != prog:
            differs += 1
        s["progress"] = prog
        out.append(s)
    return out, differs


def build_summary(d: str, run: str | None = None, update: int | None = None) -> dict:
    cfg = read_json(os.path.join(d, "config.json"))
    if not isinstance(cfg, dict):
        raise ValueError(f"{d}: no readable config.json")
    report = read_json(os.path.join(d, "report.json"))
    runj = read_json(os.path.join(d, "run.json")) or {}
    ss = cfg.get("seedset") or {}
    asc = int(ss.get("ascension") or 0)
    games, differs = load_games(d, asc)
    if not games:
        raise ValueError(f"{d}: no games/*.summary.json")
    keys = [s.get("key") or game_key(s) for s in games]
    prog = [s["progress"] for s in games if s["progress"] is not None]
    flags = [stages(s, asc) for s in games]
    funnel = {k: sum(f[k] for f in flags) for k in STAGE_KEYS}
    res = Counter(s.get("result") for s in games)
    n = len(games)
    wins = res.get("win", 0)
    floors = [s["floor"] for s in games if s.get("floor") is not None]
    r_run, r_src = (run, "arg") if run else parse_run(d, cfg, report)
    u, u_src = (int(update), "arg") if update is not None else parse_update(d, cfg)
    sha = checksum(ss) if ss.get("games") and "content" in ss else None
    declared = ss.get("sha256")
    wh = cfg.get("weights_hash")
    if not wh:
        ph = str((report or {}).get("prompt_hash") or cfg.get("prompt_hash") or "")
        wh = ph.split(":", 1)[1] if ph.startswith("weights:") else None
    expected = len(ss.get("games") or []) or None
    out = {
        "schema": SCHEMA, "generated_at": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime()),
        "generator": "scripts/analysis/eval_summary.py", "dir": os.path.abspath(d),
        "run": r_run, "run_source": r_src, "update": u, "update_source": u_src,
        "checkpoint": cfg.get("checkpoint"), "weights_hash": wh, "repo_sha": cfg.get("repo_sha"),
        "protocol_version": cfg.get("protocol_version") or PROTOCOL_VERSION,
        "seedset": {"id": ss.get("id"), "name": ss.get("name"), "role": ss.get("role"), "n": expected,
                    "ascension": asc, "sha256": sha,
                    **({"sha256_declared": declared} if declared and declared != sha else {})},
        "mode": mode_of(cfg),
        "games": n, "games_found": n, "complete": expected is None or n == expected,
        "progress_mean": statistics.fmean(prog) if prog else None,
        "progress_sd": statistics.stdev(prog) if len(prog) > 1 else None,
        "progress_ci95": mean_ci95(prog),
        "progress_stored_differs": differs,
        "clear_rate": wins / n, "clear_ci95": wilson(wins, n), "wins": wins,
        "results": {r: res.get(r, 0) for r in RESULTS},
        "act_reached": {str(k): v for k, v in sorted(Counter(s.get("act") for s in games
                                                             if s.get("act") is not None).items())},
        "floor_mean": statistics.fmean(floors) if floors else None,
        "funnel": funnel,
        "act2_boss_pass": (funnel["act2_boss_beaten"] / funnel["act2_boss"]) if funnel["act2_boss"] else None,
        "act3_rate": funnel["act3"] / n,
        "stage_bits": list(STAGE_KEYS),
        "per_seed": {k: s["progress"] for k, s in zip(keys, games)},
        "per_seed_stages": {k: stage_mask(f) for k, f in zip(keys, flags)},
        "wall_s": runj.get("wall_seconds"),
    }
    if report and isinstance(report.get("headline"), dict):
        h = report["headline"]
        out["report_check"] = {"progress_mean_match": h.get("progress_mean") == out["progress_mean"],
                               "games_match": h.get("games") == n,
                               "report_progress_mean": h.get("progress_mean")}
    else:
        out["report_check"] = None
    return out


def write_summary(d: str, run=None, update=None, force: bool = False, dry_run: bool = False) -> tuple[str, dict | None]:
    """('written' | 'exists' | 'dry-run', summary)."""
    path = os.path.join(d, "summary.json")
    if os.path.exists(path) and not force:
        return "exists", None
    s = build_summary(d, run, update)
    if dry_run:
        return "dry-run", s
    tmp = path + ".tmp"
    with open(tmp, "w", encoding="utf-8") as f:
        json.dump(s, f, indent=1, sort_keys=False)
        f.write("\n")
    os.replace(tmp, path)
    return "written", s


def find_eval_dirs(root: str) -> list[str]:
    out = []
    for d, dirs, _files in os.walk(root):
        if is_eval_dir(d):
            out.append(d)
            dirs[:] = [x for x in dirs if x != "games"]
        dirs[:] = sorted(x for x in dirs if x not in ("games", "checkpoints", "__pycache__") and not x.startswith("."))
    return sorted(out)


def backfill(roots: list[str], force: bool = False, dry_run: bool = False, log=print) -> dict:
    counts = Counter()
    mismatches = []
    for root in roots:
        for d in find_eval_dirs(root):
            try:
                st, s = write_summary(d, force=force, dry_run=dry_run)
            except ValueError as e:
                counts["error"] += 1
                log(f"error {d}: {e}")
                continue
            counts[st] += 1
            if s and s.get("report_check") and not s["report_check"]["progress_mean_match"]:
                mismatches.append(d)
                log(f"MISMATCH {d}: report {s['report_check']['report_progress_mean']} vs {s['progress_mean']}")
    return {"counts": dict(counts), "report_mismatches": mismatches}


GCLOUD_TEMPLATE = r"""#!/usr/bin/env bash
# Backfill summary.json over {bucket}/*/evals/** (printed by eval_summary.py --print-gcloud; NOT executed by it).
# Run it where gcloud is logged in (e.g. the Tokyo VM), from a checkout that has scripts/analysis/eval_summary.py.
# Additive: it only uploads <eval dir>/summary.json for eval dirs that do not have one yet.
set -euo pipefail
B={bucket}
TOOL=${{TOOL:-scripts/analysis/eval_summary.py}}
W=$(mktemp -d)
gcloud storage ls "$B/*/evals/**/config.json" | sed 's#/config.json$##' | sort -u > "$W/dirs.txt"
echo "$(wc -l < "$W/dirs.txt") eval dirs"
while read -r D; do
  if gcloud storage ls "$D/summary.json" >/dev/null 2>&1; then continue; fi
  L="$W/runs/${{D#"$B"/}}"
  mkdir -p "$L"
  gcloud storage cp "$D/config.json" "$L/" >/dev/null
  for f in report.json run.json; do gcloud storage cp "$D/$f" "$L/" >/dev/null 2>&1 || true; done
  gcloud storage rsync --recursive "$D/games" "$L/games" >/dev/null
  python3 -I "$TOOL" "$L" && gcloud storage cp "$L/summary.json" "$D/summary.json"
  rm -rf "$L/games"
done < "$W/dirs.txt"
echo "done; local copies under $W"
"""


def gcloud_commands(bucket: str = DEFAULT_BUCKET) -> str:
    return GCLOUD_TEMPLATE.format(bucket=bucket.rstrip("/"))


def main(argv=None) -> int:
    ap = argparse.ArgumentParser(prog="eval_summary.py", description=__doc__.split("\n\n")[0],
                                 epilog=__doc__.split("\n\n", 1)[1], formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("dirs", nargs="*", help="eval directories (config.json + games/)")
    ap.add_argument("--run", default=None, help="override the run name (single directory)")
    ap.add_argument("--update", type=int, default=None, help="override the update (single directory)")
    ap.add_argument("--force", action="store_true", help="rewrite an existing summary.json")
    ap.add_argument("--stdout", action="store_true", help="print the summary instead of writing summary.json")
    ap.add_argument("--backfill", nargs="+", metavar="ROOT", help="write summary.json into every eval dir under ROOT")
    ap.add_argument("--dry-run", action="store_true", help="with --backfill: build but do not write")
    ap.add_argument("--print-gcloud", action="store_true", help="print the bucket backfill loop (does not run it)")
    ap.add_argument("--bucket", default=DEFAULT_BUCKET, help=f"runs/ prefix for --print-gcloud (default {DEFAULT_BUCKET})")
    a = ap.parse_args(argv)
    if a.print_gcloud:
        sys.stdout.write(gcloud_commands(a.bucket))
        return 0
    if a.backfill:
        r = backfill(a.backfill, force=a.force, dry_run=a.dry_run)
        print(json.dumps(r))
        return 1 if r["report_mismatches"] or r["counts"].get("error") else 0
    if not a.dirs:
        ap.error("give eval directories, --backfill ROOT or --print-gcloud")
    if (a.run or a.update is not None) and len(a.dirs) > 1:
        ap.error("--run / --update apply to a single directory")
    rc = 0
    for d in a.dirs:
        if not is_eval_dir(d):
            print(f"error {d}: not an eval directory (config.json + games/)", file=sys.stderr)
            rc = 2
            continue
        if a.stdout:
            print(json.dumps(build_summary(d, a.run, a.update), indent=1))
            continue
        st, s = write_summary(d, a.run, a.update, force=a.force)
        if s is None:
            print(f"{st} {d}/summary.json (use --force to rewrite)")
            continue
        chk = s.get("report_check") or {}
        print(f"{st} {d}/summary.json run={s['run']} u={s['update']} {s['seedset']['id']} {s['mode']['name']} "
              f"games={s['games']} progress={s['progress_mean']:.4f} act3={s['funnel']['act3']}"
              + ("" if not chk else f" report_match={chk.get('progress_mean_match')}"))
        if chk and not chk.get("progress_mean_match"):
            rc = 1
    return rc


if __name__ == "__main__":
    sys.exit(main())
