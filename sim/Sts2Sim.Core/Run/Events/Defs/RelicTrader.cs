namespace Sts2Sim.Core.Run;

public sealed class RelicTrader : EventModel
{
    public const string EventId = "RELIC_TRADER";

    public const int TradeCount = 3;

    public const int MinTradableRelics = 5;

    private List<int>? _ownedRelics;

    private List<string>? _newRelics;

    public override string Id => EventId;

    private IReadOnlyList<int> OwnedRelics
    {
        get
        {
            if (_ownedRelics == null)
            {
                var relics = Owner!.Relics;
                var valid = new List<int>(relics.Count);
                for (int i = 0; i < relics.Count; i++)
                {
                    if (relics[i].IsTradable)
                    {
                        valid.Add(i);
                    }
                }
                valid.StableShuffle(Rng!, (a, b) => string.CompareOrdinal(relics[a].Id, relics[b].Id));
                _ownedRelics = valid.Take(TradeCount).ToList();
            }
            return _ownedRelics;
        }
    }

    private IReadOnlyList<string> NewRelics(RunState rs)
    {
        if (_newRelics == null)
        {
            var owner = Owner!;
            var ids = new List<string>(TradeCount);
            for (int i = 0; i < TradeCount; i++)
            {
                ids.Add(RelicFactory.PullNextRelicFromFront(rs, owner).Id);
            }
            _newRelics = ids;
        }
        return _newRelics;
    }

    public IReadOnlyList<string> OwnedRelicIds => OwnedRelics.Select(i => Owner!.Relics[i].Id).ToArray();

    public IReadOnlyList<string> NewRelicIds => (IReadOnlyList<string>?)_newRelics ?? Array.Empty<string>();

    public override bool IsAllowed(RunState runState)
    {
        if (runState.CurrentActIndex == 0)
        {
            return false;
        }
        return runState.Players.All(p => p.Relics.Count(r => r.IsTradable) >= MinTradableRelics);
    }

    public override void CalculateVars(RunState rs)
    {
        for (int i = 0; i < TradeCount; i++)
        {
            if (OwnedRelics.Count > i)
            {
                _ = NewRelics(rs).Count;
            }
        }
    }

    protected override IReadOnlyList<EventOption> GenerateInitialOptions(RunState rs)
    {
        var options = new List<EventOption>(TradeCount);
        if (OwnedRelics.Count >= 1)
        {
            options.Add(new EventOption("RELIC_TRADER.pages.INITIAL.options.TOP", Top));
            TouchHoverTips(rs, 0);
        }
        if (OwnedRelics.Count >= 2)
        {
            options.Add(new EventOption("RELIC_TRADER.pages.INITIAL.options.MIDDLE", Middle));
            TouchHoverTips(rs, 1);
        }
        if (OwnedRelics.Count >= 3)
        {
            options.Add(new EventOption("RELIC_TRADER.pages.INITIAL.options.BOTTOM", Bottom));
            TouchHoverTips(rs, 2);
        }
        if (options.Count == 0)
        {
            options.Add(new EventOption("PROCEED", Done));
        }
        return options;
    }

    private void TouchHoverTips(RunState rs, int index)
    {
        if (OwnedRelics.Count <= index)
        {
            return;
        }
        _ = NewRelics(rs).Count;
    }

    private void Top(RunState rs) => Trade(rs, 0);

    private void Middle(RunState rs) => Trade(rs, 1);

    private void Bottom(RunState rs) => Trade(rs, 2);

    private void Trade(RunState rs, int index)
    {
        var owner = Owner!;
        RelicCmd.Remove(rs, owner.Relics[OwnedRelics[index]]);
        RelicCmd.Obtain(rs, RelicRegistry.Create(NewRelics(rs)[index]), owner);
        Done(rs);
    }

    private void Done(RunState rs)
    {
        SetEventFinished("RELIC_TRADER.pages.DONE.description");
    }

    protected override void CopyMutableReferenceState(EventModel source)
    {
        var src = (RelicTrader)source;
        _ownedRelics = src._ownedRelics == null ? null : new List<int>(src._ownedRelics);
        _newRelics = src._newRelics == null ? null : new List<string>(src._newRelics);
    }

    protected override void WriteEventFields(CanonicalWriter w)
    {
        var owned = _ownedRelics;
        w.WriteBool(owned != null);
        if (owned != null)
        {
            w.WriteCount(owned.Count);
            for (int i = 0; i < owned.Count; i++)
            {
                w.WriteInt32(owned[i]);
            }
        }
        var added = _newRelics;
        w.WriteBool(added != null);
        if (added != null)
        {
            w.WriteCount(added.Count);
            for (int i = 0; i < added.Count; i++)
            {
                w.WriteString(added[i]);
            }
        }
    }
}
