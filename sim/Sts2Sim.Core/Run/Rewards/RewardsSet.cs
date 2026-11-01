namespace Sts2Sim.Core.Run;

public sealed partial class RewardsSet : ICanonical
{
    public const string FakeMerchantEventEncounterId = "FAKE_MERCHANT_EVENT_ENCOUNTER";

    public const int FakeMerchantGoldReward = 300;

    public const double PovertyAscensionGoldMultiplier = 0.75;

    private static readonly string[] NoRewardEncounterIds =
    {
        "BATTLEWORN_DUMMY_EVENT_V1_ENCOUNTER", "BATTLEWORN_DUMMY_EVENT_V2_ENCOUNTER", "BATTLEWORN_DUMMY_EVENT_V3_ENCOUNTER",
    };

    private static readonly Comparison<Reward> SetIndexOrder = static (x, y) => x.RewardsSetIndex.CompareTo(y.RewardsSetIndex);

    private bool _allowEmptyRewards;

    private bool _disallowSkipping;

    private bool _isGenerated;

    public RewardsSet(PlayerState player)
    {
        Player = player;
    }

    public PlayerState Player { get; private set; }

    public List<Reward> Rewards { get; } = new();

    public AbstractRoom? Room { get; private set; }

    public bool IsTerminal { get; set; }

    public bool DisallowSkipping => _disallowSkipping;

    public bool AllowEmptyRewards => _allowEmptyRewards;

    public bool IsGenerated => _isGenerated;

    public bool AllRewardsSuccessfullySelected => Rewards.All(r => r.SuccessfullySelected);

    public int Id { get; set; } = -1;

    public static bool ShouldGiveRewards(EncounterDef encounter) => Array.IndexOf(NoRewardEncounterIds, encounter.Id) < 0;

    public static int MinGoldReward(RunState rs, EncounterDef encounter, RoomType roomType)
    {
        if (string.Equals(encounter.Id, FakeMerchantEventEncounterId, StringComparison.Ordinal))
        {
            return FakeMerchantGoldReward;
        }
        double num = roomType switch
        {
            RoomType.Monster => 10,
            RoomType.Elite => 35,
            RoomType.Boss => 100,
            _ => 0,
        };
        if (rs.Ascension >= (int)AscensionLevel.Poverty)
        {
            num *= PovertyAscensionGoldMultiplier;
        }
        return (int)num;
    }

    public static int MaxGoldReward(RunState rs, EncounterDef encounter, RoomType roomType)
    {
        if (string.Equals(encounter.Id, FakeMerchantEventEncounterId, StringComparison.Ordinal))
        {
            return FakeMerchantGoldReward;
        }
        double num = roomType switch
        {
            RoomType.Monster => 20,
            RoomType.Elite => 45,
            RoomType.Boss => 100,
            _ => 0,
        };
        if (rs.Ascension >= (int)AscensionLevel.Poverty)
        {
            num *= PovertyAscensionGoldMultiplier;
        }
        return (int)num;
    }

    public RewardsSet EmptyForRoom(AbstractRoom room)
    {
        Room = room;
        _allowEmptyRewards = true;
        return this;
    }

    public RewardsSet WithRewardsFromRoom(RunState rs, AbstractRoom room)
    {
        ArgumentNullException.ThrowIfNull(rs);
        ArgumentNullException.ThrowIfNull(room);
        Room = room;
        if (room.RoomType == RoomType.Boss && rs.CurrentActIndex >= rs.Acts.Count - 1)
        {
            return this;
        }
        if (!TryGenerateTutorialRewards(rs, Player, room))
        {
            Rewards.AddRange(GenerateRewardsFor(rs, Player, room));
        }
        if (Room is CombatRoom combatRoom && combatRoom.ExtraRewards.Count > 0)
        {
            Rewards.AddRange(combatRoom.ExtraRewards.Where(r => ReferenceEquals(r.Player, Player)));
        }
        return this;
    }

    public RewardsSet WithCustomRewards(List<Reward> rewards)
    {
        ArgumentNullException.ThrowIfNull(rewards);
        Rewards.AddRange(rewards);
        return this;
    }

    public RewardsSet WithSkippingDisallowed()
    {
        _disallowSkipping = true;
        return this;
    }

    public List<Reward> GenerateWithoutOffering(RunState rs)
    {
        ArgumentNullException.ThrowIfNull(rs);
        if (!_isGenerated)
        {
            var val2 = Rewards.ToList();
            for (int i = 0; i < val2.Count; i++)
            {
                val2[i].Populate(rs);
            }
            var modifiers = Hook.ModifyRewards(rs, Player, Rewards, Room).ToList();
            foreach (var current2 in Rewards.Except(val2).ToList())
            {
                if (!current2.IsPopulated)
                {
                    current2.Populate(rs);
                }
            }
            Hook.AfterModifyingRewards(rs, modifiers);
            Rewards.Sort(SetIndexOrder);
            _isGenerated = true;
        }
        return Rewards;
    }

    public bool Offer(RunState rs)
    {
        ArgumentNullException.ThrowIfNull(rs);
        if (Player.IsDead)
        {
            return false;
        }
        GenerateWithoutOffering(rs);
        bool flag = Room is CombatRoom;
        if (Rewards.Count > 0 || flag || _allowEmptyRewards)
        {
            IsTerminal = flag;
            rs.RewardsStack.Add(this);
            return true;
        }
        return false;
    }

    public void SkipUnclaimed(RunState rs)
    {
        ArgumentNullException.ThrowIfNull(rs);
        for (int i = 0; i < Rewards.Count; i++)
        {
            if (!Rewards[i].SuccessfullySelected)
            {
                Rewards[i].OnSkipped(rs);
            }
        }
    }

    public RewardsSet CloneFor(PlayerState player, AbstractRoom? room)
    {
        ArgumentNullException.ThrowIfNull(player);
        var clone = new RewardsSet(player)
        {
            Room = room,
            IsTerminal = IsTerminal,
            Id = Id,
            _allowEmptyRewards = _allowEmptyRewards,
            _disallowSkipping = _disallowSkipping,
            _isGenerated = _isGenerated
        };
        for (int i = 0; i < Rewards.Count; i++)
        {
            clone.Rewards.Add(Rewards[i].CloneFor(player));
        }
        return clone;
    }

    public void WriteCanonical(CanonicalWriter w)
    {
        w.WriteTag(RewardsCanonicalTags.RewardsSet);
        w.WriteInt32(Id);
        w.WriteBool(IsTerminal);
        w.WriteBool(_allowEmptyRewards);
        w.WriteBool(_disallowSkipping);
        w.WriteBool(_isGenerated);
        w.WriteBool(Room != null);
        w.WriteInt32(Room?.Id ?? -1);
        w.WriteCount(Rewards.Count);
        for (int i = 0; i < Rewards.Count; i++)
        {
            Rewards[i].WriteCanonical(w);
        }
    }

    private static List<Reward> GenerateRewardsFor(RunState rs, PlayerState player, AbstractRoom room)
    {
        var val = new List<Reward>();
        if (room is not CombatRoom combatRoom)
        {
            if (room is not TreasureRoom)
            {
                throw new InvalidOperationException("Tried to generate a reward for invalid room type: " + room.GetType().Name);
            }
            return val;
        }
        var roomType = room.RoomType;
        var encounter = combatRoom.Encounter;
        switch (roomType)
        {
            case RoomType.Monster:
            {
                float goldProportion = (float)combatRoom.GoldProportion;
                if (goldProportion > 0f)
                {
                    val.Add(new GoldReward((int)Math.Round((double)(MinGoldReward(rs, encounter, roomType) * goldProportion)), (int)Math.Round((double)(MaxGoldReward(rs, encounter, roomType) * goldProportion)), player));
                }
                RollForPotionAndAddTo(rs, val, player, roomType);
                val.Add(new CardReward(player, CardCreationOptions.ForRoom(rs, player, roomType).WithFlags(CardCreationFlags.IsFromCombat), 3));
                break;
            }
            case RoomType.Elite:
                val.Add(new GoldReward(MinGoldReward(rs, encounter, roomType), MaxGoldReward(rs, encounter, roomType), player));
                RollForPotionAndAddTo(rs, val, player, roomType);
                val.Add(new CardReward(player, CardCreationOptions.ForRoom(rs, player, roomType).WithFlags(CardCreationFlags.IsFromCombat), 3));
                val.Add(new RelicReward(player));
                break;
            case RoomType.Boss:
                val.Add(new GoldReward(MinGoldReward(rs, encounter, roomType), MaxGoldReward(rs, encounter, roomType), player));
                RollForPotionAndAddTo(rs, val, player, roomType);
                val.Add(new CardReward(player, CardCreationOptions.ForRoom(rs, player, roomType).WithFlags(CardCreationFlags.IsFromCombat), 3));
                break;
        }
        return val;
    }

    private static void RollForPotionAndAddTo(RunState rs, List<Reward> rewards, PlayerState player, RoomType roomType)
    {
        var odds = player.PlayerOdds ?? throw new InvalidOperationException("Potion rewards need the player's odds (run state).");
        if (odds.PotionReward.Roll(rs, player, roomType))
        {
            rewards.Add(new PotionReward(player));
        }
    }

    private static bool TryGenerateTutorialRewards(RunState rs, PlayerState player, AbstractRoom room)
    {
        if (rs.Unlocks.NumberOfRuns == 0 && rs.Unlocks.EpochUnlockCount() == 0
            && string.Equals(player.CharacterId, Cards.CardPools.IroncladCharacterId, StringComparison.Ordinal) && room is CombatRoom)
        {
            throw new NotImplementedException("W2 R3: RewardsSet.TryGenerateTutorialRewards (first-run tutorial tables, Rewards/RewardsSet.cs:527-667)");
        }
        return false;
    }
}
