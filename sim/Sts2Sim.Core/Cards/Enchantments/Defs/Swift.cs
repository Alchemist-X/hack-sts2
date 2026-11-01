namespace Sts2Sim.Core.Cards.Enchantments;

public sealed class Swift : EnchantmentModel
{
    public const string EnchantmentId = "SWIFT";

    public override string Id => EnchantmentId;

    public override void OnPlay(CombatState cs, CardPlay? cardPlay)
    {
        if (Status == EnchantmentStatus.Normal)
        {
            Status = EnchantmentStatus.Disabled;
            CardPileCmd.Draw(cs, Amount, Card!.Owner);
        }
    }
}
