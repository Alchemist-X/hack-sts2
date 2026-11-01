namespace Sts2Sim.Core.Relics;

public sealed class Shovel : RelicModel
{
    public const string RelicId = "SHOVEL";

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Rare;

    public override bool IsAllowed(RunState rs) => IsBeforeAct3TreasureChest(rs.Players.Count, rs.TotalFloor);

    public override bool TryModifyRestSiteOptions(RunState rs, PlayerState player, ICollection<RestSiteOption> options)
    {
        if (!ReferenceEquals(player, Owner))
        {
            return false;
        }
        options.Add(new DigRestSiteOption(player));
        return true;
    }
}
