namespace Sts2Sim.Core.Cards;

public sealed class Clash : CardDef
{
    public const string CardId = "CLASH";
    public const int Cost = 0;
    public const decimal BaseDamage = 14m;
    public const decimal DamageUpgrade = 4m;

    public Clash()
        : base(CardId, Cost, CardType.Attack, CardRarity.Event, TargetType.AnyEnemy)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DamageVar(BaseDamage, ValueProp.Move)
    };

    public override bool IsPlayable(CombatState cs, CardInstance card) => card.Owner.Hand.Cards.All(c => c.Type == CardType.Attack);

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var target = play.Target;
        ArgumentNullException.ThrowIfNull(target);
        var card = play.Card;
        DamageCmd.Attack(cs, card.Vars.Damage.BaseValue).FromCard(card, play).Targeting(target).Execute();
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Damage.UpgradeValueBy(DamageUpgrade);
    }
}
