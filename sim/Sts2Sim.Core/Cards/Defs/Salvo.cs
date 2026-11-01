namespace Sts2Sim.Core.Cards;

public sealed class Salvo : CardDef
{
    public const string CardId = "SALVO";
    public const int Cost = 1;
    public const decimal BaseDamage = 12m;
    public const decimal RetainHandAmount = 1m;
    public const decimal DamageUpgrade = 4m;
    public const string RetainHandPowerId = "RETAIN_HAND_POWER";

    public Salvo()
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
        PowerCmd.Apply(cs, RetainHandPowerId, owner, RetainHandAmount, owner, card);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Damage.UpgradeValueBy(DamageUpgrade);
    }
}
