namespace Sts2Sim.Core.Potions;

public sealed class GlowwaterPotion : PotionModel
{
    public const string PotionId = "GLOWWATER_POTION";

    public override string Id => PotionId;

    public override PotionRarity Rarity => PotionRarity.Event;

    public override PotionUsage Usage => PotionUsage.CombatOnly;

    public override TargetType TargetType => TargetType.AnyPlayer;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new CardsVar(10)
    };

    protected override void OnUse(RunState rs, CreatureState? target)
    {
        AssertValidForTargetedPotion(target);
        var cs = RequireCombat(rs);
        var targetPlayer = (PlayerState)target;
        var hand = targetPlayer.Hand.Cards.ToList();
        for (int i = 0; i < hand.Count; i++)
        {
            CardCmd.Exhaust(cs, hand[i]);
        }
        CardPileCmd.Draw(cs, DynamicVars.Cards.BaseValue, targetPlayer);
    }
}
