namespace Sts2Sim.Core.Cards;

public sealed class GrandFinale : CardDef
{
    public const string CardId = "GRAND_FINALE";
    public const int Cost = 0;
    public const decimal BaseDamage = 60m;
    public const decimal DamageUpgrade = 15m;

    public GrandFinale()
        : base(CardId, Cost, CardType.Attack, CardRarity.Rare, TargetType.AllEnemies)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DamageVar(BaseDamage, ValueProp.Move)
    };

    public override bool IsPlayable(CombatState cs, CardInstance card) => card.Owner.DrawPile.Cards.Count == 0;

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var grandFinale = play.Card;
        DamageCmd.Attack(cs, grandFinale.Vars.Damage.BaseValue).FromCard(grandFinale, play).TargetingAllOpponents().Execute();
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Damage.UpgradeValueBy(DamageUpgrade);
    }
}
