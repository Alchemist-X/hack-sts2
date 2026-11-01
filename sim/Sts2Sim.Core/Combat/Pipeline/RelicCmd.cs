namespace Sts2Sim.Core.Combat.Pipeline;

public static class RelicCmd
{
    public const string ObtainRunLayerSite = "RelicCmd.Obtain: relic grab bags and TotalFloor are W2 run state (Commands/RelicCmd.cs:411-415, :422)";

    public static RelicModel Obtain(RunState rs, string relicId, PlayerState player)
    {
        ArgumentNullException.ThrowIfNull(relicId);
        return Obtain(rs, RelicRegistry.Create(relicId), player);
    }

    public static RelicModel Obtain(RunState rs, RelicModel relic, PlayerState player, int index = -1)
    {
        ArgumentNullException.ThrowIfNull(rs);
        ArgumentNullException.ThrowIfNull(relic);
        ArgumentNullException.ThrowIfNull(player);
        player.AddRelicInternal(relic, index);
        rs.Fidelity.Record(FidelityFlag.ApproximatedRule, ObtainRunLayerSite);
        relic.AfterObtained(rs);
        return relic;
    }

    public static void Remove(RunState rs, RelicModel relic)
    {
        ArgumentNullException.ThrowIfNull(rs);
        ArgumentNullException.ThrowIfNull(relic);
        relic.Owner.RemoveRelicInternal(relic);
        relic.AfterRemoved(rs);
    }

    public static RelicModel Replace(RunState rs, RelicModel original, RelicModel replace)
    {
        ArgumentNullException.ThrowIfNull(rs);
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(replace);
        var player = original.Owner;
        int indexOfOriginal = player.Relics.IndexOf(original);
        Remove(rs, original);
        return Obtain(rs, replace, player, indexOfOriginal);
    }

    public static void Melt(RunState rs, RelicModel relic)
    {
        ArgumentNullException.ThrowIfNull(rs);
        ArgumentNullException.ThrowIfNull(relic);
        relic.Owner.MeltRelicInternal(relic);
        relic.AfterRemoved(rs);
    }
}
