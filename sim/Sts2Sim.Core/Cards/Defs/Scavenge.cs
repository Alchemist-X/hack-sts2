namespace Sts2Sim.Core.Cards;

public sealed class Scavenge : CardDef
{
    public const string CardId = "SCAVENGE";
    public const int Cost = 1;
    public const int BaseEnergy = 2;
    public const decimal EnergyUpgrade = 1m;
    public const int SelectCount = 1;
    public const string EnergyNextTurnPowerId = "ENERGY_NEXT_TURN_POWER";

    public Scavenge()
        : base(CardId, Cost, CardType.Skill, CardRarity.Uncommon, TargetType.Self)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new EnergyVar(BaseEnergy)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var card = play.Card;
        var owner = card.Owner;
        var prefs = new CardSelectorPrefs(CardSelectorPrefs.ExhaustSelectionPrompt, SelectCount);
        var cardModel = CardSelectCmd.FromHand(cs, owner, prefs, null, ChoiceSource.For(card, "OnPlay")).FirstOrDefault();
        if (cardModel != null)
        {
            CardCmd.Exhaust(cs, cardModel);
        }
        PowerCmd.Apply(cs, PowerRegistry.Create(EnergyNextTurnPowerId), owner, card.Vars.Energy.IntValue, owner, card);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Energy.UpgradeValueBy(EnergyUpgrade);
    }
}
