namespace Sts2Sim.Core.Run;

public abstract class AbstractOdds
{
    protected AbstractOdds(float initialValue, Rng rng)
    {
        CurrentValue = initialValue;
        Rng = rng;
    }

    public float CurrentValue { get; protected set; }

    protected Rng Rng { get; }

    public void OverrideCurrentValue(float newValue) => CurrentValue = newValue;
}

public sealed class CardRarityOdds : AbstractOdds
{
    public const float RegularUncommonOdds = 0.37f;

    public const float EliteUncommonOdds = 0.4f;

    public const float BossCommonOdds = 0f;

    public const float BossUncommonOdds = 0f;

    public const float BossRareOdds = 1f;

    public const float ShopUncommonOdds = 0.37f;

    public const float BaseRarityOffset = -0.05f;

    public const float MaxRarityOffset = 0.4f;

    public CardRarityOdds(Rng rng, int ascension) : this(BaseRarityOffset, rng, ascension)
    {
    }

    public CardRarityOdds(float initialValue, Rng rng, int ascension) : base(initialValue, rng)
    {
        Ascension = ascension;
    }

    public int Ascension { get; }

    private bool HasScarcity => Ascension >= (int)AscensionLevel.Scarcity;

    public float RarityGrowth => HasScarcity ? 0.005f : 0.01f;

    public float RegularCommonOdds => HasScarcity ? 0.615f : 0.6f;

    public float RegularRareOdds => HasScarcity ? 0.0149f : 0.03f;

    public float EliteCommonOdds => HasScarcity ? 0.549f : 0.5f;

    public float EliteRareOdds => HasScarcity ? 0.05f : 0.1f;

    public float ShopCommonOdds => HasScarcity ? 0.585f : 0.54f;

    public float ShopRareOdds => HasScarcity ? 0.045f : 0.09f;

    public CardRarity Roll(CardRarityOddsType type)
    {
        CardRarity cardRarity = RollWithoutChangingFutureOdds(type, type == CardRarityOddsType.BossEncounter ? 0f : CurrentValue);
        if (cardRarity == CardRarity.Rare)
        {
            CurrentValue = BaseRarityOffset;
        }
        else
        {
            CurrentValue = Math.Min(CurrentValue + RarityGrowth, MaxRarityOffset);
        }
        return cardRarity;
    }

    public CardRarity RollWithoutChangingFutureOdds(CardRarityOddsType oddsType) => RollWithoutChangingFutureOdds(oddsType, CurrentValue);

    public CardRarity RollWithoutChangingFutureOdds(CardRarityOddsType type, float offset)
    {
        float num = Rng.NextFloat();
        float num2 = GetBaseOdds(type, CardRarity.Rare) + offset;
        float baseOdds = GetBaseOdds(type, CardRarity.Uncommon);
        if (num < num2)
        {
            return CardRarity.Rare;
        }
        if (num < baseOdds + num2)
        {
            return CardRarity.Uncommon;
        }
        return CardRarity.Common;
    }

    public CardRarity RollWithBaseOdds(CardRarityOddsType type)
    {
        float baseOdds = GetBaseOdds(type, CardRarity.Rare);
        float baseOdds2 = GetBaseOdds(type, CardRarity.Uncommon);
        float num = Rng.NextFloat();
        if (num < baseOdds)
        {
            return CardRarity.Rare;
        }
        if (num < baseOdds2 + baseOdds)
        {
            return CardRarity.Uncommon;
        }
        return CardRarity.Common;
    }

    public float GetBaseOdds(CardRarityOddsType type, CardRarity rarity)
    {
        return type switch
        {
            CardRarityOddsType.EliteEncounter => rarity switch
            {
                CardRarity.Common => EliteCommonOdds,
                CardRarity.Uncommon => EliteUncommonOdds,
                CardRarity.Rare => EliteRareOdds,
                _ => throw new ArgumentOutOfRangeException(nameof(rarity)),
            },
            CardRarityOddsType.BossEncounter => rarity switch
            {
                CardRarity.Common => BossCommonOdds,
                CardRarity.Uncommon => BossUncommonOdds,
                CardRarity.Rare => BossRareOdds,
                _ => throw new ArgumentOutOfRangeException(nameof(rarity)),
            },
            CardRarityOddsType.Shop => rarity switch
            {
                CardRarity.Common => ShopCommonOdds,
                CardRarity.Uncommon => ShopUncommonOdds,
                CardRarity.Rare => ShopRareOdds,
                _ => throw new ArgumentOutOfRangeException(nameof(rarity)),
            },
            CardRarityOddsType.RegularEncounter => rarity switch
            {
                CardRarity.Common => RegularCommonOdds,
                CardRarity.Uncommon => RegularUncommonOdds,
                CardRarity.Rare => RegularRareOdds,
                _ => throw new ArgumentOutOfRangeException(nameof(rarity)),
            },
            CardRarityOddsType.Uniform => rarity switch
            {
                CardRarity.Common => 0.33f,
                CardRarity.Uncommon => 0.33f,
                CardRarity.Rare => 0.33f,
                _ => throw new ArgumentOutOfRangeException(nameof(rarity)),
            },
            _ => throw new ArgumentOutOfRangeException(nameof(type)),
        };
    }

    public CardRarityOdds CloneFor(Rng rng) => new(CurrentValue, rng, Ascension);
}

public sealed class PotionRewardOdds : AbstractOdds
{
    public const float TargetOdds = 0.5f;

    public const float EliteBonus = 0.25f;

    public const float BasePotionRewardOdds = 0.4f;

    public PotionRewardOdds(Rng rng) : base(BasePotionRewardOdds, rng)
    {
    }

    public PotionRewardOdds(float initialValue, Rng rng) : base(initialValue, rng)
    {
    }

    public bool Roll(RunState rs, PlayerState player, RoomType roomType)
    {
        float currentValue = CurrentValue;
        if (Hook.ShouldForcePotionReward(rs, player, roomType))
        {
            return true;
        }
        float num = roomType != RoomType.Elite ? 0f : EliteBonus;
        float num3 = currentValue + num * 0.5f;
        float num4 = Rng.NextFloat();
        if (num4 < num3)
        {
            CurrentValue -= 0.1f;
            return true;
        }
        CurrentValue += 0.1f;
        return false;
    }

    public PotionRewardOdds CloneFor(Rng rng) => new(CurrentValue, rng);
}

public sealed class UnknownMapPointOdds : AbstractOdds
{
    public const float BaseMonsterOdds = 0.1f;

    public const float BaseEliteOdds = -1f;

    public const float BaseTreasureOdds = 0.02f;

    public const float BaseShopOdds = 0.03f;

    private readonly OrderedOdds _baseOdds;

    private readonly OrderedOdds _nonEventOdds;

    public UnknownMapPointOdds(Rng rng) : base(0f, rng)
    {
        _baseOdds = OrderedOdds.Defaults();
        _nonEventOdds = OrderedOdds.Defaults();
    }

    private UnknownMapPointOdds(Rng rng, OrderedOdds baseOdds, OrderedOdds nonEventOdds) : base(0f, rng)
    {
        _baseOdds = baseOdds;
        _nonEventOdds = nonEventOdds;
    }

    public float MonsterOdds
    {
        get => _nonEventOdds[RoomType.Monster];
        set => _nonEventOdds[RoomType.Monster] = value;
    }

    public float EliteOdds
    {
        get => _nonEventOdds[RoomType.Elite];
        set => _nonEventOdds[RoomType.Elite] = value;
    }

    public float TreasureOdds
    {
        get => _nonEventOdds[RoomType.Treasure];
        set => _nonEventOdds[RoomType.Treasure] = value;
    }

    public float ShopOdds
    {
        get => _nonEventOdds[RoomType.Shop];
        set => _nonEventOdds[RoomType.Shop] = value;
    }

    public float EventOdds
    {
        get
        {
            float sum = 0f;
            for (int i = 0; i < _nonEventOdds.Count; i++)
            {
                float v = _nonEventOdds.ValueAt(i);
                if (v > 0f)
                {
                    sum += v;
                }
            }
            return Math.Max(0f, 1f - sum);
        }
    }

    public void SetBaseOdds(RoomType roomType, float baseOdds) => _baseOdds[roomType] = baseOdds;

    public RoomType Roll(IEnumerable<RoomType> blacklist, RunState runState)
    {
        ArgumentNullException.ThrowIfNull(blacklist);
        ArgumentNullException.ThrowIfNull(runState);
        if (runState.Unlocks.NumberOfRuns == 0)
        {
            int num = 0;
            var history = runState.MapPointHistory;
            for (int i = 0; i < history.Count; i++)
            {
                if (history[i].MapPointType == MapPointType.Unknown)
                {
                    num++;
                }
            }
            if (num < 2)
            {
                return RoomType.Event;
            }
            if (num == 2)
            {
                return RoomType.Monster;
            }
        }
        var keys = new List<RoomType>(_nonEventOdds.Count + 1);
        for (int i = 0; i < _nonEventOdds.Count; i++)
        {
            keys.Add(_nonEventOdds.KeyAt(i));
        }
        keys.Add(RoomType.Event);
        IReadOnlySet<RoomType> roomTypes = keys.Except(blacklist).ToHashSet();
        roomTypes = Hook.ModifyUnknownMapPointRoomTypes(runState, roomTypes);
        RoomType roomType = roomTypes.Contains(RoomType.Event) ? RoomType.Event : roomTypes.Order().First();
        float num2 = Rng.NextFloat();
        float num3 = 0f;
        for (int i = 0; i < _nonEventOdds.Count; i++)
        {
            RoomType roomType3 = _nonEventOdds.KeyAt(i);
            float num5 = _nonEventOdds.ValueAt(i);
            if (roomTypes.Contains(roomType3) && !(num5 < 0f))
            {
                num3 += num5;
                if (num2 <= num3)
                {
                    roomType = roomType3;
                    break;
                }
            }
        }
        for (int i = 0; i < _baseOdds.Count; i++)
        {
            RoomType roomType4 = _baseOdds.KeyAt(i);
            float num6 = _baseOdds.ValueAt(i);
            if (roomType == roomType4)
            {
                _nonEventOdds[roomType4] = num6;
            }
            else if (roomTypes.Contains(roomType4))
            {
                float num7 = Hook.ModifyOddsIncreaseForUnrolledRoomType(runState, roomType4, num6);
                _nonEventOdds[roomType4] += num7;
            }
        }
        return roomType;
    }

    public void ResetToBase()
    {
        for (int i = 0; i < _baseOdds.Count; i++)
        {
            _nonEventOdds[_baseOdds.KeyAt(i)] = _baseOdds.ValueAt(i);
        }
    }

    public UnknownMapPointOdds CloneFor(Rng rng) => new(rng, _baseOdds.Clone(), _nonEventOdds.Clone());

    public void WriteCanonical(CanonicalWriter w)
    {
        w.WriteTag(RewardsCanonicalTags.Odds);
        _baseOdds.WriteCanonical(w);
        _nonEventOdds.WriteCanonical(w);
    }

    private sealed class OrderedOdds
    {
        private readonly List<RoomType> _keys = new();
        private readonly List<float> _values = new();

        public static OrderedOdds Defaults()
        {
            var odds = new OrderedOdds();
            odds[RoomType.Monster] = BaseMonsterOdds;
            odds[RoomType.Elite] = BaseEliteOdds;
            odds[RoomType.Treasure] = BaseTreasureOdds;
            odds[RoomType.Shop] = BaseShopOdds;
            return odds;
        }

        public int Count => _keys.Count;

        public RoomType KeyAt(int index) => _keys[index];

        public float ValueAt(int index) => _values[index];

        public float this[RoomType key]
        {
            get
            {
                int index = _keys.IndexOf(key);
                return index >= 0 ? _values[index] : throw new KeyNotFoundException($"The given key '{key}' was not present in the dictionary.");
            }
            set
            {
                int index = _keys.IndexOf(key);
                if (index >= 0)
                {
                    _values[index] = value;
                }
                else
                {
                    _keys.Add(key);
                    _values.Add(value);
                }
            }
        }

        public OrderedOdds Clone()
        {
            var clone = new OrderedOdds();
            clone._keys.AddRange(_keys);
            clone._values.AddRange(_values);
            return clone;
        }

        public void WriteCanonical(CanonicalWriter w)
        {
            w.WriteCount(_keys.Count);
            for (int i = 0; i < _keys.Count; i++)
            {
                w.WriteByte((byte)_keys[i]);
                w.WriteSingle(_values[i]);
            }
        }
    }
}

public sealed class RunOddsSet
{
    public RunOddsSet(Rng unknownMapPointRng)
    {
        UnknownMapPoint = new UnknownMapPointOdds(unknownMapPointRng);
    }

    private RunOddsSet(UnknownMapPointOdds unknownMapPoint)
    {
        UnknownMapPoint = unknownMapPoint;
    }

    public UnknownMapPointOdds UnknownMapPoint { get; }

    public static RunOddsSet FromSerializable(float monsterOdds, float eliteOdds, float treasureOdds, float shopOdds, Rng unknownMapPointRng)
    {
        var odds = new UnknownMapPointOdds(unknownMapPointRng)
        {
            MonsterOdds = monsterOdds,
            EliteOdds = eliteOdds,
            TreasureOdds = treasureOdds,
            ShopOdds = shopOdds
        };
        return new RunOddsSet(odds);
    }

    public RunOddsSet CloneFor(RunRngSet rng)
    {
        ArgumentNullException.ThrowIfNull(rng);
        return new RunOddsSet(UnknownMapPoint.CloneFor(rng.UnknownMapPoint));
    }

    public void WriteCanonical(CanonicalWriter w)
    {
        w.WriteTag(RewardsCanonicalTags.Odds);
        UnknownMapPoint.WriteCanonical(w);
    }
}

public sealed class PlayerOddsSet
{
    public PlayerOddsSet(PlayerRngSet rng, int ascension)
    {
        ArgumentNullException.ThrowIfNull(rng);
        CardRarity = new CardRarityOdds(rng.Rewards, ascension);
        PotionReward = new PotionRewardOdds(rng.Rewards);
    }

    private PlayerOddsSet(CardRarityOdds cardRarity, PotionRewardOdds potionReward)
    {
        CardRarity = cardRarity;
        PotionReward = potionReward;
    }

    public CardRarityOdds CardRarity { get; }

    public PotionRewardOdds PotionReward { get; }

    public static PlayerOddsSet FromSerializable(float cardRarityOddsValue, float potionRewardOddsValue, PlayerRngSet rng, int ascension)
    {
        ArgumentNullException.ThrowIfNull(rng);
        return new PlayerOddsSet(new CardRarityOdds(cardRarityOddsValue, rng.Rewards, ascension), new PotionRewardOdds(potionRewardOddsValue, rng.Rewards));
    }

    public void LoadFromSerializable(float cardRarityOddsValue, float potionRewardOddsValue)
    {
        CardRarity.OverrideCurrentValue(cardRarityOddsValue);
        PotionReward.OverrideCurrentValue(potionRewardOddsValue);
    }

    public PlayerOddsSet CloneFor(PlayerRngSet rng)
    {
        ArgumentNullException.ThrowIfNull(rng);
        return new PlayerOddsSet(CardRarity.CloneFor(rng.Rewards), PotionReward.CloneFor(rng.Rewards));
    }

    public void WriteCanonical(CanonicalWriter w)
    {
        w.WriteTag(RewardsCanonicalTags.Odds);
        w.WriteSingle(CardRarity.CurrentValue);
        w.WriteInt32(CardRarity.Ascension);
        w.WriteSingle(PotionReward.CurrentValue);
    }
}
