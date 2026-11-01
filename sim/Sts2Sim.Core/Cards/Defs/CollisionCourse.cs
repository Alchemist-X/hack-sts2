namespace Sts2Sim.Core.Cards;

public sealed class CollisionCourse : CardDef
{
    public const string CardId = "COLLISION_COURSE";
    public const int Cost = 0;
    public const decimal BaseDamage = 10m;
    public const decimal DamageUpgrade = 4m;
    public const string DebrisId = "DEBRIS";

    public CollisionCourse()
        : base(CardId, Cost, CardType.Attack, CardRarity.Common, TargetType.AnyEnemy)
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
        var collisionCourse = play.Card;
        var owner = collisionCourse.Owner;
        DamageCmd.Attack(cs, collisionCourse.Vars.Damage.BaseValue).FromCard(collisionCourse, play).Targeting(target).Execute();
        CardPileCmd.AddGeneratedCardToCombat(cs, cs.CreateCard(DebrisId, owner), PileType.Hand, owner);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Damage.UpgradeValueBy(DamageUpgrade);
    }
}
