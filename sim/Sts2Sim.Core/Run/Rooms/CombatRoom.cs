namespace Sts2Sim.Core.Run;

public sealed partial class CombatRoom : AbstractRoom
{
    public CombatRoom(EncounterDef encounter)
    {
        Encounter = encounter;
    }

    public EncounterDef Encounter { get; }

    public override RoomType RoomType => throw new NotImplementedException("W2 R4: CombatRoom.RoomType");

    public override string? ModelId => Encounter.Id;

    public decimal GoldProportion { get; set; } = 1m;

    public List<Reward> ExtraRewards { get; } = new();

    public bool ShouldResumeParentEventAfterCombat { get; set; }

    public string? ParentEventId { get; set; }

    public CombatState StartCombat(RunState rs) => throw new NotImplementedException("W2 R4: CombatRoom.StartCombat");

    public void OnCombatEnded(RunState rs, CombatState cs) => throw new NotImplementedException("W2 R4: CombatRoom.OnCombatEnded");

    public void OfferRoomEndRewards(RunState rs) => throw new NotImplementedException("W2 R4: CombatRoom.OfferRoomEndRewards");
}
