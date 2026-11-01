namespace Sts2Sim.Core.Relics;

public sealed class DingyRug : RelicModel
{
    public const string RelicId = "DINGY_RUG";

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Shop;

    public override CardCreationOptions ModifyCardRewardCreationOptions(RunState rs, PlayerState player, CardCreationOptions options)
    {
        if (!ReferenceEquals(Owner, player))
        {
            return options;
        }
        if (options.Flags.HasFlag(CardCreationFlags.NoCardPoolModifications))
        {
            return options;
        }
        if (!options.Flags.HasFlag(CardCreationFlags.IsCardReward))
        {
            return options;
        }
        return options.WithCardPools(options.CardPools.Union(new[] { CardPools.Colorless }));
    }
}
