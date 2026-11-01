namespace Sts2Sim.Core.Run;

public sealed class WarHistorianRepy : EventModel
{
    public const string EventId = "WAR_HISTORIAN_REPY";

    public const string LanternKeyId = "LANTERN_KEY";

    public const string HistoryCourseId = "HISTORY_COURSE";

    public override string Id => EventId;

    public override bool IsShared => true;

    private bool ShouldGetSecondReward(RunState rs)
    {
        if (Owner!.Deck.Any(IsLanternKey))
        {
            return rs.Players.Count <= 1;
        }
        return false;
    }

    private static bool IsLanternKey(CardInstance c) => c.Id == LanternKeyId;

    public override bool IsAllowed(RunState rs) => false;

    protected override IReadOnlyList<EventOption> GenerateInitialOptions(RunState rs) => new[]
    {
        new EventOption(InitialOptionKey("UNLOCK_CAGE"), InitialUnlockCage),
        new EventOption(InitialOptionKey("UNLOCK_CHEST"), InitialUnlockChest)
    };

    private void InitialUnlockCage(RunState rs)
    {
        RemoveLanternKeysForInitialChoice(rs);
        UnlockCage(rs);
        if (ShouldGetSecondReward(rs))
        {
            SetEventState(Id + ".pages.UNLOCK_CAGE.description", new[]
            {
                new EventOption(InitialOptionKey("UNLOCK_CHEST"), SecondUnlockChest)
            });
        }
        else
        {
            SetEventFinished(Id + ".pages.UNLOCK_CAGE.description");
        }
    }

    private void InitialUnlockChest(RunState rs)
    {
        RemoveLanternKeysForInitialChoice(rs);
        UnlockChest(rs);
        if (ShouldGetSecondReward(rs))
        {
            SetEventState(Id + ".pages.UNLOCK_CHEST.description", new[]
            {
                new EventOption(InitialOptionKey("UNLOCK_CAGE"), SecondUnlockCage)
            });
        }
        else
        {
            SetEventFinished(Id + ".pages.UNLOCK_CHEST.description");
        }
    }

    private void SecondUnlockCage(RunState rs)
    {
        SetEventFinished(Id + ".pages.EXTRA_UNLOCK_CAGE.description");
        RemoveLanternKeysForSecondChoice(rs);
        UnlockCage(rs);
    }

    private void SecondUnlockChest(RunState rs)
    {
        SetEventFinished(Id + ".pages.EXTRA_UNLOCK_CHEST.description");
        RemoveLanternKeysForSecondChoice(rs);
        UnlockChest(rs);
    }

    private void UnlockChest(RunState rs)
    {
        var owner = Owner!;
        RewardsCmd.OfferCustom(rs, owner, new List<Reward>
        {
            new PotionReward(owner),
            new PotionReward(owner),
            new RelicReward(owner),
            new RelicReward(owner)
        });
    }

    private void UnlockCage(RunState rs)
    {
        rs.ExtraFields.FreedRepy = true;
        RelicCmd.Obtain(rs, HistoryCourseId, Owner!);
    }

    private void RemoveLanternKeysForInitialChoice(RunState rs)
    {
        if (rs.Players.Count > 1)
        {
            RemoveLanternKeysForSecondChoice(rs);
        }
        else
        {
            RemoveFirstLanternKey(rs);
        }
    }

    private void RemoveFirstLanternKey(RunState rs)
    {
        var card = Owner!.Deck.FirstOrDefault(IsLanternKey);
        if (card != null)
        {
            PlayerCmd.CompleteQuest(rs, card);
            CardPileCmd.RemoveFromDeck(rs, card);
        }
    }

    private void RemoveLanternKeysForSecondChoice(RunState rs)
    {
        var keys = Owner!.Deck.Where(IsLanternKey).ToList();
        foreach (var card in keys)
        {
            PlayerCmd.CompleteQuest(rs, card);
            CardPileCmd.RemoveFromDeck(rs, card);
        }
    }
}
