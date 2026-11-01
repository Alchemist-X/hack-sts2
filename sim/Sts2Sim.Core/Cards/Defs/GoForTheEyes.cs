namespace Sts2Sim.Core.Cards;

public sealed class GoForTheEyes : CardDef
{
    public const string CardId = "GO_FOR_THE_EYES";
    public const int Cost = 0;
    public const decimal BaseDamage = 3m;
    public const decimal BaseWeak = 1m;
    public const decimal DamageUpgrade = 1m;
    public const decimal WeakUpgrade = 1m;

    public GoForTheEyes()
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
        var goForTheEyes = play.Card;
        DamageCmd.Attack(cs, goForTheEyes.Vars.Damage.BaseValue).FromCard(goForTheEyes, play).Targeting(target).Execute();
        if (MonsterLifecycle.IntendsToAttack((MonsterState)target))
        {
            PowerCmd.Apply<WeakPower>(cs, target, goForTheEyes.Vars.Weak.BaseValue, goForTheEyes.Owner, goForTheEyes);
        }
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Damage.UpgradeValueBy(DamageUpgrade);
        card.Vars.Weak.UpgradeValueBy(WeakUpgrade);
    }
}
