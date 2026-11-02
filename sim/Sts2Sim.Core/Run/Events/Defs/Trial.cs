namespace Sts2Sim.Core.Run;

public sealed class Trial : EventModel
{
    public const string EventId = "TRIAL";

    public const string RegretId = "REGRET";

    public const string ShameId = "SHAME";

    public const string DoubtId = "DOUBT";

    public const int GuiltyMerchantRelics = 2;

    public const int InnocentMerchantUpgrades = 2;

    public const decimal NobleGuiltyHeal = 10m;

    public const decimal NobleInnocentGold = 300m;

    public const int NondescriptGuiltyCardRewards = 2;

    public const int NondescriptInnocentTransforms = 2;

    public const string DoubleDownSite = "Trial.DoubleDown: NAbandonRunConfirmPopup not modelled; the option is a no-op (the popup's cancel path)";

    public const string MerchantInnocentSite = "TRIAL:MERCHANT INNOCENT";

    public const string NondescriptInnocentSite = "TRIAL:NONDESCRIPT INNOCENT";

    public override string Id => EventId;

    protected override IReadOnlyList<EventOption> GenerateInitialOptions(RunState rs) => new[]
    {
        new EventOption(InitialOptionKey("ACCEPT"), Accept),
        new EventOption(InitialOptionKey("REJECT"), Reject)
    };

    private void Accept(RunState rs)
    {
        string entryName;
        EventOption[] eventOptions;
        switch (Rng!.NextInt(3))
        {
            case 0:
                entryName = Id + ".pages.MERCHANT.description";
                eventOptions = new[]
                {
                    new EventOption(OptionKey("MERCHANT", "GUILTY"), MerchantGuilty),
                    new EventOption(OptionKey("MERCHANT", "INNOCENT"), MerchantInnocent)
                };
                break;
            case 1:
                entryName = Id + ".pages.NOBLE.description";
                eventOptions = new[]
                {
                    new EventOption(OptionKey("NOBLE", "GUILTY"), NobleGuilty),
                    new EventOption(OptionKey("NOBLE", "INNOCENT"), NobleInnocent)
                };
                break;
            case 2:
                entryName = Id + ".pages.NONDESCRIPT.description";
                eventOptions = new[]
                {
                    new EventOption(OptionKey("NONDESCRIPT", "GUILTY"), NondescriptGuilty),
                    new EventOption(OptionKey("NONDESCRIPT", "INNOCENT"), NondescriptInnocent)
                };
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }
        SetEventState(entryName, eventOptions);
    }

    private void Reject(RunState rs)
    {
        SetEventState(Id + ".pages.REJECT.description", new[]
        {
            new EventOption(OptionKey("REJECT", "ACCEPT"), Accept),
            new EventOption(OptionKey("REJECT", "DOUBLE_DOWN"), DoubleDown, disableOnChosen: false, isProceed: true)
        });
    }

    private void DoubleDown(RunState rs)
    {
        rs.Fidelity.Record(FidelityFlag.ApproximatedRule, DoubleDownSite);
    }

    private void MerchantGuilty(RunState rs)
    {
        var owner = Owner!;
        CardPileCmd.AddCurseToDeck(rs, RegretId, owner);
        for (int i = 0; i < GuiltyMerchantRelics; i++)
        {
            RelicCmd.Obtain(rs, RelicFactory.PullNextRelicFromFront(rs, owner), owner);
        }
        SetTrialFinished("TRIAL.pages.MERCHANT_GUILTY.description");
    }

    private void MerchantInnocent(RunState rs)
    {
        var owner = Owner!;
        CardPileCmd.AddCurseToDeck(rs, ShameId, owner);
        var picked = CardSelectCmd.FromDeckForUpgrade(rs, owner, new CardSelectorPrefs(CardSelectorPrefs.UpgradeSelectionPrompt, InnocentMerchantUpgrades), new ChoiceSource(MerchantInnocentSite, Id, ChoiceSource.NoInstance));
        foreach (var card in picked)
        {
            CardCmd.Upgrade(null, card);
        }
        SetTrialFinished("TRIAL.pages.MERCHANT_INNOCENT.description");
    }

    private void NobleGuilty(RunState rs)
    {
        CreatureCmd.Heal(rs, Owner!, NobleGuiltyHeal);
        SetTrialFinished("TRIAL.pages.NOBLE_GUILTY.description");
    }

    private void NobleInnocent(RunState rs)
    {
        var owner = Owner!;
        CardPileCmd.AddCurseToDeck(rs, RegretId, owner);
        PlayerCmd.GainGold(rs, NobleInnocentGold, owner);
        SetTrialFinished("TRIAL.pages.NOBLE_INNOCENT.description");
    }

    private void NondescriptGuilty(RunState rs)
    {
        var owner = Owner!;
        CardPileCmd.AddCurseToDeck(rs, DoubtId, owner);
        var rewards = new List<Reward>();
        for (int i = 0; i < NondescriptGuiltyCardRewards; i++)
        {
            rewards.Add(new CardReward(owner, CardCreationOptions.ForNonCombatWithDefaultOdds(new[] { CardPools.ForCharacter(owner.CharacterId) }), 3));
        }
        RewardsCmd.OfferCustom(rs, owner, rewards);
        SetTrialFinished("TRIAL.pages.NONDESCRIPT_GUILTY.description");
    }

    private void NondescriptInnocent(RunState rs)
    {
        var owner = Owner!;
        CardPileCmd.AddCurseToDeck(rs, DoubtId, owner);
        var picked = CardSelectCmd.FromDeckForTransformation(rs, owner, new CardSelectorPrefs(CardSelectorPrefs.TransformSelectionPrompt, NondescriptInnocentTransforms), new ChoiceSource(NondescriptInnocentSite, Id, ChoiceSource.NoInstance)).ToList();
        foreach (var card in picked)
        {
            CardCmd.TransformToRandom(rs, card, Rng!);
        }
        SetTrialFinished("TRIAL.pages.NONDESCRIPT_INNOCENT.description");
    }

    private void SetTrialFinished(string trialResultLoc)
    {
        SetEventFinished(trialResultLoc);
    }
}
