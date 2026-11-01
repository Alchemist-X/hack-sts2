namespace Sts2Sim.Core.Cards;

public sealed class Volley : CardDef
{
    public const string CardId = "VOLLEY";
    public const int Cost = 0;
    public const decimal BaseDamage = 10m;
    public const decimal DamageUpgrade = 4m;

    public Volley()
        : base(CardId, Cost, CardType.Attack, CardRarity.Uncommon, TargetType.RandomEnemy, costsX: true)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DamageVar(BaseDamage, ValueProp.Move)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var volley = play.Card;
        DamageCmd.Attack(cs, volley.Vars.Damage.BaseValue).WithHitCount(volley.ResolveEnergyXValue(cs)).FromCard(volley, play).TargetingRandomOpponents().Execute();
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Damage.UpgradeValueBy(DamageUpgrade);
    }
}
