namespace Sts2Sim.Core.Run;

public sealed class ByrdonisNest : EventModel
{
    public const string EventId = "BYRDONIS_NEST";

    public const string ByrdonisEggId = "BYRDONIS_EGG";

    public const decimal MaxHpGain = 7m;

    public override string Id => EventId;

    public override bool IsAllowed(RunState runState)
    {
        var players = runState.Players;
        for (int i = 0; i < players.Count; i++)
        {
            if (HasEventPet(players[i]))
            {
                return false;
            }
        }
        return true;
    }

    private static bool HasEventPet(PlayerState player)
    {
        var relics = player.Relics;
        for (int i = 0; i < relics.Count; i++)
        {
            if (relics[i].AddsPet)
            {
                return true;
            }
        }
        var deck = player.Deck;
        for (int i = 0; i < deck.Count; i++)
        {
            if (deck[i].Id == ByrdonisEggId)
            {
                return true;
            }
        }
        return false;
    }

    protected override IReadOnlyList<EventOption> GenerateInitialOptions(RunState rs)
    {
        return new[]
        {
            new EventOption("BYRDONIS_NEST.pages.INITIAL.options.EAT", Eat),
            new EventOption("BYRDONIS_NEST.pages.INITIAL.options.TAKE", Take)
        };
    }

    private void Eat(RunState rs)
    {
        Events121Support.GainMaxHp(rs, Owner!, MaxHpGain);
        SetEventFinished("BYRDONIS_NEST.pages.EAT.description");
    }

    private void Take(RunState rs)
    {
        Events121Support.AddCardToDeck(rs, ByrdonisEggId, Owner!);
        SetEventFinished("BYRDONIS_NEST.pages.TAKE.description");
    }
}
