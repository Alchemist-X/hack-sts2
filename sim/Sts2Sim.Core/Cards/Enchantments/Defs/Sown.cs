namespace Sts2Sim.Core.Cards.Enchantments;

public sealed class Sown : EnchantmentModel
{
    public const string EnchantmentId = "SOWN";

    public override string Id => EnchantmentId;

    public override void OnPlay(CombatState cs, CardPlay? cardPlay)
    {
        if (Status == EnchantmentStatus.Normal)
        {
            Status = EnchantmentStatus.Disabled;
            PlayerCmd.GainEnergy(cs, Amount, Card!.Owner);
        }
    }
}
