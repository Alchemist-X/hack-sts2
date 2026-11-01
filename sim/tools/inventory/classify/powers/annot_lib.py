"""Annotation registry for the semantic power classification (scratch)."""

ANN = {}


def A(cls, tier, mech, req=(), prompts=(), rng=(), asc=None, notes="", oq=(), vd=None, batch=None, deps=()):
    if cls in ANN:
        raise ValueError("duplicate annotation " + cls)
    ANN[cls] = {
        "tier": tier,
        "mechanics": list(mech),
        "req": list(req),
        "prompts": list(prompts),
        "rng": list(rng),
        "asc": asc,
        "notes": notes,
        "oq": list(oq),
        "vd": vd,
        "batch": batch,
        "deps": list(deps),
    }


def P(kind, method, source, min_sel, max_sel, site, can_skip=False, extra=None):
    d = {"type": kind, "cmd": method, "source": source, "min": min_sel, "max": max_sel, "site": site,
         "can_skip": can_skip}
    if extra:
        d.update(extra)
    return d
