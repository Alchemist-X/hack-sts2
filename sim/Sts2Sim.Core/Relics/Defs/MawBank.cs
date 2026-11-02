namespace Sts2Sim.Core.Relics;

public sealed class MawBank : RelicModel
{
    public const string RelicId = "MAW_BANK";

    private bool _hasItemBeenBought;

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Event;

    protected override IEnumerable<DynamicVar> CanonicalVars => new[]
    {
        new GoldVar(12)
    };

    public override bool IsUsedUp => HasItemBeenBought;

    public bool HasItemBeenBought
    {
        get => _hasItemBeenBought;
        set
        {
            _hasItemBeenBought = value;
            if (IsUsedUp)
            {
                Status = RelicStatus.Disabled;
            }
        }
    }

    public override void AfterRoomEntered(RunState rs, AbstractRoom room)
    {
        if (ReferenceEquals(RoomStack.BaseRoom(rs), room) && !HasItemBeenBought)
        {
            PlayerCmd.GainGold(rs, DynamicVars.Gold.BaseValue, Owner);
        }
    }

    public override void AfterItemPurchased(RunState rs, PlayerState player, MerchantEntry itemPurchased, int goldSpent)
    {
        if (!ReferenceEquals(player, Owner))
        {
            return;
        }
        if (HasItemBeenBought)
        {
            return;
        }
        if (goldSpent <= 0)
        {
            return;
        }
        HasItemBeenBought = true;
    }

    protected override void WriteExtraCanonical(CanonicalWriter w)
    {
        w.WriteBool(_hasItemBeenBought);
    }
}
