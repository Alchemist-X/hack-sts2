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
