namespace Sts2Sim.Core.Relics;

public sealed class Vajra : RelicModel
{
    public const string RelicId = "VAJRA";

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Common;

    protected override IEnumerable<DynamicVar> CanonicalVars => new[]
    {
        new PowerVar<StrengthPower>(1m)
    };

    public override void AfterRoomEntered(RunState rs, AbstractRoom room)
    {
        if (room is not CombatRoom)
        {
            return;
        }
        var cs = rs.Combat ?? throw new InvalidOperationException(RelicId + ".AfterRoomEntered: combat room without an attached combat");
        PowerCmd.Apply<StrengthPower>(cs, Owner, DynamicVars.Strength.BaseValue, Owner, null);
    }
}
