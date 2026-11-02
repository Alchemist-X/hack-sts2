namespace Sts2Sim.Core.Cards;

public sealed class SpectrumShift : CardDef
{
    public const string CardId = "SPECTRUM_SHIFT";
    public const int Cost = 2;
    public const string PowerId = "SPECTRUM_SHIFT_POWER";
    public const int BaseCards = 1;
    public const int CostUpgrade = -1;

    public SpectrumShift()
        : base(CardId, Cost, CardType.Power, CardRarity.Uncommon, TargetType.Self)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new CardsVar(BaseCards)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var card = play.Card;
        var creature = card.Owner;
        PowerCmd.Apply(cs, PowerRegistry.Create(PowerId), creature, card.Vars.Cards.BaseValue, creature, card);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.EnergyCost.UpgradeBy(CostUpgrade);
    }
}
