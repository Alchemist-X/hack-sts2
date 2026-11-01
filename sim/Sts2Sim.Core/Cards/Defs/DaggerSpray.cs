namespace Sts2Sim.Core.Cards;

public sealed class DaggerSpray : CardDef
{
    public const string CardId = "DAGGER_SPRAY";
    public const int Cost = 1;
    public const decimal BaseDamage = 4m;
    public const int HitCount = 2;
    public const decimal DamageUpgrade = 2m;

    public DaggerSpray()
        : base(CardId, Cost, CardType.Attack, CardRarity.Common, TargetType.AllEnemies)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DamageVar(BaseDamage, ValueProp.Move)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var card = play.Card;
        DamageCmd.Attack(cs, card.Vars.Damage.BaseValue).WithHitCount(HitCount).FromCard(card, play).TargetingAllOpponents().Execute();
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Damage.UpgradeValueBy(DamageUpgrade);
    }
}
