namespace Sts2Sim.Core.Cards;

public sealed class Refract : CardDef
{
    public const string CardId = "REFRACT";
    public const int Cost = 3;
    public const int BaseRepeat = 2;
    public const decimal BaseDamage = 10m;
    public const int HitCount = 2;
    public const decimal DamageUpgrade = 3m;

    public Refract()
        : base(CardId, Cost, CardType.Attack, CardRarity.Uncommon, TargetType.AnyEnemy)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new RepeatVar(BaseRepeat),
        new DamageVar(BaseDamage, ValueProp.Move)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var target = play.Target;
        ArgumentNullException.ThrowIfNull(target);
        var refract = play.Card;
        DamageCmd.Attack(cs, refract.Vars.Damage.BaseValue).WithHitCount(HitCount).FromCard(refract, play).Targeting(target).Execute();
        for (int i = 0; i < refract.Vars.Repeat.IntValue; i++)
        {
            OrbCmd.Channel<GlassOrb>(cs, refract.Owner);
        }
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Damage.UpgradeValueBy(DamageUpgrade);
    }
}
