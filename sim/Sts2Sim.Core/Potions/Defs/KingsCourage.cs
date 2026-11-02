namespace Sts2Sim.Core.Potions;

public sealed class KingsCourage : PotionModel
{
    public const string PotionId = "KINGS_COURAGE";

    public override string Id => PotionId;

    public override PotionRarity Rarity => PotionRarity.Uncommon;

    public override PotionUsage Usage => PotionUsage.CombatOnly;

    public override TargetType TargetType => TargetType.AnyPlayer;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new ForgeVar(15)
    };

    protected override void OnUse(RunState rs, CreatureState? target)
    {
        AssertValidForTargetedPotion(target);
        var cs = RequireCombat(rs);
        ForgeCmd.Forge(cs, DynamicVars.Forge.IntValue, (PlayerState)target, this);
    }
}
