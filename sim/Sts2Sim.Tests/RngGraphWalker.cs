using System.Collections;
using System.Reflection;
using Sts2Sim.Core.Random;

namespace Sts2Sim.Tests;

/// <summary>
/// Test-only reflection walk over a simulator object graph: every <see cref="Rng"/> object reachable from a root
/// (instance fields of Sts2Sim types, collections, boxed structs), with the first path that reached it. Used by the
/// determinization tests to prove that CloneDeterminized reseeds every live RNG object, not only the ones it names.
/// </summary>
public static class RngGraphWalker
{
    private const BindingFlags InstanceFields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

    private static readonly Dictionary<Type, FieldInfo[]> FieldCache = new();

    public static List<(string Path, Rng Rng)> Collect(object root, Func<object, bool>? skip = null)
    {
        var found = new List<(string, Rng)>();
        var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
        var queue = new Queue<(object Obj, string Path)>();
        queue.Enqueue((root, root.GetType().Name));
        while (queue.Count > 0)
        {
            var (obj, path) = queue.Dequeue();
            var type = obj.GetType();
            if (!type.IsValueType && !seen.Add(obj))
            {
                continue;
            }
            if (obj is Rng rng)
            {
                found.Add((path, rng));
                continue;
            }
            if (skip != null && skip(obj))
            {
                continue;
            }
            if (obj is IEnumerable seq && obj is not string)
            {
                int i = 0;
                foreach (var item in seq)
                {
                    if (item != null && Follow(item.GetType()))
                    {
                        queue.Enqueue((item, $"{path}[{i}]"));
                    }
                    i++;
                }
                if (!IsSimType(type))
                {
                    continue;
                }
            }
            if (!IsSimType(type) && !IsKeyValuePair(type))
            {
                continue;
            }
            foreach (var f in FieldsOf(type))
            {
                if (!Follow(f.FieldType))
                {
                    continue;
                }
                object? v;
                try
                {
                    v = f.GetValue(obj);
                }
                catch
                {
                    continue;
                }
                if (v != null && Follow(v.GetType()))
                {
                    queue.Enqueue((v, $"{path}.{Clean(f.Name)}"));
                }
            }
        }
        return found;
    }

    private static string Clean(string name) =>
        name.StartsWith('<') && name.IndexOf('>') is int end and > 0 ? name[1..end] : name;

    private static bool IsSimType(Type t) => t.Namespace?.StartsWith("Sts2Sim", StringComparison.Ordinal) == true;

    private static bool IsKeyValuePair(Type t) => t.IsGenericType && t.GetGenericTypeDefinition() == typeof(KeyValuePair<,>);

    private static bool Follow(Type t)
    {
        if (t.IsPrimitive || t.IsEnum || t == typeof(string) || t == typeof(decimal) || typeof(Delegate).IsAssignableFrom(t) || typeof(Type).IsAssignableFrom(t) || t.IsPointer)
        {
            return false;
        }
        if (IsSimType(t) || IsKeyValuePair(t))
        {
            return true;
        }
        if (t.IsArray)
        {
            return Follow(t.GetElementType()!);
        }
        if (t.IsGenericType)
        {
            return t.GetGenericArguments().Any(Follow);
        }
        return t == typeof(object) || t.IsInterface || t.IsAbstract;
    }

    private static FieldInfo[] FieldsOf(Type type)
    {
        lock (FieldCache)
        {
            if (FieldCache.TryGetValue(type, out var cached))
            {
                return cached;
            }
            var list = new List<FieldInfo>();
            for (var t = type; t != null && t != typeof(object); t = t.BaseType)
            {
                list.AddRange(t.GetFields(InstanceFields));
            }
            var arr = list.ToArray();
            FieldCache[type] = arr;
            return arr;
        }
    }
}
