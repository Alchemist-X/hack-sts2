namespace Sts2Sim.Core.Relics;

public sealed class DataDisk : RelicModel
{
    public const string RelicId = "DATA_DISK";

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Common;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new PowerVar<FocusPower>(1m)
    };

    public override void AfterRoomEntered(RunState rs, AbstractRoom room)
    {
        if (room is not CombatRoom)
        {
            return;
        }
        var cs = rs.Combat ?? throw new InvalidOperationException("DataDisk.AfterRoomEntered: combat room without combat state");
        PowerCmd.Apply<FocusPower>(cs, Owner, DynamicVars[nameof(FocusPower)].BaseValue, Owner, null);
    }
}
