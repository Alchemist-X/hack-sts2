namespace Sts2Sim.Core.Relics;

public sealed class NinjaScroll : RelicModel
{
    public const string RelicId = "NINJA_SCROLL";

    public const string ShivKey = "Shivs";

    public const string ShivCardId = "SHIV";

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Shop;

    protected override IEnumerable<DynamicVar> CanonicalVars => new[]
    {
        new DynamicVar(ShivKey, 3m)
    };

    public override void BeforeHandDraw(CombatState cs, PlayerState player)
    {
        if (!ReferenceEquals(player, Owner) || Owner.TurnNumber > 1)
        {
            return;
        }
        CreateShivsInHand(cs, Owner, DynamicVars[ShivKey].IntValue);
    }

    private static void CreateShivsInHand(CombatState cs, PlayerState owner, int count)
    {
        if (count == 0)
        {
            return;
        }
        if (CombatStatus.IsOverOrEnding(cs))
        {
            return;
        }
        var shivs = new List<CardInstance>(count);
        for (int i = 0; i < count; i++)
        {
            shivs.Add(cs.CreateCard(ShivCardId, owner));
        }
        CardPileCmd.AddGeneratedCardsToCombat(cs, shivs, PileType.Hand, owner);
    }
}
