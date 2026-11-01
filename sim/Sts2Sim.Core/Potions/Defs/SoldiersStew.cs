namespace Sts2Sim.Core.Potions;

public sealed class SoldiersStew : PotionModel
{
    public const string PotionId = "SOLDIERS_STEW";

    public override string Id => PotionId;

    public override PotionRarity Rarity => PotionRarity.Rare;

    public override PotionUsage Usage => PotionUsage.CombatOnly;

    public override TargetType TargetType => TargetType.AnyPlayer;

    protected override void OnUse(RunState rs, CreatureState? target)
    {
        AssertValidForTargetedPotion(target);
        var player = (PlayerState)target;
        var strikes = player.AllPiles.SelectMany(static p => p.Cards).Where(static c => c.HasTag(CardTag.Strike)).ToList();
        for (int i = 0; i < strikes.Count; i++)
        {
            strikes[i].BaseReplayCount++;
        }
    }
}
