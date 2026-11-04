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

    /// <remarks>
    /// The game hands this reward's own <c>Potion</c> to <c>PotionCmd.TryToProcure</c> and keeps it as <c>ClaimedPotion</c>
    /// (Rewards/PotionReward.cs:180-192); <c>Player.AddPotionInternal</c> stores that very instance in the belt
    /// (Commands/PotionCmd.cs:267, Entities/Players/Player.cs:1048-1049). A claimed reward and its belt slot therefore
    /// share one PotionModel until the potion is used or discarded (a discard flags the reward's potion too), and the clone
    /// points at the cloned belt potion instead of a second copy. Unclaimed, refused, used or discarded potions are copied.
    /// </remarks>
    private PotionReward(PotionReward source, PlayerState player) : base(player)
    {
        PotionId = source.PotionId;
        Potion = source.Potion is not { } potion ? null
            : source._wasTaken ? CloneBeltPotion(potion, source.Player, player)
            : potion.CloneFor(player);
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

    /// <summary>
    /// True when claiming this reward now would be refused: <c>PotionCmd.TryToProcure</c> fails with TooFull (a full belt) and
    /// <c>OnSelect</c> returns false, so the reward stays unclaimed and nothing is gained (game Rewards/PotionReward.cs:180-191).
    /// A procure veto (Sozu, NotAllowed) is no refusal: the game consumes the reward without a potion (:192-194). Pure; a
    /// potion id that is not ported answers false (claiming it fails the way it always did).
    /// </summary>
    public bool ClaimWouldBeRefused(RunState rs)
    {
        ArgumentNullException.ThrowIfNull(rs);
        if (SuccessfullySelected || Player.HasOpenPotionSlot)
        {
            return false;
        }
        var potion = Potion ?? (PotionId != null && PotionRegistry.Models.Contains(PotionId) ? PotionRegistry.Create(PotionId) : null);
        return potion != null && PotionCmd.ProcureCheck(rs, potion, Player) == PotionProcureFailureReason.TooFull;
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

    /// <remarks>
    /// A taken reward's card is the instance <c>CardPileCmd.Add</c> put into the deck (Rewards/SpecialCardReward.cs:158), so
    /// the clone points at the cloned deck card while it is still in the deck.
    /// </remarks>
    private SpecialCardReward(SpecialCardReward source, PlayerState player) : base(player)
    {
        _card = source._wasTaken ? CloneDeckCard(source._card, source.Player, player) : source._card.CloneFor(player);
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

    public const string RemovalSite = "RewardSynchronizer.DoUnsyncedCardRemoval";

    protected override bool OnSelect(RunState rs)
    {
        ArgumentNullException.ThrowIfNull(rs);
        var prefs = new CardSelectorPrefs("COMBAT_REWARD_CARD_REMOVAL.selectionScreenPrompt", 1)
        {
            Cancelable = true,
            RequireManualConfirmation = true,
        };
        var selected = RoomPrompts.FromDeckForRemoval(rs, Player, prefs, RemovalSite, "COMBAT_REWARD_CARD_REMOVAL");
        var card = selected.Count > 0 ? selected[0] : null;
        if (card != null)
        {
            CardPileCmd.RemoveFromDeck(rs, card);
            return true;
        }
        return false;
    }

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
