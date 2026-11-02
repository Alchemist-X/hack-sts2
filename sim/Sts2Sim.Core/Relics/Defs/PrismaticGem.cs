namespace Sts2Sim.Core.Relics;

public sealed class PrismaticGem : RelicModel
{
    public const string RelicId = "PRISMATIC_GEM";

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Ancient;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new EnergyVar(1)
    };

    public override decimal ModifyMaxEnergy(CombatState cs, PlayerState player, decimal amount)
    {
        if (!ReferenceEquals(player, Owner))
        {
            return amount;
        }
        return amount + DynamicVars.Energy.IntValue;
    }

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
        if (options.CardPools.All(p => p.IsColorless))
        {
            return options;
        }
        return options.WithCardPools(rs.Unlocks.CharacterCardPools.Union(options.CardPools));
    }
}
