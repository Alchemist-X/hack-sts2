namespace Sts2Sim.Core.Relics;

public sealed class LostCoffer : RelicModel
{
    public const string RelicId = "LOST_COFFER";

    public const int CardRewardOptionCount = 3;

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Ancient;

    public override bool HasUponPickupEffect => true;

    public override void AfterObtained(RunState rs)
    {
        if (Owner.PlayerRng == null)
        {
            return;
        }
        var val = new List<Reward>();
        var options = new CardCreationOptions(new[] { CardPools.ForCharacter(Owner.CharacterId) }, CardCreationSource.Other, CardRarityOddsType.RegularEncounter);
        val.Add(new CardReward(Owner, options, CardRewardOptionCount));
        val.Add(new PotionReward(Owner));
        RewardsCmd.OfferCustom(rs, Owner, val);
    }
}
