// Ported from STS2MCP (https://github.com/Gennadiyev/STS2MCP) — McpMod.Helpers.cs.
// Copyright 2026 Yikun Ji (Kunologist). MIT License; this attribution is retained per license.
// Sts2Recorder adaptations: HTTP/markdown helpers dropped (JSON only); all remaining
// helpers are strictly read-only; verified against game v0.99.1 decompiled source.

using System;
using System.Collections.Generic;
using System.Text;
using Godot;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;

namespace Sts2Recorder.Game;

public static partial class PassiveStateBuilder
{
    private static string? SafeGetCardDescription(CardModel card, PileType pile = PileType.Hand)
    {
        try { return StripRichTextTags(card.GetDescriptionForPile(pile)).Replace("\n", " "); }
        catch { return SafeGetText(() => card.Description)?.Replace("\n", " "); }
    }

    private static string? SafeGetText(Func<object?> getter)
    {
        try
        {
            var result = getter();
            if (result == null) return null;
            // If it's a LocString, call GetFormattedText
            if (result is MegaCrit.Sts2.Core.Localization.LocString locString)
                return StripRichTextTags(locString.GetFormattedText());
            return result.ToString();
        }
        catch { return null; }
    }

    private static string StripRichTextTags(string text)
    {
        // Remove BBCode-style tags like [color=red], [/color], etc.
        // Special case: [img]res://path/to/file.png[/img] -> [file.png]
        var sb = new StringBuilder();
        int i = 0;
        while (i < text.Length)
        {
            if (text[i] == '[')
            {
                // Check for [img]...[/img] pattern
                if (text.AsSpan(i).StartsWith("[img]"))
                {
                    int contentStart = i + 5; // length of "[img]"
                    int closeTag = text.IndexOf("[/img]", contentStart, StringComparison.Ordinal);
                    if (closeTag >= 0)
                    {
                        string path = text[contentStart..closeTag];
                        int lastSlash = path.LastIndexOf('/');
                        string filename = lastSlash >= 0 ? path[(lastSlash + 1)..] : path;
                        sb.Append('[').Append(filename).Append(']');
                        i = closeTag + 6; // length of "[/img]"
                        continue;
                    }
                }

                int end = text.IndexOf(']', i);
                if (end >= 0) { i = end + 1; continue; }
            }
            sb.Append(text[i]);
            i++;
        }
        return sb.ToString();
    }

    private static object? GetInstanceFieldValue(object source, string fieldName)
    {
        const System.Reflection.BindingFlags Flags =
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.Public |
            System.Reflection.BindingFlags.NonPublic |
            System.Reflection.BindingFlags.DeclaredOnly;

        for (var type = source.GetType(); type != null; type = type.BaseType)
        {
            var field = type.GetField(fieldName, Flags);
            if (field != null)
                return field.GetValue(source);
        }

        return null;
    }

    private static List<T> FindAll<T>(Node start) where T : Node
    {
        var list = new List<T>();
        if (IsLiveNode(start))
            FindAllRecursive(start, list);
        return list;
    }

    /// <summary>
    /// FindAll variant that sorts results by visual position (row-major: top-to-bottom, left-to-right).
    /// NGridCardHolder.OnFocus() calls MoveToFront() which scrambles child order for z-rendering.
    /// Sorting by GlobalPosition restores the correct visual order for both single-row (card rewards,
    /// choose-a-card) and multi-row (deck selection grids) layouts.
    /// </summary>
    private static List<T> FindAllSortedByPosition<T>(Node start) where T : Control
    {
        var list = FindAll<T>(start);
        list.Sort((a, b) =>
        {
            int cmp = a.GlobalPosition.Y.CompareTo(b.GlobalPosition.Y);
            return cmp != 0 ? cmp : a.GlobalPosition.X.CompareTo(b.GlobalPosition.X);
        });
        return list;
    }

    private static void FindAllRecursive<T>(Node node, List<T> found) where T : Node
    {
        if (!IsLiveNode(node))
            return;
        if (node is T item)
            found.Add(item);
        try
        {
            foreach (var child in node.GetChildren())
                FindAllRecursive(child, found);
        }
        catch (ObjectDisposedException) { }
    }

    private static List<Dictionary<string, object?>> BuildHoverTips(IEnumerable<IHoverTip> tips)
    {
        var result = new List<Dictionary<string, object?>>();
        try
        {
            var seen = new HashSet<string>();
            foreach (var tip in IHoverTip.RemoveDupes(tips))
            {
                try
                {
                    string? title = null;
                    string? description = null;

                    if (tip is HoverTip ht)
                    {
                        title = ht.Title != null ? StripRichTextTags(ht.Title) : null;
                        description = StripRichTextTags(ht.Description);
                    }
                    else if (tip is CardHoverTip cardTip)
                    {
                        title = SafeGetText(() => cardTip.Card.Title);
                        description = SafeGetCardDescription(cardTip.Card);
                    }

                    if (title == null && description == null) continue;

                    string key = title ?? description!;
                    if (!seen.Add(key)) continue;

                    result.Add(new Dictionary<string, object?>
                    {
                        ["name"] = title,
                        ["description"] = description
                    });
                }
                catch { /* skip individual tip on error */ }
            }
        }
        catch { /* return partial results */ }
        return result;
    }

    private static T? FindFirst<T>(Node start) where T : Node
    {
        if (!IsLiveNode(start))
            return null;
        if (start is T result)
            return result;
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
            var val = FindFirst<T>(child);
            if (val != null) return val;
        }
        return null;
    }

    private static bool IsLiveNode(Node? node)
    {
        try
        {
            return node != null && GodotObject.IsInstanceValid(node) && !node.IsQueuedForDeletion();
        }
        catch (ObjectDisposedException)
        {
            return false;
        }
    }

    private static bool IsNodeVisible(CanvasItem? node)
    {
        try
        {
            return node != null && IsLiveNode(node) && node.Visible && node.IsVisibleInTree();
        }
        catch (ObjectDisposedException)
        {
            return false;
        }
    }

    private static bool IsMapScreenOpenOrVisible()
    {
        var mapScreen = NMapScreen.Instance;
        return mapScreen != null && (mapScreen.IsOpen || IsNodeVisible(mapScreen));
    }

    private static bool IsControlVisibleInTree(NClickableControl? control)
    {
        try
        {
            return control != null &&
                   IsLiveNode(control) &&
                   IsNodeVisible(control);
        }
        catch (ObjectDisposedException)
        {
            return false;
        }
    }

    private static Node? GetOpenModalNode()
    {
        try
        {
            return NModalContainer.Instance?.OpenModal as Node;
        }
        catch (ObjectDisposedException)
        {
            return null;
        }
    }

    private static bool IsDescendantOf(Node? node, Node ancestor)
    {
        try
        {
            for (var current = node; current != null && IsLiveNode(current); current = current.GetParent())
            {
                if (ReferenceEquals(current, ancestor))
                    return true;
            }
        }
        catch (ObjectDisposedException) { }

        return false;
    }

    private static bool IsControlVisibleInOpenModal(NClickableControl? control, Node? openModal = null)
    {
        openModal ??= GetOpenModalNode();
        if (control == null || openModal == null || !IsLiveNode(control) || !IsLiveNode(openModal))
            return false;

        try
        {
            return control.Visible && IsDescendantOf(control, openModal);
        }
        catch (ObjectDisposedException)
        {
            return false;
        }
    }

    private static bool IsPopupButtonActionable(NClickableControl? control)
    {
        if (control == null || !IsLiveNode(control))
            return false;

        try
        {
            return control.IsEnabled &&
                   (IsControlVisibleInTree(control) || IsControlVisibleInOpenModal(control));
        }
        catch (ObjectDisposedException)
        {
            return false;
        }
    }

    private static bool IsControlVisibleOrActionable(NClickableControl? control)
    {
        return IsControlVisibleInTree(control) && control!.IsEnabled;
    }
}
