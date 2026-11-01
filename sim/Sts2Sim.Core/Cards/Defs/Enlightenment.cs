using System.Collections.ObjectModel;

namespace Sts2Sim.Core.Cards;

public sealed class Enlightenment : CardDef
{
    public const string CardId = "ENLIGHTENMENT";
    public const int Cost = 0;
    public const int MaxCost = 1;

    private static readonly ReadOnlyCollection<CardKeyword> Keywords = Array.AsReadOnly(new[] { CardKeyword.Exhaust });

    public Enlightenment()
        : base(CardId, Cost, CardType.Skill, CardRarity.Event, TargetType.Self)
    {
    }

    public override IReadOnlyCollection<CardKeyword> CanonicalKeywords => Keywords;

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var enlightenment = play.Card;
        var hand = enlightenment.Owner.Hand.Cards;
        for (int i = 0; i < hand.Count; i++)
        {
            var current = hand[i];
            if (enlightenment.IsUpgraded)
            {
                current.EnergyCost.SetThisCombat(MaxCost, reduceOnly: true);
            }
            else
            {
                current.EnergyCost.SetThisTurnOrUntilPlayed(MaxCost, reduceOnly: true);
            }
        }
    }
}
