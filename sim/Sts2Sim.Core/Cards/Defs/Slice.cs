namespace Sts2Sim.Core.Cards;

public sealed class Slice : CardDef
{
    public const string CardId = "SLICE";
    public const int Cost = 0;
    public const decimal BaseDamage = 6m;
    public const decimal DamageUpgrade = 3m;

    public Slice()
        : base(CardId, Cost, CardType.Attack, CardRarity.Common, TargetType.AnyEnemy)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DamageVar(BaseDamage, ValueProp.Move)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var target = play.Target;
        ArgumentNullException.ThrowIfNull(target);
        var slice = play.Card;
        DamageCmd.Attack(cs, slice.Vars.Damage.BaseValue).FromCard(slice, play).Targeting(target).Execute();
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Damage.UpgradeValueBy(DamageUpgrade);
    }
}
