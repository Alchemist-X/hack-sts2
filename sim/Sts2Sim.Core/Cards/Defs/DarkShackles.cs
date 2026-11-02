using System.Collections.ObjectModel;

namespace Sts2Sim.Core.Cards;

public sealed class DarkShackles : CardDef
{
    public const string CardId = "DARK_SHACKLES";
    public const int Cost = 0;
    public const string DarkShacklesPowerId = "DARK_SHACKLES_POWER";
    public const string StrengthLossVarName = "StrengthLoss";
    public const decimal BaseStrengthLoss = 9m;
    public const decimal StrengthLossUpgrade = 6m;

    private static readonly ReadOnlyCollection<CardKeyword> Keywords = Array.AsReadOnly(new[] { CardKeyword.Exhaust });

    public DarkShackles()
        : base(CardId, Cost, CardType.Skill, CardRarity.Uncommon, TargetType.AnyEnemy)
    {
    }

    public override IReadOnlyCollection<CardKeyword> CanonicalKeywords => Keywords;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DynamicVar(StrengthLossVarName, BaseStrengthLoss)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var target = play.Target;
        ArgumentNullException.ThrowIfNull(target);
        var darkShackles = play.Card;
        PowerCmd.Apply(cs, PowerRegistry.Create(DarkShacklesPowerId), target, darkShackles.Vars[StrengthLossVarName].BaseValue, darkShackles.Owner, darkShackles);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars[StrengthLossVarName].UpgradeValueBy(StrengthLossUpgrade);
    }
}
