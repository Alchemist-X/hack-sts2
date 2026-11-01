namespace Sts2Sim.Core.Relics;

public sealed class Girya : RelicModel
{
    public const string RelicId = "GIRYA";

    public const int MaxLifts = 3;

    public const string StrengthPowerId = "STRENGTH_POWER";

    private int _timesLifted;

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Rare;

    public override bool ShowCounter => true;

    public override int DisplayAmount => TimesLifted;

    public int TimesLifted
    {
        get => _timesLifted;
        set => _timesLifted = value;
    }

    public override bool IsAllowed(RunState rs) => IsBeforeAct3TreasureChest(rs.Players.Count, rs.TotalFloor);

    public override void AfterRoomEntered(RunState rs, AbstractRoom room)
    {
        if (TimesLifted > 0 && room is CombatRoom)
        {
            var cs = rs.Combat ?? throw new InvalidOperationException("Girya.AfterRoomEntered needs the combat of the entered combat room.");
            PowerCmd.Apply(cs, StrengthPowerId, Owner, TimesLifted, Owner, null);
        }
    }

    public override bool TryModifyRestSiteOptions(RunState rs, PlayerState player, ICollection<RestSiteOption> options)
    {
        if (!ReferenceEquals(player, Owner))
        {
            return false;
        }
        if (TimesLifted >= MaxLifts)
        {
            return false;
        }
        options.Add(new LiftRestSiteOption(player));
        return true;
    }

    protected override void WriteExtraCanonical(CanonicalWriter w)
    {
        w.WriteInt32(_timesLifted);
    }
}
