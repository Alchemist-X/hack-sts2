import os as _os
import sys as _sys
_sys.path.insert(0, _os.path.join(_os.path.dirname(_os.path.abspath(__file__)), ".."))
import common  # noqa: E402
import sys, json, re, collections, os
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import catalog, data_necro, data_regent, vd
D = common.DECOMPILE_ROOT + "/"
raw = json.load(open(common.inventory_path("cards.json")))
powers_manifest = {e['class']: e for e in json.load(open(common.inventory_path("powers.json")))['entries']}
ported = json.load(open(common.inventory_path("ported.json")))
ported_powers = {e['class'] for e in ported['entries']['powers']}
vdj = json.load(open(common.work_path("cards-necrobinder-regent", "vdelta2.json")))
CARDS = {**data_necro.CARDS, **data_regent.CARDS}
REQ = catalog.REQ

def req_tier(keys):
    return max([REQ[k]['tier'] for k in keys] or [1])
def req_batch(keys):
    return max([REQ[k]['batch'] for k in keys] or [1])

def power_closure(ps):
    seen, stack = [], list(ps)
    while stack:
        p = stack.pop(0)
        if p in seen:
            continue
        seen.append(p)
        stack.extend(catalog.POWER_DEPS.get(p, []))
    return seen

def power_tier(p):
    return req_tier(catalog.POWER_REQS[p])
def power_all_reqs(p):
    reqs = []
    for q in power_closure([p]):
        reqs += catalog.POWER_REQS[q]
        for t in catalog.POWER_CARD_DEPS.get(q, []):
            reqs += catalog.TOKENS[t]['reqs']
    return reqs
def power_tier_with_deps(p):
    return req_tier(power_all_reqs(p))
def power_batch(p):
    reqs = power_all_reqs(p)
    return max([1] + [REQ[k]['batch'] for k in reqs if REQ[k]['tier'] >= 3] + ([2] if p not in ported_powers else []))

entries = [e for e in raw['entries'] if e['pool'] in ('NECROBINDER', 'REGENT')]
out_entries = []
for e in entries:
    c = dict(CARDS[e['class']])
    c['reqs'] = list(c['reqs'])
    if any(REQ[k]['subsystem'] == 'Osty' for k in c['reqs']) and "Osty pet creature" not in c['reqs']:
        c['reqs'].append("Osty pet creature")
    cls = e['class']
    f = e['file']
    pw = list(c['powers'])
    if "OstyCmd.Summon" in c['reqs'] and "DieForYouPower" not in pw:
        pw.append("DieForYouPower")
    for p in list(pw):
        if "OstyCmd.Summon" in catalog.POWER_REQS.get(p, []) and "DieForYouPower" not in pw:
            pw.append("DieForYouPower")
    allp = power_closure(pw)
    cards_dep = list(c['cards'])
    for p in allp:
        for t in catalog.POWER_CARD_DEPS.get(p, []):
            if t not in cards_dep:
                cards_dep.append(t)
    dep_reqs = []
    for p in allp:
        dep_reqs += catalog.POWER_REQS[p]
    for t in cards_dep:
        dep_reqs += catalog.TOKENS[t]['reqs']
    own_tier = req_tier(c['reqs'])
    dep_tier = req_tier(dep_reqs)
    tier_with_deps = max(own_tier, dep_tier)
    unported = [p for p in allp if p not in ported_powers] + cards_dep
    dep_batch = max([2 if unported else 1] + [REQ[k]['batch'] for k in dep_reqs if REQ[k]['tier'] >= 3])
    batch = max(req_batch(c['reqs']), dep_batch, 1)
    if c['mp']:
        batch = 9
    gate_keys = [k for k in c['reqs'] if REQ[k]['batch'] == batch] + [k for k in dep_reqs if REQ[k]['tier'] >= 3 and REQ[k]['batch'] == batch]
    gates = sorted({REQ[k]['subsystem'] for k in gate_keys})
    if c['mp']:
        gates = ["Multiplayer"]
    elif not gates:
        gates = ["unported powers/tokens"] if batch == 2 else ["none"]
    drivers = sorted({k for k in c['reqs'] if REQ[k]['tier'] == own_tier}) if c['reqs'] else []
    if own_tier == 1:
        tier_reason = "only facades the sim already has (AttackCommand, GainBlock, PowerCmd.Apply, Draw, GainEnergy, energy/star cost and X cost, keywords)"
    else:
        tier_reason = "; ".join(drivers)
    subsystems = sorted({REQ[k]['subsystem'] for k in c['reqs'] + dep_reqs})
    prompts = []
    for p in c['prompts']:
        q = dict(p)
        if q['kind'] == 'Nested':
            q['site'] = "%s:OnPlay" % e['id']
        elif q.get('indirect'):
            q['site'] = q['indirect'].split(' ')[0]
        else:
            q['site'] = "%s:OnPlay" % e['id']
        q['require_manual_confirmation'] = bool(q.pop('confirm')) if q.get('confirm') is not None else (isinstance(q['min'], int) and isinstance(q['max'], int) and q['min'] >= 0 and q['min'] != q['max'])
        line = q.pop('line')
        q['cite'] = ("%s:%d" % (f, line)) if line else None
        if q.get('indirect'):
            pwr = q['site'].split(':')[0]
            pcls = ''.join(w.capitalize() for w in pwr.lower().split('_'))
            pf = powers_manifest.get(pcls)
            if pf:
                ln = pf['override_lines'].get(q['site'].split(':')[1])
                q['cite'] = "%s:%s" % (pf['file'], ln) if ln else pf['file']
        prompts.append({k: q[k] for k in ['kind', 'site', 'entry', 'pile', 'min', 'max', 'filter', 'can_skip', 'require_manual_confirmation', 'prefs', 'auto_resolve', 'then', 'indirect', 'cite']})
    st = vdj[cls]['status']
    if st == 'same':
        vdelta = {"status": "same", "note": vd.SAME_NOTE}
    elif st == 'new_in_v111':
        vdelta = {"status": "new_in_v111", "note": vd.NEW_NOTE}
    else:
        s2, note = vd.MANUAL[cls]
        vdelta = {"status": s2, "note": note, "fingerprint_diff": vdj[cls]['diffs']}
    hooks = dict(c['hooks'])
    for h in e['abstract_model_hooks']:
        if h not in hooks:
            hooks[h] = "%s:%s" % (f, e['override_lines'].get(h))
    onplay = e['override_lines'].get('OnPlay')
    add = {
        "mechanics": c['mechanics'],
        "prompts": prompts,
        "kernel_requirements": c['reqs'],
        "rng_streams": c['rng'],
        "ascension_sensitivity": "none",
        "version_delta_v107": vdelta,
        "tier": own_tier,
        "tier_reason": tier_reason,
        "tier_with_deps": tier_with_deps,
        "port_batch": batch,
        "port_batch_label": catalog.BATCH_LABEL[batch],
        "port_gates": gates,
        "depends_on": {"powers": allp, "cards": cards_dep, "dependency_requirements": sorted(set(dep_reqs)), "subsystems": subsystems},
        "multiplayer_only": c['mp'],
        "sp_reachable": not c['mp'],
        "card_listener_hooks": hooks,
        "onplay_cite": "%s:%s" % (f, onplay) if onplay else None,
        "notes": c['notes'],
        "open_questions": c['oq'],
    }
    ne = dict(e)
    ne.update(add)
    out_entries.append(ne)

# power dependency table
power_users = collections.defaultdict(list)
for ne in out_entries:
    for p in ne['depends_on']['powers']:
        power_users[p].append(ne['class'])
power_table = {}
for p in sorted(power_users):
    pm = powers_manifest.get(p, {})
    power_table[p] = {
        "model_id": pm.get('model_id'), "file": pm.get('file'), "ported": p in ported_powers,
        "provisional_tier": power_tier(p), "provisional_tier_with_deps": power_tier_with_deps(p), "provisional_batch": power_batch(p),
        "requirements": catalog.POWER_REQS[p], "depends_on_powers": catalog.POWER_DEPS.get(p, []), "depends_on_cards": catalog.POWER_CARD_DEPS.get(p, []),
        "hooks": pm.get('abstract_model_hooks', []), "rng_usage": pm.get('rng_usage', []), "choice_tokens": pm.get('choice_tokens', []),
        "summary": catalog.POWER_SUMMARY[p], "used_by": sorted(power_users[p]),
    }
token_users = collections.defaultdict(list)
for ne in out_entries:
    for t in ne['depends_on']['cards']:
        token_users[t].append(ne['class'])
token_table = {}
allc = {x['class']: x for x in raw['entries']}
for t in sorted(token_users):
    tt = catalog.TOKENS[t]
    token_table[t] = {"model_id": allc[t]['model_id'], "file": allc[t]['file'], "pool": allc[t]['pool'], "provisional_tier": req_tier(tt['reqs']),
                      "provisional_batch": max([2] + [REQ[k]['batch'] for k in tt['reqs'] if REQ[k]['tier'] >= 3]), "requirements": tt['reqs'], "summary": tt['summary'], "used_by": sorted(token_users[t])}

FIELD_DOCS = {
    "mechanics": "effect primitives in execution order (await order of the OnPlay state machine; UI calls dropped); 'upg' = upgraded value",
    "prompts": "player prompts: kind (HandSelect | PileSelect | ChooseCard, the ChoicePromptKind names of docs/sim-port/design-combat-kernel.md section 4.5; Nested = prompts of auto-played cards), site (<model id>:<method>), CardSelectCmd entry, pile, min/max (expressions when computed), filter, can_skip, require_manual_confirmation (CardSelectorPrefs: MinSelect >= 0 && MinSelect != MaxSelect), auto_resolve, then, indirect (prompt opened by a power the card applies), cite",
    "kernel_requirements": "keys into requirement_catalog: features the sim lacks today that the card's own code needs (existing sim facades are not listed)",
    "rng_streams": "RunRngType streams the card consumes, in order, incl. conditional ones and those consumed by the powers it applies",
    "ascension_sensitivity": "'none': no AscensionLevel read in the card, the powers it applies, OstyCmd, ForgeCmd or Osty",
    "version_delta_v107": "same | cosmetic | rule-change | new_in_v111 against artifacts/knowledge/decompiled-v0.107.1 (mechanical fingerprint diff, then manual reading of every flagged file)",
    "tier": "1 = only facades the sim has; 2 = needs facades/hooks/vars that exist in the game but not the sim; 3 = needs a player prompt or a new subsystem (combat history, pool generation, Stars, Osty, Forge, run-state effects); 4 = cross-cutting or unreadable. Own code only (same scale as the other W0 slices)",
    "tier_with_deps": "max of tier and the provisional tiers of the powers and token cards the card depends on (power_dependencies, token_dependencies)",
    "port_batch": "proposed batch (batch_labels, batch_prerequisites; numbering shared with cards-ironclad-colorless for 1-5): max of the batches of the card's own requirements, 2 if it needs any unported power or token, and the batch of every tier-3 requirement of those dependencies; multiplayer-only cards are 9",
    "depends_on": "powers (applied or read, transitive), token cards (created or required), requirement keys contributed by them, subsystems",
    "port_gates": "the subsystem(s) that put the card in its batch (e.g. batch 7 splits into Stars, Osty, Forge)",
    "multiplayer_only": "CardMultiplayerConstraint.MultiplayerOnly: removed from single-player pools (Models/CardPoolModel.cs:68-81, Runs/IRunState.cs:74-84, Factories/CardFactory.cs:25-31)",
    "card_listener_hooks": "AbstractModel hooks the card overrides (needs cards as listeners) with citation",
    "onplay_cite": "OnPlay declaration",
}
env = {
    "schema": "sts2sim.inventory.classified/v1",
    "kind": "cards-necrobinder-regent",
    "base_kind": "cards",
    "base_manifest": "sim/specs/inventory/cards.json",
    "game_version": raw['game_version'],
    "source_digest": raw['source_digest'],
    "slice": {"pools": ["NECROBINDER", "REGENT"], "rule": "entries of cards.json whose pool is NECROBINDER or REGENT, in manifest order"},
    "generated_by": common.generator("cards-necrobinder-regent", "vdelta2.py", "build.py"),
    "count": len(out_entries),
    "field_docs": FIELD_DOCS,
    "batch_labels": {str(k): v for k, v in catalog.BATCH_LABEL.items()},
    "batch_prerequisites": {str(k): v for k, v in catalog.BATCH_PREREQ.items()},
    "requirement_catalog": REQ,
    "power_dependencies": power_table,
    "token_dependencies": token_table,
    "entries": out_entries,
}
dst = common.stage1_path("cards-necrobinder-regent.classified.json")
with open(dst, "w", encoding="utf-8") as fh:
    json.dump(env, fh, indent=1, ensure_ascii=False, sort_keys=False)
    fh.write("\n")
print("wrote", dst, len(out_entries))
json.dump({"entries": out_entries, "powers": power_table, "tokens": token_table}, open(common.work_path("cards-necrobinder-regent", "built.json"), "w"), indent=1)
