namespace Sts2Sim.Core.Run;

public sealed class DollRoom : EventModel
{
    public const string EventId = "DOLL_ROOM";

    public const decimal TakeTimeHpLoss = 5m;

    public const decimal ExamineHpLoss = 15m;

    private static readonly (string RelicId, string DescriptionKey)[] Dolls =
    {
        ("DAUGHTER_OF_THE_WIND", "DOLL_ROOM.pages.DAUGHTER_OF_WIND.description"),
        ("MR_STRUGGLES", "DOLL_ROOM.pages.MR_STRUGGLES.description"),
        ("BING_BONG", "DOLL_ROOM.pages.FABLE.description")
    };

    public override string Id => EventId;

    public static IReadOnlyList<string> DollRelicIds => Dolls.Select(d => d.RelicId).ToArray();

    public override bool IsAllowed(RunState runState) => runState.CurrentActIndex == 1;

    protected override IReadOnlyList<EventOption> GenerateInitialOptions(RunState rs)
    {
        return new[]
        {
            new EventOption("DOLL_ROOM.pages.INITIAL.options.RANDOM", ChooseRandom),
            new EventOption("DOLL_ROOM.pages.INITIAL.options.TAKE_SOME_TIME", TakeSomeTime),
            new EventOption("DOLL_ROOM.pages.INITIAL.options.EXAMINE", Examine)
        };
    }

    private void ChooseRandom(RunState rs)
    {
        int index = Rng!.NextInt(0, Dolls.Length);
        ChooseDollAndShowDescription(rs, index);
    }

    private void TakeSomeTime(RunState rs)
    {
        Events121Support.Damage(rs, Owner!, TakeTimeHpLoss, Events121Support.HpLossProps);
        var shuffled = ShuffledDolls();
        var options = new List<EventOption>(2);
        for (int i = 0; i < shuffled.Count && i < 2; i++)
        {
            options.Add(OptionFromChoice(shuffled[i]));
        }
        SetEventState("DOLL_ROOM.pages.TAKE_SOME_TIME.description", options);
    }

    private void Examine(RunState rs)
    {
        Events121Support.Damage(rs, Owner!, ExamineHpLoss, Events121Support.HpLossProps);
        var shuffled = ShuffledDolls();
        var options = new List<EventOption>(shuffled.Count);
        for (int i = 0; i < shuffled.Count; i++)
        {
            options.Add(OptionFromChoice(shuffled[i]));
        }
        SetEventState("DOLL_ROOM.pages.EXAMINE.description", options);
    }

    private List<int> ShuffledDolls()
    {
        var list = new List<int>(Dolls.Length);
        for (int i = 0; i < Dolls.Length; i++)
        {
            list.Add(i);
        }
        return list.StableShuffle(Rng!, static (a, b) => string.CompareOrdinal(Dolls[a].RelicId, Dolls[b].RelicId));
    }

    private EventOption OptionFromChoice(int dollIndex)
    {
        return new EventOption("DOLL_ROOM.pages.TAKE.options." + Dolls[dollIndex].RelicId, TakeChosenDoll)
        {
            StringArg = Dolls[dollIndex].RelicId
        };
    }

    private void TakeChosenDoll(RunState rs)
    {
        var relicId = ChosenOption?.StringArg ?? throw new InvalidOperationException("DollRoom: chosen option has no doll.");
        for (int i = 0; i < Dolls.Length; i++)
        {
            if (Dolls[i].RelicId == relicId)
            {
                ChooseDollAndShowDescription(rs, i);
                return;
            }
        }
        throw new InvalidOperationException("DollRoom: unknown doll " + relicId);
    }

    private void ChooseDollAndShowDescription(RunState rs, int dollIndex)
    {
        Events121Support.ObtainRelic(rs, Dolls[dollIndex].RelicId, Owner!);
        SetEventFinished(Dolls[dollIndex].DescriptionKey);
    }
}
