using System.Collections.ObjectModel;

namespace Sts2Sim.Core.Cards;

public sealed class RoyalGamble : CardDef
{
    public const string CardId = "ROYAL_GAMBLE";
    public const int Cost = 0;
    public const int StarCost = 5;
    public const int BaseStars = 9;

    private static readonly ReadOnlyCollection<CardKeyword> Keywords = Array.AsReadOnly(new[] { CardKeyword.Exhaust });

    public RoyalGamble()
        : base(CardId, Cost, CardType.Skill, CardRarity.Uncommon, TargetType.Self)
    {
    }

    public override int CanonicalStarCost => StarCost;

    public override IReadOnlyCollection<CardKeyword> CanonicalKeywords => Keywords;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new StarsVar(BaseStars)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var royalGamble = play.Card;
        PlayerCmd.GainStars(cs, royalGamble.Vars.Stars.BaseValue, royalGamble.Owner);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.AddKeyword(CardKeyword.Retain);
    }
}
