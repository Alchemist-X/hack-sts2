import os as _os
import sys as _sys
_sys.path.insert(0, _os.path.join(_os.path.dirname(_os.path.abspath(__file__)), ".."))
import common  # noqa: E402
import json, os, re, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import extract as X
NEW = X.NEW
HERE = os.path.dirname(os.path.abspath(__file__))
DIRS = ["MegaCrit.Sts2.Core.Models.Encounters", "MegaCrit.Sts2.Core.Models.Encounters.Mocks"]


def analyze(path):
    raw = open(path, encoding="utf-8").read()
    t = X.strip_comments(raw)
    out = {}
    loc = X.find_method(t, r"protected override (?:System\.Collections\.Generic\.)?IReadOnlyList<ValueTuple<MonsterModel, string\?>> GenerateMonsters\(\)")
    if loc:
        s, b, e = loc
        body = t[b:e]
        out["generate_line"] = X.line_of(t, s)
        out["generate_end_line"] = X.line_of(t, e)
        out["rng_calls"] = ["%d: %s" % (X.line_of(t, b + m.start()), m.group(0)) for m in re.finditer(r"base\.Rng\.\w+\([^;]*?\)(?=[;)\]])", body)]
        out["rng_passed"] = ["%d" % X.line_of(t, b + m.start()) for m in re.finditer(r",\s*base\.Rng\)", body)]
        out["config"] = ["%d: %s" % (X.line_of(t, b + m.start()), X.norm(m.group(0))) for m in re.finditer(r"\b\w+\.(\w+)\s*=\s*[^;=]+;", body) if not re.search(r"_003C|num\d*\s*=", m.group(0))]
        out["monster_refs"] = [m.group(1) for m in re.finditer(r"ModelDb\.Monster<(\w+)>\(\)", body)]
    for name in ("Slots", "Tags", "IsWeak", "RoomType", "ShouldGiveRewards", "MinGoldReward", "MaxGoldReward", "AllPossibleMonsters"):
        m = re.search(r"public override [^\n]*\b%s\b[^\n]*" % name, t)
        if m:
            out.setdefault("override_lines", {})[name] = X.line_of(t, m.start())
    for name in ("CalculateGoldProportion", "SaveCustomState", "LoadCustomState"):
        m = re.search(r"public override [^\n]*\b%s\(" % name, t)
        if m:
            out.setdefault("override_lines", {})[name] = X.line_of(t, m.start())
    return out


res = {}
for d in DIRS:
    for fn in sorted(os.listdir(os.path.join(NEW, d))):
        if fn.endswith(".cs"):
            res[fn[:-3]] = analyze(os.path.join(NEW, d, fn))
json.dump(res, open(common.work_path("monsters", "enc_auto.json"), "w"), indent=1, sort_keys=True)
for k, v in res.items():
    print(k, v.get("generate_line"), v.get("rng_calls"), v.get("rng_passed"), v.get("config"), v.get("override_lines"))
