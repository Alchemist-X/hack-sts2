"""`sts2train report-card` / `pack` / `probe` (sts2train.reportcard): stages, endings and Wilson intervals on synthetic
game summaries; a card built from existing run directories (reused, nothing re-played; a stale one re-run); a real
two-game card through the CLI, its weights bundle (round trip, tamper check) and the probe on its eval; a card over
the run directories `python -m sts2train.lookahead eval` writes (search run, --greedy run, paired per seed)."""

from __future__ import annotations

import io
import json
import math
import tarfile
from pathlib import Path

import pytest
import torch
from sts2vec import protocol as PG

from sts2train import reportcard as RC

A10 = 10
TWO = {"name": "rc-two", "ascension": A10, "content": "ported_only",
       "games": [{"seed": "C1A10SI01", "character": "SILENT"}, {"seed": "C1A10SI02", "character": "SILENT"}]}


def _game(seed, result, act, floor, *, enemies=None, starts=None, rooms=None, phase=None, **extra) -> dict:
    """A game summary as `sts2vec eval` writes it (the fields the card reads), progress from protocol.compute."""
    s = {"key": f"SILENT-{seed}", "seed": seed, "character": "SILENT", "ascension": A10, "content": "ported_only",
         "result": result, "act": act, "floor": floor, "act_start_floor": starts or {"1": 0},
         "act_rooms": rooms or {"1": 17}, "steps": 100, "turns": 10, "illegal": 0, "seconds": 1.0,
         "replayable": True, "history_actions": [0]}
    if result == "loss":
        s["death"] = {"floor": floor, "act": act, "room_type": None, "enemies": enemies or []}
    if result == "stuck":
        s["stuck"] = {"phase": phase, "floor": floor}
    s.update(extra)
    s.update(PG.compute(result, act, floor, A10, s["act_start_floor"], s["act_rooms"]))
    return s


S2 = {"1": 0, "2": 17}
R2 = {"1": 17, "2": 16}
S3 = {"1": 0, "2": 17, "3": 33}
R3 = {"1": 17, "2": 16, "3": 16}
GAMES = [
    _game("G01", "loss", 1, 5, enemies=["SLIMES_NORMAL"]),
    _game("G02", "loss", 1, 9, enemies=["BYRDONIS_ELITE"]),
    _game("G03", "loss", 1, 17, enemies=["CEREMONIAL_BEAST_BOSS"]),      # died in the Act-1 boss room
    _game("G04", "stuck", 1, 17, phase="Rewards"),                        # stuck after winning the boss fight
    _game("G05", "capped", 1, 17),                                        # capped in the boss room: not beaten
    _game("G06", "loss", 1, 17, rooms={"1": 18}, enemies=["TOADPOLES_WEAK"]),   # golden-compass act: 17 < 18
    _game("G07", "loss", 2, 20, starts=S2, rooms=R2, enemies=["EXOSKELETONS_NORMAL"]),
    _game("G08", "loss", 2, 33, starts=S2, rooms=R2, enemies=["KNOWLEDGE_DEMON_BOSS"]),
    _game("G09", "stuck", 2, 33, starts=S2, rooms=R2, phase="Rewards"),
    _game("G10", "loss", 3, 40, starts=S3, rooms=R3, enemies=["SOUL_NEXUS_ELITE"]),
    _game("G11", "win", 3, 49, starts=S3, rooms=R3),
    _game("G12", "loss", 1, 3),                                           # no encounter recorded
    _game("G13", "loss", 1, 6, enemies=["PUNCH_OFF_EVENT_ENCOUNTER"]),
    _game("G14", "crashed", 1, 4),
]


def _wilson_ref(k: int, n: int, z: float = 1.96) -> tuple[float, float]:
    p = k / n
    d = 1 + z * z / n
    c = (p + z * z / (2 * n)) / d
    h = z * math.sqrt(p * (1 - p) / n + z * z / (4 * n * n)) / d
    return max(0.0, c - h), min(1.0, c + h)


def test_wilson_reference_values():
    assert PG.wilson(0, 20)[1] == pytest.approx(0.1611, abs=1e-4)        # protocol v0.2 §3: 16.1%
    assert PG.wilson(0, 100)[1] == pytest.approx(0.0370, abs=1e-4)
    assert PG.wilson(1, 10) == pytest.approx((0.0179, 0.4042), abs=1e-4)  # textbook Wilson score interval
    for k, n in ((0, 14), (3, 14), (8, 14), (14, 14), (37, 1000)):
        row = RC.wilson_row(k, n)
        assert row["ci95"] == pytest.approx(list(_wilson_ref(k, n)), abs=1e-12)
        assert row["rate"] == k / n and row["count"] == k and row["games"] == n


def test_stages_staged_table_and_wilson():
    st = {s["seed"]: RC.stages(s) for s in GAMES}
    assert st["G03"]["act1_boss"] and not st["G03"]["act1_boss_beaten"]
    assert st["G04"]["act1_boss_beaten"] and not st["G04"]["act2"]          # stuck on the boss floor = beaten
    assert st["G05"]["act1_boss"] and not st["G05"]["act1_boss_beaten"]     # capped there is not
    assert not st["G06"]["act1_boss"]                                       # 18-room act: floor 17 is before the boss
    assert st["G07"]["act1_boss_beaten"] and st["G07"]["act2"] and not st["G07"]["act2_boss"]
    assert st["G08"]["act2_boss"] and not st["G08"]["act2_boss_beaten"]
    assert st["G09"]["act2_boss_beaten"] and not st["G09"]["act3"]
    assert st["G10"]["act3"] and not st["G10"]["cleared"]
    assert all(st["G11"].values())
    assert not any(st["G14"].values())

    table = RC.staged_table(GAMES)
    want = {"act1_boss": 8, "act1_boss_beaten": 6, "act2": 5, "act2_boss": 4, "act2_boss_beaten": 3, "act3": 2,
            "cleared": 1}
    assert list(table) == [k for k, _ in RC.STAGES]
    for key, k in want.items():
        assert (table[key]["count"], table[key]["games"]) == (k, len(GAMES)), key
        assert table[key]["ci95"] == pytest.approx(list(_wilson_ref(k, len(GAMES))), abs=1e-12)
    # the staged rows agree with the protocol's own act bookkeeping: progress >= 1/3 iff act 2 was reached
    assert sum(s["progress"] >= 1 / 3 for s in GAMES) == want["act2"]


def test_endings_floor_histogram_and_readouts():
    e = RC.endings_table(GAMES)
    assert {k: v["games"] for k, v in e.items()} == {
        "won": 1, "died_boss": 2, "died_elite": 2, "died_normal": 3, "died_event": 1, "died_unknown": 1, "stuck": 2,
        "capped": 1, "crashed": 1}
    assert [d["name"] for d in e["died_boss"]["detail"]] == ["CEREMONIAL_BEAST_BOSS", "KNOWLEDGE_DEMON_BOSS"]
    assert e["stuck"]["detail"] == [{"name": "Rewards", "games": 2}]
    assert sum(v["share"] for v in e.values()) == pytest.approx(1.0)
    assert RC.encounter_type("TUNNELER_WEAK") == "normal" and RC.encounter_type("OVERGROWTH_CRAWLERS") == "normal"

    hist = {(r["act"], r["floor"]): r["games"] for r in RC.floor_histogram(GAMES)}
    assert hist[(1, 17)] == 4 and hist[(2, 33)] == 2 and sum(hist.values()) == len(GAMES)

    assert RC.at_death(GAMES) is None                         # sts2vec eval records neither: the card omits it
    rich = [dict(GAMES[0], deck_n=12, relic_n=2), dict(GAMES[1], death=dict(GAMES[1]["death"], deck_size=16,
                                                                            relics=["A", "B", "C"])), GAMES[10]]
    assert RC.at_death(rich) == {"deaths": 2, "deck_mean": 14.0, "deck_games": 2, "relics_mean": 2.5,
                                 "relic_games": 2}

    assert RC.win_prob_readout(GAMES) is None
    wp = [dict(GAMES[10], win_prob=[0.3, 0.9]), dict(GAMES[0], win_prob=[0.2, 0.05]),
          dict(GAMES[1], win_prob=[0.1, 0.01]), dict(GAMES[3], win_prob_first=0.2, win_prob_last=0.3), GAMES[2]]
    r = RC.win_prob_readout(wp)
    assert r["games"] == 4 and r["first_mean"] == pytest.approx(0.2) and r["observed_win_rate"] == 0.25
    assert r["last"]["loss"] == {"mean": pytest.approx(0.03), "games": 2}
    assert r["last"]["win"]["mean"] == pytest.approx(0.9) and r["last"]["other"]["mean"] == pytest.approx(0.3)

    stats = RC.run_stats(GAMES, A10)
    h = PG.aggregate(GAMES, A10)
    assert stats["headline"]["progress_mean"] == h["progress_mean"] and stats["headline"]["wins"] == 1


# ------------------------------------------------------------------------------------------------ cards from run dirs


def _checkpoint(path: Path) -> str:
    """An untrained Silent A10 smoke-preset checkpoint for this library; -> its weights hash."""
    from sts2vec import load, schema_of

    from sts2train.features import ModelSpec, bind_spec
    from sts2train.model import Policy
    from sts2train.ppo import TrainConfig, save_checkpoint

    spec = ModelSpec.from_schema(schema_of(load()))
    bind_spec(spec)
    torch.manual_seed(1)
    return save_checkpoint(path, Policy(spec), spec, TrainConfig(characters=("SILENT",), ascension=A10),
                           {"updates": 7, "env_steps": 4096})


def _write_run(d: Path, ss: dict, summaries: list[dict], **config) -> None:
    (d / "games").mkdir(parents=True, exist_ok=True)
    for g, s in zip(ss["games"], summaries):
        s = dict(s, key=PG.game_key(g), seed=g["seed"], character=g["character"])
        (d / "games" / f"{s['key']}.summary.json").write_text(json.dumps(s))
    (d / "config.json").write_text(json.dumps({"seedset": ss, "limits": RC.LIMITS, "eval_seed": 0, **config}))
    PG.write_report(d)


def _no_play(*a, **k):
    raise AssertionError("a reusable evaluation was played again")


def test_card_from_existing_run_dirs(tmp_path, monkeypatch):
    """Every run directory is reused (nothing is played); the lookahead dir becomes a labelled row; the tables are
    the synthetic games'; a stale greedy eval (other weights) is set aside and re-run."""
    import sts2train.evaluate as E
    import sts2vec.evaluate as VE

    lib = RC.library_info()["version"]
    ck = tmp_path / "ck.pt"
    wh = _checkpoint(ck)
    ss = {"name": "rc-14", "ascension": A10, "content": "ported_only",
          "games": [{"seed": s["seed"], "character": "SILENT"} for s in GAMES]}
    ss_path = tmp_path / "rc-14.json"
    ss_path.write_text(json.dumps(ss))
    out, cache = tmp_path / "card", tmp_path / "baselines"
    lb = {"mode": "on", "k": 3, "window": 64, "leave_kinds": []}
    rich = [dict(s, win_prob=[0.1, 0.9 if s["result"] == "win" else 0.02], deck_n=12 + i, relic_n=2)
            for i, s in enumerate(GAMES)]
    _write_run(out / "evals" / "rc-14", ss, rich, policy="nn:ck.pt", weights_hash=wh, server_version=lib,
               loop_breaker=lb)
    _write_run(cache / f"random-rc-14-{PG.checksum(ss)[:12]}-{lib.split('/')[1]}", ss,
               [_game(s["seed"], "loss", 1, 2) for s in GAMES], policy="random", server_version=lib)
    la = tmp_path / "la"
    _write_run(la, ss, GAMES[::-1], policy="nn:ck.pt+la(K8,R1,turn)", weights_hash=wh, server_version=lib,
               information="determinized, combat-only", search={"K": 8, "R": 1})
    monkeypatch.setattr(E, "evaluate_model", _no_play)
    monkeypatch.setattr(VE, "evaluate", _no_play)

    card = RC.report_card(ck, out, seedsets=[str(ss_path)], threads=1, baselines=("random",), baseline_cache=cache,
                          lookahead_dirs=[la], log=lambda m: None)
    sec, = card["seedsets"]
    assert sec["games"] == 14 and sec["ref"] == str(ss_path.resolve()) and sec["first_game"] == "SILENT-G01"
    assert [(r["kind"], r["reused"]) for r in sec["runs"]] == [("nn", True), ("lookahead", True), ("baseline", True)]
    nn, look, base = sec["runs"]
    assert nn["label"] == RC.GREEDY_LABEL and look["label"] == f"{RC.LOOKAHEAD_LABEL}: la(K8,R1,turn)"
    assert nn["staged"]["act2"]["count"] == 5 and nn["staged"]["cleared"]["count"] == 1
    assert nn["staged"]["act2"]["ci95"] == pytest.approx(list(_wilson_ref(5, 14)))
    assert look["staged"] == nn["staged"] and look["search"] == {"K": 8, "R": 1} and not look["notes"]
    assert look["search_stats"] is None                                  # no stats.json next to it
    # no --greedy run given: paired against the card's greedy row (loop breaker on), flagged as not like for like
    p = look["paired"]
    assert p["vs"] == RC.GREEDY_LABEL and p["vs_kind"] == "nn" and not p["like_for_like"] and p["games"] == 14
    d = sorted(b["progress"] - a["progress"] for a, b in zip(rich, GAMES[::-1]))
    assert p["delta_mean"] == pytest.approx(sum(d) / 14, abs=1e-6)
    assert (p["better"], p["worse"], p["same"]) == (sum(x > 1e-9 for x in d), sum(x < -1e-9 for x in d),
                                                    sum(abs(x) <= 1e-9 for x in d))
    assert nn["win_prob"]["first_mean"] == pytest.approx(0.1) and look["win_prob"] is None
    assert nn["at_death"]["deaths"] == 9 and look["at_death"] is None
    assert base["staged"]["act1_boss"]["count"] == 0 and base["headline"]["floor_mean"] == 2.0
    c = card["checkpoint"]
    assert c["weights_hash"] == wh and c["weights_hash_ok"] and c["update"] == 7 and c["preset"] == "smoke"
    assert c["sha256"] == RC.file_sha256(ck) and not c["aux_heads"] and c["params"] > 0
    assert json.loads((out / "model-card.json").read_text()) == card
    md = (out / "model-card.md").read_text()
    for text in ("## rc-14: A10, 14 games", "| reached Act 2 | 5/14 [", "lookahead, determinized: la(K8,R1,turn)",
                 "died: boss: CEREMONIAL_BEAST_BOSS 1, KNOWLEDGE_DEMON_BOSS 1", "stuck (floor step cap)",
                 "| random |", "### Deck and relics at death (lost games)", "| NN greedy | 9 | ",
                 "| NN greedy | 14 | 0.100 | 7.1% | 0.020 (9) | 0.900 (1) | 0.020 (4) |"):
        assert text in md, text

    # the greedy eval was played by other weights: set aside, played again (here: a stand-in writing the run)
    cfg = json.loads((out / "evals" / "rc-14" / "config.json").read_text())
    (out / "evals" / "rc-14" / "config.json").write_text(json.dumps(dict(cfg, weights_hash="0" * 16)))
    played = []

    def fake_eval(model, spec, seedset, out_dir, *, weights_hash, **kw):
        played.append(weights_hash)
        _write_run(Path(out_dir), seedset, GAMES[:1] * 14, policy="nn:ck.pt", weights_hash=weights_hash,
                   server_version=lib, loop_breaker=lb)

    monkeypatch.setattr(E, "evaluate_model", fake_eval)
    card = RC.report_card(ck, out, seedsets=[str(ss_path)], threads=1, baselines=("random",), baseline_cache=cache,
                          log=lambda m: None)
    assert played == [wh] and [p.name.startswith("rc-14.stale-") for p in (out / "evals").iterdir()].count(True) == 1
    nn = card["seedsets"][0]["runs"][0]
    assert not nn["reused"] and nn["staged"]["act1_boss"]["count"] == 0 and nn["headline"]["games"] == 14


def test_lookahead_dir_rows():
    ss = {"name": "x", "ascension": A10, "content": "ported_only", "games": []}
    search = {"algo": "la-v1", "K": 8, "R": 1, "H": 12, "horizon": "turn", "score": "value+hp"}
    assert RC.lookahead_row({"seedset": ss, "policy": "nn:best.pt+la(8,1,turn,honest)", "search": search}, "d") == (
        "lookahead", f"{RC.LOOKAHEAD_LABEL}: la(8,1,turn,honest) value+hp")
    assert RC.lookahead_row({"policy": "nn:best.pt", "greedy": True, "loop_breaker": {"mode": "off"}}, "d") == (
        "greedy-ref", "NN greedy, loop breaker off")
    assert RC.lookahead_row({"policy": "nn:best.pt", "greedy": True}, "d")[1] == "NN greedy, loop breaker off"
    assert RC.lookahead_row({"policy": "nn:best.pt", "greedy": True, "loop_breaker": {"mode": "on"}}, "d")[1] == (
        "NN greedy, loop breaker on")
    for cfg in ({"policy": "random"}, {"policy": "nn:best.pt"}, {"policy": "claude-x", "greedy": True}):
        with pytest.raises(ValueError, match="not a `python -m sts2train.lookahead eval` run directory"):
            RC.lookahead_row(cfg, "d")


def test_cli_exit_code_when_the_replay_audit_fails(tmp_path, monkeypatch, capsys):
    from sts2train.cli import main

    def card(replayable):
        h = {"games": 2, "progress_mean": 0.1, "progress_ci95": [0.0, 0.2], "wins": 0, "win_rate_ci95": [0, 0.66],
             "replayable": replayable}
        return {"seedsets": [{"id": "x", "runs": [{"kind": "nn", "reused": True, "headline": h,
                                                   "staged": {"act2": {"count": 0}}}]}]}

    for replayable, want in ((2, 0), (1, 1)):
        monkeypatch.setattr(RC, "report_card", lambda *a, r=replayable, **k: card(r))
        rc = main(["report-card", "--checkpoint", "ck.pt", "--out", str(tmp_path / "o"), "--threads", "1"])
        cap = capsys.readouterr()
        assert rc == want and json.loads(cap.out)["seedsets"][0]["replayable"] == replayable
        assert ("replay audit failed" in cap.err) == (want == 1)


# ------------------------------------------------------------------------------------------------ real two-game card


@pytest.fixture(scope="module")
def real_card(tmp_path_factory):
    """An untrained checkpoint's card on two A10 Silent games (greedy eval + the random baseline), made through the
    CLI; a second invocation reuses both evaluations."""
    from sts2train.cli import main

    n = torch.get_num_threads()
    tmp = tmp_path_factory.mktemp("rc-real")
    ck = tmp / "ck.pt"
    wh = _checkpoint(ck)
    ss = tmp / "rc-two.json"
    ss.write_text(json.dumps(TWO))
    args = ["report-card", "--checkpoint", str(ck), "--out", str(tmp / "card"), "--seedsets", str(ss), "--baselines",
            "random", "--baseline-cache", str(tmp / "baselines"), "--threads", "2"]
    try:
        assert main(args) == 0
        first = json.loads((tmp / "card" / "model-card.json").read_text())
        assert main(args) == 0
    finally:
        torch.set_num_threads(n)
    return {"tmp": tmp, "ck": ck, "wh": wh, "first": first,
            "card": json.loads((tmp / "card" / "model-card.json").read_text())}


def test_real_card_matches_the_eval_report(real_card):
    sec, = real_card["card"]["seedsets"]
    nn, rnd = sec["runs"]
    assert [r["reused"] for r in real_card["first"]["seedsets"][0]["runs"]] == [False, False]
    assert nn["reused"] and rnd["reused"] and nn["weights_hash"] == real_card["wh"]
    rep = json.loads((Path(nn["dir"]) / "report.json").read_text())
    for k in ("games", "wins", "progress_mean", "progress_ci95", "replayable", "floor_mean"):
        assert nn["headline"][k] == rep["headline"][k], k
    assert nn["headline"]["replayable"] == 2 and nn["loop_breaker"]["mode"] == "on"
    assert rnd["policy"] == "random" and Path(rnd["dir"]).parent == real_card["tmp"] / "baselines"
    assert sum(e["games"] for e in nn["endings"].values()) == 2
    md = (real_card["tmp"] / "card" / "model-card.md").read_text()
    games = (Path(nn["dir"]) / "games").glob("*.summary.json")
    losses = any(json.loads(p.read_text()).get("result") == "loss" for p in games)
    # sts2vec eval records deck / relics since 2026-11-05 (sts2vec.runstats): shown when a game was lost
    assert ("Deck and relics at death" in md) == losses
    assert "No readout: the checkpoint has no aux heads" in md


def test_pack_round_trip(real_card, tmp_path, capsys):
    from sts2train.cli import main
    from sts2train.ppo import load_checkpoint, weights_hash

    ck, card_dir = real_card["ck"], real_card["tmp"] / "card"
    out = tmp_path / "rc-weights.tar.gz"
    n = torch.get_num_threads()
    try:
        assert main(["pack", "--checkpoint", str(ck), "--out", str(out), "--card", str(card_dir),
                     "--baselines", "none", "--threads", "1"]) == 0
    finally:
        torch.set_num_threads(n)
    res = _cli_json(capsys.readouterr().out)
    assert res["bundle"] == "rc-weights" and res["sha256"] == RC.file_sha256(out) and res["files"] == 8
    assert res["card_dir"] == str(card_dir) and res["seedsets"][0]["reused"]
    check = RC.verify_bundle(out)
    assert check["root"] == "rc-weights" and check["files"] == 8
    with tarfile.open(out, "r:gz") as tar:
        tar.extractall(tmp_path / "x", filter="data")
    b = tmp_path / "x" / "rc-weights"
    names = sorted(str(p.relative_to(b)) for p in b.rglob("*") if p.is_file())
    assert names == ["README-weights.md", "SHA256SUMS", "checkpoint/ck.pt", "checkpoint/config.json",
                     "checkpoint/spec.json", "library.json", "manifest.json", "model-card.json", "model-card.md"]
    assert (b / "checkpoint" / "ck.pt").read_bytes() == ck.read_bytes()
    model, spec, kk = load_checkpoint(b / "checkpoint" / "ck.pt")
    assert weights_hash(model) == real_card["wh"] == kk["weights_hash"]
    raw = torch.load(ck, map_location="cpu", weights_only=False)
    assert json.loads((b / "checkpoint" / "spec.json").read_text()) == raw["spec"]
    assert json.loads((b / "checkpoint" / "config.json").read_text())["config"] == raw["config"]
    assert (b / "model-card.md").read_bytes() == (card_dir / "model-card.md").read_bytes()
    lib = json.loads((b / "library.json").read_text())
    assert lib["encoder_version"] == real_card["card"]["library"]["encoder_version"] and lib["abi_version"] >= 1
    man = json.loads((b / "manifest.json").read_text())
    assert man["checkpoint"]["sha256"] == RC.file_sha256(ck) and man["checkpoint"]["weights_hash"] == real_card["wh"]
    for e in man["files"]:
        assert RC.file_sha256(b / e["path"]) == e["sha256"] and (b / e["path"]).stat().st_size == e["bytes"]
    readme = (b / "README-weights.md").read_text()
    for text in ("shasum -a 256 -c SHA256SUMS", "sts2train eval --seedset", "sts2train report-card", "sts2train probe",
                 "--game SILENT-C1A10SI01", RC.LIVE_TWIN, "scripts/mcp_mod.sh on", "scripts/mcp_mod.sh off",
                 '--checkpoint "$B/checkpoint/ck.pt"'):
        assert text in readme, text

    # a changed file no longer verifies
    bad = tmp_path / "bad.tar.gz"
    with tarfile.open(out, "r:gz") as src, tarfile.open(bad, "w:gz") as dst:
        for m in src.getmembers():
            data = src.extractfile(m).read()
            if m.name.endswith("model-card.md"):
                data = data.replace(b"Model card", b"Model kard")
            m.size = len(data)
            dst.addfile(m, io.BytesIO(data))
    with pytest.raises(RC.BundleError, match="model-card.md"):
        RC.verify_bundle(bad)


def _cli_json(text: str) -> dict:
    """The JSON object a card command prints last (after its log lines)."""
    i = text.rfind("\n{\n")
    return json.loads(text[i + 1:] if i >= 0 else text)


def test_probe_replays_the_eval(real_card, tmp_path):
    nn = real_card["card"]["seedsets"][0]["runs"][0]
    hist = json.loads((Path(nn["dir"]) / "games" / "SILENT-C1A10SI01.summary.json").read_text())["history_actions"]
    logs: list[str] = []
    n = torch.get_num_threads()
    try:
        rows = RC.probe(real_card["ck"], nn["dir"], games=["C1A10SI01"], top=2, out=tmp_path / "p.jsonl", threads=2,
                        log=logs.append)
    finally:
        torch.set_num_threads(n)
    assert [r["n"] for r in rows] == list(range(len(hist))) and {r["key"] for r in rows} == {"SILENT-C1A10SI01"}
    assert [r["taken"]["index"] for r in rows] == hist
    assert all(len(r["top"]) <= 2 and r["top"][0] == r["greedy"] and r["phase"] for r in rows)
    assert all(0.0 <= r["taken"]["prob"] <= 1.0 for r in rows)
    assert len((tmp_path / "p.jsonl").read_text().splitlines()) == len(rows)
    assert not any("WARNING" in m for m in logs)                   # the replay reached the recorded end state


# ------------------------------------------------------------------------------------------------ lookahead run dirs


def test_card_reads_lookahead_eval_dirs(tmp_path, capsys):
    """The run directories `python -m sts2train.lookahead eval` writes, on two games through both CLIs: the search run
    is a "lookahead, determinized" row with its declaration and stats.json counters, paired per seed against the
    --greedy run (like for like: loop breaker off), itself an "NN greedy, loop breaker off" row; the card's own greedy
    eval keeps the loop breaker on. Without the --greedy run the pairing falls back to the card's greedy row (flagged);
    a directory that is neither (a baseline run) is refused before any game is played."""
    from sts2train import lookahead as L
    from sts2train.cli import main

    ck = tmp_path / "ck.pt"
    wh = _checkpoint(ck)
    ss = tmp_path / "rc-two.json"
    ss.write_text(json.dumps(TWO))
    la, gr, out = tmp_path / "la", tmp_path / "greedy", tmp_path / "card"
    common = ["--seedset", str(ss), "--checkpoint", str(ck), "--threads", "2"]
    n = torch.get_num_threads()
    try:
        assert L.main(["eval", *common, "--out", str(la), "--K", "4"]) == 0
        assert L.main(["eval", *common, "--out", str(gr), "--greedy"]) == 0
        capsys.readouterr()
        assert main(["report-card", "--checkpoint", str(ck), "--out", str(out), "--seedsets", str(ss), "--baselines",
                     "none", "--threads", "2", "--lookahead-dir", str(la), str(gr)]) == 0
        res = _cli_json(capsys.readouterr().out)
        card = json.loads((out / "model-card.json").read_text())
        sec, = card["seedsets"]
        assert [(r["kind"], r["label"]) for r in sec["runs"]] == [
            ("nn", RC.GREEDY_LABEL), ("lookahead", f"{RC.LOOKAHEAD_LABEL}: la(4,1,turn,honest) value+hp"),
            ("greedy-ref", f"{RC.GREEDY_LABEL}, loop breaker off")]
        nn, look, g = sec["runs"]
        assert nn["loop_breaker"]["mode"] == "on" and g["loop_breaker"]["mode"] == "off"
        assert look["loop_breaker"] is None
        assert look["weights_hash"] == g["weights_hash"] == wh and not look["notes"] and not g["notes"]
        assert look["search"]["K"] == 4 and look["search"]["score"] == "value+hp"
        assert look["information"] == "determinized, combat-only" and look["headline"]["replayable"] == 2
        st = json.loads((la / "stats.json").read_text())
        ls = look["search_stats"]
        assert ls["searched"] == st["searched"] > 0 and ls["desyncs"] == 0 and ls["api"] in ("branch", "pool")
        assert ls["changed_rate"] == st["derived"]["changed_rate"] and ls["code"] == st["code"]
        want = L.paired(gr, la)
        p = look["paired"]
        assert p["vs"] == g["label"] and p["vs_kind"] == "greedy-ref" and p["like_for_like"] and p["games"] == 2
        assert {k: p[k] for k in RC.PAIRED_KEYS} == {k: want[k] for k in RC.PAIRED_KEYS}
        assert res["lookahead"] == [{"seedset": sec["id"], "label": look["label"], "games": 2,
                                     "progress_mean": look["headline"]["progress_mean"], "replayable": 2,
                                     "paired": {k: p[k] for k in ("vs", "like_for_like", "delta_mean", "t_ci95",
                                                                  "better", "worse", "same")}}]
        md = (out / "model-card.md").read_text()
        for text in ("### Lookahead: declared search, paired per seed",
                     f"| {look['label']} | {g['label']} | {p['delta_mean']:+.4f} |",
                     "no loop breaker (outside the search: the plain argmax)", "| NN greedy, loop breaker off |"):
            assert text in md, text

        card = RC.report_card(ck, out, seedsets=[str(ss)], threads=2, baselines=(), lookahead_dirs=[la],
                              log=lambda m: None)
        nn, look = card["seedsets"][0]["runs"]
        assert nn["reused"] and look["paired"]["vs"] == RC.GREEDY_LABEL and not look["paired"]["like_for_like"]
        assert "(loop breaker on)" in (out / "model-card.md").read_text()
    finally:
        torch.set_num_threads(n)

    rnd = tmp_path / "random"
    _write_run(rnd, TWO, [_game("x", "loss", 1, 2)] * 2, policy="random")
    with pytest.raises(ValueError, match="not a `python -m sts2train.lookahead eval` run directory"):
        RC.report_card(ck, tmp_path / "card2", seedsets=(), baselines=(), lookahead_dirs=[rnd], log=_no_play)
    assert not (tmp_path / "card2" / "model-card.json").exists()
