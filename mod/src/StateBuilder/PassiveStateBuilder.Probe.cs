// Sts2Recorder addition (no upstream counterpart): single-pass collector for the
// FTUE/popup probes in PassiveStateBuilder.Popups.cs. Replaces up to five full
// scene-tree FindAll walks per snapshot with one preorder walk; detection logic
// itself is unchanged and strictly read-only.

using System;
using System.Collections.Generic;
using Godot;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;

namespace Sts2Recorder.Game;

public static partial class PassiveStateBuilder
{
    /// <summary>
    /// Nodes of interest to the popup/FTUE probes, gathered by one preorder walk
    /// from a start node (the scene root, or the open modal for the tiny
    /// modal-subtree mini-scans). Each list preserves FindAllRecursive's order (a
    /// matching node is recorded before its subtree is descended), so probes that
    /// iterate a list see the exact node order the per-probe walks used to
    /// produce.
    /// </summary>
    private sealed class PopupProbeScan
    {
        public readonly List<MegaCrit.Sts2.Core.Nodes.Ftue.NAcceptTutorialsFtue> AcceptTutorialFtues = new();
        public readonly List<Node> GenericFtueCandidates = new();
        public readonly List<NVerticalPopup> VerticalPopups = new();
        public readonly List<NPopupYesNoButton> YesNoButtons = new();
        public readonly List<NModalContainer> ModalContainers = new();
    }

    // The FTUE-namespace test is the one classification that needs string work on
    // Type.FullName, and it depends only on the runtime Type, so it is computed
    // once per Type instead of per node per snapshot. Main-thread only access; a
    // plain Dictionary is fine.
    private static readonly Dictionary<Type, bool> _genericFtueTypeCache = new();

    private static bool IsGenericFtueType(Type type)
    {
        if (_genericFtueTypeCache.TryGetValue(type, out var cached))
            return cached;

        var typeName = type.FullName ?? "";
        var isGenericFtue =
            typeName.StartsWith("MegaCrit.Sts2.Core.Nodes.Ftue.", StringComparison.Ordinal) &&
            !typeName.EndsWith(".NAcceptTutorialsFtue", StringComparison.Ordinal);

        _genericFtueTypeCache[type] = isGenericFtue;
        return isGenericFtue;
    }

    private static PopupProbeScan ScanPopupProbeTargets(Node start)
    {
        var scan = new PopupProbeScan();
        ScanPopupProbeTargetsRecursive(start, scan);
        return scan;
    }

    private static void ScanPopupProbeTargetsRecursive(Node node, PopupProbeScan scan)
    {
        if (!IsLiveNode(node))
            return;

        if (node is MegaCrit.Sts2.Core.Nodes.Ftue.NAcceptTutorialsFtue acceptFtue)
            scan.AcceptTutorialFtues.Add(acceptFtue);
        if (node is NVerticalPopup verticalPopup)
            scan.VerticalPopups.Add(verticalPopup);
        if (node is NPopupYesNoButton yesNoButton)
            scan.YesNoButtons.Add(yesNoButton);
        if (node is NModalContainer modalContainer)
            scan.ModalContainers.Add(modalContainer);
        if (IsGenericFtueType(node.GetType()))
            scan.GenericFtueCandidates.Add(node);

        try
        {
            // GetChildCount/GetChild avoid the per-node marshalled array that
            // GetChildren() allocates.
            var childCount = node.GetChildCount();
            for (var i = 0; i < childCount; i++)
                ScanPopupProbeTargetsRecursive(node.GetChild(i), scan);
        }
        catch (ObjectDisposedException) { }
    }
}
