namespace Sts2Sim.Core.Relics;

public sealed class ReptileTrinket : RelicModel
{
    public const string RelicId = "REPTILE_TRINKET";

    public const string ReptileTrinketPowerId = "REPTILE_TRINKET_POWER";

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Uncommon;

    protected override IEnumerable<DynamicVar> CanonicalVars => new[]
    {
        new DynamicVar(DynamicVarSet.StrengthName, 3m)
    };

    public override void AfterPotionUsed(RunState rs, PotionModel potion, CreatureState? target)
    {
        var cs = rs.Combat;
        if (potion.HasOwner && ReferenceEquals(potion.Owner, Owner) && cs != null && cs.IsInProgress)
        {
            PowerCmd.Apply(cs, PowerRegistry.Create(ReptileTrinketPowerId), Owner, DynamicVars.Strength.BaseValue, Owner, null);
        }
    }
}
