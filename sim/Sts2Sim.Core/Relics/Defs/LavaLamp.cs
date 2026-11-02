namespace Sts2Sim.Core.Relics;

public sealed class LavaLamp : RelicModel
{
    public const string RelicId = "LAVA_LAMP";

    private bool _tookDamageThisCombat;

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Shop;

    public bool TookDamageThisCombat
    {
        get => _tookDamageThisCombat;
        set => _tookDamageThisCombat = value;
    }

    public override void AfterRoomEntered(RunState rs, AbstractRoom room)
    {
        TookDamageThisCombat = false;
    }

    public override void AfterDamageReceived(RunState rs, CreatureState target, DamageResult result, ValueProp props, CreatureState? dealer, CardInstance? cardSource)
    {
        if (RoomStack.CurrentRoom(rs) is not CombatRoom)
        {
            return;
        }
        if (!ReferenceEquals(target, Owner))
        {
            return;
        }
        if (result.UnblockedDamage <= 0)
        {
            return;
        }
        if (props.HasFlag(ValueProp.Unblockable))
        {
            return;
        }
        TookDamageThisCombat = true;
    }

    public override bool TryModifyCardRewardOptionsLate(RunState rs, PlayerState player, List<CardCreationResult> cardRewardOptions, CardCreationOptions creationOptions)
    {
        if (RoomStack.CurrentRoom(rs) is not CombatRoom)
        {
            return false;
        }
        if (!ReferenceEquals(player, Owner))
        {
            return false;
        }
        if (TookDamageThisCombat)
        {
            return false;
        }
        foreach (var current in cardRewardOptions)
        {
            var card = current.Card;
            if (card.IsUpgradable)
            {
                var card2 = card.CloneForCombat(rs.AllocateCardInstanceId());
                card2.DeckVersionId = CardInstance.NoDeckVersion;
                CardCmd.Upgrade(rs.Combat, card2);
                current.ModifyCard(card2, this);
            }
        }
        return true;
    }

    protected override void WriteExtraCanonical(CanonicalWriter w)
    {
        w.WriteBool(_tookDamageThisCombat);
    }
}
