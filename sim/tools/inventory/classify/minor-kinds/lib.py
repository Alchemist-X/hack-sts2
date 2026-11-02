"""Record helper for the manual classification of the six minor kinds (W1 critique G1)."""

DATA = {}


def M(kind, cls, scope, mech, reqs=(), hooks=(), deps=None, rng=(), prompts=(), phases=(), tier=1, batch=1,
      notes="", oq=(), req_notes=(), asc="none", vd=None, facts=None, reads=None, co_port=None):
    """One model. reqs are requirement-catalogue keys (sim/specs/inventory/requirements.json)."""
    key = (kind, cls)
    if key in DATA:
        raise ValueError("duplicate record %s/%s" % key)
    DATA[key] = {
        "scope": scope, "mechanics": list(mech), "kernel_requirements": list(reqs), "hooks": list(hooks),
        "dependencies": {"requires": dict(deps or {}), "reads": dict(reads or {}), "co_port": dict(co_port or {})}, "rng_streams": list(rng), "prompts": list(prompts), "phases": list(phases),
        "tier": tier, "port_batch": batch, "notes": notes, "open_questions": list(oq),
        "kernel_requirement_notes": list(req_notes), "ascension_sensitivity": asc, "vd": vd, "facts": dict(facts or {}),
    }
