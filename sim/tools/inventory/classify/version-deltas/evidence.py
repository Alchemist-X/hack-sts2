"""Mechanical evidence lines for a v0.107.1 -> v0.111.0 file delta (stdlib only, deterministic).

find(lines111, lines107, hints, cls) returns up to MAX_LINES line numbers per tree and the method used:

  token-diff  lines holding tokens whose count differs between the two files, ranked by the hint tokens
              (classifier fingerprint diffs, diff-gate values/enums/calls, identifiers and "a -> b" values
              named in the classifier note);
  difflib     the first differing logical line when both files hold the same token multiset;
  anchor      the corresponding member on the side that has no differing line (the enclosing method or
              property of the other side's evidence); class-anchor when that member is absent (class declaration).

Decompiler plumbing (async state machines, awaiters, hoisted-local mangling, ReadOnlyArray wrappers, C#
keywords, generated local names) is dropped before comparing, so lines that differ only in decompile style
rank below lines with a rule token.
"""

import collections
import difflib
import re

MAX_LINES = 3
V111, V107, BOTH = "v0.111.0", "v0.107.1", "both"

TOKEN = re.compile(r'"(?:[^"\\\n]|\\.)*"|\d+(?:\.\d+)?[fFmMdD]?\b|[A-Za-z_][A-Za-z0-9_]*')
HINT_TOKEN = re.compile(r'"[^"]*"|\d+(?:\.\d+)?|[A-Za-z_][A-Za-z0-9_]*')
HOISTED = re.compile(r'_003C(\w+?)_003E5__\d+')
MANGLED = re.compile(r'_003C\w*?_003E\w*')
VERSION = re.compile(r'v?0\.1\d\d\.\d|v0\.1\d\d|v1\d\d')
KEYWORDS = set("""abstract as async await base bool break byte case catch char checked class const continue decimal default
delegate do double else enum event explicit extern false finally fixed float for foreach get goto if implicit in int interface
internal is lock long namespace new null object operator out override params private protected public readonly record ref return
sbyte sealed set short sizeof stackalloc static string struct switch this throw true try typeof uint ulong unchecked unsafe ushort
using var virtual void volatile while when where yield value global nameof""".split())
PLUMBING = set("""GetAwaiter GetResult IsCompleted AwaitUnsafeOnCompleted AwaitOnCompleted SetResult SetException SetStateMachine
MoveNext Start AsyncTaskMethodBuilder AsyncVoidMethodBuilder IAsyncStateMachine TaskAwaiter Task CompilerGenerated
DebuggerHidden DebuggerStepThrough AsyncStateMachine IteratorStateMachine System Collections Generic IEnumerable IEnumerator
IDisposable Dispose Threading Tasks Linq Enumerable op_Implicit op_Explicit CS_0024 RuntimeHelpers _002Ector ToList ToArray
Func Action List Exception InvalidOperationException Current GetEnumerator""".split())
HINT_STOP = set("""CMD GEN ENUM STR HOOK VAR PROP CALL NEW LIT ID IDS TOK only both same note now also with without from into
the and or not but for per via when then than each every all any one two three""".split())
LOCAL = re.compile(r'^(?:val|num|flag|array|list|enumerator|obj|text|item|result|task|awaiter|ex|current|wrap|locals)\d*$|^[a-z]$')
PLUMBING_LINE = re.compile(r'_003C_003E1__state|_003C_003Et__builder|_003C_003Eu__|SetStateMachine|^\s*goto\b|^\s*IL_\w+:'
                           r'|^\s*case -?\d+:\s*$|^\s*default:\s*$|\bnum\d*\s*[!=<>]=?\s*-?\d|\bnum\d* = \(|//IL_')
PRESENTATION = re.compile(r'Vfx|Sfx|Anim|Spine|Tween|Shake|Particle|Audio|Sound|Camera|Rumble|Haptic|Modulate|Visuals?\b|Node2D|'
                          r'^Node$|GlobalPosition|^Position$|^Scale$|Vector2|Color|Flash|Track|Bestiary|HoverTip|Loc(?:String|Lookup)')
SKIP_LINE = re.compile(r'^\s*(?:using\s|namespace\s|//|#|\[[^\]]*\]\s*$|[{}();,\s]*$)')
DECL = re.compile(r'^\s*(?:(?:public|private|protected|internal|static|readonly)\s+)*([A-Za-z_][\w.]*)(?:<[^;=()]*>)?(?:\[\])?\??\s+[A-Za-z_]\w*;\s*$')
DECL_NOT = {"return", "throw", "goto", "yield", "await", "else", "case", "new", "break", "continue"}
MEMBER = re.compile(r'^\s*(?:(?:public|private|protected|internal|static|override|virtual|abstract|sealed|async|readonly|new|'
                    r'extern|unsafe|partial)\s+)+[\w<>\[\],.?\s]*?\b(\w+)\s*(?:\(|=>|\{|$)')
STATE_MACHINE = re.compile(r'\b(?:struct|class)\s+_003C(\w+)_003Ed__\d+')
WEAK = {"0", "1"}


def norm_num(t):
    t = t.rstrip("fFmMdD")
    if "." in t:
        return format(round(float(t), 6), "g")
    return str(int(t))


def _ident_ok(t):
    return t not in KEYWORDS and t not in PLUMBING and not LOCAL.match(t)


def logic_lines(lines):
    """[(line number, [tokens])] for the lines that carry rule-relevant tokens."""
    out = []
    for i, raw in enumerate(lines, 1):
        if SKIP_LINE.match(raw) or PLUMBING_LINE.search(raw):
            continue
        d = DECL.match(raw)
        if d and d.group(1) not in DECL_NOT:
            continue
        line = MANGLED.sub(" ", HOISTED.sub(lambda m: " " + m.group(1) + " ", raw)).replace("global::", "")
        toks = []
        for m in TOKEN.finditer(line):
            t = m.group(0)
            if t[0].isdigit():
                t = norm_num(t)
            elif t[0] != '"' and not _ident_ok(t):
                continue
            toks.append(t)
        if toks:
            out.append((i, toks))
    return out


def hint_tokens(text, camel_only=False):
    out = set()
    for m in HINT_TOKEN.finditer(VERSION.sub(" ", text)):
        t = m.group(0)
        if t[0] == '"':
            out.add(t)
        elif t[0].isdigit():
            out.add(norm_num(t))
        elif _ident_ok(t) and t not in HINT_STOP and (not camel_only or any(c.isupper() for c in t)):
            out.add(t)
    return out


class Hints(object):
    def __init__(self):
        self.side = {V111: set(), V107: set(), BOTH: set()}

    def add(self, side, text, camel_only=False):
        self.side[side] |= hint_tokens(text, camel_only)

    def add_struct(self, obj, side=BOTH):
        """Walk a fingerprint diff: keys naming 107 / 111 switch the side of everything below them."""
        if isinstance(obj, dict):
            for k in sorted(obj):
                v = obj[k]
                if "107" in k:
                    self.add_struct(v, V107)
                elif "111" in k:
                    self.add_struct(v, V111)
                else:
                    self.add(side, k)
                    self.add_struct(v, side)
        elif isinstance(obj, (list, tuple)):
            for x in obj:
                self.add_struct(x, side)
        elif isinstance(obj, str):
            if "->" in obj and side == BOTH:
                self.add_arrow(obj)
            else:
                self.add(side, obj)

    def add_arrow(self, text):
        for clause in re.split(r"[;,()]", text):
            if "->" not in clause:
                continue
            left, right = clause.split("->", 1)
            self.add(V107, left)
            self.add(V111, right)

    def add_note(self, note):
        if not note:
            return
        self.add_arrow(note)
        self.add(BOTH, note, camel_only=True)


def _plain(t, ignore):
    """A token whose count difference alone can be evidence: not a local name, the class itself, or presentation."""
    if t in WEAK or t in ignore:
        return False
    if t[0] == '"' or t[0].isdigit():
        return True
    return t[0].isupper() and not PRESENTATION.search(t)


def _score(toks, side_hints, both_hints, c_side, c_other, ignore=()):
    s, spec, hinted = 0, False, False
    plain = 0
    present = any(t[0].isalpha() and PRESENTATION.search(t) for t in toks)
    for t in set(toks):
        h, hb = t in side_hints, t in both_hints
        ok = _plain(t, ignore) and not (present and t[0].isdigit())
        ss = c_side[t] > c_other[t] and (h or hb or ok)
        if t in WEAK and not (h or hb):
            continue
        if ss:
            spec = True
        if h:
            hinted = True
            s += 6 + (3 if ss else 0)
        elif hb:
            s += 2 + (3 if ss else 0)
        elif ss:
            plain += 1
    return s + min(plain, 3), spec, hinted


def _pick(cands):
    cands = sorted(cands, key=lambda x: (-x[0], x[1]))[:MAX_LINES]
    return sorted(i for _, i in cands)


def _token_diff(L, c_side, c_other, side_hints, both_hints, ignore=()):
    """(picked line numbers, whether any line holds a differing token)."""
    scored, any_spec = [], False
    for i, toks in L:
        sc, spec, hinted = _score(toks, side_hints, both_hints, c_side, c_other, ignore)
        any_spec = any_spec or spec or hinted
        if sc > 0:
            scored.append((sc, i))
    best = [x for x in scored if x[0] >= 2] or scored
    return _pick(best), any_spec


def _difflib(L111, L107):
    a = [" ".join(t) for _, t in L107]
    b = [" ".join(t) for _, t in L111]
    sm = difflib.SequenceMatcher(None, a, b, autojunk=False)
    for tag, i1, i2, j1, j2 in sm.get_opcodes():
        if tag == "equal":
            continue
        e107 = [L107[k][0] for k in range(i1, min(i2, i1 + 2))] or ([L107[i1 - 1][0]] if i1 > 0 else [])
        e111 = [L111[k][0] for k in range(j1, min(j2, j1 + 2))] or ([L111[j1 - 1][0]] if j1 > 0 else [])
        return e111, e107
    return [], []


def member_of(lines, lineno):
    """Name of the member enclosing `lineno` (the state machine's method name for v0.111.0 MoveNext bodies)."""
    for i in range(lineno - 1, -1, -1):
        raw = lines[i]
        m = STATE_MACHINE.search(raw)
        if m:
            return m.group(1)
        if raw.startswith("\t") and not raw.startswith("\t\t"):
            m = MEMBER.match(raw)
            if m and m.group(1) not in KEYWORDS:
                return m.group(1)
    return None


def member_line(lines, name):
    """Line of the class-level member `name` (or its v0.111.0 state machine), None when absent."""
    if not name:
        return None
    sm = re.compile(r'\b(?:struct|class)\s+_003C%s_003Ed__\d+' % re.escape(name))
    for i, raw in enumerate(lines, 1):
        if raw.startswith("\t") and not raw.startswith("\t\t"):
            m = MEMBER.match(raw)
            if m and m.group(1) == name:
                return i
    for i, raw in enumerate(lines, 1):
        if sm.search(raw):
            return i
    return None


def class_line(lines, cls):
    pat = re.compile(r'\b(?:class|struct|record)\s+%s\b' % re.escape(cls))
    for i, raw in enumerate(lines, 1):
        if pat.search(raw):
            return i
    return 1


def find(lines111, lines107, hints, cls):
    """({tree: [line numbers]}, method)."""
    L111, L107 = logic_lines(lines111), logic_lines(lines107)
    c111 = collections.Counter(t for _, ts in L111 for t in ts)
    c107 = collections.Counter(t for _, ts in L107 for t in ts)
    both = hints.side[BOTH]
    ignore = {cls, cls[:1].lower() + cls[1:]}
    e111, d111 = _token_diff(L111, c111, c107, hints.side[V111] - ignore, both - ignore, ignore)
    e107, d107 = _token_diff(L107, c107, c111, hints.side[V107] - ignore, both - ignore, ignore)
    method = "token-diff"
    if not (d111 or d107):
        f111, f107 = _difflib(L111, L107)
        if f111 or f107:
            e111, e107, method = f111, f107, "difflib"
    if e111 and not e107:
        ln = member_line(lines107, member_of(lines111, e111[0]))
        e107 = [ln or class_line(lines107, cls)]
        method += "+anchor" if ln else "+class-anchor"
    elif e107 and not e111:
        ln = member_line(lines111, member_of(lines107, e107[0]))
        e111 = [ln or class_line(lines111, cls)]
        method += "+anchor" if ln else "+class-anchor"
    elif not e111 and not e107:
        e111, e107 = [class_line(lines111, cls)], [class_line(lines107, cls)]
        method = "class-declaration"
    return {V111: e111, V107: e107}, method
