namespace Sts2Sim.Core.Potions;

public sealed class PotionOfDoom : PotionModel
{
    public const string PotionId = "POTION_OF_DOOM";

    public const string DoomPowerId = "DOOM_POWER";

    public override string Id => PotionId;

    public override PotionRarity Rarity => PotionRarity.Common;

    public override PotionUsage Usage => PotionUsage.CombatOnly;

    public override TargetType TargetType => TargetType.AnyEnemy;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DynamicVar(DynamicVarSet.DoomName, 33m)
    };

    protected override void OnUse(RunState rs, CreatureState? target)
    {
        AssertValidForTargetedPotion(target);
        var cs = RequireCombat(rs);
        PowerCmd.Apply(cs, PowerRegistry.Create(DoomPowerId), target, DynamicVars.Doom.BaseValue, Owner, null);
    }
}
