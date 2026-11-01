namespace Sts2Sim.Core.Registry;

public static class CharacterRegistry
{
    private static readonly Lazy<DefTable<CharacterDef>> Table = new(() => DefDiscovery.Build<CharacterDef>(d => d.Id));

    public static DefTable<CharacterDef> Defs => Table.Value;

    public static CharacterDef Get(string id) => Defs.Get(id);

    public static bool TryGet(string id, out CharacterDef def) => Defs.TryGet(id, out def);

    public static IReadOnlyList<string> AllIds => Defs.AllIds;
}
