namespace Sts2Sim.Core.Potions;

public sealed class Clarity : PotionModel
{
    public const string PotionId = "CLARITY";

    public const string ClarityPowerId = "CLARITY_POWER";

    public const string ClarityPowerKey = "ClarityPower";

    public override string Id => PotionId;

    public override PotionRarity Rarity => PotionRarity.Uncommon;

    public override PotionUsage Usage => PotionUsage.CombatOnly;

    public override TargetType TargetType => TargetType.AnyPlayer;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DynamicVar(ClarityPowerKey, 3m),
        new CardsVar(1)
    };

    protected override void OnUse(RunState rs, CreatureState? target)
    {
        AssertValidForTargetedPotion(target);
        var cs = RequireCombat(rs);
        CardPileCmd.Draw(cs, DynamicVars.Cards.BaseValue, (PlayerState)target);
        PowerCmd.Apply(cs, PowerRegistry.Create(ClarityPowerId), target, DynamicVars[ClarityPowerKey].BaseValue, Owner, null);
    }
}
