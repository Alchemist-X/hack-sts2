"""Generate ModelNetIdTable.<Vxxxx>.g.cs from a decompiled game tree.

v0.111.0: Multiplayer.Serialization/ModelIdSerializationCache.cs:Init — models in
ContentSorter (ModelId ordinal) order, SavedProperty names first-seen over that
order, epochs sorted by id, all hashed.
v0.107.1 (tree has Saves/Runs/SavedPropertiesTypeCache.cs): models emitted in
AbstractModelSubtypes order with their type Name (the reader re-runs the game's
List.Sort by Name, ties unstable as in the game), epochs in EpochModel.EpochIds order, hash = ids + epochs + max ids (int32 LE);
property net ids in AbstractModelSubtypes order, not hashed.
The result is checked at decode time against the modelIdHash in every replay.

usage: python3 gen_model_net_ids.py <decompile-root> <out.cs> <class-suffix> <game-version>
"""

import os
import re
import sys

ROOT = "MegaCrit.Sts2.Core."


def slugify(name: str) -> str:
    s = re.sub(r"(?<=[A-Za-z0-9])([A-Z])", r"_\1", name.strip())
    s = re.sub(r"\s+", "_", s.upper())
    return re.sub(r"[^A-Z0-9_]", "", s)


def slugify_category(name: str) -> str:
    s = slugify(name)
    return s[: -len("_MODEL")] if s.endswith("_MODEL") else s


def ordinal(s: str) -> list[int]:
    return [ord(c) for c in s]


def lit(s: str) -> str:
    return '"' + s.replace("\\", "\\\\").replace('"', '\\"') + '"'


def main() -> None:
    src, out, suffix, version = sys.argv[1], sys.argv[2], sys.argv[3], sys.argv[4]
    files: dict[tuple[str, str], str] = {}
    ns_re = re.compile(rb"^namespace ([\w.]+);", re.M)
    for dirpath, _, names in os.walk(src):
        for f in names:
            if not f.endswith(".cs"):
                continue
            path = os.path.join(dirpath, f)
            with open(path, "rb") as fh:
                m = ns_re.search(fh.read())
            if m:
                files.setdefault((m.group(1).decode(), f[:-3]), path)

    text_cache: dict[str, str] = {}

    def text(path: str) -> str:
        if path not in text_cache:
            with open(path, encoding="utf-8-sig") as fh:
                text_cache[path] = fh.read()
        return text_cache[path]

    def usings(t: str) -> list[str]:
        ns = re.search(r"^namespace ([\w.]+);", t, re.M).group(1)
        parts = ns.split(".")
        parents = [".".join(parts[:i]) for i in range(len(parts), 0, -1)]
        return parents + re.findall(r"^using ([\w.]+);", t, re.M)

    def resolve(name: str, scope: list[str]) -> tuple[str, str] | None:
        if "." in name:
            ns, _, n = name.rpartition(".")
            if (ns, n) in files:
                return (ns, n)
        for ns in scope:
            if (ns, name) in files:
                return (ns, name)
        return None

    st = text(files[(ROOT + "Models", "AbstractModelSubtypes")])
    scope = usings(st)
    type_names = list(dict.fromkeys(re.findall(r"private static readonly (?:System\.)?Type _t\d+ = typeof\(([\w.]+)\);", st)))

    class_cache: dict[tuple[str, str], tuple] = {}

    def class_info(key: tuple[str, str]):
        if key in class_cache:
            return class_cache[key]
        t = text(files[key])
        m = re.search(r"\bclass " + re.escape(key[1]) + r"\b(?:<[^>]*>)?\s*(?::\s*([\w.]+))?", t)
        base = m.group(1) if m else None
        props = []
        for pm in re.finditer(r"\[SavedProperty(?:\(([^)]*)\))?\]\s*\n(?:\s*\[[^\]]*\]\s*\n)*\s*((?:public|private|protected|internal)[\w\s]*?)\s+([\w.<>?,\[\] ]+?)\s+(\w+)\s*(?:\{|=>)", t):
            parts = [a.strip() for a in (pm.group(1) or "").split(",") if a.strip()]
            order = int(parts[1]) if len(parts) >= 2 else 0
            mods = pm.group(2).split()
            props.append((pm.group(4), order, "private" in mods and "protected" not in mods))
        info = (base, props, usings(t))
        class_cache[key] = info
        return info

    items = []
    for tn in type_names:
        key = resolve(tn, scope)
        if key is None:
            raise SystemExit(f"cannot resolve {tn}")
        chain = [key]
        cur = key
        while True:
            base, _, sc = class_info(cur)
            if base is None:
                raise SystemExit(f"{cur} has no base")
            if base == "AbstractModel":
                break
            nxt = resolve(base, sc)
            if nxt is None:
                raise SystemExit(f"cannot resolve base {base} of {cur}")
            chain.append(nxt)
            cur = nxt
        props: dict[str, int] = {}
        for depth, k in enumerate(chain):
            for pname, order, private in class_info(k)[1]:
                if private and depth > 0:
                    continue
                if pname not in props:
                    props[pname] = order
        sorted_props = [p for p, _ in sorted(props.items(), key=lambda kv: (kv[1], ordinal(kv[0])))]
        items.append((slugify_category(chain[-1][1]), slugify(key[1]), sorted_props, key))

    legacy = (ROOT + "Saves.Runs", "SavedPropertiesTypeCache") in files
    declared = list(items)
    if legacy:
        prop_source = declared
    else:
        items.sort(key=lambda it: (ordinal(it[0]), ordinal(it[1]), ordinal(it[3][0] + "." + it[3][1])))
        prop_source = items
    properties: list[str] = []
    for _, _, plist, _ in prop_source:
        for pname in plist:
            if pname not in properties:
                properties.append(pname)

    epoch_src = text(files[(ROOT + "Timeline", "EpochModel")])
    epoch_scope = usings(epoch_src)
    if legacy:
        epoch_types = re.findall(r"^\t\tGetId<(\w+)>\(\),?$", epoch_src, re.M)
    else:
        epoch_types = list(dict.fromkeys(re.findall(r"span\[num2\] = typeof\((\w+)\);", epoch_src)))
    epoch_ids = []
    for et in epoch_types:
        k = resolve(et, epoch_scope)
        m = re.search(r'override string Id => "([^"]+)"', text(files[k]))
        epoch_ids.append(m.group(1))
    if not legacy:
        epoch_ids.sort(key=ordinal)

    lines = [
        f"// <auto-generated> gen_model_net_ids.py from the {version} decompile; do not edit.",
        "namespace Sts2Sim.Diff.Replay;",
        "",
        f"public static class ModelNetIdTable{suffix}",
        "{",
        f"    public const string GameVersion = \"{version}\";",
        "",
        f"    public const bool LegacyHash = {'true' if legacy else 'false'};",
        "",
        "    public static readonly string[] Models =",
        "    {",
    ]
    lines += [f"        {lit(cat + '.' + ent + ('|' + key[1] if legacy else ''))}," for cat, ent, _, key in items]
    lines += ["    };", "", "    public static readonly string[] Properties =", "    {"]
    lines += [f"        {lit(p)}," for p in properties]
    lines += ["    };", "", "    public static readonly string[] Epochs =", "    {"]
    lines += [f"        {lit(e)}," for e in epoch_ids]
    lines += ["    };", "}", ""]
    with open(out, "w", encoding="utf-8") as fh:
        fh.write("\n".join(lines))
    print(f"legacy={legacy} models={len(items)} properties={len(properties)} epochs={len(epoch_ids)}")


if __name__ == "__main__":
    main()
