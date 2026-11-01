namespace Sts2Sim.Core.Relics;

public sealed class MeatCleaver : RelicModel
{
    public const string RelicId = "MEAT_CLEAVER";

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Ancient;

    public override bool TryModifyRestSiteOptions(RunState rs, PlayerState player, ICollection<RestSiteOption> options)
    {
        if (!ReferenceEquals(player, Owner))
        {
            return false;
        }
        options.Add(new CookRestSiteOption(player));
        return true;
    }
}
