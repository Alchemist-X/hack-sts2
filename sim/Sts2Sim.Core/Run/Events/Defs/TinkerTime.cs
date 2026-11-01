namespace Sts2Sim.Core.Run;

public sealed class TinkerTime : EventModel
{
    public const string EventId = "TINKER_TIME";

    public const string ChooseCardTypePage = "CHOOSE_CARD_TYPE";

    public const string ChooseRiderPage = "CHOOSE_RIDER";

    public const int OfferedCount = 2;

    private static readonly MadScience.RiderEffect[] AttackRiders = { MadScience.RiderEffect.Sapping, MadScience.RiderEffect.Violence, MadScience.RiderEffect.Choking };

    private static readonly MadScience.RiderEffect[] SkillRiders = { MadScience.RiderEffect.Energized, MadScience.RiderEffect.Wisdom, MadScience.RiderEffect.Chaos };

    private static readonly MadScience.RiderEffect[] PowerRiders = { MadScience.RiderEffect.Expertise, MadScience.RiderEffect.Curious, MadScience.RiderEffect.Improvement };

    private List<MadScience.RiderEffect> _riders = new();

    public override string Id => EventId;

    public CardType ChosenCardType { get; private set; }

    public IReadOnlyList<MadScience.RiderEffect> OfferedRiders => _riders;

    public static IReadOnlyList<MadScience.RiderEffect> RidersFor(CardType cardType) => cardType switch
    {
        CardType.Attack => AttackRiders,
        CardType.Skill => SkillRiders,
        CardType.Power => PowerRiders,
        _ => throw new ArgumentOutOfRangeException(nameof(cardType), cardType, null)
    };

    protected override IReadOnlyList<EventOption> GenerateInitialOptions(RunState rs) => new[]
    {
        new EventOption(InitialOptionKey("CHOOSE_CARD_TYPE"), ChooseCardType)
    };

    private void ChooseCardType(RunState rs)
    {
        var collection = new[]
        {
            new EventOption(OptionKey(ChooseCardTypePage, "ATTACK"), Attack),
            new EventOption(OptionKey(ChooseCardTypePage, "SKILL"), Skill),
            new EventOption(OptionKey(ChooseCardTypePage, "POWER"), Power)
        };
        SetEventState(Id + ".pages.CHOOSE_CARD_TYPE.description", collection.TakeRandom(OfferedCount, Rng!));
    }

    private void Attack(RunState rs)
    {
        ChosenCardType = CardType.Attack;
        ChooseRiderEffect(rs);
    }

    private void Skill(RunState rs)
    {
        ChosenCardType = CardType.Skill;
        ChooseRiderEffect(rs);
    }

    private void Power(RunState rs)
    {
        ChosenCardType = CardType.Power;
        ChooseRiderEffect(rs);
    }

    private void ChooseRiderEffect(RunState rs)
    {
        var collection = RidersFor(ChosenCardType);
        _riders = collection.TakeRandom(OfferedCount, Rng!).ToList();
        SetEventState(Id + ".pages.CHOOSE_RIDER.description", new[]
        {
            new EventOption(GetRiderLocKey(_riders[0]), FirstRiderChosen),
            new EventOption(GetRiderLocKey(_riders[1]), SecondRiderChosen)
        });
    }

    private void FirstRiderChosen(RunState rs) => RiderChosen(rs, _riders[0]);

    private void SecondRiderChosen(RunState rs) => RiderChosen(rs, _riders[1]);

    private void RiderChosen(RunState rs, MadScience.RiderEffect rider)
    {
        var owner = Owner!;
        var madScience = CardFactory.CreateRunCard(rs, CardRegistry.Get(MadScience.CardId), owner);
        MadScience.SetTinkerTimeType(madScience, ChosenCardType);
        MadScience.SetTinkerTimeRider(madScience, rider);
        CardPileCmd.Add(rs, madScience, PileType.Deck);
        SetEventFinished(Id + ".pages.DONE.description");
    }

    private string GetRiderLocKey(MadScience.RiderEffect rider) => rider switch
    {
        MadScience.RiderEffect.None => throw new ArgumentOutOfRangeException(nameof(rider), rider, "None is not a valid rider"),
        MadScience.RiderEffect.Sapping => OptionKey(ChooseRiderPage, "SAPPING"),
        MadScience.RiderEffect.Violence => OptionKey(ChooseRiderPage, "VIOLENCE"),
        MadScience.RiderEffect.Choking => OptionKey(ChooseRiderPage, "CHOKING"),
        MadScience.RiderEffect.Energized => OptionKey(ChooseRiderPage, "ENERGIZED"),
        MadScience.RiderEffect.Wisdom => OptionKey(ChooseRiderPage, "WISDOM"),
        MadScience.RiderEffect.Chaos => OptionKey(ChooseRiderPage, "CHAOS"),
        MadScience.RiderEffect.Expertise => OptionKey(ChooseRiderPage, "EXPERTISE"),
        MadScience.RiderEffect.Curious => OptionKey(ChooseRiderPage, "CURIOUS"),
        MadScience.RiderEffect.Improvement => OptionKey(ChooseRiderPage, "IMPROVEMENT"),
        _ => throw new ArgumentOutOfRangeException(nameof(rider), rider, null)
    };

    protected override void CopyMutableReferenceState(EventModel source)
    {
        _riders = new List<MadScience.RiderEffect>(((TinkerTime)source)._riders);
    }

    protected override void WriteEventFields(CanonicalWriter w)
    {
        w.WriteInt32((int)ChosenCardType);
        w.WriteCount(_riders.Count);
        for (int i = 0; i < _riders.Count; i++)
        {
            w.WriteInt32((int)_riders[i]);
        }
    }
}
