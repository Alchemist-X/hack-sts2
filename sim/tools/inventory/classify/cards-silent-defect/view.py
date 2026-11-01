import os as _os
import sys as _sys
_sys.path.insert(0, _os.path.join(_os.path.dirname(_os.path.abspath(__file__)), ".."))
import common  # noqa: E402
import os
import re
import sys

D1 = common.DECOMPILE_ROOT
D0 = common.V107_ROOT

DROP_LINE = [
    re.compile(r"^\s*//"),
    re.compile(r"^\s*using [\w.]+;\s*$"),
    re.compile(r"^\s*[{}]\s*;?\s*$"),
    re.compile(r"^\s*$"),
    re.compile(r"_003C_003E1__state\s*=\s*-[12]\)?\s*;"),
    re.compile(r"^\s*case \d+:\s*$"),
    re.compile(r"^\s*int num = _003C_003E1__state;"),
    re.compile(r"_003C_003Eu__\d+\s*=\s*default"),
    re.compile(r"=\s*_003C_003Eu__\d+;"),
    re.compile(r"AwaitUnsafeOnCompleted"),
    re.compile(r"\.IsCompleted\)"),
    re.compile(r"^\s*num = \(_003C_003E1__state = \d+\);"),
    re.compile(r"^\s*_003C_003Eu__\d+ = \w+;"),
    re.compile(r"^\s*return;\s*$"),
    re.compile(r"^\s*goto IL_\w+;\s*$"),
    re.compile(r"^\s*IL_\w+:\s*$"),
    re.compile(r"^\s*try\s*$"),
    re.compile(r"^\s*catch \(System\.Exception exception\)"),
    re.compile(r"SetException\(exception\)"),
    re.compile(r"SetResult\("),
    re.compile(r"SetStateMachine"),
    re.compile(r"^\s*\[(DebuggerHidden|CompilerGenerated|StructLayout|AsyncStateMachine|IteratorStateMachine|DebuggerBrowsable|DebuggerStepThrough)"),
    re.compile(r"^\s*(public|private) (int|AsyncTaskMethodBuilder|AsyncTaskMethodBuilder<\w+>|TaskAwaiter(<[\w<>, ?]+>)?|ConfiguredTaskAwaitable\S*|PlayerChoiceContext|CardPlay|\w+ _003C_003E4__this) \w+;"),
    re.compile(r"^\s*private (TaskAwaiter|ConfiguredTaskAwaitable)"),
    re.compile(r"^\s*public \w+ _003C_003E4__this;"),
    re.compile(r"^\s*private void MoveNext\(\)"),
    re.compile(r"^\s*private struct _003C\w+ : IAsyncStateMachine"),
    re.compile(r"_003C\w+_003Ed__\d* _003C\w+_003Ed__ = default"),
    re.compile(r"_003C\w+_003Ed__\._003C_003Et__builder = "),
    re.compile(r"_003C\w+_003Ed__\._003C_003E4__this = this;"),
    re.compile(r"_003C\w+_003Ed__\._003C_003E1__state = -1;"),
    re.compile(r"\.Start<_003C"),
    re.compile(r"_003C\w+_003Ed__\._003C_003Et__builder\)\)\.Task;"),
    re.compile(r"^\s*TaskAwaiter(<[\w<>, ?]+>)? val\d*;\s*$"),
    re.compile(r"^\s*ConfiguredTaskAwaitable\S* val\d*;\s*$"),
    re.compile(r"^\s*(val\d*|awaiter\d*)\.GetResult\(\);\s*$"),
    re.compile(r"^\s*\(\((TaskAwaiter|ConfiguredTaskAwaiter)[^)]*\)\(ref val\d*\)\)\.GetResult\(\);\s*$"),
    re.compile(r"^\s*if \(num (!=|==) \d+\)\s*$"),
    re.compile(r"^\s*else\s*$"),
]


def condense(path, show_all=False):
    with open(path, encoding="utf-8-sig") as fh:
        lines = fh.read().split("\n")
    out = []
    for i, ln in enumerate(lines, 1):
        if not show_all and any(p.search(ln) for p in DROP_LINE):
            continue
        s = ln.rstrip()
        s = s.replace("_003C_003E4__this", "THIS")
        s = re.sub(r"\.GetAwaiter\(\)", "", s)
        s = s.replace("System.Collections.Generic.", "").replace("System.Threading.Tasks.", "")
        s = s.replace("_003C_003Ez__ReadOnlySingleElementList", "List1").replace("_003C_003Ez__ReadOnlyArray", "Arr")
        s = re.sub(r"^\t+", lambda m: "  " * len(m.group(0)), s)
        out.append("%4d %s" % (i, s))
    return out


def file_for(cls, tree):
    if tree == 1:
        for d in ("MegaCrit.Sts2.Core.Models.Cards", "MegaCrit.Sts2.Core.Models.Powers", "MegaCrit.Sts2.Core.Models.Orbs",
                  "MegaCrit.Sts2.Core.Models.Relics", "MegaCrit.Sts2.Core.Models.Potions", "MegaCrit.Sts2.Core.Commands",
                  "MegaCrit.Sts2.Core.Models.Enchantments"):
            p = os.path.join(D1, d, cls + ".cs")
            if os.path.exists(p):
                return p
    else:
        for d in ("Models/Cards", "Models/Powers", "Models/Orbs", "Models/Relics", "Models/Potions", "Commands", "Models/Enchantments"):
            p = os.path.join(D0, d, cls + ".cs")
            if os.path.exists(p):
                return p
    return None


def main():
    args = common.REST
    old = "--old" in args
    only_old = "--only-old" in args
    raw = "--raw" in args
    names = [a for a in args if not a.startswith("--")]
    for n in names:
        if not only_old:
            p = file_for(n, 1)
            print("=" * 8, n, "v0.111.0", p.replace(D1 + "/", "") if p else None)
            if p:
                print("\n".join(condense(p, raw)))
        if old or only_old:
            p0 = file_for(n, 0)
            print("-" * 8, n, "v0.107.1", p0.replace(D0 + "/", "") if p0 else "MISSING")
            if p0:
                with open(p0, encoding="utf-8-sig") as fh:
                    txt = fh.read().split("\n")
                for i, ln in enumerate(txt, 1):
                    if re.match(r"^\s*using [\w.]+;\s*$", ln) or not ln.strip() or re.match(r"^\s*[{}]\s*$", ln):
                        continue
                    print("%4d %s" % (i, re.sub(r"^\t+", lambda m: "  " * len(m.group(0)), ln.rstrip())))


if __name__ == "__main__":
    main()
