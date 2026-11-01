namespace Sts2Sim.Core.Powers;

public sealed class ForbiddenGrimoirePower : PowerModel
{
    public const string PowerId = "FORBIDDEN_GRIMOIRE_POWER";

    public const string NoCombatRoomSite = "FORBIDDEN_GRIMOIRE_POWER.AfterCombatEnd: no CombatRoom on the room stack (standalone combat); CardRemovalReward not added";

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
        for (int i = 0; i < Amount; i++)
        {
            combatRoom.AddExtraReward(new CardRemovalReward(player));
        }
    }
}
