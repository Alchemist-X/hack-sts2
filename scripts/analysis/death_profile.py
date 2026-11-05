#!/usr/bin/env python3
"""death_profile.py: where and to what the Silent A10 policy dies, from eval summaries and / or episodes.jsonl.

    python3 -I scripts/analysis/death_profile.py LABEL=SRC[,SRC...][@LO-HI] [LABEL=SRC ...]
            [--update-range LO-HI] [--ascension 10] [--top 25] [--json OUT.json]

SRC is an eval directory (`sts2train eval` / `sts2vec evaluate` output: games/*.summary.json, or the summary files
directly in it) or an episodes.jsonl file (trainer; read line by line). `@LO-HI` keeps the episodes rows with
LO <= update <= HI (default --update-range, else all); several SRCs of one label are pooled. Standard library only,
so it runs as `python3 -I` on downloaded (untrusted) data. Markdown to stdout; --json also writes every table.

Sections (docs/diag-2026-11-05/death-analysis-m-u2264.md turned into a tool):
  (a) end-location bands per label: A1 pre-boss / A1 boss / A<a> rooms 1-5 / A<a> rooms 6+ pre-boss / A<a> boss
      (a = 2, 3) for deaths, then win / trunc / stall / error; progress mean;
  (b) conditional death rate per band = deaths in it / games whose end location is at or past its start
      (wins reach every band; a trunc / stall / error game reaches up to where it ended);
  (c) killer ranking (share of the label's games), the encounter-id suffix kind (BOSS / ELITE / NORMAL / WEAK),
      top killers per band;
  (d) when the wave-6 run-state fields exist (2026-11-05): HP at act-boss entry (hp / max_hp median and quartiles per
      act, and per boss: entered / died there), deck / upgraded / relics / potions / gold at death per band, the final
      room kind of deaths, and from eval `fights`: per-encounter exposure (fights fought vs lost), so "a killer rose
      because more games reach it or because it is beaten less often" is answerable;
  (e) data gaps: which fields were absent (all games) or partial, per label.

Location: k = rooms entered in the act (act 1: floor; act a > 1: floor - act_start_floor[a]); L = act_rooms[a]
(eval: the map the agent saw; episodes: the standard lengths 17 / 16 / 15, +1 for act 3 at A10, with act starts at
floors 17 / 33). Boss room: k >= L (act 3 at A10: k >= L - 1, the two boss rows); for episodes rows that carry
`boss_entry`, a boss room is an entry of the row's act on the row's floor (or room == Boss), otherwise the default
lengths (floor 17 = A1 boss, floor >= 33 ~ A2 boss, as in the analysis). Killer: the summary / row `killer`, else
the eval `death.enemies` joined with '+' (old summaries); episodes rows without `killer` have none.
"""

from __future__ import annotations

import argparse
import glob
import json
import os
import statistics as st
import sys
from collections import Counter, defaultdict

DEFAULT_ACT_ROOMS = {1: 17, 2: 16, 3: 15}          # sts2eval.progress (protocol v0.2)
DOUBLE_BOSS_ASCENSION = 10
KINDS = ("BOSS", "ELITE", "NORMAL", "WEAK")
OUTCOMES = ("win", "trunc", "stall", "error")
EVAL_RESULT = {"win": "win", "loss": "death", "capped": "trunc", "stuck": "stall", "error": "error"}
EPISODE_RESULT = {"win": "win", "death": "death", "trunc": "trunc", "stall": "stall", "error": "error"}
RUN_FIELDS = ("hp", "max_hp", "deck", "upgraded", "relics", "gold", "potions", "killer", "boss_entry", "room")
AT_DEATH = ("deck", "upgraded", "relics", "potions", "gold")


def band_names(acts: int = 3) -> list[str]:
    out = ["A1 pre-boss", "A1 boss"]
    for a in range(2, acts + 1):
        out += [f"A{a} rooms 1-5", f"A{a} rooms 6+ pre-boss", f"A{a} boss"]
    return out


BANDS = band_names()


def default_rooms(act: int, asc: int) -> int:
    n = DEFAULT_ACT_ROOMS.get(act, DEFAULT_ACT_ROOMS[3])
    return n + 1 if act == 3 and asc >= DOUBLE_BOSS_ASCENSION else n


def default_start(act: int, asc: int) -> int:
    return sum(default_rooms(a, asc) for a in range(1, act))


def boss_rows(act: int, asc: int) -> int:
    return 2 if act == 3 and asc >= DOUBLE_BOSS_ASCENSION else 1


def band_of(act: int, k: int, boss: bool) -> str:
    act = max(1, min(int(act), 3))
    if boss:
        return f"A{act} boss"
    if act == 1:
        return "A1 pre-boss"
    return f"A{act} rooms 1-5" if k <= 5 else f"A{act} rooms 6+ pre-boss"


def kind_of(enc: str | None) -> str:
    if not enc:
        return "?"
    suf = enc.rsplit("_", 1)[-1]
    return suf if suf in KINDS else "?"


def quart(xs: list[float]) -> tuple[float, float, float] | None:
    if not xs:
        return None
    if len(xs) == 1:
        return xs[0], xs[0], xs[0]
    q = st.quantiles(xs, n=4, method="inclusive")
    return q[0], q[1], q[2]


# ------------------------------------------------------------------------------------------------------ normalize
def norm_summary(s: dict) -> dict:
    """One eval game summary -> the common record."""
    res = EVAL_RESULT.get(s.get("result"), "error")
    act, floor = int(s.get("act") or 1), int(s.get("floor") or 0)
    asc = int(s.get("ascension") or DOUBLE_BOSS_ASCENSION)
    starts = {int(k): v for k, v in (s.get("act_start_floor") or {}).items()}
    rooms = {int(k): v for k, v in (s.get("act_rooms") or {}).items()}
    k = floor - (starts.get(act, default_start(act, asc)) if act > 1 else 0)
    L = rooms.get(act) or default_rooms(act, asc)
    boss = k >= L - (boss_rows(act, asc) - 1)
    death = s.get("death") or {}
    killer = s.get("killer") or ("+".join(death.get("enemies") or []) or None)
    rec = {"result": res, "act": act, "floor": floor, "k": k, "boss": boss, "progress": s.get("progress"),
           "killer": killer if res == "death" else None, "seed": s.get("seed"),
           "room": s.get("room") or death.get("room_type"), "fights": s.get("fights")}
    for f in RUN_FIELDS:
        if f not in ("killer", "room"):
            rec[f] = s.get(f)
    present = {f for f in RUN_FIELDS if f in s}
    if "fights" in s:
        present.add("fights")
    if s.get("act_rooms"):
        present.add("act_rooms")
    if res == "death" and death.get("room_type") is not None:
        present.add("death.room_type")
    rec["present"] = present
    return rec


def norm_episode(r: dict, asc: int) -> dict | None:
    res = EPISODE_RESULT.get(r.get("result"))
    if res is None or r.get("act") is None or r.get("floor") is None:
        return None                                  # dropped / start_failed / unknown rows
    act, floor = int(r["act"]), int(r["floor"])
    k = floor - (default_start(act, asc) if act > 1 else 0)
    be = r.get("boss_entry")
    if isinstance(be, list):
        boss = any(int(e.get("act") or 0) == act and int(e.get("floor") or -1) == floor for e in be) \
            or r.get("room") == "Boss"
    else:
        boss = k >= default_rooms(act, asc) - (boss_rows(act, asc) - 1)
    rec = {"result": res, "act": act, "floor": floor, "k": k, "boss": boss, "progress": r.get("progress"),
           "killer": r.get("killer") if res == "death" else None, "seed": r.get("seed_text"),
           "room": r.get("room"), "fights": None}
    for f in RUN_FIELDS:
        if f not in ("killer", "room"):
            rec[f] = r.get(f)
    rec["present"] = {f for f in RUN_FIELDS if f in r}
    return rec


def iter_source(path: str, lo: int | None, hi: int | None, asc: int):
    """(kind, record) for every game / episode of one source."""
    if os.path.isdir(path):
        files = sorted(glob.glob(os.path.join(path, "games", "*.summary.json"))) or \
            sorted(glob.glob(os.path.join(path, "*.summary.json")))
        if not files:
            raise SystemExit(f"{path}: no games/*.summary.json")
        for f in files:
            with open(f, encoding="utf-8") as fh:
                yield "eval", norm_summary(json.load(fh))
        return
    with open(path, encoding="utf-8") as fh:
        for line in fh:
            line = line.strip()
            if not line:
                continue
            try:
                r = json.loads(line)
            except json.JSONDecodeError:
                continue                             # a torn last line of a file still being appended
            u = r.get("update")
            if (lo is not None and (u is None or u < lo)) or (hi is not None and (u is None or u > hi)):
                continue
            rec = norm_episode(r, asc)
            if rec is not None:
                yield "episodes", rec


# ------------------------------------------------------------------------------------------------------ accumulate
class Profile:
    """Counters of one label (streamed: episodes files are large)."""

    def __init__(self, label: str):
        self.label = label
        self.n = 0
        self.kinds: Counter = Counter()              # eval / episodes record counts
        self.prog: list[float] = []
        self.end: Counter = Counter()                # death bands + outcomes
        self.pos: Counter = Counter()                # end-location band of every game (wins: "win")
        self.killer: Counter = Counter()
        self.killer_act: dict[str, Counter] = defaultdict(Counter)
        self.killer_band: dict[str, Counter] = defaultdict(Counter)
        self.kind: Counter = Counter()
        self.room: Counter = Counter()
        self.at_death: dict[str, dict[str, list[int]]] = defaultdict(lambda: defaultdict(list))
        self.boss_hp: dict[int, list[float]] = defaultdict(list)
        self.boss_hp_abs: dict[int, list[int]] = defaultdict(list)
        self.boss: dict[str, list] = defaultdict(lambda: [0, 0, []])   # boss -> [entered, died, hp ratios]
        self.fights: list[tuple] = []                # (act, room, enemies tuple, encounter, result)
        self.fight_games = 0
        self.present: Counter = Counter()
        self.deaths = 0

    def add(self, src: str, r: dict) -> None:
        self.n += 1
        self.kinds[src] += 1
        if isinstance(r.get("progress"), (int, float)):
            self.prog.append(float(r["progress"]))
        for f in r["present"]:
            self.present[(src, f)] += 1
        band = band_of(r["act"], r["k"], r["boss"])
        res = r["result"]
        self.pos["win" if res == "win" else band] += 1
        if res != "death":
            self.end[res] += 1
        else:
            self.deaths += 1
            self.end[band] += 1
            kil = r.get("killer")
            if kil:
                self.killer[kil] += 1
                self.killer_act[kil][r["act"]] += 1
                self.killer_band[band][kil] += 1
            self.kind[kind_of(kil)] += 1
            if r.get("room"):
                self.room[r["room"]] += 1
            for f in AT_DEATH:
                if isinstance(r.get(f), int):
                    self.at_death[band][f].append(r[f])
        be = r.get("boss_entry")
        if isinstance(be, list):
            for e in be:
                act, hp, mx = int(e.get("act") or 0), e.get("hp"), e.get("max_hp")
                ratio = hp / mx if isinstance(hp, int) and isinstance(mx, int) and mx > 0 else None
                if ratio is not None:
                    self.boss_hp[act].append(ratio)
                    self.boss_hp_abs[act].append(hp)
                name = e.get("boss") or f"A{act} boss (unknown id)"
                b = self.boss[name]
                b[0] += 1
                if ratio is not None:
                    b[2].append(ratio)
                died_here = res == "death" and r["act"] == act and r["floor"] == int(e.get("floor") or -1) and \
                    (not e.get("boss") or r.get("killer") in (None, e.get("boss")))
                if died_here:
                    b[1] += 1
        fs = r.get("fights")
        if isinstance(fs, list):
            self.fight_games += 1
            for f in fs:
                self.fights.append((int(f.get("act") or 0), f.get("room"), tuple(sorted(f.get("enemies") or [])),
                                    f.get("encounter"), f.get("result")))


def resolve_fights(profiles: list[Profile]) -> dict:
    """(act, room, sorted enemies) -> encounter ids, from the fights that carry one (boss fights, lost fights),
    pooled over every label; a signature seen with two encounters stays ambiguous."""
    sig: dict[tuple, set] = defaultdict(set)
    for p in profiles:
        for act, room, en, enc, _ in p.fights:
            if enc:
                sig[(act, room, en)].add(enc)
    return sig


def fight_key(sig: dict, act: int, room, en: tuple, enc) -> str:
    if enc:
        return enc
    hit = sig.get((act, room, en))
    if hit and len(hit) == 1:
        return next(iter(hit))
    tag = "ambiguous" if hit else "never lost"
    return f"~A{act} {room}: {'+'.join(en) or '?'} ({tag})"


# ------------------------------------------------------------------------------------------------------ report
def pct(a: int, b: int) -> str:
    return f"{a} ({100 * a / b:.1f}%)" if b else str(a)


def rate(a: int, b: int) -> str:
    return f"{a}/{b} = {100 * a / b:.1f}%" if b else f"{a}/0"


def md_table(head: list[str], rows: list[list]) -> list[str]:
    out = ["| " + " | ".join(head) + " |", "|" + "---|" * len(head)]
    out += ["| " + " | ".join(str(x) for x in r) + " |" for r in rows]
    return out


def build(profiles: list[Profile], top: int) -> tuple[list[str], dict]:
    L: list[str] = []
    labels = [p.label for p in profiles]
    J: dict = {"labels": labels}
    bands = [b for b in BANDS if b in BANDS[:5] or any(p.end[b] or p.pos[b] for p in profiles)]
    J["bands"] = bands

    # (a) end location
    L += ["## (a) End location per label", ""]
    rows = [["games"] + [p.n for p in profiles],
            ["sources"] + [", ".join(f"{k} {v}" for k, v in sorted(p.kinds.items())) for p in profiles],
            ["progress mean"] + [f"{st.mean(p.prog):.4f}" if p.prog else "n/a" for p in profiles]]
    rows += [[f"death {b}"] + [pct(p.end[b], p.n) for p in profiles] for b in bands]
    rows += [[o] + [pct(p.end[o], p.n) for p in profiles] for o in OUTCOMES]
    L += md_table(["band"] + labels, rows) + [""]
    J["end"] = {p.label: {"games": p.n, "progress_mean": st.mean(p.prog) if p.prog else None,
                          "deaths": {b: p.end[b] for b in bands}, **{o: p.end[o] for o in OUTCOMES}}
                for p in profiles}

    # (b) conditional death rates
    order = bands + ["win"]
    L += ["## (b) Conditional death rate per band (deaths / games reaching the band)", ""]
    rows, jb = [], {p.label: {} for p in profiles}
    for i, b in enumerate(bands):
        row = [b]
        for p in profiles:
            reached = sum(p.pos[x] for x in order[i:])
            row.append(rate(p.end[b], reached))
            jb[p.label][b] = {"deaths": p.end[b], "reached": reached,
                              "rate": p.end[b] / reached if reached else None}
        rows.append(row)
    L += md_table(["band"] + labels, rows) + [""]
    J["conditional"] = jb

    # (c) killers
    L += ["## (c) Killers (share of the label's games; kind = encounter-id suffix)", ""]
    allk: Counter = Counter()
    for p in profiles:
        for k, v in p.killer.items():
            allk[k] += v / p.n if p.n else 0
    ranked = [k for k, _ in sorted(allk.items(), key=lambda t: (-t[1], t[0]))]
    rows = []
    for k in ranked[:top]:
        acts = sorted({a for p in profiles for a in p.killer_act.get(k, {})})
        rows.append([k, kind_of(k), ",".join(map(str, acts))] + [pct(p.killer[k], p.n) for p in profiles])
    rest = ranked[top:]
    if rest:
        rows.append([f"other {len(rest)} encounters", "", ""] +
                    [pct(sum(p.killer[k] for k in rest), p.n) for p in profiles])
    nok = [p.deaths - sum(p.killer.values()) for p in profiles]
    if any(nok):
        rows.append(["(death without killer)", "", ""] + [pct(x, p.n) for x, p in zip(nok, profiles)])
    L += md_table(["killer", "kind", "act"] + labels, rows) + [""]
    rows = [[kd] + [pct(p.kind[kd], p.n) for p in profiles] for kd in KINDS + ("?",)
            if any(p.kind[kd] for p in profiles)]
    L += ["Killer kind (deaths; ? = no killer or no suffix):", ""] + md_table(["kind"] + labels, rows) + [""]
    rows = []
    for b in bands:
        cells = []
        for p in profiles:
            c = p.killer_band.get(b)
            cells.append(", ".join(f"{k} {v}" for k, v in c.most_common(5)) if c else "")
        if any(cells):
            rows.append([b] + cells)
    L += ["Top killers per band:", ""] + md_table(["band"] + labels, rows) + [""]
    J["killers"] = {p.label: dict(p.killer.most_common()) for p in profiles}
    J["killer_kind"] = {p.label: dict(p.kind) for p in profiles}
    J["killers_by_band"] = {p.label: {b: dict(c.most_common()) for b, c in p.killer_band.items()} for p in profiles}

    # (d) run-state fields
    L += ["## (d) Run-state fields (wave 6, 2026-11-05)", ""]
    if any(p.boss for p in profiles):
        rows = []
        for act in sorted({a for p in profiles for a in p.boss_hp}):
            row = [f"A{act} boss"]
            for p in profiles:
                q, qa = quart(p.boss_hp.get(act, [])), quart([float(x) for x in p.boss_hp_abs.get(act, [])])
                row.append(f"n {len(p.boss_hp[act])}: {q[1]:.2f} [{q[0]:.2f}, {q[2]:.2f}] (hp {qa[1]:.0f})"
                           if q else "")
            rows.append(row)
        L += ["HP at act-boss entry: hp/max_hp median [q1, q3] (median hp):", ""] + \
            md_table(["boss room"] + labels, rows) + [""]
        names = sorted({b for p in profiles for b in p.boss}, key=lambda b: (-sum(p.boss[b][0] for p in profiles), b))
        rows = []
        for b in names:
            row = [b]
            for p in profiles:
                e, d, hs = p.boss.get(b, (0, 0, []))
                row.append(f"{rate(d, e)}, hp {st.median(hs):.2f}" if e and hs else (rate(d, e) if e else ""))
            rows.append(row)
        L += ["Per boss: died there / entered (median hp/max_hp at entry):", ""] + \
            md_table(["boss"] + labels, rows) + [""]
        J["boss_entry"] = {p.label: {"hp_ratio": {a: dict(zip(("q1", "median", "q3"), quart(v)))
                                                  for a, v in p.boss_hp.items() if v},
                                     "per_boss": {b: {"entered": v[0], "died": v[1],
                                                      "hp_ratio_median": st.median(v[2]) if v[2] else None}
                                                  for b, v in p.boss.items()}} for p in profiles}
    else:
        L += ["HP at boss entry: no `boss_entry` entries in any input.", ""]
    if any(p.at_death for p in profiles):
        L += ["At death, medians per band (deck / upgraded / relics / potions / gold):", ""]
        rows = []
        for b in bands:
            row = [b]
            for p in profiles:
                d = p.at_death.get(b)
                if not d:
                    row.append("")
                    continue
                cell = " / ".join(f"{st.median(d[f]):g}" if d.get(f) else "-" for f in AT_DEATH)
                row.append(cell + (f" (n {len(d['deck'])})" if d.get("deck") else ""))
            rows.append(row)
        L += md_table(["band"] + labels, rows) + [""]
        J["at_death"] = {p.label: {b: {f: st.median(v) for f, v in d.items() if v} for b, d in p.at_death.items()}
                         for p in profiles}
    else:
        L += ["At death: no deck / relics fields in any input.", ""]
    if any(p.room for p in profiles):
        rooms = sorted({r for p in profiles for r in p.room}, key=lambda r: (-sum(p.room[r] for p in profiles), r))
        rows = [[r] + [pct(p.room[r], p.deaths) for p in profiles] for r in rooms]
        rows.append(["(no room)"] + [pct(p.deaths - sum(p.room.values()), p.deaths) for p in profiles])
        L += ["Final room kind of deaths (share of deaths):", ""] + md_table(["room"] + labels, rows) + [""]
        J["death_room"] = {p.label: dict(p.room) for p in profiles}
    else:
        L += ["Death room kind: no `room` / `death.room_type` in any input (see the kind table in (c)).", ""]
    if any(p.fights for p in profiles):
        sig = resolve_fights(profiles)
        ex = {p.label: defaultdict(lambda: [0, 0]) for p in profiles}
        for p in profiles:
            for act, room, en, enc, res in p.fights:
                c = ex[p.label][fight_key(sig, act, room, en, enc)]
                c[0] += 1
                c[1] += res == "lost"
        keys = sorted({k for d in ex.values() for k in d},
                      key=lambda k: (-sum(ex[p.label][k][1] / max(1, p.fight_games)
                                          for p in profiles if k in ex[p.label]), k))
        rows = []
        for k in keys[:top]:
            rows.append([k, kind_of(k) if not k.startswith("~") else "?"] +
                        [f"{ex[lb][k][0]} / {ex[lb][k][1]} ({100 * ex[lb][k][1] / ex[lb][k][0]:.0f}%)"
                         if k in ex[lb] else "" for lb in labels])
        L += [f"Per-encounter exposure from `fights`: fought / lost (loss rate); top {top} by lost per game; a won "
              "non-boss fight's encounter is resolved by its (act, room, monsters) signature among lost / boss "
              "fights, '~' = unresolved:", ""] + md_table(["encounter", "kind"] + labels, rows) + [""]
        L += ["Games with `fights`: " + ", ".join(f"{p.label} {p.fight_games}" for p in profiles), ""]
        J["exposure"] = {lb: {k: {"fought": v[0], "lost": v[1]} for k, v in d.items()} for lb, d in ex.items()}
    else:
        L += ["Exposure: no `fights` in any input (eval summaries since 2026-11-05 only).", ""]

    # (e) gaps
    L += ["## (e) Data gaps", ""]
    J["gaps"] = {}
    for p in profiles:
        absent, partial = [], []
        for src, cnt in sorted(p.kinds.items()):
            exp = list(RUN_FIELDS) + (["fights", "act_rooms"] if src == "eval" else [])
            for f in exp:
                have = p.present[(src, f)]
                if have == 0:
                    absent.append(f)
                elif have < cnt:
                    partial.append(f"{f} ({cnt - have}/{cnt} missing)")
            if src == "eval":
                have, nd = p.present[(src, "death.room_type")], p.deaths
                if nd and have == 0:
                    absent.append("death.room_type (null)")
                elif have < nd:
                    partial.append(f"death.room_type ({nd - have}/{nd} deaths null)")
            else:
                absent.append("act_rooms (episodes: default act lengths used)")
        J["gaps"][p.label] = {"absent": absent, "partial": partial}
        L.append(f"- data gaps {p.label}: absent: {', '.join(absent) or 'none'}; "
                 f"partial: {', '.join(partial) or 'none'}")
    L.append("")
    return L, J


def parse_range(r: str) -> tuple[int, int]:
    lo, _, hi = r.partition("-")
    return int(lo), int(hi)


def parse_spec(spec: str, default_range: tuple | None) -> tuple[str, list[str], tuple | None]:
    rng = default_range
    if "@" in spec:
        spec, r = spec.rsplit("@", 1)
        rng = parse_range(r)
    label, paths = spec.split("=", 1) if "=" in spec else ("", spec)
    ps = [x for x in paths.split(",") if x]
    if not label:
        label = "/".join(os.path.normpath(ps[0]).split(os.sep)[-2:])
    return label, ps, rng


def profile(specs: list[str], update_range: str | None = None, ascension: int = 10) -> list[Profile]:
    dr = parse_range(update_range) if update_range else None
    out = []
    for spec in specs:
        label, paths, rng = parse_spec(spec, dr)
        p = Profile(label)
        for path in paths:
            for src, rec in iter_source(path, *(rng or (None, None)), ascension):
                p.add(src, rec)
        out.append(p)
    return out


def main(argv=None) -> int:
    ap = argparse.ArgumentParser(description=__doc__.split("\n\n")[0],
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("inputs", nargs="+", metavar="LABEL=SRC[,SRC...][@LO-HI]")
    ap.add_argument("--update-range", default=None, metavar="LO-HI",
                    help="episodes.jsonl rows with LO <= update <= HI (inputs without @LO-HI)")
    ap.add_argument("--ascension", type=int, default=10, help="episodes rows (eval summaries carry their own)")
    ap.add_argument("--top", type=int, default=25)
    ap.add_argument("--json", default=None, help="also write every table as JSON to this file")
    a = ap.parse_args(argv)
    profiles = profile(a.inputs, a.update_range, a.ascension)
    lines, js = build(profiles, a.top)
    print("# Death profile: " + ", ".join(f"{p.label} (n {p.n})" for p in profiles) + "\n")
    print("\n".join(lines))
    if a.json:
        with open(a.json, "w", encoding="utf-8") as f:
            json.dump(js, f, indent=1, sort_keys=True, default=str)
    return 0


if __name__ == "__main__":
    sys.exit(main())
