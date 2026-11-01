"""Lightweight structural scanner for decompiled (ILSpy-style) C# files.

Stdlib only. Not a C# parser: it masks comments and literals, then splits type
bodies into members by brace/paren depth. Good enough for one-type-per-file
decompiled model classes.
"""

import bisect
import re

MODIFIERS = {
    "public", "private", "protected", "internal", "static", "override", "virtual",
    "abstract", "sealed", "readonly", "const", "async", "extern", "new", "unsafe",
    "volatile", "partial", "required", "file",
}
TYPE_KEYWORDS = ("class", "struct", "record", "interface", "enum")
STRING_PLACEHOLDER_RE = re.compile(r'"STR(\d+)"')
TYPE_DECL_RE = re.compile(
    r"^(?P<mods>(?:\w+\s+)*)(?P<kw>class|struct|record|interface|enum)\s+(?P<name>\w+)(?P<gen><[^>{]*>)?"
    r"(?P<rest>[^{]*)$",
    re.S,
)
STATE_MACHINE_RE = re.compile(r"^_003C(\w+?)_003Ed__\d+$")
LAMBDA_RE = re.compile(r"^_003C(\w+?)_003Eb__\w+$")
LOCAL_FUNC_RE = re.compile(r"^_003C(\w+?)_003Eg__(\w+?)(?:_007C\w*)?$")
DISPLAY_CLASS_RE = re.compile(r"^_003C_003Ec(?:__DisplayClass\w+)?$")


class Literal:
    __slots__ = ("index", "line", "kind", "value")

    def __init__(self, index, line, kind, value):
        self.index = index
        self.line = line
        self.kind = kind
        self.value = value


def _unescape_regular(s):
    out = []
    i = 0
    n = len(s)
    simple = {"n": "\n", "t": "\t", "r": "\r", "0": "\0", "\\": "\\", '"': '"', "'": "'", "a": "\a",
              "b": "\b", "f": "\f", "v": "\v"}
    while i < n:
        c = s[i]
        if c == "\\" and i + 1 < n:
            d = s[i + 1]
            if d in simple:
                out.append(simple[d])
                i += 2
                continue
            if d == "u" and i + 5 < n:
                try:
                    out.append(chr(int(s[i + 2:i + 6], 16)))
                    i += 6
                    continue
                except ValueError:
                    pass
            out.append(c)
            i += 1
            continue
        out.append(c)
        i += 1
    return "".join(out)


class Masker:
    """Removes comments and replaces string/char literals with placeholders.

    Output keeps every newline, so line numbers are unchanged. A string literal
    becomes "STR<k>" where k indexes `literals`; a char literal becomes ' '.
    """

    def __init__(self, text):
        self.text = text
        self.n = len(text)
        self.literals = []
        self._line_starts = [0]
        for m in re.finditer("\n", text):
            self._line_starts.append(m.end())

    def line_of(self, index):
        return bisect.bisect_right(self._line_starts, index)

    def run(self):
        text = self.text
        n = self.n
        out = []
        i = 0
        while i < n:
            c = text[i]
            if c == "/" and i + 1 < n and text[i + 1] == "/":
                j = text.find("\n", i)
                if j == -1:
                    j = n
                i = j
                continue
            if c == "/" and i + 1 < n and text[i + 1] == "*":
                j = text.find("*/", i + 2)
                if j == -1:
                    j = n - 2
                out.append("\n" * text.count("\n", i, j + 2))
                i = j + 2
                continue
            lit = self._try_literal(i) if c in "\"'@$" else None
            if lit is not None:
                end, kind, value = lit
                nl = text.count("\n", i, end)
                if kind == "char":
                    out.append("' '")
                else:
                    k = len(self.literals)
                    self.literals.append(Literal(k, self.line_of(i), kind, value))
                    out.append('"STR%d"' % k)
                if nl:
                    out.append("\n" * nl)
                i = end
                continue
            out.append(c)
            i += 1
        return "".join(out)

    def _try_literal(self, i):
        text = self.text
        n = self.n
        c = text[i]
        if c == "'":
            if i > 0 and (text[i - 1].isalnum() or text[i - 1] == "_"):
                return None
            j = i + 1
            if j < n and text[j] == "\\":
                j += 2
                while j < n and text[j] != "'":
                    j += 1
            else:
                j += 1
            if j < n and text[j] == "'":
                return (j + 1, "char", text[i + 1:j])
            return None
        verbatim = False
        interp = False
        k = i
        while k < n and text[k] in "@$" and k - i < 3:
            if text[k] == "@":
                verbatim = True
            else:
                interp = True
            k += 1
        if k >= n or text[k] != '"':
            return None
        if k > i:
            prev = text[i - 1] if i > 0 else ""
            if prev.isalnum() or prev == "_":
                return None
        if text.startswith('"""', k):
            q = k
            while q < n and text[q] == '"':
                q += 1
            run = q - k
            close = text.find('"' * run, q)
            if close == -1:
                close = n - run
            return (close + run, "raw", text[q:close])
        j = k + 1
        buf = []
        while j < n:
            ch = text[j]
            if verbatim:
                if ch == '"':
                    if j + 1 < n and text[j + 1] == '"':
                        buf.append('"')
                        j += 2
                        continue
                    break
            else:
                if ch == "\\":
                    buf.append(text[j:j + 2])
                    j += 2
                    continue
                if ch == '"':
                    break
                if ch == "\n":
                    break
            if interp and ch == "{":
                if j + 1 < n and text[j + 1] == "{":
                    buf.append("{")
                    j += 2
                    continue
                j = self._skip_hole(j + 1)
                buf.append("{}")
                continue
            if interp and ch == "}" and j + 1 < n and text[j + 1] == "}":
                buf.append("}")
                j += 2
                continue
            buf.append(ch)
            j += 1
        raw = "".join(buf)
        value = raw if verbatim else _unescape_regular(raw)
        kind = ("interp" if interp else "string") + ("_verbatim" if verbatim else "")
        return (j + 1, kind, value)

    def _skip_hole(self, j):
        text = self.text
        n = self.n
        depth = 0
        while j < n:
            ch = text[j]
            lit = self._try_literal(j) if ch in "\"'@$" else None
            if lit is not None:
                j = lit[0]
                continue
            if ch in "([{":
                depth += 1
            elif ch in ")]":
                depth -= 1
            elif ch == "}":
                if depth == 0:
                    return j + 1
                depth -= 1
            j += 1
        return n


class Member:
    __slots__ = ("kind", "name", "modifiers", "head", "start", "end", "decl_start", "line", "attr_line",
                 "end_line", "type_decl", "terminator", "params")

    def __init__(self):
        self.kind = None
        self.name = None
        self.modifiers = set()
        self.head = ""
        self.start = 0
        self.end = 0
        self.decl_start = 0
        self.line = 0
        self.attr_line = 0
        self.end_line = 0
        self.type_decl = None
        self.terminator = None
        self.params = ""

    @property
    def access(self):
        for a in ("public", "protected", "internal", "private"):
            if a in self.modifiers:
                return a
        return "private"

    @property
    def is_override(self):
        return "override" in self.modifiers

    def __repr__(self):
        return "Member(%s %s L%d-%d)" % (self.kind, self.name, self.line, self.end_line)


class TypeDecl:
    __slots__ = ("keyword", "name", "modifiers", "bases", "line", "end_line", "body_start", "body_end",
                 "members", "parent")

    def __init__(self):
        self.keyword = None
        self.name = None
        self.modifiers = set()
        self.bases = []
        self.line = 0
        self.end_line = 0
        self.body_start = 0
        self.body_end = 0
        self.members = []
        self.parent = None

    def find(self, name, kind=None):
        return [m for m in self.members if m.name == name and (kind is None or m.kind == kind)]

    def __repr__(self):
        return "TypeDecl(%s %s : %s)" % (self.keyword, self.name, ",".join(self.bases))


def _match_close(s, i, open_ch, close_ch):
    depth = 0
    n = len(s)
    while i < n:
        c = s[i]
        if c == open_ch:
            depth += 1
        elif c == close_ch:
            depth -= 1
            if depth == 0:
                return i
        i += 1
    return -1


def _strip_attributes(head):
    h = head.lstrip()
    while h.startswith("["):
        close = _match_close(h, 0, "[", "]")
        if close == -1:
            break
        h = h[close + 1:].lstrip()
    return h


def _split_top_commas(s):
    parts = []
    depth = 0
    cur = []
    for c in s:
        if c in "<([{":
            depth += 1
        elif c in ">)]}":
            depth -= 1
        if c == "," and depth == 0:
            parts.append("".join(cur).strip())
            cur = []
        else:
            cur.append(c)
    tail = "".join(cur).strip()
    if tail:
        parts.append(tail)
    return parts


def split_args(s):
    """Split an argument list (no outer parens) on top-level commas."""
    parts = []
    depth = 0
    cur = []
    for c in s:
        if c in "([{":
            depth += 1
        elif c in ")]}":
            depth -= 1
        if c == "," and depth == 0:
            parts.append("".join(cur).strip())
            cur = []
        else:
            cur.append(c)
    tail = "".join(cur).strip()
    if tail or parts:
        parts.append(tail)
    return parts


class CsFile:
    def __init__(self, path, rel):
        self.path = path
        self.rel = rel
        with open(path, "r", encoding="utf-8-sig", errors="replace") as fh:
            self.text = fh.read()
        self.lines = self.text.split("\n")
        masker = Masker(self.text)
        self.masked = masker.run()
        self.literals = masker.literals
        self.mlines = self.masked.split("\n")
        self._line_starts = [0]
        for m in re.finditer("\n", self.masked):
            self._line_starts.append(m.end())
        self.types = self._parse_top_types()

    @property
    def line_count(self):
        n = len(self.lines)
        if n and self.lines[-1] == "":
            n -= 1
        return n

    def line_of(self, index):
        return bisect.bisect_right(self._line_starts, index)

    def literal(self, k):
        return self.literals[int(k)].value

    def resolve_strings(self, s):
        return STRING_PLACEHOLDER_RE.sub(lambda m: '"' + self.literal(m.group(1)) + '"', s)

    def string_values(self, s):
        return [self.literal(m.group(1)) for m in STRING_PLACEHOLDER_RE.finditer(s)]

    def body_of(self, member):
        """Masked text after the member head (body or expression), without the head."""
        t = self.masked[member.decl_start:member.end]
        return t[len(member.head):]

    def main_type(self):
        return self.types[0] if self.types else None

    def _parse_top_types(self):
        s = self.masked
        types = []
        for m in re.finditer(r"(?m)^(?:\w+[ \t]+)*(?:class|struct|record|interface|enum)[ \t]+\w+", s):
            line_start = m.start()
            brace = s.find("{", m.end())
            semi = s.find(";", m.end())
            if brace == -1 or (semi != -1 and semi < brace and "record" not in m.group(0)):
                continue
            close = _match_close(s, brace, "{", "}")
            if close == -1:
                continue
            td = self._make_type(s[line_start:brace], line_start, brace, close)
            if td is not None:
                types.append(td)
        return types

    def _make_type(self, head, start, brace, close, parent=None):
        h = _strip_attributes(head)
        m = TYPE_DECL_RE.match(h.strip())
        if not m:
            return None
        td = TypeDecl()
        td.keyword = m.group("kw")
        td.name = m.group("name")
        td.modifiers = set(m.group("mods").split())
        rest = m.group("rest").strip()
        bases = []
        if rest.startswith(":"):
            rest = rest[1:]
            rest = re.split(r"\bwhere\b", rest)[0]
            bases = [re.sub(r"\s+", " ", b) for b in _split_top_commas(rest)]
        td.bases = bases
        td.line = self.line_of(start + (len(head) - len(head.lstrip())))
        td.end_line = self.line_of(close)
        td.body_start = brace + 1
        td.body_end = close
        td.parent = parent
        td.members = self._split_members(td)
        return td

    def _split_members(self, td):
        s = self.masked
        i = td.body_start
        end = td.body_end
        segs = []
        seg_start = None
        depth = 0
        paren = 0
        saw_eq = False
        while i < end:
            c = s[i]
            if seg_start is None:
                if c in " \t\r\n;":
                    i += 1
                    continue
                seg_start = i
                saw_eq = False
                depth = 0
                paren = 0
            if c == "{":
                depth += 1
            elif c == "}":
                depth -= 1
                if depth == 0 and paren == 0:
                    j = i + 1
                    while j < end and s[j] in " \t\r\n":
                        j += 1
                    nxt = s[j] if j < end else ""
                    if nxt == "=" and s[j:j + 2] != "=>":
                        saw_eq = True
                    elif saw_eq and nxt in (";", ")", ",", ".", "?", "["):
                        pass
                    else:
                        segs.append((seg_start, i + 1, "}"))
                        seg_start = None
            elif c == "(":
                paren += 1
            elif c == ")":
                paren -= 1
            elif c == ";" and depth == 0 and paren == 0:
                segs.append((seg_start, i + 1, ";"))
                seg_start = None
            elif c == "=" and depth == 0 and paren == 0:
                prev = s[i - 1] if i > 0 else ""
                nxt = s[i + 1] if i + 1 < end else ""
                if nxt == ">" or (prev not in "=!<>" and nxt != "="):
                    saw_eq = True
            i += 1
        if seg_start is not None and s[seg_start:end].strip():
            segs.append((seg_start, end, None))
        members = []
        for a, b, term in segs:
            mem = self._classify_member(td, a, b, term)
            if mem is not None:
                members.append(mem)
        return members

    def _classify_member(self, td, a, b, term):
        s = self.masked
        seg = s[a:b]
        stripped = _strip_attributes(seg)
        decl_start = b - len(stripped)
        lead_ws = len(stripped) - len(stripped.lstrip())
        decl_start += lead_ws
        stripped = stripped.lstrip()
        mem = Member()
        mem.start = a
        mem.end = b
        mem.decl_start = decl_start
        mem.attr_line = self.line_of(a)
        mem.line = self.line_of(decl_start)
        mem.end_line = self.line_of(b - 1)
        mem.terminator = term
        tm = re.match(r"^(?:\w+\s+)*(class|struct|record|interface|enum)\s+\w+", stripped)
        if tm:
            brace = s.find("{", decl_start)
            if brace != -1 and brace < b:
                close = _match_close(s, brace, "{", "}")
                nested = self._make_type(s[decl_start:brace], decl_start, brace, close, parent=td)
                if nested is not None:
                    mem.kind = "type"
                    mem.name = nested.name
                    mem.type_decl = nested
                    mem.modifiers = nested.modifiers
                    mem.head = s[decl_start:brace]
                    return mem
        head_end = len(stripped)
        depth_p = 0
        depth_a = 0
        k = 0
        while k < len(stripped):
            ch = stripped[k]
            if ch == "(":
                depth_p += 1
            elif ch == ")":
                depth_p -= 1
            elif ch == "<":
                depth_a += 1
            elif ch == ">" and depth_a > 0 and stripped[k - 1:k + 1] != "=>":
                depth_a -= 1
            if depth_p == 0:
                if ch == "{" or ch == ";":
                    head_end = k
                    break
                if ch == "=" and k + 1 < len(stripped):
                    nx = stripped[k + 1]
                    pv = stripped[k - 1] if k else ""
                    if nx == ">":
                        head_end = k
                        break
                    if nx != "=" and pv not in "=!<>":
                        head_end = k
                        break
            k += 1
        head = stripped[:head_end]
        mem.head = head
        words = re.findall(r"[A-Za-z_][A-Za-z0-9_]*", head.split("(")[0])
        mem.modifiers = set(w for w in words if w in MODIFIERS)
        h = head.strip()
        if re.search(r"\bevent\b", h.split("(")[0]):
            mem.kind = "event"
            nm = re.findall(r"(\w+)\s*$", h)
            mem.name = nm[0] if nm else None
            return mem
        if re.search(r"\bdelegate\b", h.split("(")[0]):
            mem.kind = "delegate"
            dm = re.search(r"(\w+)\s*(?:<[^()]*>)?\s*\(", h)
            mem.name = dm.group(1) if dm else None
            return mem
        if re.search(r"\boperator\b", h):
            mem.kind = "operator"
            om = re.search(r"\boperator\s*([^\s(]+)", h)
            mem.name = "operator" + (om.group(1) if om else "")
            return mem
        if re.search(r"\bthis\s*\[", h):
            mem.kind = "indexer"
            mem.name = "this[]"
            return mem
        pm = None
        depth = 0
        for mm in re.finditer(r"[<>(]", h):
            ch = mm.group(0)
            if ch == "<":
                depth += 1
            elif ch == ">":
                depth -= 1
            elif ch == "(" and depth == 0:
                pm = mm.start()
                break
        if pm is not None:
            before = h[:pm].rstrip()
            before = re.sub(r"<[^<>]*(?:<[^<>]*>[^<>]*)*>\s*$", "", before).rstrip()
            nm = re.search(r"(~?\w+)$", before)
            name = nm.group(1) if nm else None
            mem.name = name
            close = _match_close(h, pm, "(", ")")
            mem.params = h[pm + 1:close] if close != -1 else h[pm + 1:]
            nonmod = [w for w in re.findall(r"~?[A-Za-z_][A-Za-z0-9_.]*", before) if w not in MODIFIERS]
            if name == td.name and len(nonmod) <= 1:
                mem.kind = "ctor"
            elif name and name.startswith("~"):
                mem.kind = "dtor"
            else:
                mem.kind = "method"
            return mem
        nm = re.search(r"(\w+)\s*$", h)
        mem.name = nm.group(1) if nm else None
        rest = stripped[head_end:head_end + 2]
        if rest.startswith("=>") or rest.startswith("{"):
            mem.kind = "property"
        else:
            if "const" in mem.modifiers:
                mem.kind = "const"
            else:
                mem.kind = "field"
        return mem


def iter_types(td):
    yield td
    for m in td.members:
        if m.kind == "type":
            for sub in iter_types(m.type_decl):
                yield sub


def generated_owner(name):
    """Map a compiler-generated member/type name to the source method it belongs to."""
    if name is None:
        return None
    m = STATE_MACHINE_RE.match(name)
    if m:
        return m.group(1)
    m = LAMBDA_RE.match(name)
    if m:
        return m.group(1)
    m = LOCAL_FUNC_RE.match(name)
    if m:
        return m.group(1)
    return None


def balanced_call_args(s, open_index):
    """s[open_index] == '('; return (inner_text, close_index)."""
    close = _match_close(s, open_index, "(", ")")
    if close == -1:
        return None, -1
    return s[open_index + 1:close], close
