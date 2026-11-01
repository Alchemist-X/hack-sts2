namespace Sts2Sim.Core.Relics;

public sealed class BronzeScales : RelicModel
{
    public const string RelicId = "BRONZE_SCALES";

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Common;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new PowerVar<ThornsPower>(3m)
    };

    public override void AfterRoomEntered(RunState rs, AbstractRoom room)
    {
        if (room is not CombatRoom)
        {
            return;
        }
        var cs = rs.Combat ?? throw new InvalidOperationException("BronzeScales.AfterRoomEntered: combat room without combat state");
        PowerCmd.Apply<ThornsPower>(cs, Owner, DynamicVars[nameof(ThornsPower)].BaseValue, Owner, null);
    }
}
