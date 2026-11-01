namespace Sts2Sim.Core.Cards;

public sealed class FlashOfSteel : CardDef
{
    public const string CardId = "FLASH_OF_STEEL";
    public const int Cost = 0;
    public const decimal BaseDamage = 5m;
    public const int BaseCards = 1;
    public const decimal DamageUpgrade = 3m;

    public FlashOfSteel()
        : base(CardId, Cost, CardType.Attack, CardRarity.Uncommon, TargetType.AnyEnemy)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DamageVar(BaseDamage, ValueProp.Move),
        new CardsVar(BaseCards)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var target = play.Target;
        ArgumentNullException.ThrowIfNull(target);
        var flashOfSteel = play.Card;
        DamageCmd.Attack(cs, flashOfSteel.Vars.Damage.BaseValue).FromCard(flashOfSteel, play).Targeting(target).Execute();
        CardPileCmd.Draw(cs, flashOfSteel.Vars.Cards.BaseValue, flashOfSteel.Owner);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Damage.UpgradeValueBy(DamageUpgrade);
    }
}
