namespace Sts2Sim.Core.Cards;

public sealed class CaptureSpirit : CardDef
{
    public const string CardId = "CAPTURE_SPIRIT";
    public const int Cost = 1;
    public const decimal BaseDamage = 3m;
    public const int BaseCards = 3;
    public const decimal DamageUpgrade = 1m;
    public const decimal CardsUpgrade = 1m;

    public CaptureSpirit()
        : base(CardId, Cost, CardType.Skill, CardRarity.Uncommon, TargetType.AnyEnemy)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DamageVar(BaseDamage, ValueProp.Unblockable | ValueProp.Unpowered | ValueProp.Move),
        new CardsVar(BaseCards)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var target = play.Target;
        ArgumentNullException.ThrowIfNull(target);
        var captureSpirit = play.Card;
        var owner = captureSpirit.Owner;
        CreatureCmd.Damage(cs, target, captureSpirit.Vars.Damage, captureSpirit, play);
        var cards = Soul.Create(cs, owner, captureSpirit.Vars.Cards.IntValue);
        CardPileCmd.AddGeneratedCardsToCombat(cs, cards, PileType.Draw, owner, CardPilePosition.Random);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Damage.UpgradeValueBy(DamageUpgrade);
        card.Vars.Cards.UpgradeValueBy(CardsUpgrade);
    }
}
