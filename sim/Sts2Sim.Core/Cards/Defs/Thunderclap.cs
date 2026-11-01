namespace Sts2Sim.Core.Cards;

public sealed class Thunderclap : CardDef
{
    public const string CardId = "THUNDERCLAP";
    public const int Cost = 1;
    public const decimal BaseDamage = 4m;
    public const decimal BaseVulnerable = 1m;
    public const decimal DamageUpgrade = 3m;

    public Thunderclap()
        : base(CardId, Cost, CardType.Attack, CardRarity.Common, TargetType.AllEnemies)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DamageVar(BaseDamage, ValueProp.Move),
        new PowerVar<VulnerablePower>(BaseVulnerable)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var thunderclap = play.Card;
        DamageCmd.Attack(cs, thunderclap.Vars.Damage.BaseValue).FromCard(thunderclap, play).TargetingAllOpponents().Execute();
        var hittable = new List<MonsterState>(cs.Monsters.Count);
        cs.GetHittableEnemies(hittable);
        PowerCmd.Apply<VulnerablePower>(cs, hittable.ConvertAll(static m => (CreatureState)m), thunderclap.Vars.Vulnerable.BaseValue, thunderclap.Owner, thunderclap);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Damage.UpgradeValueBy(DamageUpgrade);
    }
}
