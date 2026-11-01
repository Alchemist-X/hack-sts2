import os as _os
import sys as _sys
_sys.path.insert(0, _os.path.join(_os.path.dirname(_os.path.abspath(__file__)), ".."))
import common  # noqa: E402
import json
import os
import re
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import extract as X

NEW = X.NEW
OLD = X.OLD
HERE = os.path.dirname(os.path.abspath(__file__))

UI_LINE = re.compile(
    r"VfxCmd|SfxCmd|TalkCmd|ThinkCmd|Cmd\.Wait|CustomScaledWait|TriggerAnim|NRunMusicController|PlayAnim|ScreenShake|"
    r"Animator|AnimState|AddAnyState|AddBranch\(\"|SetSkin|SetAnimation|Sfx|Vfx|Color|Spine|Skeleton|Tween|Godot|"
    r"GetCreatureNode|NCombatRoom|NCreature|Visual|Camera|Log\.|Bestiary|HoverTip|LocString|L10N|AssetPath|ScenePath|"
    r"SetVisible|SetParam|SetSlotsToSetupPose|ShouldShowMoveInBestiary|Shake|PreloadManager|SceneHelper|NGame|"
    r"Node2D|Vector2|Position|Scale|Modulate|SetBotFallPosition|SetDefaultScaleTo|SetTrans|SetEase|TaskHelper|"
    r"IsHealthBarVisible|HpBarSizeReduction|DeathAnimLength|HurtAnimationTrackOffset|ExtraDeathVfxPadding|Title|"
    r"TakeDamageSfxType|DeathSfx|AttackSfx|CastSfx|HurtSfx|HasDeathSfx|HasHurtSfx|HasPhobiaSpineSkin|ShouldFadeAfterDeath|"
    r"CanChangeScale|ShouldShowInCompendium|GetCameraScaling|GetCameraOffset|BossNode|CustomBgm|AmbientSfx|HasScene|"
    r"HasCustomBackground|FullyCenterPlayers|ExtraAssetPaths|MapBackground|Bgm|Ambience|Spine|ChestSpine|RestSite|"
    r"BackgroundAssets|MapTopBg|MapMidBg|MapBotBg|MapTraveled|MapUntraveled|MapBg|WithAttackerAnim|WithHitFx|"
    r"WithAttackerFx|OnlyPlayAnimOnce|WithHitVfx|WithNoAttackerAnim|WithWaitBeforeHit|AfterAttackerAnim")

NUM_RE = re.compile(r"(?<![\w.])(-?\d+(?:\.\d+)?)[mfMF]?(?![\w.])")
ENUM_RE = re.compile(r"\b(AscensionLevel|MoveRepeatType|ValueProp|PileType|CardType|CardRarity|TargetType|CardKeyword|"
                     r"PowerType|PowerStackType|RoomType|EncounterTag|CombatSide|IntentType|CardPilePosition|MapPointType|"
                     r"RunRngType|PlayerRngType|CardTag)\.(\w+)")
CALL_RE = re.compile(r"\b(\w*Cmd\.\w+(?:<[\w, ]+>)?|ModelDb\.\w+<\w+>|new \w+Intent|new (?:MoveState|RandomBranchState|ConditionalBranchState)|"
                     r"HasPower<\w+>|GetPower<\w+>|GetValueIfAscension|HasAscension|\.Rng\.\w+|Rng\.\w+|"
                     r"SetMoveImmediate|FollowUpState|AddBranch|AddState|WithHitCount|FromMonster|Targeting\w*|Unpowered|BeforeDamage|"
                     r"IsWeak|Slots|Tags|GenerateMonsters|MinInitialHp|MaxInitialHp|AfterAddedToRoom|BeforeRemovedFromRoom|"
                     r"AfterDeath|BeforeDeath|ShouldDisappearFromDoom|OnDieToDoom|Escape|Kill|Stun|Summon|SpawnedThisTurn)")
STR_RE = re.compile(r'"([A-Z0-9_]{3,})"')


def logic_lines(text):
    t = X.strip_comments(text)
    out = []
    for l in t.split("\n"):
        s = l.strip()
        if not s or s.startswith("using "):
            continue
        if UI_LINE.search(s):
            s2 = re.sub(r"\.(WithAttackerAnim|WithHitFx|WithAttackerFx|OnlyPlayAnimOnce|WithHitVfxNode|WithNoAttackerAnim|WithWaitBeforeHit|WithHitVfxSpawnedAtBase)\([^;]*", "", s)
            if UI_LINE.search(s2):
                continue
            s = s2
        if re.search(r"LayoutKind|_003C_003E1__state|AwaitUnsafeOnCompleted|IsCompleted|GetResult\(\)|SetResult|SetException|SetStateMachine|_003C_003Eu__|IL_[0-9a-f]{4}", s):
            continue
        out.append(s)
    return out


def tokens(text):
    lines = logic_lines(text)
    nums, enums, calls, strs = {}, {}, {}, {}
    for s in lines:
        for m in NUM_RE.finditer(s):
            v = m.group(1)
            if v in ("0", "1", "-1", "2147483647"):
                continue
            pre = s[max(0, m.start() - 12):m.start()]
            if re.search(r"(case|\[|\]|_00)", pre):
                continue
            nums[v] = nums.get(v, 0) + 1
        for m in ENUM_RE.finditer(s):
            k = m.group(0)
            enums[k] = enums.get(k, 0) + 1
        for m in CALL_RE.finditer(s):
            k = m.group(1)
            calls[k] = calls.get(k, 0) + 1
        for m in STR_RE.finditer(s):
            k = m.group(1)
            strs[k] = strs.get(k, 0) + 1
    return nums, enums, calls, strs


def mdiff(a, b):
    out = {}
    keys = set(a) | set(b)
    for k in sorted(keys):
        if (k in a) != (k in b):
            out[k] = ["v107" if k in a else "-", "v111" if k in b else "-"]
    return out


def graph_sig(g):
    if not g:
        return None
    sig = {}
    for st in g["states"]:
        d = {"kind": st["kind"], "intents": st.get("intents", []), "follow": sorted(f["to"] for f in st.get("follow_up", [])),
             "branches": [(b["to"], b.get("repeat"), b.get("weight"), b.get("max_times"), b.get("cooldown"), b.get("condition")) for b in st.get("branches", [])],
             "init": st.get("init", {})}
        sig[st["id"]] = d
    return {"states": sig, "initial": [i["id"] for i in g["initial"]], "registered": [r["id"] for r in g["registered"]]}


def norm_cmd(s):
    s = re.sub(r"^\d+: ", "", s)
    s = re.sub(r"^await ", "", s)
    s = s.replace("new ThrowingPlayerChoiceContext(), ", "")
    s = re.sub(r"\b(base|this|\w+)\.Creature\b", "Self", s)
    s = re.sub(r"\(\w+\)\b", "", s)
    s = re.sub(r"\b[a-z]\w*\.(?=[A-Z])", "", s)
    s = re.sub(r"\b(base|this)\.", "", s)
    s = re.sub(r"\.(WithAttackerAnim|WithHitFx|WithAttackerFx|OnlyPlayAnimOnce|WithHitVfxNode|WithNoAttackerAnim|WithWaitBeforeHit|WithHitVfxSpawnedAtBase)\(.*$", "", s)
    s = re.sub(r"FromMonster\(\w+\)", "FromMonster(this)", s)
    s = re.sub(r"\(System\.Collections\.Generic\.IEnumerable<\w+>\??\)", "", s)
    s = re.sub(r"\(System\.Collections\.Generic\.IReadOnlyList<\w+>\??\)", "", s)
    s = re.sub(r"\b(current|item|current\d|item\d|target|creature)\b", "X", s)
    s = re.sub(r"decimal\.op_Implicit\((.*)\)", r"\1", s)
    s = re.sub(r"\(\(System\.Collections\.Generic\.IReadOnlyCollection<\w+>\)(\w+(?:\.\w+)*)\)", r"\1", s)
    s = re.sub(r"\b[a-z]\w*\.(?=_)", "", s)
    s = re.sub(r"\.GetAwaiter\(\)", "", s)
    s = re.sub(r"\.Execute\(null\)", "", s)
    s = re.sub(r"\bawait ", "", s)
    s = re.sub(r"<(\w+)>", r"\1", s) if "Cmd.Apply<" not in s and "Add<" not in s and "Preview<" not in s else s
    s = re.sub(r"\s+", " ", s)
    s = s.rstrip(";")
    return s


def move_sig(a):
    out = {}
    for name, mv in a["moves"].items():
        cmds = [norm_cmd(re.sub(r"^\w+(\[\d+\])? = (await )?", "", re.sub(r"^\d+: ", "", e))) for e in mv["ordered"] if "Cmd." in e]
        cmds = [c for c in cmds if not re.search(r"VfxCmd|SfxCmd|TalkCmd|ThinkCmd|Cmd\.Wait|TriggerAnim|PreviewCardPileAdd", c)]
        out[name] = cmds
    return out


def values_sig(a):
    return {k: v["value"] for k, v in a["values"].items()}


def compare_file(new_path, old_path, is_monster):
    tn = open(new_path, encoding="utf-8").read()
    if not os.path.exists(old_path):
        return {"status": "only_v0.111.0"}
    to = open(old_path, encoding="utf-8").read()
    nn, en, cn, sn = tokens(tn)
    no, eo, co, so = tokens(to)
    res = {"nums": mdiff(no, nn), "enums": mdiff(eo, en), "calls": mdiff(co, cn), "strs": mdiff(so, sn)}
    if is_monster:
        an = X.analyze(new_path)
        ao = X.analyze(old_path)
        gn, go = graph_sig(an["graph"]), graph_sig(ao["graph"])
        if gn != go:
            gd = {}
            if gn and go:
                for sid in sorted(set(gn["states"]) | set(go["states"])):
                    if gn["states"].get(sid) != go["states"].get(sid):
                        gd[sid] = {"v107": go["states"].get(sid), "v111": gn["states"].get(sid)}
                if gn["initial"] != go["initial"]:
                    gd["_initial"] = {"v107": go["initial"], "v111": gn["initial"]}
                if gn["registered"] != go["registered"]:
                    gd["_registered"] = {"v107": go["registered"], "v111": gn["registered"]}
            else:
                gd = {"v107": go, "v111": gn}
            if gd:
                res["graph"] = gd
        vn, vo = values_sig(an), values_sig(ao)
        vd = {}
        for k in sorted(set(vn) | set(vo)):
            if vn.get(k) != vo.get(k):
                vd[k] = [vo.get(k), vn.get(k)]
        if vd:
            res["values"] = vd
        mn, mo = move_sig(an), move_sig(ao)
        md = {}
        for k in sorted(set(mn) | set(mo)):
            if mn.get(k) != mo.get(k):
                md[k] = {"v107": mo.get(k), "v111": mn.get(k)}
        if md:
            res["moves"] = md
    res = {k: v for k, v in res.items() if v}
    res["status"] = "differs" if res else "same_tokens"
    return res


def main():
    out = {}
    pairs = [
        ("monsters", "MegaCrit.Sts2.Core.Models.Monsters", "Models/Monsters", True),
        ("monsters", "MegaCrit.Sts2.Core.Models.Monsters.Mocks", "Models/Monsters/Mocks", True),
        ("encounters", "MegaCrit.Sts2.Core.Models.Encounters", "Models/Encounters", False),
        ("encounters", "MegaCrit.Sts2.Core.Models.Encounters.Mocks", "Models/Encounters/Mocks", False),
        ("acts", "MegaCrit.Sts2.Core.Models.Acts", "Models/Acts", False),
    ]
    for kind, nd, od, ism in pairs:
        for fn in sorted(os.listdir(os.path.join(NEW, nd))):
            if not fn.endswith(".cs"):
                continue
            name = fn[:-3]
            out.setdefault(kind, {})[name] = compare_file(os.path.join(NEW, nd, fn), os.path.join(OLD, od, fn), ism)
    with open(common.work_path("monsters", "vdiff.json"), "w") as f:
        json.dump(out, f, indent=1, sort_keys=True)
    for kind, d in out.items():
        n = sum(1 for v in d.values() if v["status"] != "same_tokens")
        print(kind, len(d), "differs:", n)


if __name__ == "__main__":
    main()
