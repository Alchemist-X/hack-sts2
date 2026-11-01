namespace Sts2Sim.Core.Powers;

public sealed class InfiniteBladesPower : PowerModel
{
    public const string PowerId = "INFINITE_BLADES_POWER";

    public const string ShivCardId = "SHIV";

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override void BeforeHandDraw(CombatState cs, PlayerState player)
    {
        if (!ReferenceEquals(player, Owner as PlayerState))
        {
            return;
        }
        CreateShivsInHand(cs, player, Amount);
    }

    private static void CreateShivsInHand(CombatState cs, PlayerState owner, int count)
    {
        if (CombatStatus.IsOverOrEnding(cs))
        {
            return;
        }
        var shivs = new List<CardInstance>(Math.Max(0, count));
        for (int i = 0; i < count; i++)
        {
            shivs.Add(cs.CreateCard(ShivCardId, owner));
        }
        CardPileCmd.AddGeneratedCardsToCombat(cs, shivs, PileType.Hand, owner);
    }
}
