namespace Sts2Sim.Core.Run;

public sealed class TheFutureOfPotions : EventModel
{
    public const string EventId = "THE_FUTURE_OF_POTIONS";

    public const string ChoiceKey = "THE_FUTURE_OF_POTIONS.pages.INITIAL.options.POTION";

    public const int MaxPotionOptions = 3;

    public const int RewardCardCount = 3;

    private List<CardType>? _cardTypes;

    public override string Id => EventId;

    public override bool IsAllowed(RunState rs) => rs.Players.All(p => p.Potions.Count() >= 2);

    protected override void BeforeEventStarted(RunState rs, bool isPreFinished)
    {
        Owner!.CanUseOrRemovePotions = false;
    }

    protected override void OnEventFinished()
    {
        if (Owner != null)
        {
            Owner.CanUseOrRemovePotions = true;
        }
    }

    private List<CardType> PotionToCardType()
    {
        if (_cardTypes == null)
        {
            _cardTypes = new List<CardType>();
            foreach (var potion in Owner!.Potions)
            {
                var types = new List<CardType> { CardType.Attack, CardType.Skill, CardType.Power };
                if (potion.Rarity == PotionRarity.Common || potion.Rarity == PotionRarity.Token)
                {
                    types.Remove(CardType.Power);
                }
                _cardTypes.Add(Rng!.NextItem((IReadOnlyList<CardType>)types));
            }
        }
        return _cardTypes;
    }

    protected override IReadOnlyList<EventOption> GenerateInitialOptions(RunState rs)
    {
        var options = new List<EventOption>();
        var potions = Owner!.Potions.ToList();
        int count = Math.Min(MaxPotionOptions, potions.Count);
        for (int i = 0; i < count; i++)
        {
            GetCardRarity(potions[i]);
            PotionToCardType();
            options.Add(new EventOption(ChoiceKey, Trade) { StringArg = i.ToString(System.Globalization.CultureInfo.InvariantCulture) });
        }
        return options;
    }

    private void Trade(RunState rs)
    {
        var owner = Owner!;
        int index = int.Parse(ChosenOption!.StringArg!, System.Globalization.CultureInfo.InvariantCulture);
        var potion = owner.Potions.ToList()[index];
        var targetRarity = GetCardRarity(potion);
        var targetType = PotionToCardType()[index];
        PotionCmd.Discard(rs, potion);
        var options = CardCreationOptions.ForNonCombatWithUniformOdds(new[] { CardPools.ForCharacter(owner.CharacterId) }, c => c.Rarity == targetRarity && c.Type == targetType)
            .WithFlags(CardCreationFlags.NoRarityModification | CardCreationFlags.NoCardPoolModifications);
        var reward = new CardReward(owner, options, RewardCardCount);
        reward.Populate(rs);
        foreach (var result in reward.Cards)
        {
            CardCmd.Upgrade(null, result.Card);
        }
        RewardsCmd.OfferCustom(rs, owner, new List<Reward> { reward });
        SetEventFinished(Id + ".pages.DONE.description");
    }

    private static CardRarity GetCardRarity(PotionModel potion)
    {
        switch (potion.Rarity)
        {
            case PotionRarity.Rare:
            case PotionRarity.Event:
                return CardRarity.Rare;
            case PotionRarity.Uncommon:
                return CardRarity.Uncommon;
            case PotionRarity.Common:
            case PotionRarity.Token:
                return CardRarity.Common;
            default:
                throw new InvalidOperationException($"Potion {potion.Id} has invalid rarity {potion.Rarity}");
        }
    }

    protected override void CopyMutableReferenceState(EventModel source)
    {
        var types = ((TheFutureOfPotions)source)._cardTypes;
        _cardTypes = types == null ? null : new List<CardType>(types);
    }

    protected override void WriteEventFields(CanonicalWriter w)
    {
        var types = _cardTypes;
        w.WriteBool(types != null);
        if (types != null)
        {
            w.WriteCount(types.Count);
            for (int i = 0; i < types.Count; i++)
            {
                w.WriteInt32((int)types[i]);
            }
        }
    }
}
