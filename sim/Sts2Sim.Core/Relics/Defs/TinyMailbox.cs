namespace Sts2Sim.Core.Relics;

public sealed class TinyMailbox : RelicModel
{
    public const string RelicId = "TINY_MAILBOX";

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Uncommon;

    public override bool TryModifyRestSiteHealRewards(RunState rs, PlayerState player, List<Reward> rewards, bool isMimicked)
    {
        if (!ReferenceEquals(player, Owner))
        {
            return false;
        }
        rewards.Add(new PotionReward(player));
        rewards.Add(new PotionReward(player));
        return true;
    }
}
