namespace Sts2Sim.Core.Cards;

public sealed class Rebound : CardDef
{
    public const string CardId = "REBOUND";
    public const int Cost = 1;
    public const decimal BaseDamage = 9m;
    public const decimal DamageUpgrade = 3m;
    public const decimal ReboundAmount = 1m;
    public const string ReboundPowerId = "REBOUND_POWER";

    public Rebound()
        : base(CardId, Cost, CardType.Attack, CardRarity.Event, TargetType.AnyEnemy)
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
        var rebound = play.Card;
        DamageCmd.Attack(cs, rebound.Vars.Damage.BaseValue).FromCard(rebound, play).Targeting(target).Execute();
        PowerCmd.Apply(cs, PowerRegistry.Create(ReboundPowerId), rebound.Owner, ReboundAmount, rebound.Owner, rebound);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Damage.UpgradeValueBy(DamageUpgrade);
    }
}
