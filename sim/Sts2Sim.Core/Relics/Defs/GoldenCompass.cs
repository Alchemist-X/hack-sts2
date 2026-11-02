namespace Sts2Sim.Core.Relics;

public sealed class GoldenCompass : RelicModel
{
    public const string RelicId = "GOLDEN_COMPASS";

    private int _goldenPathAct = -1;

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Ancient;

    public override bool HasUponPickupEffect => true;

    public int GoldenPathAct
    {
        get => _goldenPathAct;
        set => _goldenPathAct = value;
    }

    public override void AfterObtained(RunState rs)
    {
        GoldenPathAct = rs.CurrentActIndex;
        if (rs.Map == null)
        {
            return;
        }
        RunLoop.GenerateMap(rs);
    }

    public override ActMap ModifyGeneratedMap(RunState rs, ActMap map, int actIndex)
    {
        if (GoldenPathAct != actIndex)
        {
            return map;
        }
        return new GoldenPathActMap(rs);
    }

    public override IReadOnlySet<RoomType> ModifyUnknownMapPointRoomTypes(RunState rs, IReadOnlySet<RoomType> roomTypes)
    {
        if (GoldenPathAct != rs.CurrentActIndex)
        {
            return roomTypes;
        }
        var set = new HashSet<RoomType>();
        set.Add(RoomType.Event);
        return set;
    }

    protected override void WriteExtraCanonical(CanonicalWriter w)
    {
        w.WriteInt32(_goldenPathAct);
    }
}
