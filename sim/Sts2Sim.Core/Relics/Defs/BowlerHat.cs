namespace Sts2Sim.Core.Relics;

public sealed class BowlerHat : RelicModel
{
    public const string RelicId = "BOWLER_HAT";

    public const string GoldIncreaseKey = "GoldIncrease";

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Uncommon;

    public override bool IsAllowedInShops => false;

    protected override IEnumerable<DynamicVar> CanonicalVars => new[]
    {
        new DynamicVar(GoldIncreaseKey, 1.25m)
    };

    public override bool IsAllowed(RunState rs) => IsBeforeAct3TreasureChest(rs.Players.Count, rs.TotalFloor);

    public override decimal ModifyGoldGained(RunState rs, PlayerState player, decimal amount)
    {
        if (!ReferenceEquals(player, Owner))
        {
            return amount;
        }
        return amount * DynamicVars[GoldIncreaseKey].BaseValue;
    }
}
