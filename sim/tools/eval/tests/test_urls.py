"""--urls: parsing, round-robin assignment, and a multi-server run (the last needs the Release server dll)."""

import argparse
import json
import shutil

import pytest

from sts2eval.cli import build_parser, main, parse_urls
from sts2eval.runner import assign_servers
from sts2eval.server import SpawnedServer, server_dll


def test_parse_urls():
    assert parse_urls("http://a:1, http://b:2/ ,") == ["http://a:1", "http://b:2"]
    for bad in ("", " , ", "a:1", "http://a:1,http://a:1/"):
        with pytest.raises(argparse.ArgumentTypeError):
            parse_urls(bad)


def test_urls_flag_exclusive_with_url_and_spawn(capsys):
    ap = build_parser()
    a = ap.parse_args(["run", "--seedset", "a0-s10", "--urls", "http://h:1,http://h:2"])
    assert a.urls == ["http://h:1", "http://h:2"] and a.url is None and not a.spawn
    for other in (["--url", "http://h:1"], ["--spawn"]):
        with pytest.raises(SystemExit):
            ap.parse_args(["run", "--seedset", "a0-s10", "--urls", "http://h:1", *other])
    capsys.readouterr()


def test_assign_servers_round_robin():
    assert assign_servers([{}] * 5, 2) == [0, 1, 0, 1, 0]
    assert assign_servers([{}] * 3, 1) == [0, 0, 0]
    assert assign_servers([{}] * 2, 4) == [0, 1]


@pytest.mark.skipif(not server_dll().exists() or shutil.which("dotnet") is None,
                    reason="server dll not built (dotnet build sim/Sts2Sim.sln -c Release)")
def test_run_over_two_servers(tmp_path):
    seedset = {"name": "tiny", "ascension": 0, "content": "ported_only",
               "games": [{"seed": f"URLS0{i}", "character": c}
                         for i, c in enumerate(("IRONCLAD", "SILENT", "DEFECT"))]}
    sp = tmp_path / "tiny.json"
    sp.write_text(json.dumps(seedset))
    with SpawnedServer(tmp_path / "s0") as a, SpawnedServer(tmp_path / "s1") as b:
        out = tmp_path / "out"
        rc = main(["run", "--seedset", str(sp), "--policy", "random", "--concurrency", "3",
                   "--urls", f"{a.url},{b.url}", "--out", str(out), "--progress-interval", "0"])
        assert rc == 0
        want = [a.url, b.url, a.url]
        for g, url in zip(seedset["games"], want):
            key = f"{g['character']}-{g['seed']}"
            s = json.loads((out / "games" / f"{key}.summary.json").read_text())
            assert s["server_url"] == url, s
            assert s["replayable"] is True and s["steps"] > 0
            start = json.loads((out / "games" / f"{key}.jsonl").read_text().splitlines()[0])
            assert start["type"] == "start" and start["server_url"] == url
        cfg = json.loads((out / "config.json").read_text())
        assert cfg["server_urls"] == [a.url, b.url] and cfg["server_url"] == a.url
        assert set(cfg["server_versions"]) == {a.url, b.url} and cfg["server_spawned"] is False

        # the same seed set on one server gives the same token lists (the server does not change a game)
        out1 = tmp_path / "out1"
        assert main(["run", "--seedset", str(sp), "--policy", "random", "--concurrency", "3",
                     "--url", b.url, "--out", str(out1), "--progress-interval", "0"]) == 0
        for g in seedset["games"]:
            key = f"{g['character']}-{g['seed']}"
            x = json.loads((out / "games" / f"{key}.summary.json").read_text())
            y = json.loads((out1 / "games" / f"{key}.summary.json").read_text())
            assert x["history_tokens"] == y["history_tokens"]
            assert y["server_url"] == b.url
