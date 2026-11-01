namespace Sts2Sim.Core.Run;

public sealed class GoldReward : Reward
{
    public const int DefaultMinGoldAmount = 10;

    public const int DefaultMaxGoldAmount = 20;

    private readonly bool _wasGoldStolenBack;

    private readonly int _min;

    private readonly int _max;

    public GoldReward(int amount, PlayerState player, bool wasGoldStolenBack = false) : base(player)
    {
        _min = amount;
        _max = amount;
        Amount = amount;
        _wasGoldStolenBack = wasGoldStolenBack;
    }

    public GoldReward(int min, int max, PlayerState player, bool wasGoldStolenBack = false) : base(player)
    {
        _min = min;
        _max = max;
        _wasGoldStolenBack = wasGoldStolenBack;
    }

    private GoldReward(GoldReward source, PlayerState player) : base(player)
    {
        _min = source._min;
        _max = source._max;
        Amount = source.Amount;
        _wasGoldStolenBack = source._wasGoldStolenBack;
    }

    public override RewardType RewardType => RewardType.Gold;

    public override int RewardsSetIndex => 1;

    public int Amount { get; private set; } = -1;

    public int Min => _min;

    public int Max => _max;

    public bool WasGoldStolenBack => _wasGoldStolenBack;

    public override bool IsPopulated => Amount >= 0;

    public override void Populate(RunState rs)
    {
        Rng rng = RngOverride ?? RewardRngs.Rewards(Player);
        Amount = rng.NextInt(_min, _max + 1);
    }

    protected override bool OnSelect(RunState rs)
    {
        PlayerCmd.GainGold(rs, Amount, Player, _wasGoldStolenBack);
        return true;
    }

    protected override Reward CloneCore(PlayerState player) => new GoldReward(this, player);

    protected override void WriteFields(CanonicalWriter w)
    {
        w.WriteInt32(_min);
        w.WriteInt32(_max);
        w.WriteInt32(Amount);
        w.WriteBool(_wasGoldStolenBack);
    }
}

public sealed class PotionReward : Reward
{
    private bool _wasTaken;

    public PotionReward(PlayerState player) : base(player)
    {
    }

    public PotionReward(PotionModel potion, PlayerState player) : base(player)
    {
        ArgumentNullException.ThrowIfNull(potion);
        Potion = potion;
        PotionId = potion.Id;
    }

    private PotionReward(PotionReward source, PlayerState player) : base(player)
    {
        PotionId = source.PotionId;
        Potion = source.Potion?.CloneFor(player);
        ClaimedPotionId = source.ClaimedPotionId;
        _wasTaken = source._wasTaken;
    }

    public override RewardType RewardType => RewardType.Potion;

    public override int RewardsSetIndex => 2;

    public string? PotionId { get; private set; }

    public PotionModel? Potion { get; private set; }

    public string? ClaimedPotionId { get; private set; }

    public override bool IsPopulated => PotionId != null;

    public override void Populate(RunState rs)
    {
        Rng rng = RngOverride ?? RewardRngs.Rewards(Player);
        if (PotionId == null)
        {
            PotionId = PotionFactory.CreateRandomPotionOutOfCombat(rs, Player, rng).Id;
        }
    }

    protected override bool OnSelect(RunState rs)
    {
        var potion = Potion ??= RewardRngs.CreatePotion(PotionId!);
        var potionProcureResult = PotionCmd.TryToProcure(rs, potion, Player);
        if (potionProcureResult.Success)
        {
            ClaimedPotionId = potion.Id;
            _wasTaken = true;
            return true;
        }
        if (potionProcureResult.FailureReason == PotionProcureFailureReason.TooFull)
        {
            return false;
        }
        ClaimedPotionId = potion.Id;
        _wasTaken = true;
        return true;
    }

    protected override Reward CloneCore(PlayerState player) => new PotionReward(this, player);

    protected override void WriteFields(CanonicalWriter w)
    {
        w.WriteString(PotionId);
        w.WriteBool(Potion != null);
        Potion?.WriteCanonical(w);
        w.WriteString(ClaimedPotionId);
        w.WriteBool(_wasTaken);
    }
}

public sealed class RelicReward : Reward
{
    private readonly RelicRarity _rarity;

    private readonly RelicModel? _predeterminedRelic;

    private bool _wasTaken;

    public RelicReward(PlayerState player) : base(player)
    {
    }

    public RelicReward(RelicModel relic, PlayerState player) : base(player)
    {
        ArgumentNullException.ThrowIfNull(relic);
        _predeterminedRelic = relic;
        RelicId = relic.Id;
    }

    public RelicReward(RelicRarity rarity, PlayerState player) : base(player)
    {
        _rarity = rarity;
    }

    private RelicReward(RelicReward source, PlayerState player) : base(player)
    {
        _rarity = source._rarity;
        _predeterminedRelic = source._predeterminedRelic?.CloneFor(player);
        RelicId = source.RelicId;
        ClaimedRelicId = source.ClaimedRelicId;
        _wasTaken = source._wasTaken;
    }

    public override RewardType RewardType => RewardType.Relic;

    public override int RewardsSetIndex => 3;

    public RelicRarity Rarity => _rarity;

    public string? RelicId { get; private set; }

    public string? ClaimedRelicId { get; private set; }

    public override bool IsPopulated => RelicId != null;

    public override void Populate(RunState rs)
    {
        if (RelicId != null)
        {
            return;
        }
        if (_rarity == RelicRarity.None)
        {
            var rng = RngOverride;
            RelicRarity rarity = rng != null ? RelicFactory.RollRarity(rng) : RelicFactory.RollRarity(Player);
            RelicId = RelicFactory.PullNextRelicIdFromFront(rs, Player, rarity, static _ => true);
        }
        else
        {
            RelicId = RelicFactory.PullNextRelicIdFromFront(rs, Player, _rarity, static _ => true);
        }
    }

    protected override bool OnSelect(RunState rs)
    {
        var relic = _predeterminedRelic ?? RewardRngs.CreateRelic(RelicId!);
        var claimed = RelicCmd.Obtain(rs, relic, Player);
        ClaimedRelicId = claimed.Id;
        _wasTaken = true;
        CardReward.NotifyRelicObtained(rs, Player, claimed);
        return true;
    }

    protected override Reward CloneCore(PlayerState player) => new RelicReward(this, player);

    protected override void WriteFields(CanonicalWriter w)
    {
        w.WriteByte((byte)_rarity);
        w.WriteString(RelicId);
        w.WriteBool(_predeterminedRelic != null);
        w.WriteString(ClaimedRelicId);
        w.WriteBool(_wasTaken);
    }
}

public sealed class SpecialCardReward : Reward
{
    private readonly CardInstance _card;

    private bool _wasTaken;

    public SpecialCardReward(CardInstance card, PlayerState player) : base(player)
    {
        ArgumentNullException.ThrowIfNull(card);
        _card = card;
    }

    private SpecialCardReward(SpecialCardReward source, PlayerState player) : base(player)
    {
        _card = source._card.CloneFor(player);
        _wasTaken = source._wasTaken;
        CustomDescriptionEncounterSourceId = source.CustomDescriptionEncounterSourceId;
    }

    public override RewardType RewardType => RewardType.SpecialCard;

    public override int RewardsSetIndex => 4;

    public CardInstance Card => _card;

    public string? CustomDescriptionEncounterSourceId { get; private set; }

    public override bool IsPopulated => true;

    public void SetCustomDescriptionEncounterSource(string encounterId) => CustomDescriptionEncounterSourceId = encounterId;

    public override void Populate(RunState rs)
    {
    }

    protected override bool OnSelect(RunState rs)
    {
        CardPileCmd.Add(rs, _card, PileType.Deck);
        _wasTaken = true;
        return true;
    }

    protected override Reward CloneCore(PlayerState player) => new SpecialCardReward(this, player);

    protected override void WriteFields(CanonicalWriter w)
    {
        _card.WriteCanonical(w);
        w.WriteBool(_wasTaken);
        w.WriteString(CustomDescriptionEncounterSourceId);
    }
}

public sealed class CardRemovalReward : Reward
{
    public CardRemovalReward(PlayerState player) : base(player)
    {
    }

    public override RewardType RewardType => RewardType.RemoveCard;

    public override int RewardsSetIndex => 7;

    public override bool IsPopulated => true;

    public override void Populate(RunState rs)
    {
    }

    protected override bool OnSelect(RunState rs) => throw new NotImplementedException("W2 R3: CardRemovalReward.OnSelect (RewardSynchronizer.DoUnsyncedCardRemoval needs the run deck-removal prompt)");

    protected override Reward CloneCore(PlayerState player) => new CardRemovalReward(player);

    protected override void WriteFields(CanonicalWriter w)
    {
    }
}

internal static class RewardRngs
{
    public static Rng Rewards(PlayerState player)
        => (player.PlayerRng ?? throw new InvalidOperationException("Rewards need the player's rng set (run state).")).Rewards;

    public static PotionModel CreatePotion(string potionId)
        => PotionRegistry.Models.Contains(potionId)
            ? PotionRegistry.Create(potionId)
            : throw new NotImplementedException($"W2 R3: potion {potionId} is not ported");

    public static RelicModel CreateRelic(string relicId)
        => RelicRegistry.Models.Contains(relicId)
            ? RelicRegistry.Create(relicId)
            : throw new NotImplementedException($"W2 R3: relic {relicId} is not ported");
}
