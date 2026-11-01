namespace Sts2Sim.Core.Run;

public sealed class SlipperyBridge : EventModel
{
    public const string EventId = "SLIPPERY_BRIDGE";

    public const string OvercomeLocKey = "SLIPPERY_BRIDGE.pages.INITIAL.options.OVERCOME";

    public const int InitialHpLoss = 3;

    public const int MinTotalFloor = 6;

    private int _numberOfHoldOns;

    private int _randomCardToLose = -1;

    private List<int>? _skippedRemovals;

    public override string Id => EventId;

    public int NumberOfHoldOns => _numberOfHoldOns;

    public CardInstance? RandomCardToLose => _randomCardToLose < 0 || Owner == null ? null : Owner.Deck[_randomCardToLose];

    private int CurrentHpLoss => InitialHpLoss + _numberOfHoldOns;

    public override bool IsAllowed(RunState runState)
    {
        if (runState.TotalFloor > MinTotalFloor)
        {
            var players = runState.Players;
            for (int i = 0; i < players.Count; i++)
            {
                if (!players[i].Deck.Any(c => c.IsRemovable))
                {
                    return false;
                }
            }
            return true;
        }
        return false;
    }

    protected override IReadOnlyList<EventOption> GenerateInitialOptions(RunState rs)
    {
        GetNewRandomCard();
        return new[]
        {
            new EventOption(OvercomeLocKey, Overcome),
            new EventOption("SLIPPERY_BRIDGE.pages.INITIAL.options.HOLD_ON_0", HoldOn)
        };
    }

    private void GetNewRandomCard()
    {
        var deck = Owner!.Deck;
        var val = new List<int>(deck.Count);
        if (_randomCardToLose < 0)
        {
            for (int i = 0; i < deck.Count; i++)
            {
                if (deck[i].Rarity != CardRarity.Basic)
                {
                    val.Add(i);
                }
            }
        }
        else
        {
            var skipped = _skippedRemovals ??= new List<int>();
            if (!skipped.Contains(_randomCardToLose))
            {
                skipped.Add(_randomCardToLose);
            }
            string currentId = deck[_randomCardToLose].Id;
            for (int i = 0; i < deck.Count; i++)
            {
                if (deck[i].Id != currentId)
                {
                    val.Add(i);
                }
            }
        }
        val.RemoveAll(i => !deck[i].IsRemovable || (_skippedRemovals?.Contains(i) ?? false));
        if (val.Count == 0)
        {
            for (int i = 0; i < deck.Count; i++)
            {
                if (deck[i].IsRemovable)
                {
                    val.Add(i);
                }
            }
        }
        _randomCardToLose = val.Count == 0 ? -1 : Rng!.NextItem((IEnumerable<int>)val);
    }

    private void Overcome(RunState rs)
    {
        var card = RandomCardToLose;
        if (card != null)
        {
            CardPileCmd.RemoveFromDeck(rs, card);
        }
        SetEventFinished("SLIPPERY_BRIDGE.pages.OVERCOME.description");
    }

    private void HoldOn(RunState rs)
    {
        Events121Support.Damage(rs, Owner!, CurrentHpLoss, Events121Support.HpLossProps);
        _numberOfHoldOns++;
        GetNewRandomCard();
        string holdOnSuffix = GetHoldOnSuffix(_numberOfHoldOns - 1);
        string holdOnSuffix2 = GetHoldOnSuffix(_numberOfHoldOns);
        string textKey = "SLIPPERY_BRIDGE.pages.HOLD_ON_" + holdOnSuffix + ".options.HOLD_ON_" + holdOnSuffix2;
        SetEventState("SLIPPERY_BRIDGE.pages.HOLD_ON_" + holdOnSuffix + ".description", new[]
        {
            new EventOption(OvercomeLocKey, Overcome),
            new EventOption(textKey, HoldOn)
        });
    }

    private static string GetHoldOnSuffix(int holdOnNumber)
    {
        if (holdOnNumber >= 7)
        {
            return "LOOP";
        }
        return holdOnNumber.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    protected override void CopyMutableReferenceState(EventModel source)
    {
        var skipped = ((SlipperyBridge)source)._skippedRemovals;
        _skippedRemovals = skipped == null ? null : new List<int>(skipped);
    }

    protected override void WriteEventFields(CanonicalWriter w)
    {
        w.WriteInt32(_numberOfHoldOns);
        w.WriteInt32(_randomCardToLose);
        var skipped = _skippedRemovals;
        w.WriteBool(skipped != null);
        if (skipped != null)
        {
            w.WriteCount(skipped.Count);
            for (int i = 0; i < skipped.Count; i++)
            {
                w.WriteInt32(skipped[i]);
            }
        }
    }
}
