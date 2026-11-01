import os as _os
import sys as _sys
_sys.path.insert(0, _os.path.join(_os.path.dirname(_os.path.abspath(__file__)), ".."))
import common  # noqa: E402
import json
import os
import re
import sys

NEW = common.DECOMPILE_ROOT
OLD = common.V107_ROOT
HERE = os.path.dirname(os.path.abspath(__file__))

MDIR = "MegaCrit.Sts2.Core.Models.Monsters"
MMDIR = "MegaCrit.Sts2.Core.Models.Monsters.Mocks"


def read(path):
    with open(path, encoding="utf-8") as f:
        return f.read()


def strip_comments(text):
    out = []
    i = 0
    n = len(text)
    while i < n:
        c = text[i]
        if c == '"':
            j = i + 1
            while j < n and text[j] != '"':
                if text[j] == '\\':
                    j += 1
                j += 1
            out.append(text[i:j + 1])
            i = j + 1
            continue
        if text.startswith("//", i):
            j = text.find("\n", i)
            if j < 0:
                j = n
            out.append(" " * (j - i))
            i = j
            continue
        if text.startswith("/*", i):
            j = text.find("*/", i + 2)
            if j < 0:
                j = n
            seg = text[i:j + 2]
            out.append(re.sub(r"[^\n]", " ", seg))
            i = j + 2
            continue
        out.append(c)
        i += 1
    return "".join(out)


def line_of(text, idx):
    return text.count("\n", 0, idx) + 1


def match_close(s, i, o, c):
    depth = 0
    j = i
    n = len(s)
    while j < n:
        ch = s[j]
        if ch == '"':
            k = j + 1
            while k < n and s[k] != '"':
                if s[k] == '\\':
                    k += 1
                k += 1
            j = k + 1
            continue
        if ch == o:
            depth += 1
        elif ch == c:
            depth -= 1
            if depth == 0:
                return j
        j += 1
    return -1


def split_top(s, sep=","):
    parts = []
    depth = 0
    cur = []
    i = 0
    while i < len(s):
        ch = s[i]
        if ch == '"':
            j = i + 1
            while j < len(s) and s[j] != '"':
                if s[j] == '\\':
                    j += 1
                j += 1
            cur.append(s[i:j + 1])
            i = j + 1
            continue
        if ch in "([{<":
            if ch == "<" and not re.match(r"<\w", s[i:i + 2]):
                cur.append(ch)
                i += 1
                continue
            depth += 1
        elif ch in ")]}>":
            if ch == ">" and depth == 0:
                cur.append(ch)
                i += 1
                continue
            if ch == ">" and (i > 0 and s[i - 1] == "="):
                cur.append(ch)
                i += 1
                continue
            depth -= 1
        if ch == sep and depth == 0:
            parts.append("".join(cur).strip())
            cur = []
        else:
            cur.append(ch)
        i += 1
    if "".join(cur).strip():
        parts.append("".join(cur).strip())
    return parts


def norm(s):
    s = re.sub(r"\[CompilerGenerated\]\s*", "", s)
    s = re.sub(r"decimal\.op_Implicit\(([^()]*(?:\([^()]*\))?[^()]*)\)", r"\1", s)
    s = re.sub(r"\(System\.Collections\.Generic\.IEnumerable<\w+>\??\)", "", s)
    s = re.sub(r"\(MonsterState\)", "", s)
    s = re.sub(r"\s+", " ", s)
    return s.strip()


def find_method(text, header_re):
    m = re.search(header_re, text)
    if not m:
        return None
    b = text.find("{", m.end())
    e = match_close(text, b, "{", "}")
    return m.start(), b, e


def parse_statements(body, base_off, text):
    res = []

    def skip_ws(i):
        while i < len(body) and body[i] in " \t\r\n":
            i += 1
        return i

    def read_stmt(i):
        depth_p = depth_b = 0
        j = i
        while j < len(body):
            ch = body[j]
            if ch == '"':
                k = j + 1
                while k < len(body) and body[k] != '"':
                    if body[k] == '\\':
                        k += 1
                    k += 1
                j = k + 1
                continue
            if ch == "(":
                depth_p += 1
            elif ch == ")":
                depth_p -= 1
            elif ch == "{":
                depth_b += 1
            elif ch == "}":
                depth_b -= 1
            elif ch == ";" and depth_p == 0 and depth_b == 0:
                return j + 1
            j += 1
        return j

    def block(i, conds, end):
        while True:
            i = skip_ws(i)
            if i >= end:
                return i
            if body[i] == "}":
                return i + 1
            if body[i] == "{":
                i = block(i + 1, conds, end)
                continue
            m = re.match(r"if\s*\(", body[i:])
            if m:
                p = i + m.end() - 1
                q = match_close(body, p, "(", ")")
                cond = norm(body[p + 1:q])
                i = skip_ws(q + 1)
                i = one(i, conds + [cond], end)
                i2 = skip_ws(i)
                if body.startswith("else", i2) and not re.match(r"else\w", body[i2:i2 + 5]):
                    i = one(skip_ws(i2 + 4), conds + ["!(" + cond + ")"], end)
                continue
            j = read_stmt(i)
            st = body[i:j]
            res.append((line_of(text, base_off + i), conds, norm(st)))
            i = j

    def one(i, conds, end):
        i = skip_ws(i)
        if body[i] == "{":
            return block(i + 1, conds, end)
        m = re.match(r"if\s*\(", body[i:])
        if m:
            p = i + m.end() - 1
            q = match_close(body, p, "(", ")")
            cond = norm(body[p + 1:q])
            k = one(skip_ws(q + 1), conds + [cond], end)
            k2 = skip_ws(k)
            if body.startswith("else", k2) and not re.match(r"else\w", body[k2:k2 + 5]):
                k = one(skip_ws(k2 + 4), conds + ["!(" + cond + ")"], end)
            return k
        j = read_stmt(i)
        res.append((line_of(text, base_off + i), conds, norm(body[i:j])))
        return j

    block(0, [], len(body))
    return res


NEW_STATE_RE = re.compile(r"new (MoveState|RandomBranchState|ConditionalBranchState)\(")


def parse_new_state(s, i):
    p = s.index("(", i)
    q = match_close(s, p, "(", ")")
    args = split_top(s[p + 1:q])
    kind = s[i + 4:p]
    rest = s[q + 1:]
    init = {}
    m = re.match(r"\s*\{([^}]*)\}", rest)
    if m:
        for kv in m.group(1).split(","):
            if "=" in kv:
                k, v = kv.split("=", 1)
                init[k.strip()] = v.strip()
    return kind, args, init, q


def parse_graph(text, path):
    loc = find_method(text, r"protected override MonsterMoveStateMachine GenerateMoveStateMachine\(\)")
    if not loc:
        return None
    start, b, e = loc
    body = text[b + 1:e]
    stmts = parse_statements(body, b + 1, text)
    var2id = {}
    states = {}
    order = []
    registered = []
    initial = []
    other = []
    build_conds = set()

    def ensure(sid, kind, line, args=None, init=None, conds=None):
        if sid not in states:
            states[sid] = {"id": sid, "kind": kind, "line": line}
            order.append(sid)
        st = states[sid]
        if kind == "MoveState" and args is not None:
            st["perform"] = args[1] if len(args) > 1 else None
            st["intents"] = [norm(a) for a in args[2:]]
            if init:
                fu = init.pop("FollowUpState", None)
                if fu is not None:
                    st.setdefault("follow_up", []).append({"to": "?init:" + fu, "conds": conds or [], "line": line, "from_initializer": True})
                if init:
                    st["init"] = init
        if conds:
            st.setdefault("build_conditions", []).append(" && ".join(conds))
        return st

    locals_ = {}

    def resolve_initial(expr, conds, line):
        expr = expr.strip()
        if expr in locals_:
            expr = locals_[expr].strip()
        e = re.sub(r"\(\((?:MonsterState|MoveState)\)(\w+)\)", r"\1", expr)
        e = re.sub(r"\((\w+)\)", r"\1", e)
        e = re.sub(r"^\((.*)\)$", r"\1", e)
        mt = re.match(r"^(.*?)\s*\?\s*(\w+)\s*:\s*(\w+)$", e)
        if mt:
            c = mt.group(1).strip()
            return [{"id": resolve(mt.group(2)), "conds": conds + [c], "line": line},
                    {"id": resolve(mt.group(3)), "conds": conds + ["!(" + c + ")"], "line": line}]
        ms = re.match(r"^(.*?)\s+switch\s*\{(.*)\}$", e)
        if ms:
            out = []
            for arm in split_top(ms.group(2)):
                if "=>" not in arm:
                    continue
                k, v = arm.split("=>", 1)
                k = k.strip()
                out.append({"id": resolve(v.strip()), "conds": conds + (["%s == %s" % (ms.group(1).strip(), k)] if k != "_" else ["%s otherwise" % ms.group(1).strip()]), "line": line})
            return out
        return [{"id": resolve(expr), "conds": conds, "line": line}]

    def resolve(expr):
        expr = expr.strip()
        expr = re.sub(r"^\((MoveState|MonsterState|RandomBranchState|ConditionalBranchState)\)", "", expr).strip()
        if expr in var2id:
            return var2id[expr]
        m = re.match(r'^"([^"]*)"$', expr)
        if m:
            return m.group(1)
        return "?" + expr

    for line, conds, st in stmts:
        for c in conds:
            build_conds.add(c)
        handled = False
        mm = list(NEW_STATE_RE.finditer(st))
        if mm:
            m0 = re.match(r"^(?:(?:MoveState|RandomBranchState|ConditionalBranchState|MonsterState)\s+)?([\w.]+)\s*=\s*(.*)$", st)
            for m in mm:
                kind, args, init, q = parse_new_state(st, m.start())
                sid = args[0].strip('"') if args and args[0].startswith('"') else "?" + (args[0] if args else "")
                ensure(sid, kind, line, args if kind == "MoveState" else None, init, conds)
                prefix = st[:m.start()]
                for inner in re.finditer(r"([\w.]+)\.FollowUpState\s*=", prefix):
                    src = inner.group(1)
                    sid_src = var2id.get(src, "?" + src)
                    if sid_src in states:
                        states[sid_src].setdefault("follow_up", []).append({"to": sid, "conds": conds, "line": line})
                if m0:
                    var2id[m0.group(1)] = sid
            handled = True
        m = re.match(r"^([\w.]+)\.FollowUpState\s*=\s*(.+);$", st)
        if m and not handled:
            src = var2id.get(m.group(1), "?" + m.group(1))
            dst = resolve(m.group(2))
            if src in states:
                states[src].setdefault("follow_up", []).append({"to": dst, "conds": conds, "line": line})
            else:
                other.append({"line": line, "stmt": st, "conds": conds})
            handled = True
        m = re.match(r"^([\w.]+)\.FollowUpStateId\s*=\s*(.+);$", st)
        if m and not handled:
            src = var2id.get(m.group(1), "?" + m.group(1))
            dst = resolve(m.group(2))
            if src in states:
                states[src].setdefault("follow_up", []).append({"to": dst, "conds": conds, "line": line, "by_id": True})
            handled = True
        m = re.match(r"^([\w.]+)\.MustPerformOnceBeforeTransitioning\s*=\s*(.+);$", st)
        if m and not handled:
            src = var2id.get(m.group(1), "?" + m.group(1))
            if src in states:
                states[src].setdefault("init", {})["MustPerformOnceBeforeTransitioning"] = m.group(2)
            handled = True
        m = re.match(r"^([\w.]+)\.AddBranch\((.*)\);$", st)
        if m and not handled:
            src = var2id.get(m.group(1), "?" + m.group(1))
            args = split_top(m.group(2))
            br = {"to": resolve(args[0]), "line": line, "conds": conds}
            rest = args[1:]
            def isfloat(a):
                return bool(re.match(r"^-?[\d.]+f$", a)) or a.startswith("(") and "f" in a or a.startswith("()")
            if len(rest) == 1:
                if rest[0].startswith("MoveRepeatType."):
                    br.update(repeat=rest[0].split(".")[1], weight="1f")
                else:
                    br.update(repeat="CanRepeatXTimes", max_times=rest[0], weight="1f")
            elif len(rest) == 2:
                if rest[0].startswith("MoveRepeatType."):
                    br.update(repeat=rest[0].split(".")[1], weight=rest[1])
                elif rest[1].startswith("MoveRepeatType."):
                    br.update(cooldown=rest[0], repeat=rest[1].split(".")[1], weight="1f")
                else:
                    br.update(repeat="CanRepeatXTimes", max_times=rest[0], weight=rest[1])
            elif len(rest) == 3:
                if rest[1].startswith("MoveRepeatType."):
                    br.update(cooldown=rest[0], repeat=rest[1].split(".")[1], weight=rest[2])
                else:
                    br.update(cooldown=rest[0], repeat="CanRepeatXTimes", max_times=rest[1], weight=rest[2])
            else:
                br["raw"] = m.group(2)
            if src in states:
                states[src].setdefault("branches", []).append(br)
            else:
                other.append({"line": line, "stmt": st, "conds": conds})
            handled = True
        m = re.match(r"^([\w.]+)\.AddState\((.*)\);$", st)
        if m and not handled:
            src = var2id.get(m.group(1), "?" + m.group(1))
            args = split_top(m.group(2))
            br = {"to": resolve(args[0]), "line": line, "conds": conds}
            if len(args) > 1:
                br["condition"] = re.sub(r"^\(\)\s*=>\s*", "", args[1])
            if src in states:
                states[src].setdefault("branches", []).append(br)
            handled = True
        m = re.match(r"^\w+\.Add\((.*)\);$", st)
        if m and not handled:
            registered.append({"id": resolve(m.group(1)), "conds": conds, "line": line})
            handled = True
        m = re.match(r"^return new MonsterMoveStateMachine\((.*)\);$", st)
        if m and not handled:
            args = split_top(m.group(1))
            initial.extend(resolve_initial(args[-1], conds, line))
            if args and "new " in args[0]:
                inner = re.search(r"\((.*)\)\s*$", args[0])
                if inner:
                    for v in split_top(inner.group(1)):
                        v = re.sub(r"^\(\w+\)", "", v.strip())
                        if v in var2id:
                            registered.append({"id": var2id[v], "conds": conds, "line": line, "inline": True})
            handled = True
        m = re.match(r"^return \((.*)\) switch \{(.*)\};$", st)
        if m and not handled:
            for arm in split_top(m.group(2)):
                if "=>" not in arm:
                    continue
                k, v = arm.split("=>", 1)
                mm2 = re.search(r"new MonsterMoveStateMachine\((.*)\)\s*$", v.strip())
                if mm2:
                    a2 = split_top(mm2.group(1))
                    initial.append({"id": resolve(a2[-1]), "conds": conds + ["(%s) == %s" % (m.group(1), k.strip()) if k.strip() != "_" else "(%s) otherwise" % m.group(1)], "line": line})
            handled = True
        m = re.match(r"^(?:MoveState|MonsterState)\s+(\w+)\s*=\s*(.*);$", st)
        if m and not handled and "new " not in m.group(2):
            locals_[m.group(1)] = m.group(2)
            handled = True
        if not handled:
            m = re.match(r"^(?:(?:MoveState|RandomBranchState|ConditionalBranchState|MonsterState)\s+)?([\w.]+)\s*=\s*([\w.]+);$", st)
            if m and m.group(2) in var2id:
                var2id[m.group(1)] = var2id[m.group(2)]
                handled = True
        if not handled:
            if not re.match(r"^List<MonsterState> \w+ = new List<MonsterState>\(\);$", st):
                other.append({"line": line, "stmt": st, "conds": conds})
    for st in states.values():
        for f in st.get("follow_up", []):
            if isinstance(f.get("to"), str) and f["to"].startswith("?init:"):
                v = f["to"][6:].strip()
                f["to"] = var2id.get(v, "?" + v)
    return {
        "line": line_of(text, start),
        "end_line": line_of(text, e),
        "states": [states[s] for s in order],
        "registered": registered,
        "initial": initial,
        "build_conditions": sorted(build_conds),
        "other": other,
    }


PROP_RE = re.compile(r"^\s*(?:public|private|protected|internal)\s+(?:static\s+)?(?:override\s+)?(?:virtual\s+)?(int|decimal|bool|float|string|int\?)\s+(\w+)\s*=>\s*(.+?);\s*$", re.M)
CONST_RE = re.compile(r"^\s*(?:public|private|protected|internal)\s+(?:static\s+readonly|const)\s+(\w+)\s+(\w+)\s*=\s*(.+?);\s*$", re.M)


def parse_props(text):
    props = {}
    for m in PROP_RE.finditer(text):
        props[m.group(2)] = {"type": m.group(1), "expr": norm(m.group(3)), "line": line_of(text, m.start(2))}
    for m in CONST_RE.finditer(text):
        props.setdefault(m.group(2), {"type": m.group(1), "expr": norm(m.group(3)), "line": line_of(text, m.start(2)), "const": True})
    return props


GVIA = re.compile(r"AscensionHelper\.GetValueIfAscension\(AscensionLevel\.(\w+),\s*([^,]+?),\s*([^,()]+?)\)")


def eval_expr(expr, props, depth=0):
    if depth > 6:
        return None
    e = expr.strip()
    m = re.match(r"^-?\d+(\.\d+)?[mf]?$", e)
    if m:
        return {"a0": e.rstrip("mf"), "asc": None}
    m = GVIA.fullmatch(e)
    if m:
        return {"a0": m.group(3).strip().rstrip("mf"), "asc": {"level": m.group(1), "value": m.group(2).strip().rstrip("mf")}}
    if re.match(r"^\w+$", e) and e in props:
        return eval_expr(props[e]["expr"], props, depth + 1)
    return None


ASC_NUM = {"SwarmingElites": 1, "WearyTraveler": 2, "Poverty": 3, "TightBelt": 4, "AscendersBane": 5,
           "Gloom": 6, "Scarcity": 7, "ToughEnemies": 8, "DeadlyEnemies": 9, "DoubleBoss": 10}


def fmt_val(v):
    if v is None:
        return None
    if v["asc"]:
        return "%s (A%d+ %s: %s)" % (v["a0"], ASC_NUM.get(v["asc"]["level"], -1), v["asc"]["level"], v["asc"]["value"])
    return v["a0"]


def method_bodies(text, names):
    out = {}
    for name in names:
        sm = re.search(r"private (?:struct|sealed class) _003C%s_003Ed__\d+\b" % re.escape(name), text)
        if sm:
            b = text.find("{", sm.end())
            e = match_close(text, b, "{", "}")
            out[name] = ("sm", line_of(text, b), line_of(text, e), text[b:e])
            continue
        mm = re.search(r"(?:private|public|protected)(?: override)?(?: async)? (?:System\.Threading\.Tasks\.)?Task %s\(" % re.escape(name), text)
        if mm:
            b = text.find("{", mm.end())
            e = match_close(text, b, "{", "}")
            out[name] = ("sync", line_of(text, mm.start()), line_of(text, e), text[b:e])
    return out


CALL_RE = re.compile(r"\b(\w*Cmd)\.(\w+)(?:<([\w<>, ]+?)>)?\(")


def effect_lines(body_text, base_line):
    res = []
    lines = body_text.split("\n")
    for k, l in enumerate(lines):
        s = l.strip()
        if not s:
            continue
        if re.search(r"_003C_003E1__state|AwaitUnsafeOnCompleted|IsCompleted|GetResult\(\)|SetResult|SetException|^//|IL_|_003C_003Eu__\d|^if \(num [!=<>]|^if \(num < 0", s):
            continue
        s = re.sub(r"CS_0024_003C_003E8__locals\d+", "this", s)
        s = re.sub(r"_003C_003E7__wrap\d+", "<wrap>", s)
        s = re.sub(r"_003C(\w+?)_003E5__\d+", r"<\1>", s)
        s = re.sub(r"_003C_003E4__this", "this", s)
        s = re.sub(r"\.GetAwaiter\(\);?$", "", s)
        s = re.sub(r"^\w+ = (\w+Cmd\.)", r"\1", s)
        if CALL_RE.search(s) or re.search(r"\.(Monster|Creature)\.\w+\s*=|\b_\w+\s*=|\w+\+\+|\w+--|\bSetMoveImmediate|\bHook\.|\bRng\b|\bif \(|\bforeach|\bwhile|\bHasPower|\bGetPower|ModelDb\.", s):
            if re.search(r"VfxCmd|SfxCmd|TalkCmd|ThinkCmd|Cmd\.Wait|CustomScaledWait|TriggerAnim|NRunMusicController|PlayAnim", s):
                continue
            res.append("%d: %s" % (base_line + k, norm(s)))
    return res


def analyze(path):
    raw = read(path)
    text = strip_comments(raw)
    props = parse_props(text)
    graph = parse_graph(text, path)
    perf = []
    if graph:
        for st in graph["states"]:
            if st.get("perform"):
                perf.append(st["perform"])
    bodies = method_bodies(text, sorted(set(perf)))
    moves = {}
    for name, (kind, l0, l1, bt) in bodies.items():
        moves[name] = {"kind": kind, "line": l0, "end_line": l1, "effects": effect_lines(bt, l0),
                       "ordered": ordered_effects(bt, l0) if kind == "sm" else [x for x in effect_lines(bt, l0) if "Cmd." in x]}
    hooks = {}
    for m in re.finditer(r"public override (?:async )?(?:System\.Threading\.Tasks\.)?(Task|void|bool|decimal|int|float|Task<\w+>) (\w+)\(", text):
        name = m.group(2)
        if name in ("GenerateAnimator", "SetupSkins"):
            continue
        sm = re.search(r"private (?:struct|sealed class) _003C%s_003Ed__\d+\b" % re.escape(name), text)
        if sm:
            b = text.find("{", sm.end())
            e = match_close(text, b, "{", "}")
            hooks[name] = {"line": line_of(text, m.start()), "sm_line": line_of(text, sm.start()), "effects": effect_lines(text[b:e], line_of(text, b))}
        else:
            b = text.find("{", m.end())
            e = match_close(text, b, "{", "}")
            hooks[name] = {"line": line_of(text, m.start()), "effects": effect_lines(text[b:e], line_of(text, b))}
    vals = {}
    for k, p in props.items():
        v = eval_expr(p["expr"], props)
        if v is not None:
            vals[k] = {"value": fmt_val(v), "line": p["line"], "raw": v}
    if graph:
        for st in graph["states"]:
            res_int = []
            for it in st.get("intents", []):
                mi = re.match(r"new (\w+)\((.*)\)$", it)
                if not mi:
                    res_int.append(it)
                    continue
                kind = mi.group(1).replace("Intent", "")
                args = split_top(mi.group(2)) if mi.group(2) else []
                shown = []
                for a in args:
                    a2 = re.sub(r"^\(\)\s*=>\s*", "", a)
                    if a2 in vals:
                        shown.append("%s=%s" % (a2, vals[a2]["value"]))
                    else:
                        v = eval_expr(a2, props)
                        shown.append(fmt_val(v) if v else a2)
                res_int.append("%s(%s)" % (kind, ", ".join(shown)) if shown else kind)
            st["intents_resolved"] = res_int
    return {"props": props, "values": {k: {"value": v["value"], "line": v["line"]} for k, v in vals.items()},
            "graph": graph, "moves": moves, "hooks": hooks}


def main():
    out = {}
    for d in (MDIR, MMDIR):
        for fn in sorted(os.listdir(os.path.join(NEW, d))):
            if not fn.endswith(".cs"):
                continue
            name = fn[:-3]
            out[name] = analyze(os.path.join(NEW, d, fn))
            out[name]["file"] = "%s/%s" % (d.replace("MegaCrit.Sts2.Core.", ""), fn)
    with open(common.work_path("monsters", "auto.json"), "w") as f:
        json.dump(out, f, indent=1, sort_keys=True)
    print(len(out))




LABEL_RE = re.compile(r"^\s*(?:end_)?IL_([0-9a-fA-F]{4}):")


def ordered_effects(body_text, base_line):
    lines = body_text.split("\n")
    region = -1
    keyed = []
    for k, l in enumerate(lines):
        s = l.strip()
        m = LABEL_RE.match(s)
        if m:
            region = int(m.group(1), 16)
            continue
        if re.match(r"^(case \d+:|default:)", s):
            region = -1 if s.startswith("default") else -2
            continue
        if region == -2:
            continue
        if "Cmd." in s or "SetMoveImmediate" in s or re.search(r"\.\w+\s*=\s*[^=]", s) and not re.search(r"_003C_003E|\bval\d*\s*=|\bnum\b", s):
            if re.search(r"VfxCmd|SfxCmd|TalkCmd|ThinkCmd|Cmd\.Wait|CustomScaledWait|TriggerAnim|NRunMusicController|_003C_003Eu__|AwaitUnsafeOnCompleted", s):
                continue
            keyed.append((region, k, "%d: %s" % (base_line + k, norm(s))))
    keyed.sort(key=lambda t: (t[0], t[1]))
    return [t[2] for t in keyed]


if __name__ == "__main__":
    main()
