namespace Sts2Sim.Core.Relics;

public sealed class SilverCrucible : RelicModel
{
    public const string RelicId = "SILVER_CRUCIBLE";

    private int _timesUsed;
    private int _treasureRoomsEntered;

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Ancient;

    public override bool IsUsedUp
    {
        get
        {
            if (TimesUsed >= DynamicVars.Cards.IntValue)
            {
                return TreasureRoomsEntered > 0;
            }
            return false;
        }
    }

    public override bool ShowCounter
    {
        get
        {
            if (DynamicVars.Cards.IntValue > 0)
            {
                return TimesUsed < DynamicVars.Cards.IntValue;
            }
            return false;
        }
    }

    public override int DisplayAmount => DynamicVars.Cards.IntValue - TimesUsed;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new CardsVar(3)
    };

    public int TimesUsed
    {
        get => _timesUsed;
        set
        {
            _timesUsed = value;
            CheckIfUsedUp();
        }
    }

    public int TreasureRoomsEntered
    {
        get => _treasureRoomsEntered;
        set
        {
            _treasureRoomsEntered = value;
            CheckIfUsedUp();
        }
    }

    public override bool IsAllowed(RunState rs) => rs.Players.Count == 1;

    private void CheckIfUsedUp()
    {
        if (IsUsedUp)
        {
            Status = RelicStatus.Disabled;
        }
    }

    public override bool TryModifyCardRewardOptionsLate(RunState rs, PlayerState player, List<CardCreationResult> cardRewardOptions, CardCreationOptions creationOptions)
    {
        if (!ReferenceEquals(player, Owner))
        {
            return false;
        }
        if (TimesUsed >= DynamicVars.Cards.IntValue)
        {
            return false;
        }
        if (!creationOptions.Flags.HasFlag(CardCreationFlags.IsCardReward))
        {
            return false;
        }
        foreach (var current in cardRewardOptions)
        {
            var card = current.Card;
            if (card.IsUpgradable)
            {
                var card2 = MoltenEgg.CloneRunCard(rs, card);
                CardCmd.Upgrade(rs.Combat, card2);
                current.ModifyCard(card2, this);
            }
        }
        return true;
    }

    public override void AfterModifyingCardRewardOptions(RunState rs)
    {
        if (TimesUsed >= DynamicVars.Cards.IntValue)
        {
            return;
        }
        TimesUsed++;
    }

    public override void AfterRoomEntered(RunState rs, AbstractRoom room)
    {
        if (room is TreasureRoom)
        {
            TreasureRoomsEntered++;
        }
    }

    public override bool ShouldGenerateTreasure(RunState rs, PlayerState player)
    {
        if (!ReferenceEquals(player, Owner))
        {
            return true;
        }
        return TreasureRoomsEntered > 1;
    }

    protected override void WriteExtraCanonical(CanonicalWriter w)
    {
        w.WriteInt32(_timesUsed);
        w.WriteInt32(_treasureRoomsEntered);
    }
}
