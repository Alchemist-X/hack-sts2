namespace Sts2Sim.Core.Powers;

public sealed class RoyaltiesPower : PowerModel
{
    public const string PowerId = "ROYALTIES_POWER";

    public const string NoCombatRoomSite = "ROYALTIES_POWER.AfterCombatEnd: no CombatRoom on the room stack (standalone combat); GoldReward not added";

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override void AfterCombatEnd(RunState rs, CombatRoom? room)
    {
        var combatRoom = room ?? RoomStack.CurrentRoom(rs) as CombatRoom;
        if (combatRoom == null)
        {
            rs.Fidelity.Record(FidelityFlag.ApproximatedRule, NoCombatRoomSite);
            return;
        }
        var player = (PlayerState)Owner;
        combatRoom.AddExtraReward(new GoldReward(Amount, player));
    }
}
