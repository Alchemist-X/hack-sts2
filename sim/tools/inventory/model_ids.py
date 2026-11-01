"""ModelDb id rule (game v0.111.0), ported from:

- Helpers/StringHelper.cs:35-40 CamelCaseRegex  ([A-Za-z0-9]|\\G(?!^))([A-Z])
- Helpers/StringHelper.cs:56-61 SpecialCharRegex [^A-Z0-9_]
- Helpers/StringHelper.cs:63-73 SnakeCase / Slugify
- Models/ModelDb.cs:465-488 GetId / GetCategoryType / GetCategory / GetEntry
- Models/ModelId.cs:80-90 SlugifyCategory (strips a trailing "_MODEL")

Python's re has no \\G, so the CamelCase replacement is emulated by an explicit
left-to-right scan with .NET Regex.Replace semantics (\\G = end of the previous
match; a failed attempt bumps along one char and \\G no longer holds).
"""

import re

_ALNUM = set("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789")
_UPPER = set("ABCDEFGHIJKLMNOPQRSTUVWXYZ")
_WS_RE = re.compile(r"\s+")
_SPECIAL_RE = re.compile(r"[^A-Z0-9_]")


def camel_case_replace(txt):
    """Regex.Replace(txt, "([A-Za-z0-9]|\\G(?!^))([A-Z])", "$1_$2")."""
    out = []
    n = len(txt)
    i = 0
    g = 0
    while i < n:
        if i + 1 < n and txt[i] in _ALNUM and txt[i + 1] in _UPPER:
            out.append(txt[i] + "_" + txt[i + 1])
            i += 2
            g = i
            continue
        if i == g and i != 0 and txt[i] in _UPPER:
            out.append("_" + txt[i])
            i += 1
            g = i
            continue
        out.append(txt[i])
        i += 1
    return "".join(out)


def _dotnet_trim(txt):
    return txt.strip()


def snake_case(txt):
    return camel_case_replace(_dotnet_trim(txt)).lower()


def slugify(txt):
    text = camel_case_replace(_dotnet_trim(txt))
    text2 = _WS_RE.sub("_", text.upper())
    return _SPECIAL_RE.sub("", text2)


def slugify_category(category_type_name):
    text = slugify(category_type_name)
    if text.endswith("_MODEL"):
        text = text[: -len("_MODEL")]
    return text


def model_id(category_type_name, class_name):
    return "%s.%s" % (slugify_category(category_type_name), slugify(class_name))


SELF_TEST = [
    ("StrikeIronclad", "STRIKE_IRONCLAD"),
    ("NibbitsWeak", "NIBBITS_WEAK"),
    ("LeafSlimeM", "LEAF_SLIME_M"),
    ("ExpectAFight", "EXPECT_A_FIGHT"),
    ("FairyInABottle", "FAIRY_IN_A_BOTTLE"),
    ("BattlewornDummyEventV1Encounter", "BATTLEWORN_DUMMY_EVENT_V1_ENCOUNTER"),
    ("ABCD", "A_BC_D"),
    ("ABC", "A_B_C"),
    ("CardModel", "CARD_MODEL"),
]


def self_test():
    bad = [(a, b, slugify(a)) for a, b in SELF_TEST if slugify(a) != b]
    if slugify_category("CardModel") != "CARD":
        bad.append(("CardModel(category)", "CARD", slugify_category("CardModel")))
    return bad


if __name__ == "__main__":
    import sys
    failures = self_test()
    for name in sys.argv[1:]:
        print("%s\t%s" % (name, slugify(name)))
    if failures:
        for f in failures:
            print("SELF-TEST FAIL", f)
        sys.exit(1)
