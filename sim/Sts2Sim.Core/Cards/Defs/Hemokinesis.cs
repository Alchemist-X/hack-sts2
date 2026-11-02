namespace Sts2Sim.Core.Cards;

public sealed class Hemokinesis : CardDef
{
    public const string CardId = "HEMOKINESIS";
    public const int Cost = 1;
    public const decimal BaseHpLoss = 2m;
    public const decimal BaseDamage = 15m;
    public const decimal DamageUpgrade = 5m;

    public Hemokinesis()
        : base(CardId, Cost, CardType.Attack, CardRarity.Uncommon, TargetType.AnyEnemy)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new HpLossVar(BaseHpLoss),
        new DamageVar(BaseDamage, ValueProp.Move)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var target = play.Target;
        ArgumentNullException.ThrowIfNull(target);
        var hemokinesis = play.Card;
        CreatureCmd.Damage(cs, hemokinesis.Owner, hemokinesis.Vars.HpLoss.BaseValue, ValueProp.Unblockable | ValueProp.Unpowered | ValueProp.Move, hemokinesis, play);
        DamageCmd.Attack(cs, hemokinesis.Vars.Damage.BaseValue).FromCard(hemokinesis, play).Targeting(target).Execute();
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Damage.UpgradeValueBy(DamageUpgrade);
    }
}
