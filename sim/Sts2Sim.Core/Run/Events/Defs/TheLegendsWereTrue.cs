namespace Sts2Sim.Core.Run;

public sealed class TheLegendsWereTrue : EventModel
{
    public const string EventId = "THE_LEGENDS_WERE_TRUE";

    public const string SpoilsMapId = "SPOILS_MAP";

    public const decimal Damage = 8m;

    public const ValueProp DamageProps = ValueProp.Unblockable | ValueProp.Unpowered;

    public override string Id => EventId;

    public override bool IsAllowed(RunState rs)
    {
        if (rs.CurrentActIndex == 0 && rs.Players.All(p => p.Deck.Count > 0))
        {
            return rs.Players.All(p => p.CurrentHp >= 10);
        }
        return false;
    }

    protected override IReadOnlyList<EventOption> GenerateInitialOptions(RunState rs) => new[]
    {
        new EventOption(InitialOptionKey("NAB_THE_MAP"), NabTheMap),
        new EventOption(InitialOptionKey("SLOWLY_FIND_AN_EXIT"), SlowlyFindAnExit)
    };

    private void NabTheMap(RunState rs)
    {
        var owner = Owner!;
        var card = CardFactory.CreateRunCard(rs, RequireCard(SpoilsMapId), owner);
        CardPileCmd.Add(rs, card, PileType.Deck);
        SetEventFinished(Id + ".pages.NAB_THE_MAP.description");
    }

    private void SlowlyFindAnExit(RunState rs)
    {
        var owner = Owner!;
        CreatureCmd.Damage(rs, owner, Damage, DamageProps);
        var potion = owner.PlayerRng!.Rewards.NextItem(PotionFactory.GetPotionOptions(rs, owner));
        if (potion != null)
        {
            RewardsCmd.OfferCustom(rs, owner, new List<Reward> { new PotionReward(potion.ToMutable(), owner) });
        }
        SetEventFinished(Id + ".pages.SLOWLY_FIND_AN_EXIT.description");
    }

    private static CardDef RequireCard(string cardId)
        => CardRegistry.TryGet(cardId, out var def) ? def : throw new NotImplementedException("W3 cross-owner: card " + cardId + " is not ported");
}
