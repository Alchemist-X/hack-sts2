namespace Sts2Sim.Core.Relics;

public sealed class SmallCapsule : RelicModel
{
    public const string RelicId = "SMALL_CAPSULE";

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Ancient;

    public const string StandaloneSkipSite = "pickup reward offer skipped: standalone combat has no player rng / odds (CONTRACTS C4)";

    internal static bool HasRunLayer(RunState rs, RelicModel relic)
    {
        var owner = relic.Owner;
        if (owner.PlayerRng != null && owner.PlayerOdds != null)
        {
            return true;
        }
        rs.Fidelity.Record(FidelityFlag.ApproximatedRule, relic.Id + ": " + StandaloneSkipSite);
        return false;
    }

    public override void AfterObtained(RunState rs)
    {
        if (!HasRunLayer(rs, this))
        {
            return;
        }
        var rewards = new List<Reward>(1)
        {
            new RelicReward(Owner)
        };
        RewardsCmd.OfferCustom(rs, Owner, rewards);
    }
}
