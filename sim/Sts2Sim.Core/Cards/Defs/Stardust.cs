namespace Sts2Sim.Core.Cards;

public sealed class Stardust : CardDef
{
    public const string CardId = "STARDUST";
    public const int Cost = 0;
    public const decimal BaseDamage = 5m;
    public const decimal DamageUpgrade = 2m;

    public Stardust()
        : base(CardId, Cost, CardType.Attack, CardRarity.Uncommon, TargetType.RandomEnemy)
    {
    }

    public override bool HasStarCostX => true;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DamageVar(BaseDamage, ValueProp.Move)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var stardust = play.Card;
        DamageCmd.Attack(cs, stardust.Vars.Damage.BaseValue).WithHitCount(stardust.ResolveStarXValue(cs)).FromCard(stardust, play).TargetingRandomOpponents().Execute();
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Damage.UpgradeValueBy(DamageUpgrade);
    }
}
