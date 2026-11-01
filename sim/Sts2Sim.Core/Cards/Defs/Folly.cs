using System.Collections.ObjectModel;

namespace Sts2Sim.Core.Cards;

public sealed class Folly : CardDef
{
    public const string CardId = "FOLLY";
    public const int Cost = -1;

    private static readonly ReadOnlyCollection<CardKeyword> Keywords = Array.AsReadOnly(new[] { CardKeyword.Unplayable, CardKeyword.Eternal, CardKeyword.Innate, CardKeyword.Ethereal });

    public Folly()
        : base(CardId, Cost, CardType.Curse, CardRarity.Curse, TargetType.None)
    {
    }

    public override bool CanBeGeneratedByModifiers => false;

    public override int MaxUpgradeLevel => 0;

    public override IReadOnlyCollection<CardKeyword> CanonicalKeywords => Keywords;

    public override void OnPlay(CombatState cs, CardPlay play)
    {
    }
}
