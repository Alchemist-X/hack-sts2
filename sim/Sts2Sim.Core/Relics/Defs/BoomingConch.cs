namespace Sts2Sim.Core.Relics;

public sealed class BoomingConch : RelicModel
{
    public const string RelicId = "BOOMING_CONCH";

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Ancient;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new CardsVar(2),
        new EnergyVar(1)
    };

    public override decimal ModifyHandDraw(CombatState cs, PlayerState player, decimal count)
    {
        if (!ReferenceEquals(player, Owner))
        {
            return count;
        }
        if (Owner.TurnNumber > 1)
        {
            return count;
        }
        if (!IsCurrentRoomElite(cs))
        {
            return count;
        }
        return count + DynamicVars.Cards.IntValue;
    }

    public override void AfterSideTurnStart(CombatState cs, CombatSide side, IReadOnlyList<CreatureState> participants)
    {
        if (!participants.Any(p => ReferenceEquals(p, Owner)) || Owner.TurnNumber > 1)
        {
            return;
        }
        if (!IsCurrentRoomElite(cs))
        {
            return;
        }
        PlayerCmd.GainEnergy(cs, DynamicVars.Energy.BaseValue, Owner);
    }

    private static bool IsCurrentRoomElite(CombatState cs)
    {
        var currentRoom = RoomStack.CurrentRoom(cs.Run);
        return currentRoom != null && currentRoom.RoomType == RoomType.Elite;
    }
}
