namespace Sts2Sim.Core.Relics;

public sealed class GlassEye : RelicModel
{
    public const string RelicId = "GLASS_EYE";

    public const int CardRewardOptionCount = 3;

    private static readonly CardRarity[] RewardRarities =
    {
        CardRarity.Common,
        CardRarity.Common,
        CardRarity.Uncommon,
        CardRarity.Uncommon,
        CardRarity.Rare,
    };

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Ancient;

    public override void AfterObtained(RunState rs)
    {
        if (Owner.PlayerRng == null)
        {
            return;
        }
        var val = new List<Reward>();
        var array3 = RewardRarities;
        for (int i = 0; i < array3.Length; i++)
        {
            var rarity = array3[i];
            var options = CardCreationOptions.ForNonCombatWithUniformOdds(new[] { CardPools.ForCharacter(Owner.CharacterId) }, c => c.Rarity == rarity)
                .WithFlags(CardCreationFlags.NoRarityModification);
            val.Add(new CardReward(Owner, options, CardRewardOptionCount));
        }
        RewardsCmd.OfferCustom(rs, Owner, val);
    }
}
