namespace Sts2Sim.Core.Relics;

public sealed class SneckoEye : RelicModel
{
    public const string RelicId = "SNECKO_EYE";

    public const string ConfusedPowerId = "CONFUSED_POWER";

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Ancient;

    protected override IEnumerable<DynamicVar> CanonicalVars => new[]
    {
        new CardsVar(2)
    };

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

    public override decimal ModifyHandDraw(CombatState cs, PlayerState player, decimal count)
    {
        if (!ReferenceEquals(player, Owner))
        {
            return count;
        }
        return count + DynamicVars.Cards.BaseValue;
    }

    private void ApplyPower(CombatState cs)
    {
        PowerCmd.Apply(cs, PowerRegistry.Create(ConfusedPowerId), Owner, 1m, Owner, null);
    }
}
