namespace Sts2Sim.Core.Relics;

public sealed class Pantograph : RelicModel
{
    public const string RelicId = "PANTOGRAPH";

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Uncommon;

    protected override IEnumerable<DynamicVar> CanonicalVars => new[]
    {
        new HealVar(25m)
    };

    public override void AfterRoomEntered(RunState rs, AbstractRoom room)
    {
        if (Owner.IsDead)
        {
            return;
        }
        var map = rs.Map;
        var coord = RoomStack.CurrentMapCoord(rs);
        var current = coord is { } c ? map?.GetPoint(c) : null;
        bool flag = map != null && current != null && map.BossMapPoint.parents.Contains(current);
        Status = flag ? RelicStatus.Active : RelicStatus.Normal;
    }

    public override void BeforeCombatStart(RunState rs)
    {
        if (!Owner.IsDead && RoomStack.CurrentRoom(rs)?.RoomType == RoomType.Boss)
        {
            CreatureCmd.Heal(rs, Owner, DynamicVars.Heal.BaseValue);
        }
    }
}
