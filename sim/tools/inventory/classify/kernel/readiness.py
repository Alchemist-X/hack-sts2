import os as _os
import sys as _sys
_sys.path.insert(0, _os.path.join(_os.path.dirname(_os.path.abspath(__file__)), ".."))
import common  # noqa: E402
import re, os, json, sys, collections
D = common.DECOMPILE_ROOT
K = common.work_dir("kernel")
virt = json.load(open(os.path.join(K, "virtuals.json")))["virtuals"]
cmds = json.load(open(os.path.join(K, "cmds.json")))
vported = {v["name"]: v["ported"] for v in virt}
STRIP_CLASSES = {"VfxCmd", "SfxCmd", "TalkCmd", "ThinkCmd"}
STRIP_METHODS = {("Cmd", "Wait"), ("Cmd", "CustomScaledWait"), ("CreatureCmd", "TriggerAnim"), ("CardCmd", "Preview"), ("CardCmd", "PreviewCardPileAdd"),
                 ("CardPileCmd", "GetTweenForCardsChangingPiles"), ("ForgeCmd", "PlayCombatRoomForgeVfx")}
VFX_BUILDER = {"WithAttackerAnim", "WithNoAttackerAnim", "AfterAttackerAnim", "WithAttackerFx", "WithWaitBeforeHit", "WithHitFx", "SpawningHitVfxOnEachCreature", "WithHitVfxSpawnedAtBase", "WithHitVfxNode", "OnlyPlayAnimOnce"}
SIM_EQUIV = {("CardCmd", "Discard"): "partial: sim CardPileCmd.Discard(cs, card) single-card only; Sly auto-play throws",
             ("CardCmd", "Exhaust"): "sim CardPileCmd.Exhaust(cs, card, causedByEthereal)",
             ("CreatureCmd", "Damage"): "partial: decimal overloads only; DamageVar overloads missing",
             ("CardPileCmd", "Add"): "partial: PileType overloads only; CardPile overloads and clonedBy missing; no AfterCardChangedPiles",
             ("DamageCmd", "Attack"): "partial: decimal overload only; CalculatedDamageVar overload missing",
             ("CardPileCmd", "AddGeneratedCardToCombat"): "ported",
             ("PowerCmd", "Apply"): "ported (choiceContext dropped)",
             ("CardPileCmd", "AddDuringManualCardPlay"): "partial: sim inlines in ActionExecutor; hook AfterCardChangedPiles (:980) not dispatched"}
cmd_status = {}
for c, rows in cmds.items():
    for r in rows:
        key = (c, r["name"])
        if c in STRIP_CLASSES or key in STRIP_METHODS or (c == "AttackCommand" and r["name"] in VFX_BUILDER):
            st = "strip"
        elif key in SIM_EQUIV:
            st = "partial" if SIM_EQUIV[key].startswith("partial") else "ported"
        elif r["ported"]:
            st = "ported"
        else:
            st = "missing"
        cmd_status[key] = st
json.dump({f"{a}.{b}": v for (a, b), v in cmd_status.items()}, open(os.path.join(K, "cmd_status.json"), "w"), indent=0)
KINDS = ["Cards", "Powers", "Relics", "Potions", "Monsters", "Encounters", "Enchantments", "Afflictions", "Orbs", "Modifiers", "Events"]
CMD_CLASSES = sorted(set(c for c in cmds if c != "AttackCommand"))
cmd_pat = re.compile(r'\b(' + "|".join(CMD_CLASSES) + r')\.(\w+)\s*(?:<[^>(]*>)?\(')
builder_pat = re.compile(r'\.(FromOsty|FromCard|FromMonster|Targeting|TargetingAllOpponents|TargetingRandomOpponents|Unpowered|WithHitCount|BeforeDamage)\(')
hook_pat = re.compile(r'(?:public|protected) (?:sealed )?override (?:async )?[^\(=;{]*? (\w+)\(')
feat = {
    "prompt": re.compile(r'CardSelectCmd\.(From\w+)'),
    "history": re.compile(r'\.History\b'),
    "stars": re.compile(r'\bStars\b|GainStars|StarCost'),
    "forge": re.compile(r'ForgeCmd|SovereignBlade'),
    "osty": re.compile(r'\bOsty\b|OstyCmd|PetOwner|IsOstyAlive'),
    "orbs": re.compile(r'OrbCmd|OrbQueue|OrbModel'),
    "summon": re.compile(r'OstyCmd\.Summon|SummonVar|AfterSummon'),
    "doom": re.compile(r'DoomPower'),
    "enchant": re.compile(r'Enchantment\b|CardCmd\.Enchant'),
    "afflict": re.compile(r'Affliction\b|CardCmd\.Afflict'),
    "autoplay": re.compile(r'CardCmd\.AutoPlay|AutoPlayFromDrawPile'),
    "calculated_var": re.compile(r'Calculated(Damage|Block)?Var|CalculationBaseVar'),
    "temporary_power": re.compile(r'ITemporaryPower'),
    "card_generation": re.compile(r'CardFactory\.|CreateCard\b|CreateCard<'),
    "spawn_monster": re.compile(r'CreatureCmd\.Add\b|CreatureCmd\.Add<'),
    "rng_targets": re.compile(r'Rng\.CombatTargets'),
    "rng_cardgen": re.compile(r'Rng\.CombatCardGeneration'),
    "rng_cardsel": re.compile(r'Rng\.CombatCardSelection'),
    "rng_potion": re.compile(r'Rng\.CombatPotionGeneration'),
    "rng_energy": re.compile(r'Rng\.CombatEnergyCosts'),
    "rng_orb": re.compile(r'Rng\.CombatOrbGeneration'),
    "rng_niche": re.compile(r'Rng\.Niche'),
    "rng_shuffle": re.compile(r'Rng\.Shuffle'),
    "rng_monster_private": re.compile(r'base\.Rng\b|\bRng\.Next|_rng\b'),
    "player_end_turn": re.compile(r'PlayerCmd\.EndTurn'),
    "gold": re.compile(r'PlayerCmd\.(Gain|Lose)Gold'),
}
def call_args(s, name):
    out = []
    for m in re.finditer(r'\b' + re.escape(name) + r'\s*(?:<[^>(]*>)?\(', s):
        depth = 0
        i = m.end() - 1
        while i < len(s):
            if s[i] == "(":
                depth += 1
            elif s[i] == ")":
                depth -= 1
                if depth == 0:
                    break
            i += 1
        out.append(s[m.end():i])
    return out


def top_args(a):
    parts, depth, cur = [], 0, ""
    for i, ch in enumerate(a):
        if ch in "([{<":
            depth += 1
        elif ch in ")]}" or (ch == ">" and a[i - 1:i] != "="):
            depth -= 1
        if ch == "," and depth == 0:
            parts.append(cur.strip())
            cur = ""
        else:
            cur += ch
    if cur.strip():
        parts.append(cur.strip())
    return parts


def partial_gaps(c, s):
    """Blocking gaps of a partially ported Cmd: only the overloads the sim lacks count."""
    calls = call_args(s, c)
    if c == "CreatureCmd.Damage":
        def decimal_overload(a):
            parts = top_args(a)
            return len(parts) > 3 and ("ValueProp" in parts[3] or parts[3].endswith(".Props"))
        return ["CreatureCmd.Damage(DamageVar)"] if any(not decimal_overload(a) for a in calls) else []
    if c == "DamageCmd.Attack":
        return ["DamageCmd.Attack(CalculatedDamageVar)"] if any("Calculated" in a for a in calls) else []
    if c == "CardPileCmd.Add":
        return ["CardPileCmd.Add(CardPile|clonedBy)"] if any("GetPile" in a or "clonedBy" in a for a in calls) else []
    if c == "CardCmd.Discard":
        out = []
        for a in calls:
            parts = top_args(a)
            arg = parts[1] if len(parts) > 1 else ""
            tok = "CardCmd.Discard(list)" if (arg == "cards" or arg.startswith("(System.Collections.Generic.IEnumerable")) else "CardCmd.Discard(single, Sly)"
            if tok not in out:
                out.append(tok)
        return out
    return [c + "~"]


FEATURE_LABEL = {"rng_monster_private": "private_rng"}
files = {}
blocking_hooks = collections.Counter(); blocking_cmds = collections.Counter()
kind_summary = {}
for k in KINDS:
    d = os.path.join(D, "MegaCrit.Sts2.Core.Models." + k)
    tot = 0; hook_ready = 0; cmd_ready = 0; both = 0; feats = collections.Counter()
    for f in sorted(os.listdir(d)):
        s = open(os.path.join(d, f), encoding="utf-8", errors="replace").read()
        name = f[:-3]
        hooks = sorted(set(h for h in hook_pat.findall(s) if h in vported))
        miss_h = [h for h in hooks if not vported[h]]
        calls = sorted(set(f"{a}.{b}" for a, b in cmd_pat.findall(s)))
        bcalls = sorted(set("AttackCommand." + b for b in builder_pat.findall(s))) if "DamageCmd.Attack" in s else []
        allc = calls + bcalls
        miss_c = []
        for c in allc:
            a, b = c.split(".", 1)
            st = cmd_status.get((a, b), "missing")
            if st == "missing":
                miss_c.append(c)
            elif st == "partial":
                for r in partial_gaps(c, s):
                    if r not in miss_c:
                        miss_c.append(r)
        fl = sorted(n for n, p in feat.items() if p.search(s))
        files[f"{k}/{name}"] = dict(hooks=hooks, missing_hooks=miss_h, cmds=allc, missing_cmds=miss_c, features=[FEATURE_LABEL.get(n, n) for n in fl])
        tot += 1
        hr = not miss_h; cr = not [m for m in miss_c if not m.endswith("~")]
        hook_ready += hr; cmd_ready += cr; both += (hr and cr)
        for h in miss_h: blocking_hooks[h] += 1
        for m in miss_c: blocking_cmds[m] += 1
        for x in fl: feats[x] += 1
    kind_summary[k] = dict(files=tot, hook_ready=hook_ready, cmd_ready=cmd_ready, hook_and_cmd_ready=both,
                           features=dict((FEATURE_LABEL.get(a, a), b) for a, b in sorted(feats.items())))
out = dict(kind_summary=kind_summary, blocking_hooks=blocking_hooks.most_common(), blocking_cmds=blocking_cmds.most_common(), files=files)
json.dump(out, open(os.path.join(K, "readiness.json"), "w"), indent=0)
for k, v in kind_summary.items():
    print(k, {a: b for a, b in v.items() if a != "features"})
    print("   ", v["features"])
print("top blocking hooks:", blocking_hooks.most_common(25))
print("top blocking cmds:", blocking_cmds.most_common(40))
