namespace Sts2Sim.Core.Cards;

public sealed class GiantRock : CardDef
{
    public const string CardId = "GIANT_ROCK";
    public const int Cost = 1;
    public const decimal BaseDamage = 20m;
    public const decimal DamageUpgrade = 4m;

    public GiantRock()
        : base(CardId, Cost, CardType.Attack, CardRarity.Token, TargetType.AnyEnemy)
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
        var giantRock = play.Card;
        DamageCmd.Attack(cs, giantRock.Vars.Damage.BaseValue).FromCard(giantRock, play).Targeting(target).Execute();
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Damage.UpgradeValueBy(DamageUpgrade);
    }
}
