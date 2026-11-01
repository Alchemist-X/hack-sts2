namespace Sts2Sim.Core.Relics;

public sealed class SignetRing : RelicModel
{
    public const string RelicId = "SIGNET_RING";

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Ancient;

    protected override IEnumerable<DynamicVar> CanonicalVars => new[]
    {
        new GoldVar(888)
    };

    public override void AfterObtained(RunState rs)
    {
        PlayerCmd.GainGold(rs, DynamicVars.Gold.BaseValue, Owner);
    }
}
