import os as _os
import sys as _sys
_sys.path.insert(0, _os.path.join(_os.path.dirname(_os.path.abspath(__file__)), ".."))
import common  # noqa: E402
import re, os, sys, json

UI = re.compile(r"TriggerAnim|Cmd\.Wait|CustomScaledWait|SfxCmd|VfxCmd|TalkCmd|ThinkCmd|PlayVfx|Preview|OnEnqueuePlayVfx|Task\.Delay|Yield")
SM_RE = re.compile(r"struct _003C(\w+?)_003Ed__\d+ : IAsyncStateMachine")
AW_NEW = re.compile(r"(\w+) = ((?:(?!\bGetAwaiter\b)[^;])*?)\.GetAwaiter\(\);\s*if \(!\(*(?:\(\w+(?:<[^;]*?>)?\)\(ref )?\1\)*\.IsCompleted\)\s*\{\s*num = \(_003C_003E1__state = (\d+)\);", re.S)
AW_OLD = re.compile(r"\bawait\s+((?:(?!;).)*?)(?:;|\)\s*\)|\)\.FirstOrDefault|\)\.ToList)", re.S)
CALL = re.compile(r"\b((?:\w+Cmd|Hook|CardPileCmd|AttackCommand)\.\w+(?:<[\w.]+>)?|[A-Z]\w*(?:<[\w.]+>)?(?=\())")

def who(a):
    if re.search(r"cardPlay\.Target|\btarget\b|<enemy>|\benemy\b|current\b", a):
        return "target"
    if re.search(r"Owner\.Creature|\bcreature\b", a):
        return "self"
    if re.search(r"HittableEnemies|Enemies", a):
        return "enemies"
    return "?"


def sig(expr):
    expr = re.sub(r"\s+", " ", expr)
    m = CALL.search(expr)
    if not m:
        m2 = re.search(r"\.(\w+)\(", expr)
        return m2.group(1) if m2 else expr[:40]
    name = m.group(1)
    if name.startswith("PowerCmd.Apply") or name.startswith("CreatureCmd.Damage"):
        args = expr[m.end():]
        parts = [x.strip() for x in re.split(r",(?![^(<]*[)>])", args[1:] if args.startswith("(") else args)]
        if len(parts) > 1:
            name += "@" + who(parts[1])
    elif name.startswith("CreatureCmd.GainBlock"):
        args = expr[m.end():]
        parts = [x.strip() for x in re.split(r",(?![^(<]*[)>])", args[1:] if args.startswith("(") else args)]
        if parts:
            name += "@" + who(parts[0])
    return name

def new_awaits(path):
    t = open(path, encoding="utf-8-sig", errors="replace").read()
    t = re.sub(r"//[^\n]*", "", t)
    out = {}
    for m in SM_RE.finditer(t):
        name = m.group(1)
        start = m.end()
        nxt = SM_RE.search(t, start)
        seg = t[start: nxt.start() if nxt else len(t)]
        items = []
        seen = set()
        for a in re.finditer(r"num = \(_003C_003E1__state = (\d+)\);\s*_003C_003Eu__\d+ = (\w+);", seg):
            k = int(a.group(1))
            if k in seen:
                continue
            var = a.group(2)
            head = seg[:a.start()]
            gi = head.rfind(".GetAwaiter()")
            if gi < 0:
                continue
            starts = [m.start() for m in re.finditer(r"(?<![\w.])" + re.escape(var) + r" = ", head[:gi])]
            if not starts:
                continue
            expr = head[starts[-1] + len(var) + 3:gi]
            seen.add(k)
            items.append((k, sig(expr)))
        items.sort()
        out.setdefault(name, []).extend(s for _, s in items)
    return out

def old_awaits(path):
    t = open(path, encoding="utf-8-sig", errors="replace").read()
    t = re.sub(r"//[^\n]*", "", t)
    out = {}
    for m in re.finditer(r"\basync\s+[\w.<>?, \[\]]+\s+(\w+)\s*\(", t):
        name = m.group(1)
        i = t.find("{", m.end())
        depth = 0; j = i
        while j < len(t):
            if t[j] == "{": depth += 1
            elif t[j] == "}":
                depth -= 1
                if depth == 0: break
            j += 1
        body = t[i:j]
        items = [sig(x.group(1)) for x in re.finditer(r"\bawait\s+((?:(?!;).)*?);", body, re.S)]
        out.setdefault(name, []).extend(items)
    return out

def strip_ui(seq):
    return [s for s in seq if not UI.search(s)]

def main():
    from condense import NEW, SLICE, old_path
    cards = json.load(open(common.inventory_path("cards.json")))
    out = {}
    for e in cards["entries"]:
        if e["pool"] not in SLICE:
            continue
        rec = {"new": {k: strip_ui(v) for k, v in new_awaits(os.path.join(NEW, "MegaCrit.Sts2.Core." + e["file"])).items()}}
        op = old_path(e["file"])
        if op:
            rec["old"] = {k: strip_ui(v) for k, v in old_awaits(op).items()}
        out[e["id"]] = rec
    with open(common.work_path("cards-ironclad-colorless", "awaits.json"), "w") as fh:
        json.dump(out, fh, indent=1)


if __name__ == "__main__":
    if common.REST:
        for p in common.REST:
            print(p, json.dumps(new_awaits(p) if p.startswith(common.DECOMPILE_ROOT) else old_awaits(p)))
    else:
        main()
