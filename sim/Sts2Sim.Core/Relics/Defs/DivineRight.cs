namespace Sts2Sim.Core.Relics;

public sealed class DivineRight : RelicModel
{
    public const string RelicId = "DIVINE_RIGHT";

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Starter;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new StarsVar(3)
    };

    public override void AfterRoomEntered(RunState rs, AbstractRoom room)
    {
        if (room is CombatRoom)
        {
            var cs = rs.Combat ?? throw new InvalidOperationException(RelicId + ".AfterRoomEntered(CombatRoom) without combat");
            PlayerCmd.GainStars(cs, DynamicVars.Stars.BaseValue, Owner);
        }
    }
}
