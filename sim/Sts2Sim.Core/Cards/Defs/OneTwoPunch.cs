namespace Sts2Sim.Core.Cards;

public sealed class OneTwoPunch : CardDef
{
    public const string CardId = "ONE_TWO_PUNCH";
    public const int Cost = 1;
    public const string OneTwoPunchPowerId = "ONE_TWO_PUNCH_POWER";
    public const string AttacksKey = "Attacks";
    public const decimal BaseAttacks = 1m;
    public const decimal AttacksUpgrade = 1m;

    public OneTwoPunch()
        : base(CardId, Cost, CardType.Skill, CardRarity.Rare, TargetType.Self)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DynamicVar(AttacksKey, BaseAttacks)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var card = play.Card;
        var owner = card.Owner;
        PowerCmd.Apply(cs, PowerRegistry.Create(OneTwoPunchPowerId), owner, card.Vars[AttacksKey].BaseValue, owner, card);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars[AttacksKey].UpgradeValueBy(AttacksUpgrade);
    }
}
