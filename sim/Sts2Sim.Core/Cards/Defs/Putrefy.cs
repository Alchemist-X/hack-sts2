using System.Collections.ObjectModel;

namespace Sts2Sim.Core.Cards;

public sealed class Putrefy : CardDef
{
    public const string CardId = "PUTREFY";
    public const int Cost = 1;
    public const string PowerKey = "Power";
    public const decimal BasePower = 2m;
    public const decimal PowerUpgrade = 1m;

    private static readonly ReadOnlyCollection<CardKeyword> Keywords = Array.AsReadOnly(new[] { CardKeyword.Exhaust });

    public Putrefy()
        : base(CardId, Cost, CardType.Skill, CardRarity.Uncommon, TargetType.AnyEnemy)
    {
    }

    public override IReadOnlyCollection<CardKeyword> CanonicalKeywords => Keywords;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DynamicVar(PowerKey, BasePower)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var target = play.Target;
        ArgumentNullException.ThrowIfNull(target);
        var card = play.Card;
        int amount = card.Vars[PowerKey].IntValue;
        PowerCmd.Apply<WeakPower>(cs, target, amount, card.Owner, card);
        PowerCmd.Apply<VulnerablePower>(cs, target, amount, card.Owner, card);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars[PowerKey].UpgradeValueBy(PowerUpgrade);
    }
}
