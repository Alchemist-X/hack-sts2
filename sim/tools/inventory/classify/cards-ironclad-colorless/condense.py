import os as _os
import sys as _sys
_sys.path.insert(0, _os.path.join(_os.path.dirname(_os.path.abspath(__file__)), ".."))
import common  # noqa: E402
import json
import os
import re
import sys

ROOT = common.REPO_ROOT
NEW = common.DECOMPILE_ROOT
OLD = common.V107_ROOT
SLICE = {"IRONCLAD", "COLORLESS", "CURSE", "STATUS", "TOKEN", "EVENT", "QUEST", "DEPRIVED", "DEPRECATED", "unlisted", "MOCK"}

DROP_RES = [re.compile(p) for p in [
    r"^\s*//",
    r"^\s*using [\w.]+;\s*$",
    r"^\s*namespace ",
    r"^\s*\[(StructLayout|CompilerGenerated|DebuggerHidden|AsyncStateMachine|IteratorStateMachine|DebuggerStepThrough|Nullable|NullableContext|IsReadOnly|Serializable|DebuggerBrowsable)",
    r"^\s*(public|private|internal) (int|object|bool) _003C_003E1__state;",
    r"^\s*(public|private) (AsyncTaskMethodBuilder|AsyncVoidMethodBuilder|AsyncTaskMethodBuilder<[^;]*>) _003C_003Et__builder;",
    r"^\s*(public|private) [\w.<>?, \[\]]+ _003C_003E4__this;",
    r"^\s*private (TaskAwaiter|ConfiguredTaskAwaitable|YieldAwaitable|ValueTaskAwaiter)[^;]*_003C_003Eu__\d+;",
    r"^\s*int num = _003C_003E1__state;",
    r"^\s*\w+ \w+ = _003C_003E4__this;",
    r"^\s*try\s*$",
    r"^\s*catch \(System\.Exception exception\)",
    r"^\s*catch \(Exception exception\)",
    r"_003C_003E1__state = -2;",
    r"\.SetException\(",
    r"\.SetResult\(",
    r"\.IsCompleted\)",
    r"num = \(_003C_003E1__state = -?\d+\);",
    r"_003C_003Eu__\d+ = \w+;",
    r"AwaitUnsafeOnCompleted",
    r"AwaitOnCompleted",
    r"= _003C_003Eu__\d+;",
    r"_003C_003Eu__\d+ = default\(",
    r"^\s*\w+\.GetResult\(\);\s*$",
    r"^\s*goto IL_\w+;\s*$",
    r"^\s*IL_\w+:\s*;?\s*$",
    r"^\s*(TaskAwaiter|ConfiguredTaskAwaitable|ValueTaskAwaiter)(<[^;]*>)? \w+;\s*$",
    r"^\s*if \(num [!=<>]=? -?\d+\)\s*$",
    r"^\s*switch \(num\)\s*$",
    r"^\s*return;\s*$",
    r"^\s*[{}]\s*$",
    r"^\s*$",
    r"_003C\w+_003Ed__\w* _003C\w+_003Ed__ = default\(",
    r"_003C\w+_003Ed__\._003C_003Et__builder = ",
    r"_003C\w+_003Ed__\._003C_003E4__this = this;",
    r"_003C\w+_003Ed__\._003C_003E1__state = -1;",
    r"_003C\w+_003Ed__\.\w+ = \w+;\s*$",
    r"\)\.Start<_003C",
    r"_003C\w+_003Ed__\._003C_003Et__builder\)\)\.Task;",
    r"private void SetStateMachine\(",
    r"\)\.SetStateMachine\(stateMachine\);",
    r"IHoverTip",
    r"HoverTipFactory",
    r"^\s*(public|private) (CardPlay|PlayerChoiceContext|Creature\??|CardModel\??|Player\??|PlayerChoiceContext\??|bool|decimal|int|CombatSide|ICombatState|IRunState|AbstractRoom|DamageResult|ValueProp|CardPile\??|PowerModel\??|AbstractModel\??|CombatState|AttackCommand\??|IEnumerable<\w+>|IReadOnlyList<\w+>|PileType|CardPilePosition|CardModel\[\]|List<\w+>\??) \w+;\s*$",
]]
CASE_RE = re.compile(r"^\s*case -?\d+:\s*$|^\s*default:\s*$")


def readable(s):
    s = s.replace("_003C_003E", "<>").replace("_003C", "<").replace("_003E", ">")
    s = re.sub(r"\.GetAwaiter\(\)", "", s)
    s = re.sub(r"^(\s*)val\d*\s*=\s*", r"\1await ", s)
    s = re.sub(r"^(\s*)awaiter\d*\s*=\s*", r"\1await ", s)
    return s


def condense(path, with_lines=True, text=None):
    out = []
    if text is None:
        with open(path, encoding="utf-8-sig", errors="replace") as fh:
            text = fh.read()
    lines = text.split("\n")
    in_switch_num = False
    for i, line in enumerate(lines, 1):
        if any(r.search(line) for r in DROP_RES):
            if "switch (num)" in line:
                in_switch_num = True
            continue
        if in_switch_num and CASE_RE.match(line):
            continue
        t = readable(line.rstrip())
        t = t.replace("\t", "  ")
        out.append(("L%-4d %s" % (i, t)) if with_lines else t)
    return out


def old_path(rel):
    ns, fn = rel.split("/")
    sub = ns.replace("Models.Cards", "Models/Cards").replace(".Mocks", "/Mocks")
    p = os.path.join(OLD, sub, fn)
    return p if os.path.exists(p) else None


def main():
    d = json.load(open(common.inventory_path("cards.json")))
    outdir = common.work_dir("cards-ironclad-colorless", "dumps")
    os.makedirs(outdir, exist_ok=True)
    by = {}
    for e in d["entries"]:
        if e["pool"] not in SLICE:
            continue
        by.setdefault(e["pool"], []).append(e)
    for pool, es in by.items():
        buf = []
        for e in es:
            p = os.path.join(NEW, "MegaCrit.Sts2.Core." + e["file"])
            buf.append("=" * 100)
            buf.append("%s  [%s] %s  cost=%s%s type=%s rarity=%s target=%s kw=%s tags=%s" % (
                e["id"], pool, e["file"], e["cost"], " X" if e["costs_x"] else "", e["type"], e["rarity"],
                e["target_type"], ",".join(e["keywords"]), ",".join(e["tags"])))
            buf.append("  vars=%s upgrade=%s maxup=%s star=%s hooks=%s" % (
                json.dumps(e["dynamic_vars"]), " ".join(e["upgrade"]), e["max_upgrade_level"], e["star_cost"],
                ",".join(e["abstract_model_hooks"])))
            buf.append("  api=%s rng=%s choice=%s" % (",".join(e["api_calls"]), ",".join(e["rng_usage"]), ",".join(e["choice_tokens"])))
            buf.extend(condense(p))
            op = old_path(e["file"])
            buf.append("  -- v0.107.1: %s" % (op.replace(OLD + "/", "") if op else "ABSENT"))
        with open(os.path.join(outdir, pool + ".txt"), "w") as fh:
            fh.write("\n".join(buf) + "\n")
        print(pool, len(es), sum(1 for _ in buf))


if __name__ == "__main__":
    if common.REST:
        for line in condense(common.REST[0]):
            print(line)
    else:
        main()
