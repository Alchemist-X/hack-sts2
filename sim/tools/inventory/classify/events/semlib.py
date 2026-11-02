import os as _os
import sys as _sys
_sys.path.insert(0, _os.path.join(_os.path.dirname(_os.path.abspath(__file__)), ".."))
import common  # noqa: E402
import os
import re

V111 = common.DECOMPILE_ROOT + "/"
V107 = common.V107_ROOT + "/"

_cache = {}


def _lines(path):
    if path not in _cache:
        with open(path, encoding="utf-8-sig", errors="replace") as f:
            _cache[path] = f.read().split("\n")
    return _cache[path]


def event_path(cls, v107=False):
    if v107:
        for sub in ("Models/Events/", "Models/Events/Mocks/"):
            p = os.path.join(V107, sub, cls + ".cs")
            if os.path.exists(p):
                return p, "[v107] " + sub + cls + ".cs"
        raise KeyError(cls)
    for d in ("MegaCrit.Sts2.Core.Models.Events", "MegaCrit.Sts2.Core.Models.Events.Mocks"):
        p = os.path.join(V111, d, cls + ".cs")
        if os.path.exists(p):
            return p, d.replace("MegaCrit.Sts2.Core.", "") + "/" + cls + ".cs"
    raise KeyError(cls)


def resolve(cls, spec):
    if spec is None:
        return None
    if ".cs:" in spec:
        return spec
    v107 = False
    if spec.startswith("107:"):
        v107 = True
        spec = spec[4:]
    path, rel = event_path(cls, v107)
    lines = _lines(path)
    if spec.startswith("m:"):
        name = spec[2:]
        pats = [
            r"private struct _003C" + re.escape(name) + r"_003Ed__\d+",
            r"\b(?:private|protected|public|internal)\b[^\n;=]*\b" + re.escape(name) + r"\s*(?:<[^>]*>)?\(",
            r"\b(?:private|protected|public|internal)\b[^\n;(]*\b" + re.escape(name) + r"\s*(?:=>|$)",
        ]
        if v107:
            pats = pats[1:]
        for pat in pats:
            rx = re.compile(pat)
            for i, ln in enumerate(lines, 1):
                if rx.search(ln):
                    return "%s:%d" % (rel, i)
        raise ValueError("cannot resolve %s %s" % (cls, spec))
    if spec.startswith("re:"):
        rx = re.compile(spec[3:])
        for i, ln in enumerate(lines, 1):
            if rx.search(ln):
                return "%s:%d" % (rel, i)
        raise ValueError("cannot resolve %s %s" % (cls, spec))
    if re.match(r"^\d+(-\d+)?$", spec):
        return "%s:%s" % (rel, spec)
    raise ValueError("bad cite spec %s %s" % (cls, spec))


def O(page, key, effect, cond="always", cost=None, nxt="finish", cite=None, notes=None):
    d = {"page": page, "key": key, "condition": cond, "cost": cost, "effect": effect, "next": nxt, "cite": cite}
    if notes:
        d["notes"] = notes
    return d


def P(ptype, mn, mx, source, option, cite, filt=None, cancelable=False, auto=None, notes=None):
    d = {"type": ptype, "min": mn, "max": mx, "source": source, "filter": filt, "cancelable": cancelable,
         "auto_resolve": auto, "option": option, "cite": cite}
    if notes:
        d["notes"] = notes
    return d


def R(stream, when, draws, cite=None):
    return {"stream": stream, "when": when, "draws": draws, "cite": cite}


def CB(encounter, via, resume, extra_rewards, cite, notes=None):
    d = {"encounter": encounter, "via": via, "resume_after_combat": resume, "extra_rewards": extra_rewards,
         "cite": cite}
    if notes:
        d["notes"] = notes
    return d


def V(cls, note, mech="fingerprint-equal"):
    return {"class": cls, "note": note, "mechanical": mech}
