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
        => throw new NotImplementedException("W3 run-layer: Pantograph.AfterRoomEntered (RunState.Map.BossMapPoint.parents, CurrentMapPoint)");

    public override void BeforeCombatStart(RunState rs)
        => throw new NotImplementedException("W3 run-layer: Pantograph.BeforeCombatStart (RunState.CurrentRoom.RoomType == Boss)");
}
