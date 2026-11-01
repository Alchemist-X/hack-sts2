namespace Sts2Sim.Core.Cards;

public sealed class Modded : CardDef
{
    public const string CardId = "MODDED";
    public const int Cost = 0;
    public const int BaseRepeat = 1;
    public const int BaseCards = 1;
    public const decimal CardsUpgrade = 1m;
    public const int CostIncrease = 1;

    public Modded()
        : base(CardId, Cost, CardType.Skill, CardRarity.Rare, TargetType.Self)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new RepeatVar(BaseRepeat),
        new CardsVar(BaseCards)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var modded = play.Card;
        var owner = modded.Owner;
        OrbCmd.AddSlots(cs, owner, modded.Vars.Repeat.IntValue);
        CardPileCmd.Draw(cs, modded.Vars.Cards.BaseValue, owner);
        modded.EnergyCost.AddThisCombat(CostIncrease);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Cards.UpgradeValueBy(CardsUpgrade);
    }
}
