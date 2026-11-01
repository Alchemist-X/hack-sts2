namespace Sts2Sim.Core.Potions;

public sealed class CureAll : PotionModel
{
    public const string PotionId = "CURE_ALL";

    public override string Id => PotionId;

    public override PotionRarity Rarity => PotionRarity.Uncommon;

    public override PotionUsage Usage => PotionUsage.CombatOnly;

    public override TargetType TargetType => TargetType.AnyPlayer;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new EnergyVar(1),
        new CardsVar(2)
    };

    protected override void OnUse(RunState rs, CreatureState? target)
    {
        AssertValidForTargetedPotion(target);
        var cs = RequireCombat(rs);
        var player = (PlayerState)target;
        PlayerCmd.GainEnergy(cs, DynamicVars.Energy.BaseValue, player);
        CardPileCmd.Draw(cs, DynamicVars.Cards.BaseValue, player);
    }
}
