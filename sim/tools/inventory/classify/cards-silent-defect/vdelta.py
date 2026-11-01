import os as _os
import sys as _sys
_sys.path.insert(0, _os.path.join(_os.path.dirname(_os.path.abspath(__file__)), ".."))
import common  # noqa: E402
import json
import os
import re
import sys
from collections import Counter

D1 = common.DECOMPILE_ROOT
D0 = common.V107_ROOT

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from view import DROP_LINE  # noqa: E402

UI_CMDS = {"VfxCmd", "SfxCmd", "TalkCmd", "ThinkCmd"}
UI_METHODS = {"Wait", "CustomScaledWait", "TriggerAnim", "PreviewCardPileAdd", "PlayFullScreenInCombat",
              "PlayOnCreatureCenter", "PlayOnCreature"}
BOILER = set("""
TaskAwaiter ConfiguredTaskAwaitable ConfiguredTaskAwaiter AsyncTaskMethodBuilder IAsyncStateMachine Exception
ArgumentNullException ThrowIfNull GetAwaiter GetResult IsCompleted AwaitUnsafeOnCompleted ConfigureAwait
SetException SetResult SetStateMachine DebuggerHidden CompilerGenerated StructLayout LayoutKind AsyncStateMachine
IteratorStateMachine DebuggerBrowsable DebuggerBrowsableState MoveNext Start Create Task System Collections Generic
Threading Tasks IEnumerable IReadOnlyList IReadOnlyCollection List Enumerable THIS Unknown CompletedTask Dispose
IDisposable IEnumerator Current PlayerChoiceContext CardPlay Arr List1 ReadOnlySingleElementList ReadOnlyArray
Image AddChildSafely Instance NCombatRoom Node Node2D Vector2 Color Colors CombatVfxContainer VfxSpawnPosition
GetCreatureNode GetBottomOfHitbox ExtraHoverTips HoverTipFactory StaticHoverTip IHoverTip FromPower FromCard
FromOrb Static FromKeyword FromCardWithCardHoverTips FromForge FromOsty Godot Variant Tween Mathf
CastAnimDelay Character AttackAnimDelay PowerUpAnimDelay Func GetEnumerator Enumerator MoveNextAsync
ValueTaskAwaiter DisposeAsync ExceptionDispatchInfo Capture Throw IAsyncEnumerator IAsyncEnumerable ValueTask
AttackCommand CardPileAddResult CardModel Creature OrbModel PowerModel HashSet Add ICombatState Player
IGrouping ModelId TestMode IsOff IsOn GetValueOrDefault Concat ToList ToArray Select Where Any Count First
FirstOrDefault Last LastOrDefault OfType Contains Sum Max Min OrderBy ThenBy Skip Take Distinct
""".split())


def strip_comments_strings(text):
    text = re.sub(r"//[^\n]*", "", text)
    text = re.sub(r"/\*.*?\*/", "", text, flags=re.S)
    text = re.sub(r'@?"(?:\\.|[^"\\])*"', lambda m: ' "' + re.sub(r"\W", "_", m.group(0)[1:-1]) + '" ', text)
    text = re.sub(r"'(?:\\.|[^'\\])'", " 'C' ", text)
    return text


DECL_RE = re.compile(r"^\s*(?:private |public )?[\w.]+(?:<[\w<>, ?.\[\]]+>)?(?:\[\])?\??\s+\w+;\s*$")


def drop_plumbing(text):
    out = []
    for ln in text.split("\n"):
        if any(p.search(ln) for p in DROP_LINE):
            continue
        if re.search(r"^\s*(switch \(num\)|default:|case -?\d+:|break;|if \(num [<>=!]+ -?\d+\))\s*$", ln):
            continue
        if re.search(r"_003C_003E1__state|_003C_003Eu__\d+|_003C_003Et__builder", ln):
            continue
        if DECL_RE.match(ln):
            continue
        out.append(ln)
    return "\n".join(out)


def normalize(text, new):
    text = strip_comments_strings(text)
    if new:
        text = drop_plumbing(text)
    text = re.sub(r"_003C(\w+?)_003E5__\d+", r"\1", text)
    text = re.sub(r"\bCS_0024\w*", " ", text)
    text = re.sub(r"_003C_003E\w+", " ", text)
    text = re.sub(r"_003C\w+?_003E\w*", " ", text)
    text = re.sub(r"\bIL_\w+", " ", text)
    text = re.sub(r"\bglobal::", "", text)
    text = re.sub(r"\bEnumerable\.(\w+)<[\w<>, .?]+>\(", r"Enumerable.\1(", text)
    text = re.sub(r"\bdecimal\.op_Implicit\b", " ", text)
    text = re.sub(r"\bnamespace [\w.]+;", "", text)
    text = re.sub(r"^\s*using [\w.]+;\s*$", "", text, flags=re.M)
    text = re.sub(r"\[(?:DebuggerHidden|CompilerGenerated|StructLayout\([^\]]*\)|AsyncStateMachine\([^\]]*\)|IteratorStateMachine\([^\]]*\)|DebuggerBrowsable\([^\]]*\))\]", " ", text)
    return text


TOKEN_RE = re.compile(r"\d+(?:\.\d+)?[mfMFdD]?|[A-Za-z_]\w*|==|!=|<=|>=|&&|\|\||\?\?|[<>+\-*/%!]")


def sem_tokens(text, new):
    out = Counter()
    for t in TOKEN_RE.findall(normalize(text, new)):
        if t[0].isdigit():
            out["#" + re.sub(r"[mfMFdD]$", "", t)] += 1
        elif t[0].isalpha() or t[0] == "_":
            if t[0].isupper() and t not in BOILER and not re.search(r"Vfx|Sfx|Anim|Hover|Sound|Shake", t) \
                    and not re.match(r"^N[A-Z]", t):
                out[t] += 1
        elif t in ("==", "!=", "<=", ">=", "&&", "||", "??", "+", "-", "*", "/", "%"):
            out["op" + t] += 1
    return out


CMD_RE = re.compile(r"\b(\w*Cmd)\.(\w+)(?:<([\w.]+)>)?\(")


def cmd_calls_new(text):
    t = strip_comments_strings(text)
    calls = []
    for m in CMD_RE.finditer(t):
        if m.group(1) in UI_CMDS or m.group(2) in UI_METHODS:
            continue
        name = "%s.%s%s" % (m.group(1), m.group(2), ("<%s>" % m.group(3)) if m.group(3) else "")
        rest = t[m.end():]
        mm = re.search(r"_003C_003E1__state = (\d+)\)", rest)
        st = int(mm.group(1)) if mm else 10 ** 6
        semi = rest.find(";")
        ga = rest.find("GetAwaiter()")
        awaited = ga != -1 and semi != -1 and ga < semi
        calls.append((st, 1 if awaited else 0, m.start(), name))
    calls.sort()
    return [c[3] for c in calls], []


def cmd_calls_old(text):
    t = strip_comments_strings(text)
    seq = []
    for m in CMD_RE.finditer(t):
        if m.group(1) in UI_CMDS or m.group(2) in UI_METHODS:
            continue
        seq.append("%s.%s%s" % (m.group(1), m.group(2), ("<%s>" % m.group(3)) if m.group(3) else ""))
    return seq


def ctor(text):
    m = re.search(r":\s*base\(([^;{]*?)\)\s*\{", text, re.S)
    return re.sub(r"\s+", " ", m.group(1)).strip() if m else None


def prop_expr(text, name):
    m = re.search(r"\b" + name + r"\s*=>\s*(.*?);\s*\n", text, re.S)
    if not m:
        return None
    s = m.group(1)
    s = re.sub(r"global::", "", s)
    s = re.sub(r"_003C_003Ez__\w+<\w+>", "C", s)
    s = re.sub(r"new HashSet<\w+>\s*\{([^}]*)\}", r"C(\1)", s)
    s = re.sub(r"\s+", "", s)
    return s


def member_text(text, name):
    m = re.search(r"\b" + name + r"\b[\s\S]*?(?=\n\t(?:public|protected|private|internal|\[)|\n\}\s*$)", text)
    return m.group(0) if m else ""


def tags_from(text, enum):
    return sorted(set(re.findall(enum + r"\.(\w+)", text)))


def upgrade_body(text):
    m = re.search(r"override void OnUpgrade\(\)\s*\{(.*?)\n\t\}", text, re.S)
    if not m:
        return None
    s = m.group(1)
    s = re.sub(r"\s+", " ", s).strip()
    return s


def compare(cls):
    p1 = os.path.join(D1, "MegaCrit.Sts2.Core.Models.Cards", cls + ".cs")
    p0 = os.path.join(D0, "Models/Cards", cls + ".cs")
    t1 = open(p1, encoding="utf-8-sig").read()
    if not os.path.exists(p0):
        return {"status": "new", "old_file": None}
    t0 = open(p0, encoding="utf-8-sig").read()
    c1, c0 = sem_tokens(t1, True), sem_tokens(t0, False)
    seq1, unawaited1 = cmd_calls_new(t1)
    seq0 = cmd_calls_old(t0)
    struct = {}
    for label, f in (("ctor", ctor),
                     ("vars", lambda t: prop_expr(t, "CanonicalVars")),
                     ("keywords", lambda t: tags_from(member_text(t, "CanonicalKeywords"), "CardKeyword")),
                     ("tags", lambda t: tags_from(member_text(t, "CanonicalTags"), "CardTag")),
                     ("upgrade", upgrade_body),
                     ("overrides", lambda t: sorted(set(re.findall(r"(?:public|protected) override [\w<>?, .\[\]]+? (\w+)\s*(?:\(|=>|\{)", t))))):
        a, b = f(t1), f(t0)
        struct[label] = {"same": a == b, "new": a, "old": b}
    multiset_new = Counter(seq1 + unawaited1)
    multiset_old = Counter(seq0)
    res = {
        "old_file": "Models/Cards/%s.cs" % cls,
        "token_only_new": dict(c1 - c0),
        "token_only_old": dict(c0 - c1),
        "cmd_seq_new": seq1,
        "cmd_unawaited_new": unawaited1,
        "cmd_seq_old": seq0,
        "cmd_multiset_equal": multiset_new == multiset_old,
        "struct": struct,
    }
    seq_old_awaited = [c for c in seq0]
    res["cmd_order_equal"] = seq1 == seq0
    flags = []
    for k, v in struct.items():
        if not v["same"]:
            flags.append(k)
    if not res["cmd_multiset_equal"]:
        flags.append("cmd_multiset")
    elif not res["cmd_order_equal"]:
        flags.append("cmd_order")
    noise = {cls, "#0", "CardTag", "CardKeyword", "Cast", "PowerUp"}
    tn = {k: v for k, v in res["token_only_new"].items() if k not in noise}
    to = {k: v for k, v in res["token_only_old"].items() if k not in noise}
    res["token_only_new"], res["token_only_old"] = tn, to
    if tn or to:
        flags.append("tokens")
    res["flags"] = flags
    res["status"] = "same_tokens" if not flags else "differs"
    return res


def main():
    man = json.load(open(common.inventory_path("cards.json")))
    pools = set(common.REST) or {"SILENT", "DEFECT"}
    out = {}
    for e in man["entries"]:
        if e["pool"] not in pools:
            continue
        out[e["class"]] = compare(e["class"])
    json.dump(out, open(common.work_path("cards-silent-defect", "vdelta_raw.json"), "w"), indent=1, sort_keys=True)
    cnt = Counter(v["status"] for v in out.values())
    print(cnt)
    for k, v in sorted(out.items()):
        if v["status"] == "differs":
            parts = [k, "FLAGS=" + ",".join(v["flags"])]
            for s, d in v["struct"].items():
                if not d["same"]:
                    parts.append("%s: new=%r old=%r" % (s, d["new"], d["old"]))
            if not v["cmd_multiset_equal"] or not v["cmd_order_equal"]:
                parts.append("CMD new=%s unawaited=%s old=%s" % (v["cmd_seq_new"], v["cmd_unawaited_new"], v["cmd_seq_old"]))
            if v["token_only_new"] or v["token_only_old"]:
                parts.append("TOK +%s -%s" % (v["token_only_new"], v["token_only_old"]))
            print(" | ".join(parts))
        elif v["status"] == "new":
            print(k, "NEW FILE (no v0.107.1)")


if __name__ == "__main__":
    main()
