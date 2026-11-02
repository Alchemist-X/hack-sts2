namespace Sts2Sim.Core.Cards;

public sealed class MeteorShower : CardDef
{
    public const string CardId = "METEOR_SHOWER";
    public const int Cost = 0;
    public const int StarCost = 2;
    public const decimal BaseDamage = 14m;
    public const decimal BaseVulnerable = 2m;
    public const decimal BaseWeak = 2m;
    public const decimal DamageUpgrade = 7m;

    public MeteorShower()
        : base(CardId, Cost, CardType.Attack, CardRarity.Ancient, TargetType.AllEnemies)
    {
    }

    public override int CanonicalStarCost => StarCost;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DamageVar(BaseDamage, ValueProp.Move),
        new PowerVar<VulnerablePower>(BaseVulnerable),
        new PowerVar<WeakPower>(BaseWeak)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var meteorShower = play.Card;
        var owner = meteorShower.Owner;
        DamageCmd.Attack(cs, meteorShower.Vars.Damage.BaseValue).FromCard(meteorShower, play).TargetingAllOpponents().Execute();
        var hittable = new List<MonsterState>();
        cs.GetHittableEnemies(hittable);
        PowerCmd.Apply<WeakPower>(cs, hittable, meteorShower.Vars.Weak.BaseValue, owner, meteorShower);
        cs.GetHittableEnemies(hittable);
        PowerCmd.Apply<VulnerablePower>(cs, hittable, meteorShower.Vars.Vulnerable.BaseValue, owner, meteorShower);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Damage.UpgradeValueBy(DamageUpgrade);
    }
}
