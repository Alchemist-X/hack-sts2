import re, sys, os

BOILER = [
    r'^\s*//IL_', r'^\s*using ', r'^\s*\[StructLayout', r'^\s*\[CompilerGenerated\]', r'^\s*\[DebuggerHidden\]',
    r'^\s*\[AsyncStateMachine', r'^\s*\[IteratorStateMachine', r'_003C_003Et__builder', r'AwaitUnsafeOnCompleted',
    r'^\s*[\w().& ]*\.GetResult\(\);\s*$', r'^\s*if \(!val\d*\.IsCompleted\)', r'^\s*if \(!\w+\.IsCompleted\)',
    r'_003C_003E1__state = -[12];', r'^\s*num = \(_003C_003E1__state = \d+\);', r'_003C_003Eu__\d+ = (val\d*|default)',
    r'^\s*public int _003C_003E1__state;', r'^\s*public AsyncTaskMethodBuilder', r'^\s*private TaskAwaiter',
    r'^\s*public \w+ _003C_003E4__this;', r'^\s*private void SetStateMachine', r'SetStateMachine\(stateMachine\)',
    r'^\s*int num = _003C_003E1__state;', r'^\s*catch \(System\.Exception exception\)', r'SetException\(exception\)',
    r'^\s*try\s*$', r'^\s*return;\s*$', r'^\s*[{}]\s*$', r'^\s*\}\s*$', r'^\s*$', r'^\s*namespace ',
    r'_003C_003Eu__\d+;\s*$', r'^\s*(val\d*) = _003C_003Eu__\d+;', r'^\s*private object _003C_003E7__wrap',
    r'^\s*private int _003C_003E7__wrap', r'^\s*goto IL_\w+;\s*$', r'^\s*IL_\w+:\s*;?\s*$', r'^\s*end_IL_\w+:\s*;?\s*$',
    r'^\s*TaskAwaiter(<[^>]*>)? val\d*;\s*$', r'^\s*ConfiguredTaskAwaiter', r'^\s*if \(num (==|!=) \d+\)\s*$',
    r'_003C_003Et__builder\)\)\.Start', r'\.Task;\s*$', r'= default\(_003C\w+_003Ed__\d+\);',
    r'^\s*_003C\w+_003Ed__\._003C_003E', r'^\s*\(\(IDisposable\)', r'^\s*private void MoveNext\(\)',
    r'^\s*bool IEnumerator', r'^\s*void IEnumerator', r'^\s*object IEnumerator', r'IEnumerator<\w+>\.Current',
    r'^\s*\[DebuggerStepThrough\]', r'^\s*private struct _003C\w+_003Ed__\d+ : IAsyncStateMachine',
    r'^\s*finally\s*$', r'^\s*switch \(num\)\s*$', r'^\s*default:\s*$', r'^\s*case \d+:\s*$',
    r'^\s*\[SuppressMessage', r'^\s*\[field: CompilerGenerated\]', r'^\s*\[CompilerGenerated\]\s*$',
    r'num = \(_003C_003E1__state = -1\);', r'^\s*goto end_IL_\w+;', r'^\s*_003C\w+_003Ed__\.\w+ = \w+;\s*$',
    r'IsCompleted\)\s*$', r'^\t\tpublic [\w<>\[\],?. ]+ \w+;\s*$', r'^\t\tprivate [\w<>\[\],?. ]+ _003C\w+;\s*$',
    r'^\s*\w+ \w+ = _003C_003E4__this;\s*$',
]
BRX = [re.compile(b) for b in BOILER]

def clean_line(l):
    l = l.replace('_003C_003E4__this', 'this').replace('_003C', '<').replace('_003E', '>')
    l = re.sub(r'\(System\.Collections\.Generic\.IEnumerable<(\w+)>\)', '', l)
    l = l.replace('System.Collections.Generic.', '').replace('System.Threading.Tasks.', '')
    l = re.sub(r'decimal\.op_Implicit\(([^()]*(\([^()]*\))?[^()]*)\)', r'\1', l)
    return l

def condense(path):
    out = []
    with open(path, encoding='utf-8', errors='replace') as f:
        lines = f.readlines()
    for i, l in enumerate(lines, 1):
        if any(r.search(l) for r in BRX):
            continue
        s = clean_line(l.rstrip('\n'))
        s = s.replace('\t', '  ')
        out.append(f'{i:4d}: {s}')
    return out

if __name__ == '__main__':
    root = sys.argv[1]
    files = sys.argv[2:]
    for fn in files:
        p = os.path.join(root, fn)
        print(f'==== {fn}')
        for l in condense(p):
            print(l)
