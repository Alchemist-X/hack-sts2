namespace Sts2Sim.Core.Run;

public sealed class ColorfulPhilosophers : EventModel
{
    public const string EventId = "COLORFUL_PHILOSOPHERS";

    public const int Cards = 3;

    public const int MaxOptions = 3;

    public static readonly IReadOnlyList<string> CardPoolColorOrder = new[]
    {
        CardPools.NecrobinderCharacterId,
        CardPools.IroncladCharacterId,
        CardPools.RegentCharacterId,
        CardPools.SilentCharacterId,
        CardPools.DefectCharacterId
    };

    public override string Id => EventId;

    public override bool IsAllowed(RunState runState) => runState.Unlocks.CharacterCardPools.Count > 1;

    protected override IReadOnlyList<EventOption> GenerateInitialOptions(RunState rs)
    {
        var owner = EventKit3456.OwnerOf(this);
        var rng = EventKit3456.RngOf(this);
        var options = new List<EventOption>();
        var characterPool = CardPools.ForCharacter(owner.CharacterId);
        var unlocked = rs.Unlocks.CharacterCardPools;
        for (int i = 0; i < CardPoolColorOrder.Count; i++)
        {
            var pool = CardPools.ForCharacter(CardPoolColorOrder[i]);
            if (!ReferenceEquals(characterPool, pool) && unlocked.Contains(pool))
            {
                options.Add(new EventOption("COLORFUL_PHILOSOPHERS.pages.INITIAL.options." + CardPoolColorOrder[i].ToUpperInvariant(), OfferRewards)
                {
                    StringArg = CardPoolColorOrder[i]
                });
            }
        }
        int num = Math.Min(MaxOptions, options.Count);
        while (options.Count > num)
        {
            options.RemoveAt(rng.NextInt(options.Count));
        }
        return options;
    }

    private void OfferRewards(RunState rs)
    {
        var owner = EventKit3456.OwnerOf(this);
        var pool = CardPools.ForCharacter(ChosenOption?.StringArg ?? throw new InvalidOperationException("ColorfulPhilosophers: option has no pool."));
        var rewards = new List<Reward>(3)
        {
            new CardReward(owner, Options(pool, CardRarity.Common), Cards),
            new CardReward(owner, Options(pool, CardRarity.Uncommon), Cards),
            new CardReward(owner, Options(pool, CardRarity.Rare), Cards)
        };
        RewardsCmd.OfferCustom(rs, owner, rewards);
        SetEventFinished("COLORFUL_PHILOSOPHERS.pages.DONE.description");
    }

    private static CardCreationOptions Options(CardPool pool, CardRarity rarity)
        => new CardCreationOptions(new[] { pool }, CardCreationSource.Other, CardRarityOddsType.Uniform, c => c.Rarity == rarity).WithFlags(CardCreationFlags.NoRarityModification | CardCreationFlags.NoCardPoolModifications);
}
