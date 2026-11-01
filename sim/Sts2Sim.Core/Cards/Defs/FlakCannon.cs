namespace Sts2Sim.Core.Cards;

public sealed class FlakCannon : CardDef
{
    public const string CardId = "FLAK_CANNON";
    public const int Cost = 2;
    public const decimal BaseDamage = 8m;
    public const decimal DamageUpgrade = 3m;
    public const decimal CalculationBase = 0m;
    public const decimal CalculationExtra = 1m;
    public const string CalculatedHitsKey = "CalculatedHits";

    private static readonly Func<CombatState, CardInstance, CreatureState?, decimal> StatusCount = static (_, card, _) => GetStatuses(card.Owner).Count;

    public FlakCannon()
        : base(CardId, Cost, CardType.Attack, CardRarity.Rare, TargetType.RandomEnemy)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DamageVar(BaseDamage, ValueProp.Move),
        new CalculationBaseVar(CalculationBase),
        new CalculationExtraVar(CalculationExtra),
        new CalculatedVar(CalculatedHitsKey).WithMultiplier(StatusCount)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var flakCannon = play.Card;
        var statuses = GetStatuses(flakCannon.Owner);
        int statusCount = (int)((CalculatedVar)flakCannon.Vars[CalculatedHitsKey]).Calculate(cs, flakCannon, play.Target);
        for (int i = 0; i < statuses.Count; i++)
        {
            CardCmd.Exhaust(cs, statuses[i]);
        }
        DamageCmd.Attack(cs, flakCannon.Vars.Damage.BaseValue).WithHitCount(statusCount).FromCard(flakCannon, play).TargetingRandomOpponents().Execute();
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Damage.UpgradeValueBy(DamageUpgrade);
    }

    private static List<CardInstance> GetStatuses(PlayerState owner)
    {
        var statuses = new List<CardInstance>();
        var piles = owner.AllPiles;
        for (int p = 0; p < piles.Count; p++)
        {
            var pile = piles[p];
            var cards = pile.Cards;
            for (int i = 0; i < cards.Count; i++)
            {
                var card = cards[i];
                if (card.Type == CardType.Status && pile.Type != PileType.Exhaust)
                {
                    statuses.Add(card);
                }
            }
        }
        return statuses;
    }
}
