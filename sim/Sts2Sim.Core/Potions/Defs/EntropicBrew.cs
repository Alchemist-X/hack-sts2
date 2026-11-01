namespace Sts2Sim.Core.Potions;

public sealed class EntropicBrew : PotionModel
{
    public const string PotionId = "ENTROPIC_BREW";

    public override string Id => PotionId;

    public override PotionRarity Rarity => PotionRarity.Rare;

    public override PotionUsage Usage => PotionUsage.AnyTime;

    public override TargetType TargetType => TargetType.AnyPlayer;

    protected override void OnUse(RunState rs, CreatureState? target)
    {
        AssertValidForTargetedPotion(target);
        var targetPlayer = (PlayerState)target;
        while (targetPlayer.HasOpenPotionSlot)
        {
            var potion = PotionFactory.CreateRandomPotionOutOfCombat(rs, targetPlayer, rs.Rng.CombatPotionGeneration).ToMutable();
            if (!PotionCmd.TryToProcure(rs, potion, targetPlayer).Success)
            {
                break;
            }
        }
    }
}
