namespace Sts2Sim.Core.Registry;

public static class ActRegistry
{
    private static readonly Lazy<DefTable<ActModel>> Table = new(() => DefDiscovery.Build<ActModel>(d => d.Id));

    public static DefTable<ActModel> Defs => Table.Value;

    public static ActModel Get(string id) => Defs.Get(id);

    public static bool TryGet(string id, out ActModel def) => Defs.TryGet(id, out def);

    public static IReadOnlyList<string> AllIds => Defs.AllIds;
}

public static class EventRegistry
{
    private static readonly Lazy<ModelTable<EventModel>> Table = new(() => ModelTable<EventModel>.Build(m => m.Id));

    public static ModelTable<EventModel> Models => Table.Value;

    public static int Count => Table.Value.Count;

    public static IReadOnlyList<string> AllIds => Table.Value.AllIds;

    public static EventModel Create(string id) => Table.Value.Create(id);

    public static Type TypeOf(string id) => Table.Value.TypeOf(id);
}
