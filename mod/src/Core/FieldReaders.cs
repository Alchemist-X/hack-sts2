using System;
using System.Collections.Concurrent;
using System.Reflection;

namespace Sts2Recorder.Core;

/// <summary>Cache metadata, never values: dynamic candidates are read afresh.</summary>
internal static class FieldReaders
{
    private sealed record Lookup(FieldInfo? Field);
    private static readonly ConcurrentDictionary<(Type, string), Lookup> Cache = new();

    public static object? Read(object source, string name) =>
        Cache.GetOrAdd((source.GetType(), name), key => Find(key.Item1, key.Item2)).Field?.GetValue(source);

    private static Lookup Find(Type type, string name)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public |
            BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        for (Type? current = type; current != null; current = current.BaseType)
            if (current.GetField(name, flags) is { } field) return new Lookup(field);
        return new Lookup(null);
    }
}
