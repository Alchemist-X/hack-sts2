namespace Sts2Sim.Core.Powers;

public sealed class FurnacePower : PowerModel
{
    public const string PowerId = "FURNACE_POWER";

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override void AfterSideTurnStart(CombatState cs, CombatSide side, IReadOnlyList<CreatureState> participants)
    {
        if (!CombatStatus.ContainsById(participants, Owner))
        {
            return;
        }
        var player = Owner as PlayerState ?? throw new InvalidOperationException($"{PowerId} forges for its owner's player, but its owner is not a player");
        ForgeCmd.Forge(cs, Amount, player, this);
    }
}
