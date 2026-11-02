namespace Sts2Sim.Core.Cards;

public sealed class Devastate : CardDef
{
    public const string CardId = "DEVASTATE";
    public const int Cost = 1;
    public const int StarCost = 4;
    public const decimal BaseDamage = 35m;
    public const decimal DamageUpgrade = 10m;

    public Devastate()
        : base(CardId, Cost, CardType.Attack, CardRarity.Uncommon, TargetType.AnyEnemy)
    {
    }

    public override int CanonicalStarCost => StarCost;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DamageVar(BaseDamage, ValueProp.Move)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var target = play.Target;
        ArgumentNullException.ThrowIfNull(target);
        var devastate = play.Card;
        DamageCmd.Attack(cs, devastate.Vars.Damage.BaseValue).FromCard(devastate, play).Targeting(target).Execute();
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Damage.UpgradeValueBy(DamageUpgrade);
    }
}
