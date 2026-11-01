using System.Collections.ObjectModel;

namespace Sts2Sim.Core.Cards;

public sealed class EnfeeblingTouch : CardDef
{
    public const string CardId = "ENFEEBLING_TOUCH";
    public const int Cost = 1;
    public const string EnfeeblingTouchPowerId = "ENFEEBLING_TOUCH_POWER";
    public const string StrengthLossVarName = "StrengthLoss";
    public const decimal BaseStrengthLoss = 8m;
    public const decimal StrengthLossUpgrade = 3m;

    private static readonly ReadOnlyCollection<CardKeyword> Keywords = Array.AsReadOnly(new[] { CardKeyword.Ethereal });

    public EnfeeblingTouch()
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
        var enfeeblingTouch = play.Card;
        PowerCmd.Apply(cs, PowerRegistry.Create(EnfeeblingTouchPowerId), target, enfeeblingTouch.Vars[StrengthLossVarName].BaseValue, enfeeblingTouch.Owner, enfeeblingTouch);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars[StrengthLossVarName].UpgradeValueBy(StrengthLossUpgrade);
    }
}
