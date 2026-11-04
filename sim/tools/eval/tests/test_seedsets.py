import json
from collections import Counter

import pytest

from sts2eval import seedsets as S


@pytest.mark.parametrize("name,n,asc", [("a0-s10", 10, 0), ("a0-s20", 20, 0), ("a10-s20", 20, 10)])
def test_committed_seedsets(name, n, asc):
    d = S.load(name)
    assert d["name"] == name
    assert d["ascension"] == asc
    assert d["content"] == "ported_only"
    assert len(d["games"]) == n
    per = Counter(g["character"] for g in d["games"])
    assert set(per) == set(S.CHARACTERS)
    assert set(per.values()) == {n // 5}
    for g in d["games"]:
        assert len(g["seed"]) <= 10 and g["seed"].isalnum() and g["seed"].upper() == g["seed"]


FROZEN = {  # protocol v0.2 (docs/sts2-bench-protocol-v0.2-2026-11-01.md): never edit; publish a new id instead
    "std-a0-v1": ("a0-s20", "22bb774255260efb9fbab6dea0df41d26de9d3da49364f37f3fab88bf0d81e01"),
    "std-a10-v1": ("a10-s20", "f072b6dd171cc16da26a053b01a0ec87b7a28028542ca44cfd6108e68bcbee16"),
    "smoke-a0-v1": ("a0-s10", "ef6f0ac92552406ad00cf9a9cb8c15d290a5e255cf1870ff6b458422ad570ebb"),
    "silent-a0-v1": ("silent-a0-s20", "1c0bf087e2b28e986484b412b64c705706996f79dac261de3d80abc8e7c15206"),
    "silent-a10-v1": ("silent-a10-s20", "e82165b0a2e86c5700d87eba86b17c4b8e657393d4fb6c4498b661983d322199"),
}


@pytest.mark.parametrize("sid", sorted(FROZEN))
def test_frozen_seedsets(sid):
    name, sha = FROZEN[sid]
    by_id, by_name = S.load(sid), S.load(name)
    assert by_id == by_name and by_id["id"] == sid and by_id["version"] == 1
    assert by_id["sha256"] == sha == S.checksum(by_id)
    ident = S.identity(by_id)
    assert ident == {"seedset_id": sid, "seedset_version": 1, "seedset_sha256": sha, "seedset_frozen": True}
    # a seed set embedded in an older config.json (no id/sha fields) is still recognised by its content
    bare = {k: by_id[k] for k in ("name", "ascension", "content", "games")}
    assert S.identity(bare)["seedset_id"] == sid and S.identity(bare)["seedset_frozen"]


def test_checksum_guards_frozen_content(tmp_path):
    d = S.load("std-a0-v1")
    d["games"] = d["games"][:-1] + [{"seed": "E1A0NE05", "character": "NECROBINDER"}]
    p = tmp_path / "tampered.json"
    p.write_text(json.dumps(d))
    with pytest.raises(S.SeedSetError, match="sha256"):
        S.load(str(p))
    assert not S.identity({k: d[k] for k in ("name", "ascension", "content", "games")})["seedset_frozen"]


# Single-character target sets (role "target"; Silent A10 training cycle, 2026-11-03). Not official: leaderboard
# rows use std-*, rows on these are labelled. Seeds are a fixed prefix + 01..20, disjoint from std/smoke so the
# official sets stay held out from target-set model selection; ascension/content/protocol equal the std counterpart.
SILENT_TARGET = {"silent-a0-v1": ("std-a0-v1", "C1A0SI"), "silent-a10-v1": ("std-a10-v1", "C1A10SI")}


@pytest.mark.parametrize("sid", sorted(SILENT_TARGET))
def test_silent_target_seedsets(sid):
    std_id, prefix = SILENT_TARGET[sid]
    d, std = S.load(sid), S.load(std_id)
    assert d["role"] == "target" and std["role"] == "official"
    assert (d["ascension"], d["content"], d["protocol"]) == (std["ascension"], std["content"], std["protocol"])
    assert d["games"] == [{"seed": f"{prefix}{i:02d}", "character": "SILENT"} for i in range(1, 21)]
    official = {(g["character"], g["seed"])
                for x in ("std-a0-v1", "std-a10-v1", "smoke-a0-v1") for g in S.load(x)["games"]}
    assert not official & {(g["character"], g["seed"]) for g in d["games"]}


def test_a0_s20_extends_a0_s10():
    small = {(g["character"], g["seed"]) for g in S.load("a0-s10")["games"]}
    big = {(g["character"], g["seed"]) for g in S.load("a0-s20")["games"]}
    assert small <= big


def test_load_by_path_and_validation(tmp_path):
    p = tmp_path / "x.json"
    p.write_text(json.dumps({"name": "x", "ascension": 0, "content": "ported_only",
                             "games": [{"seed": "AB12", "character": "SILENT"}]}))
    assert S.load(str(p))["games"][0]["seed"] == "AB12"
    bad = [
        {"name": "x", "ascension": 0, "content": "ported_only", "games": [{"seed": "ab12", "character": "SILENT"}]},
        {"name": "x", "ascension": 0, "content": "ported_only", "games": [{"seed": "ABCDEFGHIJK", "character": "SILENT"}]},
        {"name": "x", "ascension": 0, "content": "ported_only", "games": [{"seed": "AB", "character": "WATCHER"}]},
        {"name": "x", "ascension": 0, "content": "nope", "games": [{"seed": "AB", "character": "SILENT"}]},
        {"name": "x", "ascension": 0, "content": "ported_only", "games": []},
        {"name": "x", "ascension": 0, "content": "ported_only",
         "games": [{"seed": "AB", "character": "SILENT"}, {"seed": "AB", "character": "SILENT"}]},
        {"ascension": 0, "content": "ported_only", "games": [{"seed": "AB", "character": "SILENT"}]},
    ]
    for i, b in enumerate(bad):
        q = tmp_path / f"bad{i}.json"
        q.write_text(json.dumps(b))
        with pytest.raises(S.SeedSetError):
            S.load(str(q))
    with pytest.raises(S.SeedSetError):
        S.load("does-not-exist")
