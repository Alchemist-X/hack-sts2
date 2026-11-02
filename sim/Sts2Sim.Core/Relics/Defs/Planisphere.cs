namespace Sts2Sim.Core.Relics;

public sealed class Planisphere : RelicModel
{
    public const string RelicId = "PLANISPHERE";

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Uncommon;

    protected override IEnumerable<DynamicVar> CanonicalVars => new[]
    {
        new HealVar(5m)
    };

    public override bool IsAllowed(RunState rs) => IsBeforeAct3TreasureChest(rs);

    public override void AfterRoomEntered(RunState rs, AbstractRoom room)
    {
        if (Owner.IsDead)
        {
            return;
        }
        var currentMapPoint = RunLoop.CurrentMapPoint(rs);
        if (currentMapPoint != null && currentMapPoint.PointType == MapPointType.Unknown && rs.CurrentRooms.Count <= 1)
        {
            CreatureCmd.Heal(rs, Owner, DynamicVars.Heal.BaseValue);
        }
    }
}
