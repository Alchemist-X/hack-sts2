namespace Sts2Sim.Core.Relics;

public sealed class Gorget : RelicModel
{
    public const string RelicId = "GORGET";

    public const string PlatingPowerId = "PLATING_POWER";

    public const string PlatingKey = "PlatingPower";

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Common;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DynamicVar(PlatingKey, 4m)
    };

    public override void AfterRoomEntered(RunState rs, AbstractRoom room)
    {
        if (room is not CombatRoom)
        {
            return;
        }
        var cs = rs.Combat ?? throw new InvalidOperationException("Gorget.AfterRoomEntered: combat room without combat state");
        PowerCmd.Apply(cs, PowerRegistry.Create(PlatingPowerId), Owner, DynamicVars[PlatingKey].BaseValue, Owner, null);
    }
}
