namespace Sts2Sim.Core.Cards;

public sealed class Resonance : CardDef
{
    public const string CardId = "RESONANCE";
    public const int Cost = 1;
    public const int StarCost = 2;
    public const decimal BaseStrength = 1m;
    public const decimal StrengthUpgrade = 1m;
    public const decimal EnemyStrengthLoss = -1m;

    public Resonance()
        : base(CardId, Cost, CardType.Skill, CardRarity.Uncommon, TargetType.AllEnemies)
    {
    }

    public override int CanonicalStarCost => StarCost;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new PowerVar<StrengthPower>(BaseStrength)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var resonance = play.Card;
        var owner = resonance.Owner;
        int intValue = resonance.Vars[nameof(StrengthPower)].IntValue;
        PowerCmd.Apply<StrengthPower>(cs, owner, intValue, owner, resonance);
        var enemies = new List<MonsterState>();
        cs.GetHittableEnemies(enemies);
        foreach (var current in enemies)
        {
            PowerCmd.Apply<StrengthPower>(cs, current, EnemyStrengthLoss, owner, resonance);
        }
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars[nameof(StrengthPower)].UpgradeValueBy(StrengthUpgrade);
    }
}
