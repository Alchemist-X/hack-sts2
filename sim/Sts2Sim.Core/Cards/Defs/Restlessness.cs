using System.Collections.ObjectModel;

namespace Sts2Sim.Core.Cards;

public sealed class Restlessness : CardDef
{
    public const string CardId = "RESTLESSNESS";
    public const int Cost = 0;
    public const int BaseCards = 2;
    public const int BaseEnergy = 2;
    public const decimal CardsUpgrade = 1m;
    public const decimal EnergyUpgrade = 1m;

    private static readonly ReadOnlyCollection<CardKeyword> Keywords = Array.AsReadOnly(new[] { CardKeyword.Retain });

    public Restlessness()
        : base(CardId, Cost, CardType.Skill, CardRarity.Uncommon, TargetType.Self)
    {
    }

    public override IReadOnlyCollection<CardKeyword> CanonicalKeywords => Keywords;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new CardsVar(BaseCards),
        new EnergyVar(BaseEnergy)
    };

    public static bool IsOnlyCardInHand(CardInstance card)
    {
        var hand = card.Owner.Hand.Cards;
        for (int i = 0; i < hand.Count; i++)
        {
            if (!ReferenceEquals(hand[i], card))
            {
                return false;
            }
        }
        return true;
    }

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var card = play.Card;
        var owner = card.Owner;
        if (IsOnlyCardInHand(card))
        {
            for (int i = 0; i < card.Vars.Cards.IntValue; i++)
            {
                CardPileCmd.Draw(cs, owner);
            }
            PlayerCmd.GainEnergy(cs, card.Vars.Energy.IntValue, owner);
        }
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Cards.UpgradeValueBy(CardsUpgrade);
        card.Vars.Energy.UpgradeValueBy(EnergyUpgrade);
    }
}
