namespace Sts2Sim.Core.Cards;

public sealed class SuckerPunch : CardDef
{
    public const string CardId = "SUCKER_PUNCH";
    public const int Cost = 1;
    public const decimal BaseDamage = 8m;
    public const decimal BaseWeak = 1m;
    public const decimal DamageUpgrade = 2m;
    public const decimal WeakUpgrade = 1m;

    public SuckerPunch()
        : base(CardId, Cost, CardType.Attack, CardRarity.Common, TargetType.AnyEnemy)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DamageVar(BaseDamage, ValueProp.Move),
        new PowerVar<WeakPower>(BaseWeak)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var target = play.Target;
        ArgumentNullException.ThrowIfNull(target);
        var suckerPunch = play.Card;
        DamageCmd.Attack(cs, suckerPunch.Vars.Damage.BaseValue).FromCard(suckerPunch, play).Targeting(target).Execute();
        PowerCmd.Apply<WeakPower>(cs, target, suckerPunch.Vars.Weak.BaseValue, suckerPunch.Owner, suckerPunch);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Damage.UpgradeValueBy(DamageUpgrade);
        card.Vars.Weak.UpgradeValueBy(WeakUpgrade);
    }
}
