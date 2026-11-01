namespace Sts2Sim.Core.Run;

public abstract partial class AncientEventModel : EventModel
{
    public const string UnportedRelicSite = "AncientEventModel.RelicOption: relic def not ported; option kept by id, obtain skipped";

    public const string UnportedRelicSetupSite = "AncientEventModel: relic SetupForPlayer not ported; option treated as eligible";

    private string? _customDonePage;

    public override EventLayoutType LayoutType => EventLayoutType.Ancient;

    public int HealedAmount { get; private set; }

    protected string? CustomDonePage
    {
        get => _customDonePage;
        set => _customDonePage = value;
    }

    protected override void BeforeEventStarted(RunState rs, bool isPreFinished)
    {
        if (!isPreFinished)
        {
            ApplyArrivalHeal(rs);
        }
    }

    protected void ApplyArrivalHeal(RunState rs)
    {
        var player = Owner ?? throw new InvalidOperationException("Ancient event has no owner.");
        if (this is Neow)
        {
            CreatureInternals.SetCurrentHpInternal(player, 0m);
        }
        int oldHp = player.CurrentHp;
        decimal amount = player.MaxHp - player.CurrentHp;
        if (rs.Ascension >= (int)AscensionLevel.WearyTraveler)
        {
            amount *= 0.8m;
        }
        HealOutOfCombat(player, amount);
        HealedAmount = player.CurrentHp - oldHp;
    }

    private static void HealOutOfCombat(PlayerState player, decimal amount)
    {
        CreatureInternals.HealInternal(player, amount);
    }

    protected sealed override IReadOnlyList<EventOption> GenerateInitialOptionsWrapper(RunState rs)
    {
        var player = Owner ?? throw new InvalidOperationException("Ancient event has no owner.");
        if (Hook.ShouldAllowAncient(rs, player, this))
        {
            var generated = new List<EventOption>(GenerateInitialOptions(rs));
            ReplaceNullOptions(generated);
            return generated;
        }
        return new[] { new EventOption("PROCEED", Proceed, disableOnChosen: false, isProceed: true) };
    }

    protected override void SetInitialEventState(RunState rs, bool isPreFinished)
    {
        var options = GenerateInitialOptionsWrapper(rs);
        if (options.Count == 0 || isPreFinished)
        {
            StartPreFinished();
        }
        else
        {
            SetEventState(InitialDescription, options);
        }
    }

    public void StartPreFinished()
    {
        SetEventFinished(CustomDonePage ?? Id + ".pages.DONE.description");
    }

    protected void Done()
    {
        SetEventFinished(CustomDonePage ?? Id + ".pages.DONE.description");
    }

    private void Proceed(RunState rs)
    {
        SetEventFinished("PROCEED.description");
    }

    protected EventOption AncientRelicOption(string relicId, string pageName = InitialPageKey, string? customDonePage = null)
        => AncientRelicOption(relicId, CreateOptionRelic(relicId), pageName, customDonePage);

    protected EventOption AncientRelicOption(string relicId, RelicModel? relic, string pageName = InitialPageKey, string? customDonePage = null)
    {
        var option = RelicOption(relicId, relic, ObtainChosenRelic, pageName);
        option.DonePage = customDonePage;
        return option;
    }

    private void ObtainChosenRelic(RunState rs)
    {
        var option = ChosenOption ?? throw new InvalidOperationException("No chosen option.");
        var player = Owner ?? throw new InvalidOperationException("Ancient event has no owner.");
        var relic = option.Relic;
        if (relic != null)
        {
            RelicCmd.Obtain(rs, relic, player);
        }
        else
        {
            rs.Fidelity.Record(FidelityFlag.ApproximatedRule, UnportedRelicSite + " (" + option.RelicId + ")");
        }
        CustomDonePage = option.DonePage;
        Done();
    }

    protected static bool SetupRelicForPlayer(RunState rs, PlayerState player, RelicModel? relic, string relicId)
    {
        if (relic is IEventRelicSetup setup)
        {
            return setup.SetupForPlayer(rs, player);
        }
        rs.Fidelity.Record(FidelityFlag.ApproximatedRule, UnportedRelicSetupSite + " (" + relicId + ")");
        return true;
    }

    protected override void WriteEventFields(CanonicalWriter w)
    {
        w.WriteTag(EventsCanonicalTags.AncientEvent);
        w.WriteInt32(HealedAmount);
        w.WriteString(_customDonePage);
        WriteAncientFields(w);
    }

    protected virtual void WriteAncientFields(CanonicalWriter w)
    {
    }
}
