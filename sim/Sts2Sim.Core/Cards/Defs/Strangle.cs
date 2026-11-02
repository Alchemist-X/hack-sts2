namespace Sts2Sim.Core.Cards;

public sealed class Strangle : CardDef
{
    public const string CardId = "STRANGLE";
    public const int Cost = 1;
    public const string PowerVarName = "StranglePower";
    public const decimal BaseDamage = 8m;
    public const decimal BaseStrangle = 2m;
    public const decimal DamageUpgrade = 2m;
    public const decimal StrangleUpgrade = 1m;
    public const string StranglePowerId = "STRANGLE_POWER";

    public Strangle()
        : base(CardId, Cost, CardType.Attack, CardRarity.Uncommon, TargetType.AnyEnemy)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DamageVar(BaseDamage, ValueProp.Move),
        new DynamicVar(PowerVarName, BaseStrangle)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var card = play.Card;
        var target = play.Target;
        ArgumentNullException.ThrowIfNull(target);
        DamageCmd.Attack(cs, card.Vars.Damage.BaseValue).FromCard(card, play).Targeting(target).Execute();
        PowerCmd.Apply(cs, StranglePowerId, target, card.Vars[PowerVarName].BaseValue, card.Owner, card);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Damage.UpgradeValueBy(DamageUpgrade);
        card.Vars[PowerVarName].UpgradeValueBy(StrangleUpgrade);
    }
}
