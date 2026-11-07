using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Sts2Sim.Core.Cards;
using Sts2Sim.Core.Cards.Enchantments;
using Sts2Sim.Core.Potions;
using Sts2Sim.Core.Relics;
using Sts2Sim.Core.Run;

namespace Sts2Sim.Vec;

/// <summary>
/// The numbers and items an event option's button text names, resolved on the live event model (the
/// <c>event_option_values</c> table). Which placeholders an option shows comes from the game's localisation templates
/// (<see cref="Generated"/>, written by sim/tools/vec/tools/gen_event_option_vars.py); each name is resolved exactly as
/// the play server's rules text does (Sts2Sim.Server/RulesText.cs EventScope): <c>{Relic}</c> / <c>{RelicName}</c> = the
/// option's relic; else, for the name, then name + "Amount", then name + "Gain", a <c>&lt;Name&gt;For(RunState)</c>
/// method, a public property or a public field of the event (static or instance); else, for Relic / RelicName / Potion /
/// Card / Curse, the event's single public const id of that kind. These are values the option text shows the player
/// (docs/parity-audit-2026-11-02.md); event internals that no text names are never read (H9/H11). Pure: getters and
/// <c>For</c> methods are read-only accessors (the encoder purity test checks the state hash).
/// </summary>
public static partial class EventOptionVars
{
    /// <summary>What a resolved placeholder holds.</summary>
    public enum ValueKind
    {
        None,
        Number,
        Item,
    }

    private static readonly Lazy<Dictionary<string, string[]>> ByKey = new(() =>
        Generated.ToDictionary(x => x.Key, x => x.Names, StringComparer.OrdinalIgnoreCase));

    private static readonly ConcurrentDictionary<(Type Type, string Name), Accessor> Accessors = new();

    [ThreadStatic]
    private static object?[]? _args;

    /// <summary>The placeholder names of an option text key, or null when its text names none.</summary>
    public static string[]? NamesOf(string? textKey)
        => textKey != null && ByKey.Value.TryGetValue(textKey, out var names) ? names : null;

    /// <summary>Number of option keys in the generated table.</summary>
    public static int Count => ByKey.Value.Count;

    /// <summary>
    /// Resolves one placeholder of <paramref name="option"/>: a number (truncated to an int as the UI shows it) or an
    /// item (vocabulary kind name + id); <see cref="ValueKind.None"/> for flags, texts and names without a value.
    /// </summary>
    public static ValueKind Resolve(EventModel ev, EventOption option, RunState rs, string name, out int number, out string? itemKind, out int itemId)
    {
        number = 0;
        itemKind = null;
        itemId = 0;
        if ((name == "Relic" || name == "RelicName") && option.RelicId is { } relicId)
        {
            return Item(relicId, "relic", out itemKind, out itemId);
        }
        var acc = Accessors.GetOrAdd((ev.GetType(), name), static key => Accessor.Build(key.Type, key.Name));
        object? value;
        try
        {
            value = acc.Read(ev, rs);
        }
        catch (Exception)
        {
            return ValueKind.None; // the server's rules text shows '?' then; nothing to encode
        }
        switch (value)
        {
            case int i:
                number = i;
                return ValueKind.Number;
            case long l:
                number = (int)Math.Clamp(l, int.MinValue, int.MaxValue);
                return ValueKind.Number;
            case decimal d:
                number = (int)decimal.Truncate(Math.Clamp(d, int.MinValue, int.MaxValue));
                return ValueKind.Number;
            case double db when double.IsFinite(db):
                number = (int)Math.Clamp(Math.Truncate(db), int.MinValue, int.MaxValue);
                return ValueKind.Number;
            case float f when float.IsFinite(f):
                number = (int)Math.Clamp(MathF.Truncate(f), int.MinValue, int.MaxValue);
                return ValueKind.Number;
            case string s:
                return Item(s, null, out itemKind, out itemId);
            case CardInstance c:
                return Item(c.Id, "card", out itemKind, out itemId);
            case RelicModel r:
                return Item(r.Id, "relic", out itemKind, out itemId);
            case PotionModel p:
                return Item(p.Id, "potion", out itemKind, out itemId);
            case EnchantmentModel e:
                return Item(e.Id, "enchantment", out itemKind, out itemId);
            default:
                return ValueKind.None;
        }
    }

    /// <summary>An id string as an item: the first vocabulary that lists it (or <paramref name="kind"/>'s only).</summary>
    private static ValueKind Item(string id, string? kind, out string? itemKind, out int itemId)
    {
        itemKind = null;
        itemId = 0;
        foreach (var (k, list) in ItemVocabs)
        {
            if (kind != null && k != kind)
            {
                continue;
            }
            int index = list.Index(id);
            if (index > VocabList.Unknown)
            {
                itemKind = k;
                itemId = index;
                return ValueKind.Item;
            }
        }
        return ValueKind.None;
    }

    private static readonly (string Kind, VocabList List)[] ItemVocabs =
    {
        ("card", Vocab.Cards), ("relic", Vocab.Relics), ("potion", Vocab.Potions), ("enchantment", Vocab.Enchantments),
        ("affliction", Vocab.Afflictions),
    };

    /// <summary>How one (event type, placeholder) is read; built once per pair by reflection, then cached.</summary>
    private sealed class Accessor
    {
        private MethodInfo? _method;
        private PropertyInfo? _property;
        private FieldInfo? _field;
        private object? _constant;
        private bool _static;

        public object? Read(EventModel ev, RunState rs)
        {
            if (_method != null)
            {
                var args = _args ??= new object?[1];
                args[0] = rs;
                try
                {
                    return _method.Invoke(_static ? null : ev, args);
                }
                finally
                {
                    args[0] = null;
                }
            }
            if (_property != null)
            {
                return _property.GetValue(_static ? null : ev);
            }
            if (_field != null)
            {
                return _field.GetValue(_static ? null : ev);
            }
            return _constant;
        }

        private const BindingFlags Flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.IgnoreCase | BindingFlags.FlattenHierarchy;

        [UnconditionalSuppressMessage("Trimming", "IL2070", Justification = "Sts2Sim.Core is rooted in the NativeAOT publish (TrimmerRootAssembly); event members are kept.")]
        [UnconditionalSuppressMessage("Trimming", "IL2075", Justification = "Sts2Sim.Core is rooted in the NativeAOT publish (TrimmerRootAssembly); event members are kept.")]
        public static Accessor Build(Type type, string name)
        {
            var a = new Accessor();
            try
            {
                foreach (var candidate in new[] { name, name + "Amount", name + "Gain" })
                {
                    if (type.GetMethod(candidate + "For", Flags, new[] { typeof(RunState) }) is { } method)
                    {
                        a._method = method;
                        a._static = method.IsStatic;
                        return a;
                    }
                    if (type.GetProperty(candidate, Flags) is { } prop && prop.GetIndexParameters().Length == 0 && prop.GetGetMethod() is { } getter)
                    {
                        a._property = prop;
                        a._static = getter.IsStatic;
                        return a;
                    }
                    if (type.GetField(candidate, Flags) is { } field)
                    {
                        if (field.IsLiteral)
                        {
                            a._constant = field.GetRawConstantValue();
                        }
                        else
                        {
                            a._field = field;
                            a._static = field.IsStatic;
                        }
                        return a;
                    }
                }
            }
            catch (AmbiguousMatchException)
            {
                return a; // the server's rules text fails the same way ('?'); no value
            }
            a._constant = ModelConstant(type, name);
            return a;
        }

        // {Relic}/{RelicName}/{Potion}/{Card}/{Curse} without a same-named member: the event's single public const id of
        // that kind (SunkenStatue.SwordOfStoneId = "SWORD_OF_STONE"); null when there is none or it is ambiguous.
        [UnconditionalSuppressMessage("Trimming", "IL2070", Justification = "Sts2Sim.Core is rooted in the NativeAOT publish (TrimmerRootAssembly); event members are kept.")]
        private static string? ModelConstant(Type type, string name)
        {
            VocabList? list = name switch
            {
                "Relic" or "RelicName" => Vocab.Relics,
                "Potion" => Vocab.Potions,
                "Card" or "Curse" => Vocab.Cards,
                _ => null,
            };
            if (list == null)
            {
                return null;
            }
            string? match = null;
            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy))
            {
                if (field.IsLiteral && field.FieldType == typeof(string) && field.Name != "EventId"
                    && field.GetRawConstantValue() is string id && list.Index(id) > VocabList.Unknown)
                {
                    if (match != null && match != id)
                    {
                        return null;
                    }
                    match = id;
                }
            }
            return match;
        }
    }
}
