using System.Collections.Frozen;
using System.Reflection;

namespace Sts2Sim.Core.Registry;

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class SimExcludeAttribute : Attribute
{
}

public sealed class DefTable<T> where T : class
{
    private readonly FrozenDictionary<string, T> _byId;
    private readonly string[] _ids;

    internal DefTable(FrozenDictionary<string, T> byId)
    {
        _byId = byId;
        _ids = byId.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray();
    }

    public int Count => _ids.Length;

    public IReadOnlyList<string> AllIds => _ids;

    public T Get(string id) => _byId.TryGetValue(id, out var def)
        ? def
        : throw new KeyNotFoundException($"No {typeof(T).Name} registered with id '{id}'");

    public bool TryGet(string id, out T def)
    {
        if (_byId.TryGetValue(id, out var found))
        {
            def = found;
            return true;
        }
        def = null!;
        return false;
    }

    public bool Contains(string id) => _byId.ContainsKey(id);
}

public static class DefDiscovery
{
    public static IEnumerable<Type> ConcreteSubclassesOf<T>()
    {
        var baseType = typeof(T);
        return baseType.Assembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && baseType.IsAssignableFrom(t)
                        && t.GetConstructor(Type.EmptyTypes) != null
                        && !t.IsDefined(typeof(SimExcludeAttribute), false))
            .OrderBy(t => t.FullName, StringComparer.Ordinal);
    }

    public static DefTable<T> Build<T>(Func<T, string> idOf) where T : class
    {
        var map = new Dictionary<string, T>(StringComparer.Ordinal);
        foreach (var type in ConcreteSubclassesOf<T>())
        {
            var instance = (T)Activator.CreateInstance(type)!;
            var id = idOf(instance);
            if (string.IsNullOrEmpty(id))
            {
                throw new InvalidOperationException($"{type.FullName} has an empty id");
            }
            if (!map.TryAdd(id, instance))
            {
                throw new InvalidOperationException($"Duplicate {typeof(T).Name} id '{id}': {map[id].GetType().FullName} and {type.FullName}");
            }
        }
        return new DefTable<T>(map.ToFrozenDictionary(StringComparer.Ordinal));
    }
}

public static class CardRegistry
{
    private static readonly Lazy<DefTable<CardDef>> Table = new(() => DefDiscovery.Build<CardDef>(d => d.Id));

    public static DefTable<CardDef> Defs => Table.Value;

    public static CardDef Get(string id) => Defs.Get(id);

    public static bool TryGet(string id, out CardDef def) => Defs.TryGet(id, out def);

    public static IReadOnlyList<string> AllIds => Defs.AllIds;
}

public static class MonsterRegistry
{
    private static readonly Lazy<DefTable<MonsterDef>> Table = new(() => DefDiscovery.Build<MonsterDef>(d => d.Id));

    public static DefTable<MonsterDef> Defs => Table.Value;

    public static MonsterDef Get(string id) => Defs.Get(id);

    public static bool TryGet(string id, out MonsterDef def) => Defs.TryGet(id, out def);

    public static IReadOnlyList<string> AllIds => Defs.AllIds;
}

public static class EncounterRegistry
{
    private static readonly Lazy<DefTable<EncounterDef>> Table = new(() => DefDiscovery.Build<EncounterDef>(d => d.Id));

    public static DefTable<EncounterDef> Defs => Table.Value;

    public static EncounterDef Get(string id) => Defs.Get(id);

    public static bool TryGet(string id, out EncounterDef def) => Defs.TryGet(id, out def);

    public static IReadOnlyList<string> AllIds => Defs.AllIds;
}

public static class PowerRegistry
{
    private sealed class Entry
    {
        public Entry(Type type, Func<PowerModel> factory)
        {
            Type = type;
            Factory = factory;
        }

        public Type Type { get; }

        public Func<PowerModel> Factory { get; }
    }

    private static readonly Lazy<FrozenDictionary<string, Entry>> Table = new(BuildTable);

    private static FrozenDictionary<string, Entry> BuildTable()
    {
        var map = new Dictionary<string, Entry>(StringComparer.Ordinal);
        foreach (var type in DefDiscovery.ConcreteSubclassesOf<PowerModel>())
        {
            var ctor = type.GetConstructor(Type.EmptyTypes)!;
            Func<PowerModel> factory = () => (PowerModel)ctor.Invoke(null);
            var id = factory().Id;
            if (!map.TryAdd(id, new Entry(type, factory)))
            {
                throw new InvalidOperationException($"Duplicate power id '{id}': {map[id].Type.FullName} and {type.FullName}");
            }
        }
        return map.ToFrozenDictionary(StringComparer.Ordinal);
    }

    public static int Count => Table.Value.Count;

    public static IReadOnlyList<string> AllIds => Table.Value.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray();

    public static PowerModel Create(string id) => Table.Value.TryGetValue(id, out var e)
        ? e.Factory()
        : throw new KeyNotFoundException($"No power registered with id '{id}'");

    public static Type TypeOf(string id) => Table.Value[id].Type;
}
