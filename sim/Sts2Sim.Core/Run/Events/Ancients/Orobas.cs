namespace Sts2Sim.Core.Run;

public sealed class Orobas : AncientEventModel
{
    public const string EventId = "OROBAS";

    public const float PrismaticOdds = 0.3333333f;

    public const string SeaGlassId = "SEA_GLASS";

    public const string PrismaticGemId = "PRISMATIC_GEM";

    public const string TouchOfOrobasId = "TOUCH_OF_OROBAS";

    public const string ArchaicToothId = "ARCHAIC_TOOTH";

    public const string OptionPool3LockedKey = "OROBAS.pages.INITIAL.options.OPTION_POOL_3_LOCKED";

    public static readonly IReadOnlyList<string> OptionPool1Ids = new[] { "ELECTRIC_SHRYMP", "GLASS_EYE" };

    public static readonly IReadOnlyList<string> OptionPool2Ids = new[] { "ALCHEMICAL_COFFER", "DRIFTWOOD", "RADIANT_PEARL", "SAND_CASTLE" };

    public override string Id => EventId;

    private List<EventOption> Pool(IReadOnlyList<string> ids)
    {
        var list = new List<EventOption>(ids.Count);
        for (int i = 0; i < ids.Count; i++)
        {
            list.Add(AncientRelicOption(ids[i]));
        }
        return list;
    }

    private EventOption SeaGlassOption(string characterId)
    {
        var relic = CreateOptionRelic(SeaGlassId);
        if (relic is ICharacterBoundRelic bound)
        {
            bound.CharacterId = characterId;
        }
        var option = AncientRelicOption(SeaGlassId, relic);
        option.StringArg = characterId;
        return option;
    }

    private List<EventOption> OptionPool3(RunState rs)
    {
        var list = new List<EventOption>(2);
        var owner = Owner;
        var touch = CreateOptionRelic(TouchOfOrobasId);
        if (owner == null || SetupRelicForPlayer(rs, owner, touch, TouchOfOrobasId))
        {
            list.Add(AncientRelicOption(TouchOfOrobasId, touch));
        }
        var tooth = CreateOptionRelic(ArchaicToothId);
        if (owner == null || SetupRelicForPlayer(rs, owner, tooth, ArchaicToothId))
        {
            list.Add(AncientRelicOption(ArchaicToothId, tooth));
        }
        if (list.Count == 0)
        {
            list.Add(new EventOption(OptionPool3LockedKey, null));
        }
        return list;
    }

    protected override IReadOnlyList<EventOption> GenerateInitialOptions(RunState rs)
    {
        var player = Owner ?? throw new InvalidOperationException("Orobas has no owner.");
        var rng = Rng ?? throw new InvalidOperationException("Orobas has no rng.");
        string character = player.CharacterId;
        var others = new List<string>();
        var characters = rs.Unlocks.Characters;
        for (int i = 0; i < characters.Count; i++)
        {
            if (!string.Equals(characters[i], character, StringComparison.Ordinal))
            {
                others.Add(characters[i]);
            }
        }
        string seaGlassCharacter = rng.NextItem((IEnumerable<string>)others) ?? character;
        var pool1 = Pool(OptionPool1Ids);
        EventOption extra = rng.NextFloat() < PrismaticOdds ? AncientRelicOption(PrismaticGemId) : SeaGlassOption(seaGlassCharacter);
        pool1.Add(extra);
        var first = rng.NextItem((IEnumerable<EventOption>)pool1)!;
        var second = rng.NextItem((IEnumerable<EventOption>)Pool(OptionPool2Ids))!;
        var third = rng.NextItem((IEnumerable<EventOption>)OptionPool3(rs))!;
        return new[] { first, second, third };
    }
}
