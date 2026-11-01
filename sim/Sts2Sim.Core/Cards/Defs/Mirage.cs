using System.Collections.ObjectModel;

namespace Sts2Sim.Core.Cards;

public sealed class Mirage : CardDef
{
    public const string CardId = "MIRAGE";
    public const int Cost = 1;
    public const decimal CalculationBase = 0m;
    public const decimal CalculationExtra = 1m;
    public const string PoisonPowerId = "POISON_POWER";

    private static readonly ReadOnlyCollection<CardKeyword> Keywords = Array.AsReadOnly(new[] { CardKeyword.Exhaust });

    private static readonly Func<CombatState, CardInstance, CreatureState?, decimal> EnemyPoison = static (cs, _, _) => SumEnemyPoison(cs);

    public Mirage()
        : base(CardId, Cost, CardType.Skill, CardRarity.Uncommon, TargetType.Self)
    {
    }

    public override bool GainsBlock => true;

    public override IReadOnlyCollection<CardKeyword> CanonicalKeywords => Keywords;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new CalculationBaseVar(CalculationBase),
        new CalculationExtraVar(CalculationExtra),
        new CalculatedBlockVar(ValueProp.Move).WithMultiplier(EnemyPoison)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var mirage = play.Card;
        var calculatedBlock = mirage.Vars.CalculatedBlock;
        CreatureCmd.GainBlock(cs, mirage.Owner, calculatedBlock.Calculate(cs, mirage, play.Target), calculatedBlock.Props, play);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.RemoveKeyword(CardKeyword.Exhaust);
    }

    private static int SumEnemyPoison(CombatState cs)
    {
        int sum = 0;
        var enemies = cs.Monsters;
        for (int i = 0; i < enemies.Count; i++)
        {
            var enemy = enemies[i];
            if (enemy.IsAlive)
            {
                sum += enemy.GetPower(PoisonPowerId)?.Amount ?? 0;
            }
        }
        return sum;
    }
}
