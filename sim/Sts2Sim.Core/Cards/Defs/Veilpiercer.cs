namespace Sts2Sim.Core.Cards;

public sealed class Veilpiercer : CardDef
{
    public const string CardId = "VEILPIERCER";
    public const int Cost = 1;
    public const decimal BaseDamage = 10m;
    public const decimal VeilpiercerAmount = 1m;
    public const decimal DamageUpgrade = 3m;
    public const string VeilpiercerPowerId = "VEILPIERCER_POWER";

    public Veilpiercer()
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
        PowerCmd.Apply(cs, VeilpiercerPowerId, owner, VeilpiercerAmount, owner, card);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Damage.UpgradeValueBy(DamageUpgrade);
    }
}
