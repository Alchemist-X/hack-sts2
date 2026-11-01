namespace Sts2Sim.Core.Run;

public sealed partial class CardReward : Reward
{
    private readonly List<CardCreationResult> _cards = new();

    private bool _cardsWereManuallySet;

    private bool _hasBeenRerolled;

    private bool _listensForRelics;

    private IReadOnlyList<CardRewardAlternative> _alternatives = Array.Empty<CardRewardAlternative>();

    private readonly List<CardInstance> _chosenCards = new();

    private bool _rewardComplete;

    public CardReward(PlayerState player, CardCreationOptions options, int optionCount) : base(player)
    {
        ArgumentNullException.ThrowIfNull(options);
        OptionCount = optionCount;
        Options = options.WithFlags(CardCreationFlags.IsCardReward);
        RerollOptions = options.WithFlags(CardCreationFlags.IsCardReward);
        _listensForRelics = true;
    }

    public CardReward(IEnumerable<CardInstance> cardsToOffer, CardCreationSource source, PlayerState player, CardCreationOptions rerollOptions) : base(player)
    {
        ArgumentNullException.ThrowIfNull(cardsToOffer);
        ArgumentNullException.ThrowIfNull(rerollOptions);
        Options = new CardCreationOptions(Array.Empty<CardPool>(), source, CardRarityOddsType.Uniform)
            .WithFlags(CardCreationFlags.NoCardPoolModifications | CardCreationFlags.NoCardModelModifications | CardCreationFlags.IsCardReward);
        RerollOptions = rerollOptions;
        _cardsWereManuallySet = true;
        _cards.AddRange(cardsToOffer.Select(c => new CardCreationResult(c)));
        OptionCount = _cards.Count;
    }

    private CardReward(CardReward source, PlayerState player) : base(player)
    {
        OptionCount = source.OptionCount;
        Options = source.Options.CloneFor(source.Player, player);
        RerollOptions = ReferenceEquals(source.RerollOptions, source.Options) ? Options : source.RerollOptions.CloneFor(source.Player, player);
        for (int i = 0; i < source._cards.Count; i++)
        {
            _cards.Add(source._cards[i].CloneFor(player));
        }
        _cardsWereManuallySet = source._cardsWereManuallySet;
        _hasBeenRerolled = source._hasBeenRerolled;
        _listensForRelics = source._listensForRelics;
        _alternatives = source._alternatives;
        for (int i = 0; i < source._chosenCards.Count; i++)
        {
            _chosenCards.Add(source._chosenCards[i].CloneFor(player));
        }
        _rewardComplete = source._rewardComplete;
        CanReroll = source.CanReroll;
        CanSkip = source.CanSkip;
        IsSelecting = source.IsSelecting;
    }

    public override RewardType RewardType => RewardType.Card;

    public override int RewardsSetIndex => 5;

    public CardCreationOptions Options { get; }

    public CardCreationOptions RerollOptions { get; }

    public int OptionCount { get; }

    public IReadOnlyList<CardCreationResult> Cards => _cards;

    public IReadOnlyList<CardRewardAlternative> Alternatives => _alternatives;

    public IReadOnlyList<CardInstance> ChosenCards => _chosenCards;

    public bool CanReroll { get; set; }

    public bool CanSkip { get; init; } = true;

    public bool IsSelecting { get; private set; }

    public override bool IsPopulated => _cards.Count > 0;

    public override void Populate(RunState rs)
    {
        ArgumentNullException.ThrowIfNull(rs);
        var cardCreationOptions = _hasBeenRerolled ? RerollOptions : Options;
        if (_cardsWereManuallySet && !_hasBeenRerolled)
        {
            List<AbstractModel>? modifiers = null;
            if (Hook.TryModifyCardRewardOptions(rs, Player, _cards, cardCreationOptions, ref modifiers))
            {
                Hook.AfterModifyingCardRewardOptions(rs, modifiers);
            }
        }
        else if (_cards.Count <= 0)
        {
            var enumerable = CardFactory.CreateForReward(rs, Player, OptionCount, cardCreationOptions);
            _cards.Clear();
            _cards.AddRange(enumerable);
            var extraOptions = CardRewardAlternative.Generate(rs, this);
            if (IsSelecting)
            {
                _alternatives = extraOptions;
            }
        }
    }

    public override bool OnSelectWrapper(RunState rs)
    {
        ArgumentNullException.ThrowIfNull(rs);
        if (IsSelecting)
        {
            throw new InvalidOperationException("The card reward screen is already open.");
        }
        _chosenCards.Clear();
        _rewardComplete = false;
        _alternatives = CardRewardAlternative.Generate(rs, this);
        IsSelecting = true;
        return false;
    }

    protected override bool OnSelect(RunState rs) => throw new InvalidOperationException("CardReward selection runs through PickCard and ChooseAlternative.");

    public bool PickCard(RunState rs, int index)
    {
        ArgumentNullException.ThrowIfNull(rs);
        RequireSelecting();
        if ((uint)index >= (uint)_cards.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }
        var obtainedCard = _cards[index].Card;
        _rewardComplete = true;
        bool endSelection = !Hook.ShouldAllowSelectingMoreCardRewards(rs, Player, this);
        var cardPileAddResult = CardPileCmd.Add(rs, obtainedCard, PileType.Deck);
        if (cardPileAddResult.Success)
        {
            obtainedCard = cardPileAddResult.CardAdded;
            _chosenCards.Add(obtainedCard);
            _cards.RemoveAll(c => ReferenceEquals(c.Card, obtainedCard));
        }
        if (endSelection)
        {
            EndSelection(rs);
        }
        return endSelection;
    }

    public bool ChooseAlternative(RunState rs, int index)
    {
        ArgumentNullException.ThrowIfNull(rs);
        RequireSelecting();
        var options = _alternatives;
        if ((uint)index >= (uint)options.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }
        var cardRewardAlternative = options[index];
        _rewardComplete = cardRewardAlternative.AfterSelected == PostAlternateCardRewardAction.EndSelectionAndCompleteReward;
        bool endSelection = cardRewardAlternative.EndsSelection;
        cardRewardAlternative.OnSelect?.Invoke(rs, this);
        if (endSelection)
        {
            EndSelection(rs);
        }
        return endSelection;
    }

    public void Reroll(RunState rs)
    {
        ArgumentNullException.ThrowIfNull(rs);
        CanReroll = false;
        _hasBeenRerolled = true;
        _cards.Clear();
        Populate(rs);
    }

    public override void OnSkipped(RunState rs) => _listensForRelics = false;

    public static void NotifyRelicObtained(RunState rs, PlayerState player, RelicModel relic)
    {
        ArgumentNullException.ThrowIfNull(rs);
        ArgumentNullException.ThrowIfNull(relic);
        var stack = rs.RewardsStack;
        for (int s = 0; s < stack.Count; s++)
        {
            var rewards = stack[s].Rewards;
            for (int i = 0; i < rewards.Count; i++)
            {
                if (rewards[i] is CardReward cardReward && ReferenceEquals(cardReward.Player, player) && cardReward._listensForRelics)
                {
                    cardReward.OnRelicObtained(rs, relic);
                }
            }
        }
    }

    private void OnRelicObtained(RunState rs, RelicModel relic)
    {
        if (relic.TryModifyCardRewardOptions(rs, Player, _cards, Options))
        {
            relic.AfterModifyingRewards(rs);
        }
        if (relic.TryModifyCardRewardOptionsLate(rs, Player, _cards, Options))
        {
            relic.AfterModifyingRewards(rs);
        }
    }

    private void EndSelection(RunState rs)
    {
        IsSelecting = false;
        _listensForRelics = false;
        _alternatives = Array.Empty<CardRewardAlternative>();
        CompleteSelection(rs, _rewardComplete);
    }

    private void RequireSelecting()
    {
        if (!IsSelecting)
        {
            throw new InvalidOperationException("The card reward screen is not open.");
        }
    }

    protected override Reward CloneCore(PlayerState player) => new CardReward(this, player);

    protected override void WriteFields(CanonicalWriter w)
    {
        w.WriteInt32(OptionCount);
        Options.WriteCanonical(w);
        bool aliased = ReferenceEquals(Options, RerollOptions);
        w.WriteBool(aliased);
        if (!aliased)
        {
            RerollOptions.WriteCanonical(w);
        }
        w.WriteCount(_cards.Count);
        for (int i = 0; i < _cards.Count; i++)
        {
            _cards[i].WriteCanonical(w);
        }
        w.WriteBool(_cardsWereManuallySet);
        w.WriteBool(_hasBeenRerolled);
        w.WriteBool(_listensForRelics);
        w.WriteBool(CanReroll);
        w.WriteBool(CanSkip);
        w.WriteBool(IsSelecting);
        w.WriteBool(_rewardComplete);
        w.WriteCount(_alternatives.Count);
        for (int i = 0; i < _alternatives.Count; i++)
        {
            _alternatives[i].WriteCanonical(w);
        }
        w.WriteCount(_chosenCards.Count);
        for (int i = 0; i < _chosenCards.Count; i++)
        {
            _chosenCards[i].WriteCanonical(w);
        }
    }
}
