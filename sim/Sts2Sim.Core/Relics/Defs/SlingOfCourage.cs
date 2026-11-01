namespace Sts2Sim.Core.Relics;

public sealed class SlingOfCourage : RelicModel
{
    public const string RelicId = "SLING_OF_COURAGE";

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Shop;

    protected override IEnumerable<DynamicVar> CanonicalVars => new[]
    {
        new PowerVar<StrengthPower>(2m)
    };

    public override void AfterRoomEntered(RunState rs, AbstractRoom room)
        => throw new NotImplementedException("W3 run-layer: SlingOfCourage.AfterRoomEntered (AbstractRoom.RoomType == Elite)");
}
