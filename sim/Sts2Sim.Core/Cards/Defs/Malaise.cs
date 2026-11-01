using System.Collections.ObjectModel;

namespace Sts2Sim.Core.Cards;

public sealed class Malaise : CardDef
{
    public const string CardId = "MALAISE";
    public const int Cost = 0;
    public const int UpgradedXBonus = 1;

    private static readonly ReadOnlyCollection<CardKeyword> Keywords = Array.AsReadOnly(new[] { CardKeyword.Exhaust });

    public Malaise()
        : base(CardId, Cost, CardType.Skill, CardRarity.Rare, TargetType.AnyEnemy, costsX: true)
    {
    }

    public override IReadOnlyCollection<CardKeyword> CanonicalKeywords => Keywords;

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var target = play.Target;
        ArgumentNullException.ThrowIfNull(target);
        var card = play.Card;
        int powerAmount = card.ResolveEnergyXValue(cs);
        if (card.IsUpgraded)
        {
            powerAmount += UpgradedXBonus;
        }
        PowerCmd.Apply<StrengthPower>(cs, target, -powerAmount, card.Owner, card);
        PowerCmd.Apply<WeakPower>(cs, target, powerAmount, card.Owner, card);
    }
}
