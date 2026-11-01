namespace Sts2Sim.Core.Powers;

public sealed class PillarOfCreationPower : PowerModel
{
    public const string PowerId = "PILLAR_OF_CREATION_POWER";

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override void AfterCardGeneratedForCombat(CombatState cs, CardInstance card, PlayerState? creator)
    {
        if (creator == null || !ReferenceEquals(creator, Owner))
        {
            return;
        }
        CreatureCmd.GainBlock(cs, Owner, Amount, ValueProp.Unpowered, null);
    }
}
