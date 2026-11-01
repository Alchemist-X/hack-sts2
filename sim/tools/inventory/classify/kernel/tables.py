import os as _os
import sys as _sys
_sys.path.insert(0, _os.path.join(_os.path.dirname(_os.path.abspath(__file__)), ".."))
import common  # noqa: E402
import re, os, json, collections
K = common.work_dir("kernel")
virt = json.load(open(f"{K}/virtuals.json"))["virtuals"]
hooks = json.load(open(f"{K}/hooks.json"))
cmds = json.load(open(f"{K}/cmds2.json"))
cmd_status = json.load(open(f"{K}/cmd_status.json"))
ready = json.load(open(f"{K}/readiness.json"))
sites = json.load(open(f"{K}/sites.json"))
ov_files = json.load(open(f"{K}/override_files.json"))

def short_t(t):
    t = t.replace("System.Collections.Generic.", "").replace("System.Threading.Tasks.", "")
    t = t.replace("IReadOnlyList<Creature>", "IReadOnlyList<Creature>").replace("IEnumerable<Creature>", "IEnumerable<Creature>")
    return t
def short_params(p):
    if not p.strip(): return ""
    out = []
    depth = 0; cur = ""
    for ch in p:
        if ch in "<([": depth += 1
        if ch in ">)]": depth -= 1
        if ch == "," and depth == 0:
            out.append(cur.strip()); cur = ""
        else: cur += ch
    if cur.strip(): out.append(cur.strip())
    res = []
    for a in out:
        a = re.sub(r'\s*=\s*.*$', '', a)
        parts = a.rsplit(" ", 1)
        t = parts[0] if len(parts) == 2 else a
        n = parts[1] if len(parts) == 2 else ""
        t = short_t(t)
        if t == "PlayerChoiceContext": t = "ctx"
        if t == "HookPlayerChoiceContext": t = "hookCtx"
        res.append(t if n in ("", ) else f"{t} {n}" if n in ("participants", "deathAnimLength", "autoPlayType", "combatState", "fromHandDraw", "causedByEthereal", "wasRemovalPrevented", "isInitialShuffle", "isMimicked", "isAutoPlay") else t)
    return ", ".join(res)

called = collections.defaultdict(list)
for h in hooks:
    for c in h["calls"]:
        called[c].append(h)
DIRECT = {"ModifyDamageAdditive": "Run+Combat via ModifyDamageInternal (Hooks/Hook.cs:11766)",
          "ModifyDamageMultiplicative": "Run+Combat via ModifyDamageInternal (Hooks/Hook.cs:11766)",
          "ModifyDamageCap": "Run+Combat via ModifyDamageInternal (Hooks/Hook.cs:11766)",
          "AfterModifyingCardPlayResultLocation": "direct on modifiers (Models/CardModel.cs:524)",
          "AfterAddToDeckPrevented": "direct on preventer (Commands/CardPileCmd.cs:406)",
          "AfterTargetingBlockedVfx": "UI only (Nodes.Combat/NTargetManager.cs)"}
SRC_ABBR = {"Guarded": "G", "Combat": "C", "Run+Combat": "R+C", "RunOnly": "R", "Combat(power.CombatState)": "C"}
def vsource(name):
    if name in DIRECT: return DIRECT[name]
    hs = called.get(name, [])
    srcs = sorted(set(SRC_ABBR[s] for h in hs for s in h["sources"]))
    disp = sorted(set(h["name"] for h in hs))
    return ("/".join(srcs) + " via " + ",".join(disp)) if hs else "none"
def relevance(name):
    if name in ("IsMock", "PreviewOutsideOfCombat", "CompareTo"): return "infra"
    if name in DIRECT:
        return {"AfterAddToDeckPrevented": "run", "AfterTargetingBlockedVfx": "ui"}.get(name, "combat")
    hs = called.get(name, [])
    srcs = set(s for h in hs for s in h["sources"])
    if not srcs: return "none"
    if srcs <= {"RunOnly"}: return "run"
    return "combat"
DROPPED = {"BeforeSideTurnStart": "participants", "AfterSideTurnStart": "participants", "AfterSideTurnStartLate": "participants",
           "BeforeSideTurnEndVeryEarly": "participants", "BeforeSideTurnEndEarly": "participants", "BeforeSideTurnEnd": "participants",
           "AfterSideTurnEnd": "participants", "AfterSideTurnEndLate": "participants", "AfterDeath": "deathAnimLength (VFX only)",
           "ShouldPlay": "autoPlayType (read by Cards/Enthralled)"}
PART_READERS = {"BeforeSideTurnStart": 26, "AfterSideTurnStart": 47, "AfterSideTurnStartLate": 0, "BeforeSideTurnEndVeryEarly": 3,
                "BeforeSideTurnEndEarly": 3, "BeforeSideTurnEnd": 12, "AfterSideTurnEnd": 52, "AfterSideTurnEndLate": 1}
KCOLS = ["Cards", "Powers", "Relics", "Potions", "Monsters"]
OTHER = ["Enchantments", "Afflictions", "Orbs", "Modifiers", "Singleton", "Events", "Characters", "Encounters"]
def kc(o, k): return o.get(k, 0)
vrows = []
for v in virt:
    o = v["overrides"]
    other = {k: c for k, c in o.items() if k in OTHER}
    nonrule = {k: c for k, c in o.items() if k in ("Achievements", "Badges")}
    mocks = sum(c for k, c in o.items() if "Mock" in k)
    rel = relevance(v["name"])
    content_total = sum(c for k, c in o.items() if k in KCOLS + OTHER)
    vrows.append(dict(line=v["line"], name=v["name"], ported=v["ported"], ret=short_t(v["ret"]), params=short_params(v["params"]),
                      default=v["default"].replace("79228162514264337593543950335m", "decimal.MaxValue"), relevance=rel, dispatch=vsource(v["name"]),
                      overrides={k: c for k, c in o.items() if "Mock" not in k and k != "(base)"}, mocks=mocks, content_total=content_total,
                      sim_dropped_param=DROPPED.get(v["name"]), participants_readers=PART_READERS.get(v["name"]),
                      override_files={k: f for k, f in ov_files.get(v["name"], {}).items() if "Mock" not in k}))
json.dump(vrows, open(f"{K}/vrows.json", "w"), indent=0)
# markdown: virtual table
L = []
L.append("| L | Virtual | Sim | Rel | Signature (game; sim adds `cs`, drops `ctx`) | Default | Cards | Powers | Relics | Potions | Monsters | Other | Σ |")
L.append("|---:|---|:-:|:-:|---|---|--:|--:|--:|--:|--:|---|--:|")
for r in vrows:
    o = r["overrides"]
    other = ", ".join(f"{k[:5]} {c}" for k, c in o.items() if k in OTHER)
    nr = ", ".join(f"{k[:5]} {c}" for k, c in o.items() if k in ("Achievements", "Badges"))
    if nr: other = (other + "; " if other else "") + "non-rule: " + nr
    sig = f"`({r['params']}) → {r['ret']}`"
    note = ""
    if r["sim_dropped_param"]: note = f" ⚠ sim drops `{r['sim_dropped_param']}`" + (f" ({r['participants_readers']} readers)" if r["participants_readers"] else "")
    dflt = r["default"]
    if len(dflt) > 22: dflt = dflt[:22] + "…"
    L.append(f"| {r['line']} | `{r['name']}` | {'Y' if r['ported'] else '–'} | {r['relevance']} | {sig}{note} | `{dflt}` | {kc(o,'Cards') or ''} | {kc(o,'Powers') or ''} | {kc(o,'Relics') or ''} | {kc(o,'Potions') or ''} | {kc(o,'Monsters') or ''} | {other} | {r['content_total']} |")
open(f"{K}/t_virtuals.md", "w").write("\n".join(L) + "\n")
# dispatcher table
D = []
D.append("| L | Dispatcher | Sim | Kind | Listener source (passes) | Calls (pass order) | Choice ctx | out/ref | Rel |")
D.append("|---:|---|:-:|:-:|---|---|---|---|:-:|")
for h in hooks:
    srcs = [SRC_ABBR[s] for s in h["sources"]]
    if h["name"] == "ModifyDamage": srcs = ["R+C ×3 (via ModifyDamageInternal :11766)"]
    flags = h["flags"]
    ctx = "—"
    if any("per-listener" in f for f in flags): ctx = "**per-listener Hook ctx + WhenAll (deferral)**"
    elif any("PushModel" in f for f in flags): ctx = "caller ctx (Push/PopModel)"
    elif any(f == "ctx param" for f in flags): ctx = "caller ctx"
    gates = []
    if any("NetId" in f for f in flags): gates.append("NetId gate")
    if any("modifier-filtered" in f for f in flags): gates.append("modifier/preventer-filtered")
    if any("enchantment" in f for f in flags): gates.append("enchantment pre-pass")
    outs = "; ".join(o.replace("System.Collections.Generic.", "") for o in h["outs"])
    rel = "run" if set(h["sources"]) <= {"RunOnly"} and h["sources"] else "combat"
    calls = ",".join(h["calls"]) if h["name"] != "ModifyDamage" else "ModifyDamageAdditive,ModifyDamageMultiplicative,ModifyDamageCap"
    note = (" · " + ", ".join(gates)) if gates else ""
    D.append(f"| {h['line']} | `{h['name']}` | {'Y' if h['ported'] else '–'} | {h['kind']} | {' → '.join(srcs) or '—'} | {calls}{note} | {ctx} | {outs} | {rel} |")
open(f"{K}/t_dispatchers.md", "w").write("\n".join(D) + "\n")
# cmd tables
NOTES = {
 ("CardPileCmd", "Add"): "batched; AfterCardChangedPiles (Run+Combat, normal+Late) per moved card not dispatched by sim (:3466)",
 ("CardPileCmd", "AddDuringManualCardPlay"): "sim inlines in ActionExecutor; hook AfterCardChangedPiles (:980) not dispatched",
 ("CardPileCmd", "AddGeneratedCardsToCombat"): "batch variant; History.CardGenerated per card",
 ("CardPileCmd", "AutoPlayFromDrawPile"): "draws top N and CardCmd.AutoPlay(forceExhaust); prompts inside use caller ctx",
 ("CardPileCmd", "DrawWithoutBlockingOnOtherPlayers"): "multiplayer draw; single player ≡ Draw (verify)",
 ("CardPileCmd", "Shuffle"): "AfterShuffle carries ctx → StratagemPower prompt",
 ("CardPileCmd", "RemoveFromDeck"): "deck (run state); reachable in combat: DeprecatedCard.OnPlay removes its DeckVersion, SwipePower", ("CardPileCmd", "AddCursesToDeck"): "run layer (deck)",
 ("CardPileCmd", "AddCurseToDeck"): "run layer (deck)", ("CardPileCmd", "GiveToAnotherPlayer"): "multiplayer only",
 ("CardPileCmd", "AddToCombatAndPreview"): "create N copies of T into a pile (statuses from monsters); preview stripped",
 ("CardCmd", "AutoPlay"): "ShouldPlay(AutoPlayType); random AnyEnemy target via Rng.CombatTargets; X capture; OnPlayWrapper(isAutoPlay)",
 ("CardCmd", "Discard"): "via DiscardAndDraw: Sly cards auto-play (AutoPlayType.SlyDiscard, Commands/CardCmd.cs:843,917)",
 ("CardCmd", "DiscardAndDraw"): "Sly auto-play then draw", ("CardCmd", "Exhaust"): "sim: CardPileCmd.Exhaust",
 ("CardCmd", "Upgrade"): "sync void; combat + deck", ("CardCmd", "Downgrade"): "IsEnding gate; DowngradeInternal",
 ("CardCmd", "Transform"): "replacement card; combat and deck scopes", ("CardCmd", "TransformToRandom"): "explicit Rng parameter",
 ("CardCmd", "Enchant"): "attaches EnchantmentModel (mostly run layer)", ("CardCmd", "Afflict"): "ShouldAfflict gate; History.CardAfflicted",
 ("CardCmd", "Preview"): "UI", ("CardCmd", "PreviewCardPileAdd"): "UI",
 ("CreatureCmd", "Add"): "mid-combat monster spawn (CreateCreature/AddCreature/SetUpForCombat/AfterCreatureAdded)",
 ("CreatureCmd", "Escape"): "monster leaves combat without dying", ("CreatureCmd", "TriggerAnim"): "animation (strip)",
 ("CreatureCmd", "Damage"): "decimal overloads ported; DamageVar overloads (props from the var) missing",
 ("CreatureCmd", "GainBlock"): "decimal overload ported; BlockVar overload missing",
 ("DamageCmd", "Attack"): "decimal overload ported; CalculatedDamageVar overload missing", ("CreatureCmd", "Stun"): "Wrapper state machine body missing in decompile (pipeline spec §9)",
 ("PlayerCmd", "GainStars"): "ShouldGainStars gate (no overrides), AfterStarsGained", ("PlayerCmd", "EndTurn"): "card-forced end of turn",
 ("PlayerCmd", "AddPet"): "creates pet creature on the player side (Osty)", ("PlayerCmd", "GainGold"): "run resource touched in combat (ModifyGoldGained/AfterGoldGained)",
 ("PlayerCmd", "CompleteQuest"): "quest cards", ("PlayerCmd", "MimicRestSiteHeal"): "event",
 ("OrbCmd", "Channel"): "evoke front when full; TryEnqueue; AfterOrbChanneled; History.OrbChanneled",
 ("OrbCmd", "Passive"): "ModifyOrbPassiveTriggerCount (+AfterModifying…)", ("OrbCmd", "EvokeNext"): "AfterOrbEvoked",
 ("OstyCmd", "Summon"): "ModifySummonAmount; GainMaxHp or revive/create Osty + DieForYouPower; AfterOstyRevived; History.Summoned; AfterSummon",
 ("ForgeCmd", "Forge"): "create SovereignBlade in hand if none; add damage to all blades (incl. exhausted); AfterForge",
 ("ForgeCmd", "PlayCombatRoomForgeVfx"): "VFX (strip)",
 ("PotionCmd", "TryToProcure"): "slot capacity; ShouldProcurePotion; AfterPotionProcured", ("PotionCmd", "Discard"): "AfterPotionDiscarded",
 ("RelicCmd", "Obtain"): "AfterObtained (run-layer prompts)", ("RelicCmd", "Melt"): "melted relics leave listener set",
 ("Cmd", "Wait"): "NonInteractiveMode → immediate (strip)", ("Cmd", "CustomScaledWait"): "strip",
 ("CardSelectCmd", "FromHand"): "see §4", ("RewardsCmd", "OfferCustom"): "run layer",
 ("AttackCommand", "FromOsty"): "Osty as attacker with card source", ("AttackCommand", "Execute"): "ported (choiceContext dropped)",
}
C = []
for c, rows in cmds.items():
    C.append(f"\n**{c}** (`Commands{'.Builders' if c == 'AttackCommand' else ''}/{c}.cs`)\n")
    C.append("| L | Method | Status | Scope | Uses (models) | By kind | Notes |")
    C.append("|---:|---|:-:|:-:|--:|---|---|")
    for r in rows:
        st = r.get("status", cmd_status.get(f"{c}.{r['name']}", "missing"))
        kinds = ", ".join(f"{k} {v}" for k, v in r["uses"].items() if not k.startswith("core:") and "Mock" not in k)
        note = NOTES.get((c, r["name"]), "")
        sig = f"`{r['name']}{r['generic']}({short_params(r['params'])})`"
        C.append(f"| {r['line']} | {sig} | {st} | {r.get('scope', '')} | {r['uses_models']} | {kinds} | {note} |")
open(f"{K}/t_cmds.md", "w").write("\n".join(C) + "\n")
# prompt sites table
P = ["| Kind | Model | Site (hook) | CardSelectCmd | Pile/options | min | max | Skip | Filter | Category |", "|---|---|---|---|---|---|---|:-:|---|---|"]
for s in sites:
    if s["site"] == "AfterObtained": continue
    def simp(x):
        if x is None: return ""
        x = re.sub(r'\w+\.DynamicVars\.Cards\.IntValue', 'Cards', x)
        x = re.sub(r'\w+\.DynamicVars\["(\w+)"\]\.IntValue', r'\1', x)
        x = re.sub(r'\w+Power\.Amount', 'Amount', x)
        x = x.replace("999999999", "∞").replace("_003CcardCount_003E5__2", "cardCount")
        return x
    f = s["filter"] or ""
    f = re.sub(r'\(CardModel \w+\) => ', '', f)
    f = re.sub(r'delegate\(CardModel c\) \{ ', '', f)
    f = f[:46]
    P.append(f"| {s['kind']} | {s['file']} | {s['site']} | {s['method']} | {s['pile']} | {simp(s['min'])} | {simp(s['max'])} | {'Y' if s['can_skip'] else ''} | {f.replace('|','/')} | {s['category']} |")
open(f"{K}/t_prompts.md", "w").write("\n".join(P) + "\n")
# readiness
R = ["| Kind | Files | Overridden hooks all ported | Cmd calls all ported | Both | Feature flags (files) |", "|---|--:|--:|--:|--:|---|"]
for k, v in ready["kind_summary"].items():
    feats = ", ".join(f"{a} {b}" for a, b in sorted(v["features"].items(), key=lambda x: -x[1]))
    R.append(f"| {k} | {v['files']} | {v['hook_ready']} | {v['cmd_ready']} | {v['hook_and_cmd_ready']} | {feats} |")
open(f"{K}/t_readiness.md", "w").write("\n".join(R) + "\n")
print("ok", len(vrows), len(hooks), sum(len(r) for r in cmds.values()), len(P) - 2)
