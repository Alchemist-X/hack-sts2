namespace Sts2Sim.Core.Relics;

public sealed class LavaRock : RelicModel
{
    public const string RelicId = "LAVA_ROCK";

    public const string RelicsKey = "Relics";

    private bool _hasTriggered;

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Ancient;

    public override bool ShowCounter => false;

    public bool HasTriggered
    {
        get => _hasTriggered;
        set => _hasTriggered = value;
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new[]
    {
        new DynamicVar(RelicsKey, 2m)
    };

    public override bool TryModifyRewards(RunState rs, PlayerState player, List<Reward> rewards, AbstractRoom? room)
    {
        if (!ReferenceEquals(player, Owner))
        {
            return false;
        }
        if (room == null || room.RoomType != RoomType.Boss)
        {
            return false;
        }
        if (rs.CurrentActIndex != 0)
        {
            return false;
        }
        if (HasTriggered)
        {
            return false;
        }
        for (int i = 0; i < DynamicVars[RelicsKey].IntValue; i++)
        {
            rewards.Add(new RelicReward(player));
        }
        HasTriggered = true;
        Status = RelicStatus.Disabled;
        return true;
    }

    protected override void WriteExtraCanonical(CanonicalWriter w)
    {
        w.WriteBool(_hasTriggered);
    }
}
