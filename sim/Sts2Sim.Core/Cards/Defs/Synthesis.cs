namespace Sts2Sim.Core.Cards;

public sealed class Synthesis : CardDef
{
    public const string CardId = "SYNTHESIS";
    public const int Cost = 2;
    public const decimal BaseDamage = 14m;
    public const decimal FreePowerAmount = 1m;
    public const decimal DamageUpgrade = 6m;
    public const string FreePowerPowerId = "FREE_POWER_POWER";

    public Synthesis()
        : base(CardId, Cost, CardType.Attack, CardRarity.Uncommon, TargetType.AnyEnemy)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DamageVar(BaseDamage, ValueProp.Move)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var card = play.Card;
        var target = play.Target;
        ArgumentNullException.ThrowIfNull(target);
        DamageCmd.Attack(cs, card.Vars.Damage.BaseValue).FromCard(card, play).Targeting(target).Execute();
        var owner = card.Owner;
        PowerCmd.Apply(cs, FreePowerPowerId, owner, FreePowerAmount, owner, card);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Damage.UpgradeValueBy(DamageUpgrade);
    }
}
