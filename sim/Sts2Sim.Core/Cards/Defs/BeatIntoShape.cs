namespace Sts2Sim.Core.Cards;

public sealed class BeatIntoShape : CardDef
{
    public const string CardId = "BEAT_INTO_SHAPE";
    public const int Cost = 1;
    public const string CalculatedForgeKey = "CalculatedForge";
    public const decimal BaseDamage = 5m;
    public const decimal CalculationBase = 5m;
    public const decimal CalculationExtra = 5m;
    public const decimal DamageUpgrade = 2m;
    public const decimal CalculationBaseUpgrade = 2m;
    public const decimal CalculationExtraUpgrade = 2m;

    private static readonly Func<CombatState, CardInstance, CreatureState?, decimal> PoweredHitsOnTargetThisTurn = static (cs, card, target) => CountPoweredHitsOnTargetThisTurn(cs, card, target);

    public BeatIntoShape()
        : base(CardId, Cost, CardType.Attack, CardRarity.Rare, TargetType.AnyEnemy)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DamageVar(BaseDamage, ValueProp.Move),
        new CalculationBaseVar(CalculationBase),
        new CalculationExtraVar(CalculationExtra),
        new CalculatedVar(CalculatedForgeKey).WithMultiplier(PoweredHitsOnTargetThisTurn)
    };

    public static decimal CountPoweredHitsOnTargetThisTurn(CombatState cs, CardInstance card, CreatureState? target)
    {
        if (target == null)
        {
            return 0m;
        }
        var dealer = card.Owner;
        var entries = cs.History.DamageReceivedEntries;
        int count = 0;
        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            if (e.ReceiverId == target.CombatId && e.DealerId == dealer.CombatId && e.Result.Props.IsPoweredAttack() && e.HappenedThisTurn(cs))
            {
                count++;
            }
        }
        return count;
    }

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var target = play.Target;
        ArgumentNullException.ThrowIfNull(target);
        var beatIntoShape = play.Card;
        var attackCommand = DamageCmd.Attack(cs, beatIntoShape.Vars.Damage.BaseValue).FromCard(beatIntoShape, play).Targeting(target).Execute();
        decimal num = ((CalculatedVar)beatIntoShape.Vars[CalculatedForgeKey]).Calculate(cs, beatIntoShape, target);
        num -= attackCommand.HitResults.Count * beatIntoShape.Vars.CalculationExtra.BaseValue;
        ForgeCmd.Forge(cs, num, beatIntoShape.Owner, null);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Damage.UpgradeValueBy(DamageUpgrade);
        card.Vars.CalculationBase.UpgradeValueBy(CalculationBaseUpgrade);
        card.Vars.CalculationExtra.UpgradeValueBy(CalculationExtraUpgrade);
    }
}
