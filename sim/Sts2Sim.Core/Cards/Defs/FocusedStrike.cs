using System.Collections.ObjectModel;

namespace Sts2Sim.Core.Cards;

public sealed class FocusedStrike : CardDef
{
    public const string CardId = "FOCUSED_STRIKE";
    public const int Cost = 1;
    public const decimal BaseDamage = 9m;
    public const decimal BaseFocus = 1m;
    public const decimal DamageUpgrade = 2m;
    public const decimal FocusUpgrade = 1m;
    public const string FocusVarName = "FocusPower";
    public const string FocusedStrikePowerId = "FOCUSED_STRIKE_POWER";

    private static readonly ReadOnlyCollection<CardTag> Tags = Array.AsReadOnly(new[] { CardTag.Strike });

    public FocusedStrike()
        : base(CardId, Cost, CardType.Attack, CardRarity.Common, TargetType.AnyEnemy)
    {
    }

    public override IReadOnlyCollection<CardTag> CanonicalTags => Tags;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DamageVar(BaseDamage, ValueProp.Move),
        new PowerVar<FocusPower>(BaseFocus)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var target = play.Target;
        ArgumentNullException.ThrowIfNull(target);
        var focusedStrike = play.Card;
        DamageCmd.Attack(cs, focusedStrike.Vars.Damage.BaseValue).FromCard(focusedStrike, play).Targeting(target).Execute();
        var owner = focusedStrike.Owner;
        PowerCmd.Apply(cs, PowerRegistry.Create(FocusedStrikePowerId), owner, focusedStrike.Vars[FocusVarName].BaseValue, owner, focusedStrike);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Damage.UpgradeValueBy(DamageUpgrade);
        card.Vars[FocusVarName].UpgradeValueBy(FocusUpgrade);
    }
}
