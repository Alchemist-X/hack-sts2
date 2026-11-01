namespace Sts2Sim.Core.Cards;

public sealed class Uppercut : CardDef
{
    public const string CardId = "UPPERCUT";
    public const int Cost = 2;
    public const decimal BaseDamage = 13m;
    public const decimal BasePower = 1m;
    public const decimal PowerUpgrade = 1m;
    public const string PowerKey = "Power";

    public Uppercut()
        : base(CardId, Cost, CardType.Attack, CardRarity.Uncommon, TargetType.AnyEnemy)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DamageVar(BaseDamage, ValueProp.Move),
        new DynamicVar(PowerKey, BasePower)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var target = play.Target;
        ArgumentNullException.ThrowIfNull(target);
        var uppercut = play.Card;
        DamageCmd.Attack(cs, uppercut.Vars.Damage.BaseValue).FromCard(uppercut, play).Targeting(target).Execute();
        int amount = uppercut.Vars[PowerKey].IntValue;
        PowerCmd.Apply<WeakPower>(cs, target, amount, uppercut.Owner, uppercut);
        PowerCmd.Apply<VulnerablePower>(cs, target, amount, uppercut.Owner, uppercut);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars[PowerKey].UpgradeValueBy(PowerUpgrade);
    }
}
