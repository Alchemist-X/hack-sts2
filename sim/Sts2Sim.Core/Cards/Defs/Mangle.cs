namespace Sts2Sim.Core.Cards;

public sealed class Mangle : CardDef
{
    public const string CardId = "MANGLE";
    public const int Cost = 3;
    public const string ManglePowerId = "MANGLE_POWER";
    public const string StrengthLossKey = "StrengthLoss";
    public const decimal BaseDamage = 20m;
    public const decimal BaseStrengthLoss = 10m;
    public const decimal DamageUpgrade = 6m;
    public const decimal StrengthLossUpgrade = 5m;

    public Mangle()
        : base(CardId, Cost, CardType.Attack, CardRarity.Rare, TargetType.AnyEnemy)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DamageVar(BaseDamage, ValueProp.Move),
        new DynamicVar(StrengthLossKey, BaseStrengthLoss)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var target = play.Target;
        ArgumentNullException.ThrowIfNull(target);
        var card = play.Card;
        DamageCmd.Attack(cs, card.Vars.Damage.BaseValue).FromCard(card, play).Targeting(target).Execute();
        PowerCmd.Apply(cs, PowerRegistry.Create(ManglePowerId), target, card.Vars[StrengthLossKey].BaseValue, card.Owner, card);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Damage.UpgradeValueBy(DamageUpgrade);
        card.Vars[StrengthLossKey].UpgradeValueBy(StrengthLossUpgrade);
    }
}
