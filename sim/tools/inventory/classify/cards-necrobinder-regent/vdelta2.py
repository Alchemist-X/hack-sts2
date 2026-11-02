import os as _os
import sys as _sys
_sys.path.insert(0, _os.path.join(_os.path.dirname(_os.path.abspath(__file__)), ".."))
import common  # noqa: E402
import re, sys, os, json
sys.path.insert(0, os.path.dirname(__file__))
D = common.DECOMPILE_ROOT + "/"
O = common.V107_ROOT + "/"
UI = re.compile(r"Vfx|Sfx|Anim|HoverTip|NCombatRoom|NCreature|\bNode\b|Tween|Color|Spine|Shake|Cmd\.Wait|CustomScaledWait|Preview|Godot|Vector2|Glow|Flash\(|Visual|InvokeDisplayAmountChanged|PlayOnCreature|GetSideCenter")
SM_LINE = re.compile(r"TaskAwaiter|_003C_003Eu__|_003C_003E1__state|\bnum\s*(==|!=|<|>|=)\s|\(uint\)\(num|^case\s|switch \(num\)|AsyncTaskMethodBuilder|AwaitUnsafeOnCompleted|IAsyncStateMachine|SetStateMachine|_003C_003E4__this|^int num|\.GetResult\(\);$|IsCompleted|^goto |^end_IL|^IL_|_003C_003E7__wrap|Dispose\(\)|\[CompilerGenerated\]")
NOISE_IDS = {"Enumerable","Func","IEnumerable","IReadOnlyList","IReadOnlyCollection","GetEnumerator","item","list","List","ToList","ToArray","Array","Empty",
             "result","results","Create","stateMachine","selection","enemies","enemy","target","card","cards","creature","current","GetResult","Count","Any",
             "OfType","Where","Select","SelectMany","Sum","First","FirstOrDefault","Contains","Instance","Collections","Generic","System","IDisposable","Enumerator",
             "CS_0024","locals","wrap","pile","Current","MoveNext","decimal","op_Implicit","HashSet","Add","ROL","DynamicVar","num2","num3","flag","flag2","flag3","Value","HasValue"}
BOILER = {"num","state","val","GetAwaiter","AwaitUnsafeOnCompleted","Task","builder","SetException","SetResult","Start","exception","Exception","Threading","Tasks",
          "await","async","return","if","else","goto","try","catch","finally","default","this","base","new","public","private","protected","override","virtual",
          "sealed","class","static","readonly","const","void","int","bool","var","null","true","false","get","set","value","using","namespace","struct","in","out",
          "ref","is","as","break","case","switch","while","for","foreach","object","string","float","CompletedTask","global","ConfigureAwait","CompilerGenerated"}
def strip_comments(t):
    t = re.sub(r"//[^\n]*", "", t)
    return re.sub(r"/\*.*?\*/", "", t, flags=re.S).replace("global::", "").replace("\r", "")
def logic_text(text):
    out = []
    for l in strip_comments(text).split("\n"):
        s = l.strip()
        if not s or s.startswith(("using ", "namespace ", "[")) or UI.search(s) or SM_LINE.search(s):
            continue
        out.append(s)
    return "\n".join(out)
def member(text, name):
    m = re.search(r"\b" + name + r"\b[^\n]*", text)
    if not m:
        return ""
    i = m.start()
    j = text.find("\n\t" + "p", m.end())
    nxt = re.search(r"\n\t(public|protected|private|internal)\b", text[m.end():])
    return text[i:m.end() + (nxt.start() if nxt else len(text))]
def var_sig(text):
    body = member(text, "CanonicalVars")
    sig = []
    for m in re.finditer(r"new\s+(\w+Var)(?:<(\w+)>)?\(([^()]*(?:\([^()]*\)[^()]*)*)\)", body):
        args = re.sub(r"\s+", "", m.group(3))
        args = re.sub(r"(\d+(?:\.\d+)?)m\b", r"\1", args)
        sig.append((m.group(1) + ("<%s>" % m.group(2) if m.group(2) else ""), args[:80]))
    mults = re.findall(r"WithMultiplier\((.*)", body, re.S)
    ids = set()
    for x in mults:
        ids |= set(re.findall(r"[A-Za-z_]\w*", x))
    return sig, sorted(ids - NOISE_IDS - BOILER - {"WithMultiplier","CardModel","Creature","card","_","e","c","r","p"})
def facts(text, cls):
    t = strip_comments(text)
    f = {}
    m = re.search(r":\s*base\(([^)]*)\)", t)
    f["ctor"] = re.sub(r"\s+", "", m.group(1)) if m else None
    f["vars"], f["var_mult_ids"] = var_sig(t)
    f["keywords"] = sorted(set(re.findall(r"CardKeyword\.(\w+)", member(t, "CanonicalKeywords").split("\n")[0])))
    f["tags"] = sorted(set(re.findall(r"CardTag\.(\w+)", member(t, "CanonicalTags"))))
    up = member(t, "OnUpgrade")
    f["upgrade"] = sorted(set(re.sub(r"\s+", "", s) for s in re.findall(r"[^{};\n]+;", up)))
    pr = {}
    for x in re.findall(r"override\s+[\w<>?.,\[\]]+\s+(\w+)\s*=>\s*([^;]+);", t):
        if x[0] in ("CanonicalVars", "CanonicalKeywords", "CanonicalTags", "ExtraHoverTips") or UI.search(x[0]):
            continue
        pr[x[0]] = re.sub(r"\s+", " ", x[1]).strip()
    f["prop_overrides"] = pr
    f["overridden_members"] = sorted(set(re.findall(r"override\s+(?:async\s+)?[\w<>?.,\[\]]+(?:\s+[\w<>?.,\[\]]+)?\s+(\w+)\s*\(", t)))
    lt = logic_text(t)
    f["cmds"] = sorted(set(x for x in re.findall(r"\b(\w*Cmd\.\w+|Hook\.\w+)", lt) if not x.startswith(("VfxCmd", "SfxCmd", "TalkCmd", "ThinkCmd"))))
    ids = set(re.findall(r"[A-Za-z_]\w*", re.sub(r'"[^"]*"', "", lt)))
    alias = cls[:1].lower() + cls[1:]
    ids = {i for i in ids if i not in NOISE_IDS and i not in BOILER and i != alias and not re.fullmatch(r"val\d+|num\d+|IL_\w+|flag\d*|result\d*|enumerator\d*|current\d*|enumerable\d*|cardModel\d*|obj|i|CS_\w+|c\d*|e|p|r|_|wrap\d*|locals\d*|_003C\w*|\w*_003E\w*", i)}
    f["logic_ids"] = sorted(ids)
    lits = set(re.findall(r"(?<![\w.])(\d+(?:\.\d+)?)m?\b", re.sub(r'"[^"]*"', "", lt)))
    f["logic_lits"] = sorted(lits - {"0", "1"})
    f["strings"] = sorted(set(s for s in re.findall(r'"([^"]*)"', lt) if not re.search(r"vfx|sfx|event:|\.mp3|anim|Cast|PowerUp|Attack", s, re.I)))
    return f
def compare(rel):
    cls = os.path.basename(rel)[:-3]
    a = os.path.join(O, "Models/Cards", cls + ".cs")
    if not os.path.exists(a):
        return {"status": "new_in_v111"}
    fa = facts(open(a, encoding="utf-8", errors="replace").read(), cls)
    fb = facts(open(os.path.join(D, rel), encoding="utf-8", errors="replace").read(), cls)
    diffs = {}
    for k in fa:
        if fa[k] != fb[k]:
            if isinstance(fa[k], list) and k in ("logic_ids", "logic_lits", "strings", "cmds", "overridden_members", "keywords", "tags", "upgrade", "var_mult_ids"):
                diffs[k] = {"v107_only": sorted(set(fa[k]) - set(fb[k])), "v111_only": sorted(set(fb[k]) - set(fa[k]))}
            else:
                diffs[k] = {"v107": fa[k], "v111": fb[k]}
    return {"status": "diff" if diffs else "same", "diffs": diffs}
if __name__ == "__main__":
    raw = json.load(open(common.inventory_path("cards.json")))
    files = ["MegaCrit.Sts2.Core." + e["file"] for e in raw["entries"] if e["pool"] in ("NECROBINDER", "REGENT")]
    out = {os.path.basename(f)[:-3]: compare(f) for f in files}
    json.dump(out, open(common.work_path("cards-necrobinder-regent", "vdelta2.json"), "w"), indent=1, sort_keys=True)
    for k, v in sorted(out.items()):
        if v["status"] != "same":
            print(k, v["status"], json.dumps(v.get("diffs", {}))[:700])
