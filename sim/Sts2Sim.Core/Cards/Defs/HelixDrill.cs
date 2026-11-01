namespace Sts2Sim.Core.Cards;

public sealed class HelixDrill : CardDef
{
    public const string CardId = "HELIX_DRILL";
    public const int Cost = 0;
    public const decimal BaseDamage = 3m;
    public const decimal DamageUpgrade = 2m;
    public const string CalculatedHitsKey = "CalculatedHits";

    public static readonly Func<CombatState, CardInstance, CreatureState?, decimal> EnergySpentThisTurn = static (cs, card, _) =>
    {
        var pile = card.GetCombatPile(cs);
        if (pile == null)
        {
            return 0m;
        }
        int ownerId = card.Owner.CombatId;
        int num = cs.History.SumThisTurn<EnergySpentEntry>(cs, c => c.Amount, e => e.Stamp.ActorId == ownerId);
        if (pile.Type == PileType.Play)
        {
            num -= card.EnergyCost.GetWithModifiers(cs, card, CostModifiers.All);
        }
        return num;
    };

    public HelixDrill()
        : base(CardId, Cost, CardType.Attack, CardRarity.Rare, TargetType.AnyEnemy)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DamageVar(BaseDamage, ValueProp.Move),
        new CalculationBaseVar(0m),
        new CalculationExtraVar(1m),
        new CalculatedVar(CalculatedHitsKey).WithMultiplier(EnergySpentThisTurn)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var target = play.Target;
        ArgumentNullException.ThrowIfNull(target);
        var helixDrill = play.Card;
        int hitCount = (int)((CalculatedVar)helixDrill.Vars[CalculatedHitsKey]).Calculate(cs, helixDrill, target);
        DamageCmd.Attack(cs, helixDrill.Vars.Damage.BaseValue).WithHitCount(hitCount).FromCard(helixDrill, play).Targeting(target).Execute();
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Damage.UpgradeValueBy(DamageUpgrade);
    }
}
