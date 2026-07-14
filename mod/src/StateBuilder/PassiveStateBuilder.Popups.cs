// Ported from STS2MCP (https://github.com/Gennadiyev/STS2MCP) — McpMod.Helpers.cs (FTUE/popup detection).
// Copyright 2026 Yikun Ji (Kunologist). MIT License; this attribution is retained per license.
// Sts2Recorder adaptations: state_type reports the popup kind directly ("tutorial_prompt" /
// "tutorial" / "popup") instead of upstream's "menu" + menu_screen pair; detection is
// unchanged and strictly read-only. Verified against game v0.99.1 decompiled source.

using System;
using System.Collections.Generic;
using System.Text;
using Godot;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;

namespace Sts2Recorder.Game;

public static partial class PassiveStateBuilder
{
    private static bool IsFtueNodeActive(Node node)
    {
        if (node is not CanvasItem canvas || !IsLiveNode(node))
            return false;

        if (IsNodeVisible(canvas))
            return true;

        if (ReferenceEquals(GetOpenModalNode(), node))
            return true;

        try
        {
            return GetInstanceFieldValue(node, "_confirmButton") is NClickableControl confirmButton &&
                   (IsControlVisibleInTree(confirmButton) || IsControlVisibleInOpenModal(confirmButton));
        }
        catch (ObjectDisposedException)
        {
            return false;
        }
    }

    private static Dictionary<string, object?>? BuildVisibleFtueState(Node root)
    {
        var tutorialFtue = FindVisibleAcceptTutorialsFtue(root);
        if (tutorialFtue != null && IsFtueNodeActive(tutorialFtue))
        {
            return new Dictionary<string, object?>
            {
                ["state_type"] = "tutorial_prompt",
                ["message"] = "Enable Tutorials? prompt is active."
            };
        }

        var ftue = FindVisibleGenericFtue(root);
        if (ftue != null)
        {
            var canAdvance = FindFtueAdvanceButton(ftue) != null;
            return new Dictionary<string, object?>
            {
                ["state_type"] = "tutorial",
                ["message"] = "Tutorial popup active.",
                ["can_advance"] = canAdvance
            };
        }

        var popup = BuildVisiblePopupState(root);
        if (popup != null)
            return popup;

        return null;
    }

    private static Dictionary<string, object?>? BuildVisiblePopupState(Node root)
    {
        var popup = FindVisibleVerticalPopup(root);
        var options = popup != null
            ? GetPopupOptions(popup)
            : GetVisiblePopupButtonOptions(root);

        var stateOptions = new List<Dictionary<string, object?>>();
        foreach (var option in options)
        {
            stateOptions.Add(new Dictionary<string, object?>
            {
                ["name"] = option.Name,
                ["enabled"] = option.Button.IsEnabled
            });
        }

        if (stateOptions.Count == 0)
            return null;

        return new Dictionary<string, object?>
        {
            ["state_type"] = "popup",
            ["message"] = popup != null ? GetVerticalPopupText(popup, "TitleLabel") ?? "Popup active." : "Popup active.",
            ["body"] = popup != null ? GetVerticalPopupText(popup, "BodyLabel") : null,
            ["options"] = stateOptions
        };
    }

    private static MegaCrit.Sts2.Core.Nodes.Ftue.NAcceptTutorialsFtue? FindVisibleAcceptTutorialsFtue(Node root)
    {
        var openModal = GetOpenModalNode();
        if (openModal is MegaCrit.Sts2.Core.Nodes.Ftue.NAcceptTutorialsFtue openFtue &&
            IsFtueNodeActive(openFtue))
        {
            return openFtue;
        }

        foreach (var ftue in FindAll<MegaCrit.Sts2.Core.Nodes.Ftue.NAcceptTutorialsFtue>(root))
        {
            if (IsFtueNodeActive(ftue) || ReferenceEquals(openModal, ftue))
                return ftue;
        }
        return null;
    }

    private static NVerticalPopup? FindVisibleVerticalPopup(Node root)
    {
        var openModal = GetOpenModalNode();
        if (openModal is NVerticalPopup openPopup && GetPopupOptions(openPopup).Count > 0)
            return openPopup;

        if (openModal != null)
        {
            foreach (var popup in FindAll<NVerticalPopup>(openModal))
            {
                if (GetPopupOptions(popup).Count > 0)
                    return popup;
            }
        }

        foreach (var popup in FindAll<NVerticalPopup>(root))
        {
            if ((IsNodeVisible(popup) || (openModal != null && IsDescendantOf(popup, openModal))) &&
                GetPopupOptions(popup).Count > 0)
            {
                return popup;
            }
        }
        return null;
    }

    private static List<(string Name, NClickableControl Button)> GetPopupOptions(NVerticalPopup popup)
    {
        var options = new List<(string Name, NClickableControl Button)>();
        AddPopupOption(options, popup.YesButton, "yes");
        AddPopupOption(options, popup.NoButton, "no");
        return options;
    }

    private static List<(string Name, NClickableControl Button)> GetVisiblePopupButtonOptions(Node root)
    {
        var options = new List<(string Name, NClickableControl Button)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var openModal = GetOpenModalNode();
        if (openModal != null)
        {
            foreach (var button in FindAll<NClickableControl>(openModal))
                AddVisiblePopupButtonOption(options, seen, button, null, openModal);
        }

        foreach (var button in FindAll<NPopupYesNoButton>(root))
        {
            AddVisiblePopupButtonOption(options, seen, button, button.IsYes ? "yes" : "no", null);
        }

        foreach (var modal in FindAll<NModalContainer>(root))
        {
            if (!IsNodeVisible(modal))
                continue;

            foreach (var button in FindAll<NClickableControl>(modal))
                AddVisiblePopupButtonOption(options, seen, button, null, null);
        }

        return options;
    }

    private static void AddVisiblePopupButtonOption(
        List<(string Name, NClickableControl Button)> options,
        HashSet<string> seen,
        NClickableControl? button,
        string? fallback,
        Node? openModal)
    {
        if (!(IsControlVisibleInTree(button) || IsControlVisibleInOpenModal(button, openModal)))
            return;

        var label = button is NPopupYesNoButton popupButton
            ? GetPopupButtonLabel(popupButton)
            : GetClickableControlLabel(button!);
        var name = NormalizeMenuOptionName(label) ?? fallback;
        if (string.IsNullOrWhiteSpace(name) || !seen.Add(name))
            return;

        options.Add((name, button!));
    }

    private static void AddPopupOption(
        List<(string Name, NClickableControl Button)> options,
        NPopupYesNoButton? button,
        string fallback)
    {
        if (!IsPopupButtonActionable(button))
            return;

        var label = GetPopupButtonLabel(button!);
        var name = NormalizeMenuOptionName(label) ?? fallback;
        options.Add((name, button!));
    }

    private static string? GetVerticalPopupText(NVerticalPopup popup, string propertyName)
    {
        var label = popup.GetType().GetProperty(propertyName)?.GetValue(popup);
        return GetNodeText(label);
    }

    private static string? GetPopupButtonLabel(NPopupYesNoButton button)
    {
        var label = GetInstanceFieldValue(button, "_label");
        return GetNodeText(label);
    }

    private static string? GetClickableControlLabel(NClickableControl button)
    {
        foreach (var fieldName in new[] { "_label", "_textLabel", "_title", "_buttonLabel" })
        {
            var label = GetNodeText(GetInstanceFieldValue(button, fieldName));
            if (!string.IsNullOrWhiteSpace(label))
                return label;
        }

        foreach (var propName in new[] { "Text", "Label", "Title" })
        {
            var label = SafeGetText(() => button.GetType().GetProperty(propName)?.GetValue(button));
            if (!string.IsNullOrWhiteSpace(label))
                return label;
        }

        var childText = FindNodeTextRecursive(button);
        if (!string.IsNullOrWhiteSpace(childText))
            return childText;

        var nodeName = button.Name.ToString();
        if (!string.IsNullOrWhiteSpace(nodeName))
            return nodeName.EndsWith("Button", StringComparison.OrdinalIgnoreCase)
                ? nodeName[..^"Button".Length]
                : nodeName;

        return null;
    }

    private static string? FindNodeTextRecursive(Node start)
    {
        if (!IsLiveNode(start))
            return null;

        var text = GetNodeText(start);
        if (!string.IsNullOrWhiteSpace(text))
            return text;

        Godot.Collections.Array<Node> children;
        try
        {
            children = start.GetChildren();
        }
        catch (ObjectDisposedException)
        {
            return null;
        }

        foreach (var child in children)
        {
            text = FindNodeTextRecursive(child);
            if (!string.IsNullOrWhiteSpace(text))
                return text;
        }

        return null;
    }

    private static string? GetNodeText(object? node)
    {
        if (node == null)
            return null;

        foreach (var propName in new[] { "Text", "BbcodeText" })
        {
            var text = SafeGetText(() => node.GetType().GetProperty(propName)?.GetValue(node));
            if (!string.IsNullOrWhiteSpace(text))
                return text;
        }

        return null;
    }

    private static string? NormalizeMenuOptionName(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var sb = new StringBuilder();
        foreach (var ch in StripRichTextTags(text).Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(ch))
                sb.Append(ch);
            else if (sb.Length > 0 && sb[^1] != '_')
                sb.Append('_');
        }

        var normalized = sb.ToString().Trim('_');
        return normalized.Length == 0 ? null : normalized;
    }

    private static Node? FindVisibleGenericFtue(Node start)
    {
        if (ReferenceEquals(start, (Godot.Engine.GetMainLoop() as SceneTree)?.Root))
        {
            var openModal = GetOpenModalNode();
            if (openModal != null)
            {
                var openFtue = FindVisibleGenericFtue(openModal);
                if (openFtue != null)
                    return openFtue;
            }
        }

        if (!IsLiveNode(start))
            return null;

        var typeName = start.GetType().FullName ?? "";
        if (typeName.StartsWith("MegaCrit.Sts2.Core.Nodes.Ftue.", StringComparison.Ordinal) &&
            !typeName.EndsWith(".NAcceptTutorialsFtue", StringComparison.Ordinal) &&
            IsFtueNodeActive(start))
        {
            return start;
        }

        Godot.Collections.Array<Node> children;
        try
        {
            children = start.GetChildren();
        }
        catch (ObjectDisposedException)
        {
            return null;
        }

        foreach (var child in children)
        {
            var val = FindVisibleGenericFtue(child);
            if (val != null)
                return val;
        }

        return null;
    }

    private static NClickableControl? FindFtueAdvanceButton(Node ftue)
    {
        foreach (var fieldName in new[]
        {
            "_confirmButton",
            "_advanceButton",
            "_nextButton",
            "_proceedButton",
            "_acknowledgeButton",
            "_arrowButton",
            "_rightArrowButton",
            "_rightButton"
        })
        {
            if (GetInstanceFieldValue(ftue, fieldName) is NClickableControl fieldButton &&
                IsPopupButtonActionable(fieldButton))
            {
                return fieldButton;
            }
        }

        try
        {
            foreach (var field in ftue.GetType().GetFields(
                         System.Reflection.BindingFlags.Public |
                         System.Reflection.BindingFlags.NonPublic |
                         System.Reflection.BindingFlags.Instance))
            {
                if (field.GetValue(ftue) is NClickableControl fieldButton &&
                    IsPopupButtonActionable(fieldButton))
                {
                    return fieldButton;
                }
            }
        }
        catch (ObjectDisposedException)
        {
            return null;
        }

        foreach (var button in FindAll<NClickableControl>(ftue))
        {
            if (IsPopupButtonActionable(button))
                return button;
        }

        return null;
    }
}
