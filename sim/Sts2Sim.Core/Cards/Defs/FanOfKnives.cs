namespace Sts2Sim.Core.Cards;

public sealed class FanOfKnives : CardDef
{
    public const string CardId = "FAN_OF_KNIVES";
    public const int Cost = 2;
    public const string ShivsKey = "Shivs";
    public const int BaseShivs = 4;
    public const decimal ShivsUpgrade = 1m;
    public const decimal PowerAmount = 1m;
    public const string FanOfKnivesPowerId = "FAN_OF_KNIVES_POWER";
    public const string ShivId = "SHIV";

    public FanOfKnives()
        : base(CardId, Cost, CardType.Power, CardRarity.Rare, TargetType.Self)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new CardsVar(ShivsKey, BaseShivs)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var fanOfKnives = play.Card;
        var owner = fanOfKnives.Owner;
        PowerCmd.Apply(cs, PowerRegistry.Create(FanOfKnivesPowerId), owner, PowerAmount, owner, fanOfKnives);
        for (int i = 0; i < fanOfKnives.Vars[ShivsKey].IntValue; i++)
        {
            CreateShivInHand(cs, owner);
        }
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars[ShivsKey].UpgradeValueBy(ShivsUpgrade);
    }

    private static void CreateShivInHand(CombatState cs, PlayerState owner)
    {
        if (CombatStatus.IsOverOrEnding(cs))
        {
            return;
        }
        var shiv = cs.CreateCard(ShivId, owner);
        CardPileCmd.AddGeneratedCardsToCombat(cs, new[] { shiv }, PileType.Hand, owner);
    }
}
