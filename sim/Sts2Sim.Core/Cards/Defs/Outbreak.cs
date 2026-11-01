using System.Reflection;

namespace Sts2Sim.Core.Cards;

public sealed class Outbreak : CardDef
{
    public const string CardId = "OUTBREAK";
    public const int Cost = 3;
    public const string PoisonPowerId = "POISON_POWER";
    public const string PoisonTriggerMethod = "Trigger";
    public const decimal BasePoison = 9m;
    public const decimal PoisonUpgrade = 3m;

    public Outbreak()
        : base(CardId, Cost, CardType.Skill, CardRarity.Rare, TargetType.AllEnemies)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DynamicVar(DynamicVarSet.PoisonName, BasePoison)
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
        var triggerTargets = HittableEnemies(cs);
        for (int i = 0; i < triggerTargets.Count; i++)
        {
            var power = triggerTargets[i].GetPower(PoisonPowerId);
            if (power == null)
            {
                continue;
            }
            TriggerPoison(cs, power);
        }
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Poison.UpgradeValueBy(PoisonUpgrade);
    }

    private static void TriggerPoison(CombatState cs, PowerModel power)
    {
        var method = power.GetType().GetMethod(PoisonTriggerMethod, BindingFlags.Public | BindingFlags.Instance, new[] { typeof(CombatState) })
            ?? throw new NotImplementedException("W3 gap: POISON_POWER.Trigger(CombatState)");
        method.Invoke(power, new object[] { cs });
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
