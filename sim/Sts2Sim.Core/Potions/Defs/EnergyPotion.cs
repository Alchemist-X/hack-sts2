namespace Sts2Sim.Core.Potions;

public sealed class EnergyPotion : PotionModel
{
    public const string PotionId = "ENERGY_POTION";

    public override string Id => PotionId;

    public override PotionRarity Rarity => PotionRarity.Common;

    public override PotionUsage Usage => PotionUsage.CombatOnly;

    public override TargetType TargetType => TargetType.AnyPlayer;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new EnergyVar(2)
    };

    protected override void OnUse(RunState rs, CreatureState? target)
    {
        AssertValidForTargetedPotion(target);
        var cs = RequireCombat(rs);
        PlayerCmd.GainEnergy(cs, DynamicVars.Energy.BaseValue, (PlayerState)target);
    }
}
