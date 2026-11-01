namespace Sts2Sim.Core.Cards;

public sealed class Null : CardDef
{
    public const string CardId = "NULL";
    public const int Cost = 2;
    public const decimal BaseDamage = 10m;
    public const decimal BaseWeak = 1m;
    public const decimal DamageUpgrade = 3m;
    public const decimal WeakUpgrade = 1m;

    public Null()
        : base(CardId, Cost, CardType.Attack, CardRarity.Uncommon, TargetType.AnyEnemy)
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
        var card = play.Card;
        DamageCmd.Attack(cs, card.Vars.Damage.BaseValue).FromCard(card, play).Targeting(target).Execute();
        PowerCmd.Apply<WeakPower>(cs, target, card.Vars.Weak.BaseValue, card.Owner, card);
        OrbCmd.Channel<DarkOrb>(cs, card.Owner);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Damage.UpgradeValueBy(DamageUpgrade);
        card.Vars.Weak.UpgradeValueBy(WeakUpgrade);
    }
}
