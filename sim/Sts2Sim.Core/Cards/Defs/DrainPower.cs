namespace Sts2Sim.Core.Cards;

public sealed class DrainPower : CardDef
{
    public const string CardId = "DRAIN_POWER";
    public const int Cost = 1;
    public const decimal BaseDamage = 10m;
    public const int BaseCards = 2;
    public const decimal DamageUpgrade = 2m;
    public const decimal CardsUpgrade = 1m;

    public DrainPower()
        : base(CardId, Cost, CardType.Attack, CardRarity.Common, TargetType.AnyEnemy)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DamageVar(BaseDamage, ValueProp.Move),
        new CardsVar(BaseCards)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var target = play.Target;
        ArgumentNullException.ThrowIfNull(target);
        var drainPower = play.Card;
        DamageCmd.Attack(cs, drainPower.Vars.Damage.BaseValue).FromCard(drainPower, play).Targeting(target).Execute();
        var enumerable = drainPower.Owner.DiscardPile.Cards.Where(static c => c.IsUpgradable).TakeRandom(drainPower.Vars.Cards.IntValue, cs.Rng.CombatCardSelection);
        foreach (var current in enumerable)
        {
            CardCmd.Upgrade(cs, current);
        }
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Damage.UpgradeValueBy(DamageUpgrade);
        card.Vars.Cards.UpgradeValueBy(CardsUpgrade);
    }
}
