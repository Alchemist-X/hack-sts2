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
    {
        if (room.RoomType == RoomType.Elite)
        {
            var cs = rs.Combat ?? throw new InvalidOperationException("SLING_OF_COURAGE: an elite room was entered without a combat set up (Models.Relics/SlingOfCourage.cs AfterRoomEntered).");
            PowerCmd.Apply<StrengthPower>(cs, Owner, DynamicVars.Strength.BaseValue, Owner, null);
        }
    }
}
