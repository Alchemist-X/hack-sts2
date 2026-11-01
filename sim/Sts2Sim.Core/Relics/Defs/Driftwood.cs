namespace Sts2Sim.Core.Relics;

public sealed class Driftwood : RelicModel
{
    public const string RelicId = "DRIFTWOOD";

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Ancient;

    public override bool TryModifyRewardsLate(RunState rs, PlayerState player, List<Reward> rewards, AbstractRoom? room)
    {
        if (!ReferenceEquals(player, Owner))
        {
            return false;
        }
        foreach (var current in rewards.OfType<CardReward>())
        {
            current.CanReroll = true;
        }
        return true;
    }
}
