namespace Sts2Sim.Core.Relics;

public sealed class PetrifiedToad : RelicModel
{
    public const string RelicId = "PETRIFIED_TOAD";

    public const string PotionShapedRockId = "POTION_SHAPED_ROCK";

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Uncommon;

    public override void BeforeCombatStartLate(RunState rs)
    {
        PotionCmd.TryToProcure(rs, PotionShapedRockId, Owner);
    }
}
