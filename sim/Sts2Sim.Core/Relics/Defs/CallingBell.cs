namespace Sts2Sim.Core.Relics;

public sealed class CallingBell : RelicModel
{
    public const string RelicId = "CALLING_BELL";

    public const string RelicsKey = "Relics";

    public const string CurseOfTheBellId = "CURSE_OF_THE_BELL";

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Ancient;

    public override bool HasUponPickupEffect => true;

    protected override IEnumerable<DynamicVar> CanonicalVars => new[]
    {
        new DynamicVar(RelicsKey, 3m)
    };

    public override void AfterObtained(RunState rs)
    {
        CardPileCmd.AddCurseToDeck(rs, CurseOfTheBellId, Owner);
        if (Owner.PlayerRng == null)
        {
            return;
        }
        RewardsCmd.OfferCustom(rs, Owner, GenerateRewards());
    }

    private List<Reward> GenerateRewards()
    {
        return new List<Reward>(3)
        {
            new RelicReward(RelicRarity.Common, Owner),
            new RelicReward(RelicRarity.Uncommon, Owner),
            new RelicReward(RelicRarity.Rare, Owner),
        };
    }
}
