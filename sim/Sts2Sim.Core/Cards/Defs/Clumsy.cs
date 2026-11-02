using System.Collections.ObjectModel;

namespace Sts2Sim.Core.Cards;

public sealed class Clumsy : CardDef
{
    public const string CardId = "CLUMSY";
    public const int Cost = -1;

    private static readonly ReadOnlyCollection<CardKeyword> Keywords = Array.AsReadOnly(new[] { CardKeyword.Unplayable, CardKeyword.Ethereal });

    public Clumsy()
        : base(CardId, Cost, CardType.Curse, CardRarity.Curse, TargetType.None)
    {
    }

    public override int MaxUpgradeLevel => 0;

    public override IReadOnlyCollection<CardKeyword> CanonicalKeywords => Keywords;

    public override void OnPlay(CombatState cs, CardPlay play)
    {
    }
}
