namespace Sts2Sim.Core.Cards;

public sealed class BladeOfInk : CardDef
{
    public const string CardId = "BLADE_OF_INK";
    public const int Cost = 1;
    public const int BaseCards = 2;
    public const decimal CardsUpgrade = 1m;
    public const string ShivId = "SHIV";
    public const string InkyId = "INKY";
    public const decimal InkyAmount = 1m;

    public BladeOfInk()
        : base(CardId, Cost, CardType.Skill, CardRarity.Rare, TargetType.Self)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new CardsVar(BaseCards)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var bladeOfInk = play.Card;
        var shivs = CreateShivsInHand(cs, bladeOfInk.Owner, bladeOfInk.Vars.Cards.IntValue);
        foreach (var current in shivs)
        {
            CardCmd.Enchant(InkyId, current, InkyAmount);
        }
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Cards.UpgradeValueBy(CardsUpgrade);
    }

    private static IReadOnlyList<CardInstance> CreateShivsInHand(CombatState cs, PlayerState owner, int count)
    {
        if (count == 0)
        {
            return Array.Empty<CardInstance>();
        }
        if (CombatStatus.IsEnding(cs))
        {
            return Array.Empty<CardInstance>();
        }
        var shivs = new List<CardInstance>(count);
        for (int i = 0; i < count; i++)
        {
            shivs.Add(cs.CreateCard(ShivId, owner));
        }
        CardPileCmd.AddGeneratedCardsToCombat(cs, shivs, PileType.Hand, owner);
        return shivs;
    }
}
