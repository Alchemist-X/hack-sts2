namespace Sts2Sim.Core.Potions;

public sealed class PotOfGhouls : PotionModel
{
    public const string PotionId = "POT_OF_GHOULS";

    public override string Id => PotionId;

    public override PotionRarity Rarity => PotionRarity.Rare;

    public override PotionUsage Usage => PotionUsage.CombatOnly;

    public override TargetType TargetType => TargetType.AnyPlayer;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new CardsVar(2)
    };

    protected override void OnUse(RunState rs, CreatureState? target)
    {
        AssertValidForTargetedPotion(target);
        var cs = RequireCombat(rs);
        var player = (PlayerState)target;
        var souls = Soul.Create(cs, player, DynamicVars.Cards.IntValue);
        CardPileCmd.AddGeneratedCardsToCombat(cs, souls, PileType.Hand, Owner);
    }
}
