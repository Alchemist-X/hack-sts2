namespace Sts2Sim.Core.Cards;

public sealed class ColdSnap : CardDef
{
    public const string CardId = "COLD_SNAP";
    public const int Cost = 1;
    public const decimal BaseDamage = 6m;
    public const decimal DamageUpgrade = 3m;

    public ColdSnap()
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
        var coldSnap = play.Card;
        DamageCmd.Attack(cs, coldSnap.Vars.Damage.BaseValue).FromCard(coldSnap, play).Targeting(target).Execute();
        OrbCmd.Channel<FrostOrb>(cs, coldSnap.Owner);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Damage.UpgradeValueBy(DamageUpgrade);
    }
}
