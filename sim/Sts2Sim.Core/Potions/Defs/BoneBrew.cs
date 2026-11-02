namespace Sts2Sim.Core.Potions;

public sealed class BoneBrew : PotionModel
{
    public const string PotionId = "BONE_BREW";

    public override string Id => PotionId;

    public override PotionRarity Rarity => PotionRarity.Uncommon;

    public override PotionUsage Usage => PotionUsage.CombatOnly;

    public override TargetType TargetType => TargetType.AnyPlayer;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new SummonVar(15m)
    };

    protected override void OnUse(RunState rs, CreatureState? target)
    {
        AssertValidForTargetedPotion(target);
        var cs = RequireCombat(rs);
        OstyCmd.Summon(cs, (PlayerState)target, DynamicVars.Summon.BaseValue, this);
    }
}
