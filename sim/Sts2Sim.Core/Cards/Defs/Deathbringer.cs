namespace Sts2Sim.Core.Cards;

public sealed class Deathbringer : CardDef
{
    public const string CardId = "DEATHBRINGER";
    public const int Cost = 2;
    public const string DoomPowerId = "DOOM_POWER";
    public const decimal BaseDoom = 21m;
    public const decimal BaseWeak = 1m;
    public const decimal DoomUpgrade = 5m;

    public Deathbringer()
        : base(CardId, Cost, CardType.Skill, CardRarity.Uncommon, TargetType.AllEnemies)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DynamicVar(DynamicVarSet.DoomName, BaseDoom),
        new PowerVar<WeakPower>(BaseWeak)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var deathbringer = play.Card;
        var owner = deathbringer.Owner;
        var hittable = new List<MonsterState>();
        cs.GetHittableEnemies(hittable);
        for (int i = 0; i < hittable.Count; i++)
        {
            PowerCmd.Apply(cs, PowerRegistry.Create(DoomPowerId), hittable[i], deathbringer.Vars.Doom.BaseValue, owner, deathbringer);
        }
        cs.GetHittableEnemies(hittable);
        PowerCmd.Apply<WeakPower>(cs, hittable, deathbringer.Vars.Weak.BaseValue, owner, deathbringer);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Doom.UpgradeValueBy(DoomUpgrade);
    }
}
