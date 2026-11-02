using System.Collections.ObjectModel;

namespace Sts2Sim.Core.Cards;

public sealed class Discovery : CardDef
{
    public const string CardId = "DISCOVERY";
    public const int Cost = 1;
    public const int OptionCount = 3;

    private static readonly ReadOnlyCollection<CardKeyword> Keywords = Array.AsReadOnly(new[] { CardKeyword.Exhaust });

    public Discovery()
        : base(CardId, Cost, CardType.Skill, CardRarity.Uncommon, TargetType.Self)
    {
    }

    public override IReadOnlyCollection<CardKeyword> CanonicalKeywords => Keywords;

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var discovery = play.Card;
        var owner = discovery.Owner;
        var rs = cs.Run;
        var pool = CardPools.ForCharacter(owner.CharacterId);
        var options = pool.GetUnlockedCards(rs.Unlocks, CardPools.MultiplayerConstraintFor(rs));
        var cards = CardFactory.GetDistinctForCombat(cs, owner, options, OptionCount, rs.Rng.CombatCardGeneration).ToList();
        var cardModel = CardSelectCmd.FromChooseACardScreen(cs, cards, owner, canSkip: true, ChoiceSource.For(discovery, "OnPlay"));
        if (cardModel != null)
        {
            cardModel.SetToFreeThisTurn();
            CardPileCmd.AddGeneratedCardToCombat(cs, cardModel, PileType.Hand, owner);
        }
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.RemoveKeyword(CardKeyword.Exhaust);
    }
}
