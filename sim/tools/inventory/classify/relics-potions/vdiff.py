import os as _os
import sys as _sys
_sys.path.insert(0, _os.path.join(_os.path.dirname(_os.path.abspath(__file__)), ".."))
import common  # noqa: E402
import re, os, sys, json
D111 = common.DECOMPILE_ROOT
D107 = common.V107_ROOT
KINDS = {'relics': ('MegaCrit.Sts2.Core.Models.Relics', 'Models/Relics'),
         'potions': ('MegaCrit.Sts2.Core.Models.Potions', 'Models/Potions')}

UI_WORDS = re.compile(r'(Vfx|Sfx|HoverTip|Flash|Preview|Wait|TalkCmd|ThinkCmd|Anim|Color|Colors|Node|Godot|Tween|Texture|Icon|Skin|Splash|Scene|Preload|Spine|Shake|Camera|Visual|Display|Title|LocString|Loc|Text|Sentry|Log|Instance|GlobalUi|TopBar|Screen|Container|Child|Position|Control|Variant|StringName|Shader|Glow|Puff|Smoke|Fire|Gaseous|Impact|Ground|Creature Node)')
NOISE = set('''Enumerable IEnumerable IEnumerator Enumerator TaskAwaiter Task Tasks System Collections Generic List IReadOnlyList
IReadOnlyCollection ReadOnlySingleElementList ReadOnlyArray ReadOnlyList Func Action Predicate IDisposable Dispose MoveNext Current GetEnumerator
GetAwaiter GetResult IsCompleted AsyncTaskMethodBuilder AwaitUnsafeOnCompleted SetResult SetException SetStateMachine IAsyncStateMachine
Exception CompilerGenerated DebuggerHidden AsyncStateMachine StructLayout LayoutKind Start Builder ToList ToArray Where Select Any All Contains
FirstOrDefault First LastOrDefault Last Count Take OfType Concat Union OrderBy ElementAt Distinct Except ToHashSet HashSet Dictionary
Add AddRange Remove Insert Clear TryGetValue ContainsKey Values Keys Span CollectionsMarshal SetCount AsSpan Math Max Min decimal Decimal
op_Implicit Implicit Unknown IL String Join Empty Array Object Equals CompletedTask Interlocked CompareExchange Delegate Combine
Override Assert AssertMutable base this Instance NotNull ArgumentNullException InvalidOperationException ArgumentOutOfRangeException
Type GetType TrueForAll IList ICollection IReadOnlySet ISet Nullable Value HasValue GetValueOrDefault RuntimeHelpers InitializeArray
RuntimeFieldHandle StringComparer Ordinal IComparer Comparer ValueTuple Item1 Item2 Mathf FloorToInt Volatile ExtensionAttribute
Create Threading CardPileAddResult DamageResult SummonResult PotionProcureResult CardCreationResult Enum CardModel Creature Player
PlayerChoiceContext ICombatState CombatSide Control Node Length Collections IEnumerator'''.split())

def strip_comments(s):
    s = re.sub(r'//.*', '', s)
    s = re.sub(r'/\*.*?\*/', '', s, flags=re.S)
    return s

def norm_text(s):
    s = strip_comments(s)
    s = re.sub(r'^\s*using .*$', '', s, flags=re.M)
    s = re.sub(r'^\s*namespace .*$', '', s, flags=re.M)
    s = s.replace('_003C_003E4__this', 'this')
    s = re.sub(r'_003C(\w*?)_003E\w*', r'\1', s)
    s = s.replace('global::', '')
    return s

def features(text, code_override=None):
    t = norm_text(text)
    f = {}
    props = {}
    for m in re.finditer(r'public override (?:[\w<>?\[\]., ]+) (\w+) => ([^;]+);', t):
        props[m.group(1)] = re.sub(r'\s+', ' ', m.group(2)).strip()
    f['props'] = props
    cv = re.search(r'CanonicalVars\s*=>(.*?);\s*\n', t, flags=re.S)
    vars_ = []
    if cv:
        for m in re.finditer(r'new (\w+(?:<\w+>)?)\(([^()]*)\)', cv.group(1)):
            if 'ReadOnly' in m.group(1):
                continue
            args = re.sub(r'(\d)m\b', r'\1', m.group(2)).replace(' ', '')
            vars_.append(m.group(1) + '(' + args + ')')
    f['vars'] = sorted(vars_)
    consts = {}
    for m in re.finditer(r'(?:const|static readonly) [\w<>\[\]]+ (\w+) = ([^;]+);', t):
        consts[m.group(1)] = re.sub(r'\s+', ' ', m.group(2)).strip()
    f['consts'] = consts
    calls = set()
    for m in re.finditer(r'\b(\w*Cmd|Hook|CardFactory|PotionFactory|RelicFactory|CardSelectCmd)\.(\w+)(<\w+>)?', t):
        if m.group(1) in ('VfxCmd', 'SfxCmd', 'TalkCmd', 'ThinkCmd') or (m.group(1) == 'Cmd' and m.group(2) in ('Wait', 'CustomScaledWait')):
            continue
        if m.group(1) == 'CardCmd' and m.group(2).startswith('Preview'):
            continue
        calls.add(m.group(1) + '.' + m.group(2) + (m.group(3) or ''))
    for m in re.finditer(r'\b(Rng|PlayerRng)\.(\w+)', t):
        if m.group(2) not in ('Chaotic',):
            calls.add('rng:' + m.group(1) + '.' + m.group(2))
    for m in re.finditer(r'ModelDb\.(\w+)<(\w+)>', t):
        calls.add('modeldb:' + m.group(1) + '<' + m.group(2) + '>')
    for m in re.finditer(r'(?:Apply|Remove|TryToProcure|AddCurseToDeck|CreateCard|Channel|Enchant|TransformTo|AddPet|GetPower|HasPower|AddToCombatAndPreview)<(\w+)>', t):
        calls.add('generic:' + m.group(1))
    f['calls'] = sorted(calls)
    code_lines = []
    for line in t.split('\n'):
        if re.search(r'_003C_003E1__state|_003C_003Eu__|num = \(|case \d+:|state = -|AwaitUnsafe|IL_|<>1__state|ref this|SetStateMachine|\.Start<', line):
            continue
        code_lines.append(line)
    code = '\n'.join(code_lines) if code_override is None else code_override
    nums = []
    for m in re.finditer(r'(?<![\w.])(-?\d+(?:\.\d+)?)(m|f|u|uL|L)?\b', code):
        v = m.group(1)
        nums.append(v.rstrip('0').rstrip('.') if '.' in v else v)
    f['nums'] = sorted(nums)
    idents = set(re.findall(r'\b([A-Z][A-Za-z0-9]+)\b', code))
    idents = {i for i in idents if i not in NOISE}
    f['idents'] = sorted(idents)
    return f

def classify(f111, f107):
    if f107 is None:
        return 'new', []
    diffs = []
    for k in ('props', 'vars', 'consts', 'calls'):
        if f111[k] != f107[k]:
            diffs.append(k)
    from collections import Counter
    c1, c2 = Counter(f111['nums']), Counter(f107['nums'])
    num_add = sorted((c1 - c2).elements())
    num_rem = sorted((c2 - c1).elements())
    id_add = sorted(set(f111['idents']) - set(f107['idents']))
    id_rem = sorted(set(f107['idents']) - set(f111['idents']))
    sig_add = [i for i in id_add if not UI_WORDS.search(i)]
    sig_rem = [i for i in id_rem if not UI_WORDS.search(i)]
    detail = {'num_add': num_add, 'num_rem': num_rem, 'id_add': id_add, 'id_rem': id_rem}
    if not diffs and not sig_add and not sig_rem and not num_add and not num_rem:
        status = 'same' if not id_add and not id_rem else 'cosmetic'
    else:
        status = 'candidate'
    return status, (diffs, detail)

def main(kind):
    d111, d107 = KINDS[kind]
    out = {}
    files111 = sorted(os.listdir(os.path.join(D111, d111)))
    files107 = set(os.listdir(os.path.join(D107, d107)))
    extra = []
    if kind == 'potions':
        files111 += ['Mocks/' + x for x in sorted(os.listdir(os.path.join(D111, d111 + '.Mocks')))]
    for fn in files111:
        if fn.startswith('Mocks/'):
            p111 = os.path.join(D111, d111 + '.Mocks', fn[6:])
            p107 = os.path.join(D107, 'Models/Potions/Mocks', fn[6:])
            key = fn[6:]
        else:
            p111 = os.path.join(D111, d111, fn)
            p107 = os.path.join(D107, d107, fn)
            key = fn
        t111 = open(p111, encoding='utf-8', errors='replace').read()
        import condense
        cl = '\n'.join(re.sub(r'^\s*\d+: ', '', l) for l in condense.condense(p111))
        cl = re.sub(r'\(uint\)num <= \du', '', cl)
        f111 = features(t111, code_override=cl)
        f107 = features(open(p107, encoding='utf-8', errors='replace').read()) if os.path.exists(p107) else None
        st, det = classify(f111, f107)
        out[key[:-3]] = {'status': st, 'detail': det, 'f111': f111, 'f107': f107}
    removed = sorted(x[:-3] for x in files107 if x.endswith('.cs') and x not in set(files111))
    return out, removed

if __name__ == '__main__':
    kind = common.REST[0]
    out, removed = main(kind)
    from collections import Counter
    print(Counter(v['status'] for v in out.values()), 'removed_in_v111:', removed)
    json.dump({'entries': out, 'removed': removed}, open(common.work_path('relics-potions', f'vdiff_{kind}.json'), 'w'), indent=1, default=list)
    for k, v in sorted(out.items()):
        if v['status'] == 'candidate':
            diffs, det = v['detail']
            print(k, diffs, {kk: vv for kk, vv in det.items() if vv})
