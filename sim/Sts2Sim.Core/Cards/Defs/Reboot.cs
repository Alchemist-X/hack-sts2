using System.Collections.ObjectModel;

namespace Sts2Sim.Core.Cards;

public sealed class Reboot : CardDef
{
    public const string CardId = "REBOOT";
    public const int Cost = 0;
    public const int BaseCards = 4;
    public const decimal CardsUpgrade = 2m;

    private static readonly ReadOnlyCollection<CardKeyword> Keywords = Array.AsReadOnly(new[] { CardKeyword.Exhaust });

    public Reboot()
        : base(CardId, Cost, CardType.Skill, CardRarity.Rare, TargetType.Self)
    {
    }

    public override IReadOnlyCollection<CardKeyword> CanonicalKeywords => Keywords;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new CardsVar(BaseCards)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var card = play.Card;
        var owner = card.Owner;
        var hand = owner.Hand.Cards.ToList();
        for (int i = 0; i < hand.Count; i++)
        {
            CardPileCmd.Add(cs, hand[i], PileType.Draw);
        }
        CardPileCmd.Shuffle(cs, owner);
        CardPileCmd.Draw(cs, card.Vars.Cards.BaseValue, owner);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Cards.UpgradeValueBy(CardsUpgrade);
    }
}
