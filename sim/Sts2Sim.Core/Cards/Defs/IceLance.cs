namespace Sts2Sim.Core.Cards;

public sealed class IceLance : CardDef
{
    public const string CardId = "ICE_LANCE";
    public const int Cost = 3;
    public const decimal BaseDamage = 19m;
    public const int BaseRepeat = 3;
    public const decimal DamageUpgrade = 5m;

    public IceLance()
        : base(CardId, Cost, CardType.Attack, CardRarity.Rare, TargetType.AnyEnemy)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DamageVar(BaseDamage, ValueProp.Move),
        new RepeatVar(BaseRepeat)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var target = play.Target;
        ArgumentNullException.ThrowIfNull(target);
        var iceLance = play.Card;
        DamageCmd.Attack(cs, iceLance.Vars.Damage.BaseValue).FromCard(iceLance, play).Targeting(target).Execute();
        for (int i = 0; i < iceLance.Vars.Repeat.IntValue; i++)
        {
            OrbCmd.Channel<FrostOrb>(cs, iceLance.Owner);
        }
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Damage.UpgradeValueBy(DamageUpgrade);
    }
}
