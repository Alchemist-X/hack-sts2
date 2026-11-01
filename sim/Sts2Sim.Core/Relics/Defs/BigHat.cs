namespace Sts2Sim.Core.Relics;

public sealed class BigHat : RelicModel
{
    public const string RelicId = "BIG_HAT";

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Rare;

    protected override IEnumerable<DynamicVar> CanonicalVars => new[]
    {
        new CardsVar(2)
    };

    public override void AfterSideTurnStart(CombatState cs, CombatSide side, IReadOnlyList<CreatureState> participants)
    {
        if (!participants.Any(p => ReferenceEquals(p, Owner)) || Owner.TurnNumber > 1)
        {
            return;
        }
        var options = CardPools.ForCharacter(Owner.CharacterId)
            .GetUnlockedCards(cs.Run.Unlocks, CardPools.MultiplayerConstraintFor(cs.Run))
            .Where(c => c.CanonicalKeywords.Contains(CardKeyword.Ethereal))
            .ToList();
        if (options.Count > 0)
        {
            var cards = CardFactory.GetDistinctForCombat(cs, Owner, options, DynamicVars.Cards.IntValue, cs.Run.Rng.CombatCardGeneration).ToList();
            CardPileCmd.AddGeneratedCardsToCombat(cs, cards, PileType.Hand, Owner);
        }
    }
}
