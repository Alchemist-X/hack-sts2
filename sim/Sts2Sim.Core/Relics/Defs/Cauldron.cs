namespace Sts2Sim.Core.Relics;

public sealed class Cauldron : RelicModel
{
    public const string RelicId = "CAULDRON";

    public const string PotionsKey = "Potions";

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Shop;

    public override bool HasUponPickupEffect => true;

    protected override IEnumerable<DynamicVar> CanonicalVars => new[]
    {
        new DynamicVar(PotionsKey, 5m)
    };

    public override void AfterObtained(RunState rs)
    {
        if (Owner.PlayerRng == null)
        {
            return;
        }
        RewardsCmd.OfferCustom(rs, Owner, GenerateRewards());
    }

    private List<Reward> GenerateRewards()
    {
        int intValue = DynamicVars[PotionsKey].IntValue;
        var val = new List<Reward>();
        for (int j = 0; j < intValue; j++)
        {
            val.Add(new PotionReward(Owner));
        }
        return val;
    }
}
