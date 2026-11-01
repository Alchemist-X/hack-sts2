namespace Sts2Sim.Core.Powers;

public sealed class BarricadePower : PowerModel
{
    public const string PowerId = "BARRICADE_POWER";

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Single;

    public override bool ShouldClearBlock(CombatState cs, CreatureState creature)
    {
        if (!ReferenceEquals(Owner, creature))
        {
            return true;
        }
        return false;
    }
}
