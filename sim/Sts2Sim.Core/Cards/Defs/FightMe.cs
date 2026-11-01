namespace Sts2Sim.Core.Cards;

public sealed class FightMe : CardDef
{
    public const string CardId = "FIGHT_ME";
    public const int Cost = 2;
    public const string EnemyStrengthKey = "EnemyStrength";
    public const decimal BaseDamage = 5m;
    public const int BaseRepeat = 2;
    public const decimal BaseStrength = 3m;
    public const decimal BaseEnemyStrength = 1m;
    public const decimal DamageUpgrade = 1m;
    public const decimal StrengthUpgrade = 1m;

    public FightMe()
        : base(CardId, Cost, CardType.Attack, CardRarity.Uncommon, TargetType.AnyEnemy)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DamageVar(BaseDamage, ValueProp.Move),
        new RepeatVar(BaseRepeat),
        new PowerVar<StrengthPower>(BaseStrength),
        new DynamicVar(EnemyStrengthKey, BaseEnemyStrength)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var target = play.Target;
        ArgumentNullException.ThrowIfNull(target);
        var fightMe = play.Card;
        var owner = fightMe.Owner;
        DamageCmd.Attack(cs, fightMe.Vars.Damage.BaseValue).WithHitCount(fightMe.Vars.Repeat.IntValue).FromCard(fightMe, play).Targeting(target).Execute();
        PowerCmd.Apply<StrengthPower>(cs, owner, fightMe.Vars[DynamicVarSet.StrengthName].BaseValue, owner, fightMe);
        PowerCmd.Apply<StrengthPower>(cs, target, fightMe.Vars[EnemyStrengthKey].BaseValue, owner, fightMe);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Damage.UpgradeValueBy(DamageUpgrade);
        card.Vars.Strength.UpgradeValueBy(StrengthUpgrade);
    }
}
