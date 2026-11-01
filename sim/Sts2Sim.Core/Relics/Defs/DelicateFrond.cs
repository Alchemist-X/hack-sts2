namespace Sts2Sim.Core.Relics;

public sealed class DelicateFrond : RelicModel
{
    public const string RelicId = "DELICATE_FROND";

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Ancient;

    public override void BeforeCombatStart(RunState rs)
    {
        while (Owner.HasOpenPotionSlot)
        {
            var potion = PotionFactory.CreateRandomPotionOutOfCombat(rs, Owner, rs.Rng.CombatPotionGeneration).ToMutable();
            if (!PotionCmd.TryToProcure(rs, potion, Owner).Success)
            {
                break;
            }
        }
    }
}
