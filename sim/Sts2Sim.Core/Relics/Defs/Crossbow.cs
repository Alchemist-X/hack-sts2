namespace Sts2Sim.Core.Relics;

public sealed class Crossbow : RelicModel
{
    public const string RelicId = "CROSSBOW";

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Ancient;

    public override void AfterSideTurnStart(CombatState cs, CombatSide side, IReadOnlyList<CreatureState> participants)
    {
        if (!participants.Any(p => ReferenceEquals(p, Owner)))
        {
            return;
        }
        var options = CardPools.ForCharacter(Owner.CharacterId)
            .GetUnlockedCards(cs.Run.Unlocks, CardPools.MultiplayerConstraintFor(cs.Run))
            .Where(c => c.Type == CardType.Attack)
            .ToList();
        if (options.Count == 0)
        {
            return;
        }
        var cards = CardFactory.GetDistinctForCombat(cs, Owner, options, 1, cs.Run.Rng.CombatCardGeneration).ToList();
        foreach (var card in cards)
        {
            card.SetToFreeThisTurn();
        }
        CardPileCmd.AddGeneratedCardsToCombat(cs, cards, PileType.Hand, Owner);
    }
}
