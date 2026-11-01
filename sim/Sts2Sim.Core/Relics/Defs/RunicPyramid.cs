namespace Sts2Sim.Core.Relics;

public sealed class RunicPyramid : RelicModel
{
    public const string RelicId = "RUNIC_PYRAMID";

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Ancient;

    public override bool ShouldFlush(CombatState cs, PlayerState player)
    {
        if (!ReferenceEquals(player, Owner))
        {
            return true;
        }
        return false;
    }
}
