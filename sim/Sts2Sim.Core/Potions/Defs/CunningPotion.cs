namespace Sts2Sim.Core.Potions;

public sealed class CunningPotion : PotionModel
{
    public const string PotionId = "CUNNING_POTION";

    public const string ShivCardId = "SHIV";

    public override string Id => PotionId;

    public override PotionRarity Rarity => PotionRarity.Uncommon;

    public override PotionUsage Usage => PotionUsage.CombatOnly;

    public override TargetType TargetType => TargetType.AnyPlayer;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new CardsVar(3)
    };

    protected override void OnUse(RunState rs, CreatureState? target)
    {
        AssertValidForTargetedPotion(target);
        var cs = RequireCombat(rs);
        var player = (PlayerState)target;
        var shivs = CreateShivsInHand(cs, player, DynamicVars.Cards.IntValue, Owner);
        for (int i = 0; i < shivs.Count; i++)
        {
            CardCmd.Upgrade(cs, shivs[i]);
        }
    }

    private static IReadOnlyList<CardInstance> CreateShivsInHand(CombatState cs, PlayerState owner, int count, PlayerState? creator)
    {
        if (count == 0)
        {
            return Array.Empty<CardInstance>();
        }
        if (CombatStatus.IsOverOrEnding(cs))
        {
            return Array.Empty<CardInstance>();
        }
        var shivs = new List<CardInstance>(count);
        for (int i = 0; i < count; i++)
        {
            shivs.Add(cs.CreateCard(ShivCardId, owner));
        }
        CardPileCmd.AddGeneratedCardsToCombat(cs, shivs, PileType.Hand, creator ?? owner);
        return shivs;
    }
}
