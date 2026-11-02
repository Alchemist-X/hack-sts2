"""Tests of scripts/hook_drift.py: hook-map parsing, signature comparison, drift statuses, mod coverage.

  python3 -m unittest discover -s scripts -p 'test_hook_drift.py'

A miniature hook map, decompile tree and mod source are built in a temp dir. Two checks read the real
docs/hook-map.md; the one that also needs the rule-truth decompile is skipped when the tree is absent.
"""

import os
import shutil
import sys
import tempfile
import unittest

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import hook_drift as H  # noqa: E402
from test_version_delta import write  # noqa: E402

HOOK_MAP = """
# Hook Map

## Old body
| Purpose | Target | Signature | Mechanism |
|---|---|---|---|
| old | `RunManager.SetUpNewSinglePlayer` (RunManager.cs:204) | `public void SetUpNewSinglePlayer(RunState state)` | harmony_postfix |

## v9 drift addendum

### Patched members (all verified)

| Hook id | v9 target | v8 line | v9 line | Signature / parameters bound |
|---|---|---|---|---|
| `patch:RunManager.OnEnded` | `Runs/RunManager.cs` | 10 | 9 | `SerializableRun OnEnded(bool isVictory)` |
| `patch:RunManager.CleanUp` (prefix + **finalizer**, new) | `Runs/RunManager.cs` | 20 | 14 | `void CleanUp(bool graceful = true)`; writes the replay |
| `patch:MerchantEntry.OnTryPurchaseWrapper` | `Entities.Merchant/MerchantEntry.cs` | 69 | 5 | `async Task<bool> (MerchantInventory? inventory, bool ignoreCost = false)` |
| `patch:NEndTurnButton.CallReleaseLogic` | `Nodes.Combat/NEndTurnButton.cs` | 577 | 3 | End vs Undo enqueue unchanged (:500-509) |
| `patch:RewardSynchronizer.SyncLocal*` (2) | `Multiplayer.Game/RewardSynchronizer.cs` | 72-166 | 3, 6 | parameter names `card` / `goldLost` unchanged |

Events: `RunManager.RunStarted` (v8 :211 -> `Runs/RunManager.cs:5`, invoked in `Launch` :40),
`RoomEntered`/`RoomExited` (:6/:7), `CombatManager.Instance` still an eager static (:3).

FieldRefs and reflection: `RunManager._startTime` (long, :4), `PickRelicAction._relicIndex` (int?, :5),
and the new `fieldref:SpecialCardReward._card` (`Rewards/SpecialCardReward.cs:4`, v8 :26).

### Drift found
"""

RUN_MANAGER = """
namespace MegaCrit.Sts2.Core.Runs;

public class RunManager
{
    private long _startTime;
    public event Action<RunState>? RunStarted;
    public event Action? RoomEntered;
    public event Action? RoomExited;
    public SerializableRun OnEnded(bool isVictory)
    {
        return null;
    }

    public void CleanUp(bool graceful = true)
    {
    }

    public void Abandon()
    {
    }
}
"""

FILES = {
    "MegaCrit.Sts2.Core.Runs/RunManager.cs": RUN_MANAGER,
    "MegaCrit.Sts2.Core.Entities.Merchant/MerchantEntry.cs": """
        namespace MegaCrit.Sts2.Core.Entities.Merchant;
        public abstract class MerchantEntry
        {
            [AsyncStateMachine(typeof(X))]
            public async System.Threading.Tasks.Task<bool> OnTryPurchaseWrapper(MerchantInventory? inventory, bool ignoreCost = false)
            {
                return true;
            }
        }
        """,
    "MegaCrit.Sts2.Core.Nodes.Combat/NEndTurnButton.cs": """
        public class NEndTurnButton
        {
            public void CallReleaseLogic()
            {
            }
        }
        """,
    "MegaCrit.Sts2.Core.Multiplayer.Game/RewardSynchronizer.cs": """
        public class RewardSynchronizer
        {
            public void SyncLocalObtainedCard(CardModel card)
            {
            }
            public void SyncLocalGoldLost(int goldLost)
            {
            }
            public void SyncLocalExtra(int x)
            {
            }
        }
        """,
    "MegaCrit.Sts2.Core.Combat/CombatManager.cs": """
        public class CombatManager
        {
            public static CombatManager Instance { get; } = new CombatManager();
        }
        """,
    "MegaCrit.Sts2.Core.GameActions/PickRelicAction.cs": """
        public class PickRelicAction
        {
            private readonly Player _player;
            private readonly int? _relicIndex;
        }
        """,
    "MegaCrit.Sts2.Core.Rewards/SpecialCardReward.cs": """
        public class SpecialCardReward
        {
            private CardModel? _card;
        }
        """,
}

MOD = """
public static class Funnels
{
    internal static void Patch(Harmony harmony)
    {
        RecorderMod.TryPatch(harmony, "patch:RunManager.OnEnded", () => AccessTools.Method(typeof(RunManager), "OnEnded"));
        RecorderMod.TryPatch(harmony, "patch:RunManager.Abandon", () => AccessTools.Method(typeof(RunManager), "Abandon"));
        PatchSyncLocal(harmony, nameof(RewardSynchronizer.SyncLocalGoldLost), nameof(Prefix));
        var f = AccessTools.FieldRefAccess<PickRelicAction, int?>("_relicIndex");
    }
}
"""


def build_tree(root, files):
    for rel, text in files.items():
        write(root, rel, text)


class ParseTests(unittest.TestCase):
    def test_signature_forms(self):
        s = H.parse_signature("SerializableRun OnEnded(bool isVictory)", "OnEnded")
        self.assertEqual((s["ret"], s["name"], s["params"]), ("SerializableRun", "OnEnded", [("bool", "isVictory")]))
        s = H.parse_signature("async Task<bool> (MerchantInventory? inventory, bool ignoreCost = false)", "X")
        self.assertEqual((s["ret"], s["name"]), ("Task<bool>", None))
        self.assertEqual(s["params"], [("MerchantInventory?", "inventory"), ("bool", "ignoreCost")])
        s = H.parse_signature("(MerchantInventory?, bool ignoreCost = false, bool cancelable = true)", "X")
        self.assertEqual(s["params"][0], ("MerchantInventory?", None))
        s = H.parse_signature("void ()", "SkipLocalRewardsSet")
        self.assertEqual((s["ret"], s["params"]), ("void", []))
        s = H.parse_signature("async Task<bool>()", "SelectUnsynchronized")
        self.assertEqual((s["ret"], s["params"]), ("Task<bool>", []))
        self.assertIsNone(H.parse_signature("End vs Undo enqueue unchanged", "X"))

    def test_parse_mini_map(self):
        tmp = tempfile.mkdtemp(prefix="hdrift-")
        try:
            path = write(tmp, "hook-map.md", HOOK_MAP)
            ts = H.parse_hook_map(path)
            ids = [t["id"] for t in ts]
            self.assertNotIn("patch:RunManager.SetUpNewSinglePlayer", ids)  # the old body is not the latest table
            self.assertIn("patch:RunManager.CleanUp", ids)
            kinds = {t["id"]: t["kind"] for t in ts}
            self.assertEqual(kinds["patch:RewardSynchronizer.SyncLocal*"], "method_prefix")
            self.assertEqual(kinds["event:RunManager.RoomExited"], "event")
            self.assertEqual(kinds["fieldref:SpecialCardReward._card"], "field")
            self.assertNotIn("event:RunManager.Launch", ids)  # inside a parenthesised annotation
            ft = {t["id"]: t.get("field_type") for t in ts}
            self.assertEqual(ft["fieldref:RunManager._startTime"], "long")
            self.assertEqual(ft["fieldref:PickRelicAction._relicIndex"], "int?")
            files = {t["id"]: t.get("file") for t in ts}
            self.assertEqual(files["fieldref:SpecialCardReward._card"], "Rewards/SpecialCardReward.cs")
        finally:
            shutil.rmtree(tmp, ignore_errors=True)

    def test_real_hook_map_parses(self):
        ts = H.parse_hook_map(os.path.join(H.REPO, "docs", "hook-map.md"))
        kinds = [t["kind"] for t in ts]
        self.assertGreaterEqual(kinds.count("method"), 25)
        self.assertGreaterEqual(kinds.count("event"), 15)
        self.assertGreaterEqual(kinds.count("field"), 10)
        self.assertTrue(all(t["file"] for t in ts if t["kind"] == "method"))


class DriftTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.mkdtemp(prefix="hdrift-")
        self.map = write(self.tmp, "hook-map.md", HOOK_MAP)
        self.mod = os.path.join(self.tmp, "mod")
        write(self.mod, "Funnels.cs", MOD)
        self.tree = os.path.join(self.tmp, "tree")

    def tearDown(self):
        shutil.rmtree(self.tmp, ignore_errors=True)

    def run_with(self, files, mod=True):
        build_tree(self.tree, files)
        rep = H.run(self.tree, self.map, self.mod if mod else None)
        return rep, {r["id"] + "@" + r["source"]: r for r in rep["results"]}

    def test_clean_tree_has_no_drift(self):
        rep, by = self.run_with(FILES, mod=False)
        self.assertEqual(rep["drift"], 0, [r for r in rep["results"] if r["status"] != "ok"])
        self.assertEqual(by["patch:RunManager.OnEnded@hook-map"]["status"], "ok")
        self.assertEqual(by["patch:RewardSynchronizer.SyncLocal*@hook-map"]["status"], "ok")

    def test_signature_change_is_drift(self):
        files = dict(FILES)
        files["MegaCrit.Sts2.Core.Runs/RunManager.cs"] = RUN_MANAGER.replace(
            "OnEnded(bool isVictory)", "OnEnded(bool isVictory, bool isAbandoned)")
        rep, by = self.run_with(files, mod=False)
        r = by["patch:RunManager.OnEnded@hook-map"]
        self.assertEqual(r["status"], "signature_changed")
        self.assertIn("isAbandoned", r["detail"])
        self.assertEqual(rep["drift"], 1)

    def test_parameter_rename_is_drift(self):
        files = dict(FILES)
        files["MegaCrit.Sts2.Core.Runs/RunManager.cs"] = RUN_MANAGER.replace("bool graceful = true", "bool clean = true")
        _, by = self.run_with(files, mod=False)
        self.assertEqual(by["patch:RunManager.CleanUp@hook-map"]["status"], "signature_changed")

    def test_missing_member_type_change_and_line_move(self):
        files = dict(FILES)
        files["MegaCrit.Sts2.Core.Runs/RunManager.cs"] = "\n\n" + RUN_MANAGER.replace(
            "public event Action? RoomExited;\n", "").replace("private long _startTime;", "private int _startTime;")
        rep, by = self.run_with(files, mod=False)
        self.assertEqual(by["event:RunManager.RoomExited@hook-map"]["status"], "missing_member")
        self.assertEqual(by["fieldref:RunManager._startTime@hook-map"]["status"], "type_changed")
        self.assertEqual(by["patch:MerchantEntry.OnTryPurchaseWrapper@hook-map"]["status"], "ok")
        moved = by["patch:RunManager.CleanUp@hook-map"]
        self.assertEqual(moved["status"], "line_moved")  # info only
        self.assertEqual(rep["drift"], 2)

    def test_prefix_count(self):
        files = dict(FILES)
        files["MegaCrit.Sts2.Core.Multiplayer.Game/RewardSynchronizer.cs"] = """
            public class RewardSynchronizer
            {
                public void SyncLocalObtainedCard(CardModel card)
                {
                }
            }
            """
        _, by = self.run_with(files, mod=False)
        self.assertEqual(by["patch:RewardSynchronizer.SyncLocal*@hook-map"]["status"], "count_changed")

    def test_missing_file(self):
        files = {k: v for k, v in FILES.items() if "MerchantEntry" not in k}
        _, by = self.run_with(files, mod=False)
        self.assertEqual(by["patch:MerchantEntry.OnTryPurchaseWrapper@hook-map"]["status"], "missing_file")

    def test_mod_targets_checked_and_undocumented_reported(self):
        rep, by = self.run_with(FILES)
        self.assertEqual(by["patch:RunManager.OnEnded@mod"]["status"], "ok")
        self.assertEqual(by["patch:RewardSynchronizer.SyncLocalGoldLost@mod"]["status"], "ok")  # via the prefix row
        self.assertEqual(by["fieldref:PickRelicAction._relicIndex@mod"]["status"], "ok")
        self.assertEqual(by["patch:RunManager.Abandon@mod"]["status"], "undocumented")
        self.assertEqual(rep["drift"], 1)
        md = H.render_md(rep)
        self.assertIn("undocumented", md)


@unittest.skipUnless(os.path.isdir(H.DEFAULT_TREE), "rule-truth decompile not present")
class RealTreeTests(unittest.TestCase):
    def test_current_tree_has_no_drift(self):
        rep = H.run(H.DEFAULT_TREE, os.path.join(H.REPO, "docs", "hook-map.md"), os.path.join(H.REPO, "mod", "src"))
        self.assertEqual(rep["drift"], 0, [r for r in rep["results"] if r["status"] not in H.OK])


if __name__ == "__main__":
    unittest.main()
