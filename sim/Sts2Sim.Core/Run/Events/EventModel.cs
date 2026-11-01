namespace Sts2Sim.Core.Run;

public abstract partial class EventModel : AbstractModel, ICanonical
{
    public const string InitialPageKey = "INITIAL";

    private List<EventOption>? _currentOptions;

    private bool _isFinished;

    private bool _cleanupCalled;

    public abstract string Id { get; }

    public virtual bool IsShared => false;

    public virtual bool IsDeterministic => !IsShared;

    public virtual EncounterDef? CanonicalEncounter => null;

    public virtual EventLayoutType LayoutType => EventLayoutType.Default;

    public PlayerState? Owner { get; protected set; }

    public Rng? Rng { get; protected set; }

    public string? Description { get; private set; }

    public virtual string InitialDescription => Id + ".pages.INITIAL.description";

    public EventOption? ChosenOption { get; private set; }

    internal Action<EventModel>? StateChanged { get; set; }

    public IReadOnlyList<EventOption> CurrentOptions => _currentOptions ??= new List<EventOption>();

    public bool IsFinished => _isFinished;

    public virtual bool IsAllowed(RunState runState) => true;

    public virtual EventModel ToMutable() => (EventModel)Activator.CreateInstance(GetType())!;

    public void BeginEvent(RunState rs, PlayerState player, bool isPreFinished)
    {
        ArgumentNullException.ThrowIfNull(rs);
        ArgumentNullException.ThrowIfNull(player);
        if (Owner != null)
        {
            throw new InvalidOperationException("Tried to begin event, but it already has an owner!");
        }
        Owner = player;
        int slot = rs.Players.IndexOf(player);
        Rng = new Rng(RngSeeds.Event(rs.Rng.Seed, IsShared, slot, Id));
        try
        {
            BeforeEventStarted(rs, isPreFinished);
            CalculateVars(rs);
            if (player.IsDead)
            {
                SetEventFinished("GENERIC.youAreDead.description");
            }
            else
            {
                SetInitialEventState(rs, isPreFinished);
            }
        }
        catch
        {
            EnsureCleanup();
            throw;
        }
    }

    protected virtual void BeforeEventStarted(RunState rs, bool isPreFinished)
    {
    }

    public virtual void CalculateVars(RunState rs)
    {
    }

    protected virtual void SetInitialEventState(RunState rs, bool isPreFinished)
    {
        if (isPreFinished && this is not AncientEventModel)
        {
            throw new InvalidOperationException($"Tried to load into pre-finished event {Id}! Only ancient events can be pre-finished.");
        }
        var options = GenerateInitialOptionsWrapper(rs);
        SetEventState(InitialDescription, options);
    }

    protected virtual IReadOnlyList<EventOption> GenerateInitialOptionsWrapper(RunState rs)
    {
        var options = new List<EventOption>(GenerateInitialOptions(rs));
        ReplaceNullOptions(options);
        return options;
    }

    protected void ReplaceNullOptions(List<EventOption> options)
    {
        for (int i = 0; i < options.Count; i++)
        {
            if (options[i] == null)
            {
                options[i] = new EventOption("ERROR", null);
            }
        }
    }

    protected virtual IReadOnlyList<EventOption> GenerateInitialOptions(RunState rs) => throw new NotImplementedException("W3 events: " + Id + ".GenerateInitialOptions");

    protected void ClearCurrentOptions()
    {
        (_currentOptions ??= new List<EventOption>()).Clear();
    }

    public virtual void AfterEventStarted(RunState rs)
    {
    }

    public virtual void OnRoomEnter(RunState rs)
    {
    }

    public void ChooseOption(RunState rs, int optionIndex)
    {
        ArgumentNullException.ThrowIfNull(rs);
        if (IsFinished)
        {
            throw new InvalidOperationException($"Option chosen on {Id}, but it is already finished!");
        }
        var options = CurrentOptions;
        if ((uint)optionIndex >= (uint)options.Count)
        {
            throw new InvalidOperationException($"Attempted to choose option index {optionIndex} in event {Id}, but there were only {options.Count} options available!");
        }
        var option = options[optionIndex];
        ChosenOption = option;
        option.Chosen(rs);
    }

    protected void SetEventState(IReadOnlyList<EventOption> options) => SetEventState(Description ?? InitialDescription, options);

    protected virtual void SetEventState(string description, IEnumerable<EventOption> eventOptions)
    {
        var current = _currentOptions ??= new List<EventOption>();
        current.Clear();
        current.AddRange(eventOptions);
        Description = description;
        if (current.Count == 0)
        {
            if (_isFinished)
            {
                throw new InvalidOperationException("Tried to set event options after event was finished!");
            }
            _isFinished = true;
        }
        StateChanged?.Invoke(this);
    }

    protected void SetEventFinished(string description)
    {
        SetEventState(description, Array.Empty<EventOption>());
        _isFinished = true;
        EnsureCleanup();
    }

    protected virtual void OnEventFinished()
    {
    }

    public void EnsureCleanup()
    {
        if (!_cleanupCalled)
        {
            _cleanupCalled = true;
            OnEventFinished();
        }
    }

    protected void EnterCombatWithoutExitingEvent(RunState rs, EncounterDef encounter, IReadOnlyList<Reward> extraRewards, bool shouldResumeAfterCombat)
    {
        ArgumentNullException.ThrowIfNull(rs);
        ArgumentNullException.ThrowIfNull(encounter);
        ArgumentNullException.ThrowIfNull(extraRewards);
        if (!IsShared)
        {
            throw new InvalidOperationException($"Tried to enter combat in non-shared event {Id}!");
        }
        if (shouldResumeAfterCombat && LayoutType == EventLayoutType.Combat)
        {
            throw new InvalidOperationException($"Cannot resume event {Id} after combat because it has a Combat layout — there is no event layout to return to.");
        }
        var room = new CombatRoom(encounter)
        {
            ShouldResumeParentEventAfterCombat = shouldResumeAfterCombat,
            ParentEventId = Id
        };
        for (int i = 0; i < extraRewards.Count; i++)
        {
            room.ExtraRewards.Add(extraRewards[i]);
        }
        RoomStack.EnterRoomWithoutExitingCurrentRoom(rs, room);
    }

    public virtual void Resume(RunState rs, AbstractRoom exitedRoom)
    {
    }

    protected RelicModel? CreateOptionRelic(string relicId)
    {
        if (!RelicRegistry.Models.Contains(relicId))
        {
            return null;
        }
        var relic = RelicRegistry.Create(relicId);
        var owner = Owner;
        if (owner != null)
        {
            relic.AttachTo(owner);
        }
        return relic;
    }

    protected EventOption RelicOption(string relicId, Action<RunState>? onChosen, string pageName = InitialPageKey)
        => RelicOption(relicId, CreateOptionRelic(relicId), onChosen, pageName);

    protected EventOption RelicOption(string relicId, RelicModel? relic, Action<RunState>? onChosen, string pageName = InitialPageKey)
        => new EventOption(OptionKey(pageName, relicId), onChosen).WithRelic(relicId, relic);

    protected string InitialOptionKey(string optionName) => OptionKey(InitialPageKey, optionName);

    protected string OptionKey(string pageName, string optionName) => Id + ".pages." + pageName + ".options." + optionName;

    public virtual EventModel CloneFor(PlayerState? owner, Rng? rng)
    {
        var clone = (EventModel)MemberwiseClone();
        clone.Owner = owner;
        clone.Rng = rng;
        clone.StateChanged = null;
        var options = _currentOptions;
        if (options != null)
        {
            var cloned = new List<EventOption>(options.Count);
            for (int i = 0; i < options.Count; i++)
            {
                cloned.Add(options[i].CloneFor(this, clone, owner));
            }
            clone._currentOptions = cloned;
            var chosen = ChosenOption;
            int chosenIndex = chosen == null ? -1 : options.IndexOf(chosen);
            clone.ChosenOption = chosenIndex < 0 ? null : cloned[chosenIndex];
        }
        else
        {
            clone.ChosenOption = null;
        }
        clone.CopyMutableReferenceState(this);
        return clone;
    }

    protected virtual void CopyMutableReferenceState(EventModel source)
    {
    }

    public virtual void WriteCanonical(CanonicalWriter w)
    {
        w.WriteTag(EventsCanonicalTags.Event);
        w.WriteString(Id);
        w.WriteBool(Owner != null);
        var rng = Rng;
        w.WriteBool(rng != null);
        rng?.WriteCanonical(w);
        w.WriteBool(_isFinished);
        w.WriteBool(_cleanupCalled);
        w.WriteString(Description);
        var options = CurrentOptions;
        w.WriteTag(EventsCanonicalTags.EventOptionList);
        w.WriteCount(options.Count);
        for (int i = 0; i < options.Count; i++)
        {
            options[i].WriteCanonical(w);
        }
        w.WriteTag(EventsCanonicalTags.EventSubclass);
        WriteEventFields(w);
    }

    protected virtual void WriteEventFields(CanonicalWriter w)
    {
    }
}
