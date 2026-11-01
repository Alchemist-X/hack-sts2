namespace Sts2Sim.Core.Potions;

public sealed class PotionOfCapacity : PotionModel
{
    public const string PotionId = "POTION_OF_CAPACITY";

    public override string Id => PotionId;

    public override PotionRarity Rarity => PotionRarity.Uncommon;

    public override PotionUsage Usage => PotionUsage.CombatOnly;

    public override TargetType TargetType => TargetType.AnyPlayer;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new RepeatVar(2)
    };

    protected override void OnUse(RunState rs, CreatureState? target)
    {
        AssertValidForTargetedPotion(target);
        var cs = RequireCombat(rs);
        OrbCmd.AddSlots(cs, (PlayerState)target, DynamicVars.Repeat.IntValue);
    }
}
