using System.Collections.ObjectModel;

namespace Sts2Sim.Core.Cards;

public sealed class Abundance : CardDef
{
    public const string CardId = "ABUNDANCE";
    public const int Cost = 1;
    public const int OptionCount = 3;
    public const int CostUpgrade = -1;

    private static readonly ReadOnlyCollection<CardKeyword> Keywords = Array.AsReadOnly(new[] { CardKeyword.Exhaust });

    public Abundance()
        : base(CardId, Cost, CardType.Skill, CardRarity.Ancient, TargetType.Self)
    {
    }

    public override bool CanBeGeneratedInCombat => false;

    public override IReadOnlyCollection<CardKeyword> CanonicalKeywords => Keywords;

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var abundance = play.Card;
        var owner = abundance.Owner;
        var rs = cs.Run;
        var pool = CardPools.ForCharacter(owner.CharacterId);
        var options = pool.GetUnlockedCards(rs.Unlocks, CardPools.MultiplayerConstraintFor(rs)).Where(c => c.Type == CardType.Power);
        var cards = CardFactory.GetDistinctForCombat(cs, owner, options, OptionCount, rs.Rng.CombatCardGeneration).ToList();
        foreach (var current in cards)
        {
            CardCmd.Upgrade(cs, current);
        }
        var cardModel = CardSelectCmd.FromChooseACardScreen(cs, cards, owner, canSkip: false, ChoiceSource.For(abundance, "OnPlay"));
        if (cardModel != null)
        {
            cardModel.SetToFreeThisTurn();
            CardPileCmd.AddGeneratedCardToCombat(cs, cardModel, PileType.Hand, owner);
        }
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.EnergyCost.UpgradeBy(CostUpgrade);
    }
}
