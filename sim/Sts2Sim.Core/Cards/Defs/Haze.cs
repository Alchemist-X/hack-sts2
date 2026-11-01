namespace Sts2Sim.Core.Cards;

public sealed class Haze : CardDef
{
    public const string CardId = "HAZE";
    public const int Cost = 2;
    public const string PoisonPowerId = "POISON_POWER";
    public const decimal BasePoison = 4m;
    public const decimal BaseWeak = 1m;
    public const decimal PoisonUpgrade = 2m;
    public const decimal WeakUpgrade = 1m;

    public Haze()
        : base(CardId, Cost, CardType.Skill, CardRarity.Uncommon, TargetType.AllEnemies)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DynamicVar(DynamicVarSet.PoisonName, BasePoison),
        new PowerVar<WeakPower>(BaseWeak)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var card = play.Card;
        var owner = card.Owner;
        var poisonTargets = HittableEnemies(cs);
        for (int i = 0; i < poisonTargets.Count; i++)
        {
            PowerCmd.Apply(cs, PowerRegistry.Create(PoisonPowerId), poisonTargets[i], card.Vars.Poison.BaseValue, owner, card);
        }
        PowerCmd.Apply<WeakPower>(cs, HittableEnemies(cs), card.Vars.Weak.BaseValue, owner, card);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Poison.UpgradeValueBy(PoisonUpgrade);
        card.Vars.Weak.UpgradeValueBy(WeakUpgrade);
    }

    private static List<CreatureState> HittableEnemies(CombatState cs)
    {
        var monsters = cs.Monsters;
        var hittable = new List<CreatureState>(monsters.Count);
        for (int i = 0; i < monsters.Count; i++)
        {
            if (CombatStatus.IsHittable(cs, monsters[i]))
            {
                hittable.Add(monsters[i]);
            }
        }
        return hittable;
    }
}
