namespace Sts2Sim.Core.Cards;

public sealed class BouncingFlask : CardDef
{
    public const string CardId = "BOUNCING_FLASK";
    public const int Cost = 2;
    public const string PoisonPowerId = "POISON_POWER";
    public const decimal BasePoison = 3m;
    public const int BaseRepeat = 3;
    public const decimal RepeatUpgrade = 1m;

    public BouncingFlask()
        : base(CardId, Cost, CardType.Skill, CardRarity.Uncommon, TargetType.RandomEnemy)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DynamicVar(DynamicVarSet.PoisonName, BasePoison),
        new RepeatVar(BaseRepeat)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var bouncingFlask = play.Card;
        var hittable = new List<MonsterState>();
        for (int i = 0; i < bouncingFlask.Vars.Repeat.IntValue; i++)
        {
            cs.GetHittableEnemies(hittable);
            var enemy = cs.Rng.CombatTargets.NextItem(hittable);
            if (enemy != null)
            {
                PowerCmd.Apply(cs, PowerRegistry.Create(PoisonPowerId), enemy, bouncingFlask.Vars.Poison.BaseValue, bouncingFlask.Owner, bouncingFlask);
            }
        }
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Repeat.UpgradeValueBy(RepeatUpgrade);
    }
}
