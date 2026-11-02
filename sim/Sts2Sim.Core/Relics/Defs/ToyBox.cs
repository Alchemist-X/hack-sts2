namespace Sts2Sim.Core.Relics;

public sealed class ToyBox : RelicModel
{
    public const string RelicId = "TOY_BOX";

    private const string RelicsKey = "Relics";

    private const string CombatsKey = "Combats";

    private int _combatsSeen;

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Ancient;

    public override bool HasUponPickupEffect => true;

    public override bool IsUsedUp => CombatsSeen >= DynamicVars[CombatsKey].IntValue * DynamicVars[RelicsKey].IntValue;

    public override bool ShowCounter => !IsUsedUp;

    public override int DisplayAmount => CombatsSeen % DynamicVars[CombatsKey].IntValue;

    public int CombatsSeen
    {
        get => _combatsSeen;
        set
        {
            _combatsSeen = value;
            if (IsUsedUp)
            {
                Status = RelicStatus.Disabled;
            }
        }
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new[]
    {
        new DynamicVar(RelicsKey, 5m),
        new DynamicVar(CombatsKey, 3m)
    };

    public override void AfterObtained(RunState rs)
    {
        if (!SmallCapsule.HasRunLayer(rs, this))
        {
            return;
        }
        var rewards = new List<Reward>();
        for (int i = 0; i < DynamicVars[RelicsKey].IntValue; i++)
        {
            var relicModel = RelicFactory.PullNextRelicFromFront(rs, Owner);
            relicModel.IsWax = true;
            rewards.Add(new RelicReward(relicModel, Owner));
        }
        RewardsCmd.OfferCustom(rs, Owner, rewards);
    }

    public override void AfterCombatEnd(RunState rs, CombatRoom? room)
    {
        if (IsUsedUp)
        {
            return;
        }
        CombatsSeen++;
        if (CombatsSeen % DynamicVars[CombatsKey].IntValue != 0)
        {
            return;
        }
        var relicModel = Owner.Relics.FirstOrDefault(r => r != null && r.IsWax && !r.IsMelted);
        if (relicModel != null)
        {
            RelicCmd.Melt(rs, relicModel);
        }
    }

    protected override void WriteExtraCanonical(CanonicalWriter w)
    {
        w.WriteInt32(_combatsSeen);
    }
}
