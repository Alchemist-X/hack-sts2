import re


def short_cond(c):
    c = re.sub(r"\(\(\w+\)base\.Creature\.Monster\)\.", "", c)
    c = c.replace("base.Creature.", "").replace("base.", "")
    c = re.sub(r"\s+", " ", c)
    c = re.sub(r"(?<![\w.])(\d+(?:\.\d+)?)f\b", r"\1", c)
    c = re.sub(r"\((\d+(?:\.\d+)?) / (\d+(?:\.\d+)?)\)", r"\1/\2", c)
    c = re.sub(r"^\(!(\w+\(\))\) \? ", r"!\1 ? ", c)
    return c


def fmt_branch(b):
    if "condition" in b:
        return "%s if %s" % (b["to"], short_cond(b["condition"]))
    if "condition" not in b and "repeat" not in b and "raw" not in b:
        return "%s (always)" % b["to"]
    parts = []
    rep = b.get("repeat")
    if rep == "CanRepeatXTimes":
        parts.append("x%s" % b.get("max_times"))
    elif rep:
        parts.append(rep)
    w = b.get("weight", "1f")
    w = re.sub(r"^\(\)\s*=>\s*", "", w)
    if w not in ("1f", "1"):
        parts.append("w=" + short_cond(w))
    if b.get("cooldown") not in (None, "0"):
        parts.append("cd" + b["cooldown"])
    if "raw" in b:
        parts.append("raw=" + b["raw"])
    return "%s[%s]" % (b["to"], ",".join(parts))


def render(g):
    if not g:
        return None
    lines = []
    ini = g["initial"]
    if len(ini) == 1:
        lines.append("initial " + ini[0]["id"])
    else:
        lines.append("initial " + "; ".join(("%s if %s" % (i["id"], " && ".join(short_cond(c) for c in i["conds"]))) if i["conds"] else ("%s otherwise" % i["id"]) for i in ini))
    for st in g["states"]:
        sid = st["id"]
        if st["kind"] == "MoveState":
            fus = st.get("follow_up", [])
            if fus and all(not f["conds"] for f in fus):
                fus = [fus[-1]]
            if len(fus) > 1:
                fu = " / ".join("%s%s" % (f["to"], (" if " + " && ".join(short_cond(c) for c in f["conds"])) if f["conds"] else "") for f in fus)
            elif fus:
                fu = fus[-1]["to"] + ((" if " + " && ".join(short_cond(c) for c in fus[-1]["conds"])) if fus[-1]["conds"] else "")
            else:
                fu = "(none)"
            flag = " [MustPerformOnce]" if st.get("init", {}).get("MustPerformOnceBeforeTransitioning") == "true" else ""
            lines.append("%s -> %s%s" % (sid, fu, flag))
        elif st["kind"] == "RandomBranchState":
            lines.append("%s = RAND{%s}" % (sid, ", ".join(fmt_branch(b) for b in st.get("branches", []))))
        else:
            lines.append("%s = COND{%s}" % (sid, "; ".join(fmt_branch(b) for b in st.get("branches", []))))
    reg = [r["id"] for r in g["registered"]]
    unreg = [st["id"] for st in g["states"] if st["id"] not in reg]
    if unreg:
        lines.append("not registered: " + ", ".join(unreg))
    return lines


def move_table(g):
    out = []
    for st in g["states"]:
        if st["kind"] != "MoveState":
            continue
        out.append({"id": st["id"], "line": st["line"], "perform": st.get("perform"), "intents": st.get("intents_resolved", [])})
    return out
