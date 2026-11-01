namespace Sts2Sim.Core.Relics;

public sealed class Bookmark : RelicModel
{
    public const string RelicId = "BOOKMARK";

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Rare;

    public override void AfterFlush(CombatState cs, PlayerState player, IReadOnlyList<CardInstance> flushedCards, IReadOnlyList<CardInstance> retainedCards)
    {
        if (!ReferenceEquals(player, Owner))
        {
            return;
        }
        var candidates = retainedCards.Where(c => !c.EnergyCost.CostsX && c.EnergyCost.GetWithModifiers(CostModifiers.Local) > 0).ToList();
        if (candidates.Count == 0)
        {
            return;
        }
        cs.Rng.CombatCardSelection.NextItem(candidates)?.EnergyCost.AddUntilPlayed(-1);
    }
}
