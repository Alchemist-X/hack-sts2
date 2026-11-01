namespace Sts2Sim.Core.Cards;

public sealed class BallLightning : CardDef
{
    public const string CardId = "BALL_LIGHTNING";
    public const int Cost = 1;
    public const decimal BaseDamage = 7m;
    public const decimal DamageUpgrade = 3m;

    public BallLightning()
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
        var ballLightning = play.Card;
        DamageCmd.Attack(cs, ballLightning.Vars.Damage.BaseValue).FromCard(ballLightning, play).Targeting(target).Execute();
        OrbCmd.Channel<LightningOrb>(cs, ballLightning.Owner);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Damage.UpgradeValueBy(DamageUpgrade);
    }
}
