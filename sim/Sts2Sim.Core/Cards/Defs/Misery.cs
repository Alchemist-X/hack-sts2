namespace Sts2Sim.Core.Cards;

public sealed class Misery : CardDef
{
    public const string CardId = "MISERY";
    public const int Cost = 0;
    public const decimal BaseDamage = 7m;
    public const decimal DamageUpgrade = 2m;

    public Misery()
        : base(CardId, Cost, CardType.Attack, CardRarity.Rare, TargetType.AnyEnemy)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DamageVar(BaseDamage, ValueProp.Move)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var target = play.Target;
        ArgumentNullException.ThrowIfNull(target);
        var misery = play.Card;
        var debuffAmounts = new List<(PowerModel Power, int Amount)>();
        foreach (var p in target.Powers)
        {
            if (p.TypeForCurrentAmount == PowerType.Debuff)
            {
                debuffAmounts.Add((p.CloneDetached(), p.Amount));
            }
        }
        for (int i = 0; i < debuffAmounts.Count; i++)
        {
            var current = debuffAmounts[i];
            if (current.Power is ITemporaryPower temporaryPower)
            {
                int index = debuffAmounts.FindIndex(e => e.Power.Id == temporaryPower.InternallyAppliedPowerId);
                if (index >= 0)
                {
                    var entry = debuffAmounts[index];
                    debuffAmounts[index] = (entry.Power, entry.Amount + current.Amount);
                }
            }
        }
        DamageCmd.Attack(cs, misery.Vars.Damage.BaseValue).FromCard(misery, play).Targeting(target).Execute();
        var enemies = new List<MonsterState>();
        cs.GetHittableEnemies(enemies);
        foreach (var enemy in enemies)
        {
            if (ReferenceEquals(enemy, target))
            {
                continue;
            }
            for (int i = 0; i < debuffAmounts.Count; i++)
            {
                var current2 = debuffAmounts[i];
                if (current2.Amount == 0)
                {
                    continue;
                }
                var applier = current2.Power.GetApplier(cs);
                var powerModel = PowerCmd.FindExistingInstanceForStacking(current2.Power, enemy, applier);
                if (powerModel != null)
                {
                    PowerCmd.ModifyAmount(cs, powerModel, current2.Amount, applier, misery);
                }
                else
                {
                    var power = current2.Power.CloneDetached();
                    PowerCmd.Apply(cs, power, enemy, current2.Amount, applier, misery);
                }
            }
        }
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Damage.UpgradeValueBy(DamageUpgrade);
        card.AddKeyword(CardKeyword.Retain);
    }
}
