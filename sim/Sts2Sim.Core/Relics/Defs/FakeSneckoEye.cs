namespace Sts2Sim.Core.Relics;

public sealed class FakeSneckoEye : RelicModel
{
    public const string RelicId = "FAKE_SNECKO_EYE";

    public const string ConfusedPowerId = "CONFUSED_POWER";

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Event;

    public override int MerchantCost => 50;

    public override void AfterObtained(RunState rs)
    {
        var cs = rs.Combat;
        if (cs != null && cs.IsInProgress)
        {
            ApplyPower(cs);
        }
    }

    public override void BeforeCombatStart(RunState rs)
    {
        ApplyPower(rs.Combat ?? throw new InvalidOperationException(RelicId + ".BeforeCombatStart outside combat"));
    }

    private void ApplyPower(CombatState cs)
    {
        PowerCmd.Apply(cs, PowerRegistry.Create(ConfusedPowerId), Owner, 1m, Owner, null);
    }
}
