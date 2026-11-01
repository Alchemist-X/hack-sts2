namespace Sts2Sim.Core.Relics;

public sealed class NeowsBones : RelicModel
{
    public const string RelicId = "NEOWS_BONES";

    private const string RelicCountKey = "Relics";

    private const string CursesCountKey = "Curses";

    public const string UnportedNeowRelicSite = "NeowsBones: Neow relic option not ported; skipped from the offered rewards";

    public const string UnportedAllowedAtNeowSite = "NeowsBones: Neow relic option not ported; IsAllowedAtNeow evaluated from Neow's copy of the relic rule";

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Ancient;

    protected override IEnumerable<DynamicVar> CanonicalVars => new[]
    {
        new DynamicVar(RelicCountKey, 2m),
        new DynamicVar(CursesCountKey, 1m)
    };

    private static IEnumerable<string> AllPossibleNeowRelicIds()
    {
        foreach (var id in Neow.CurseRelicIds)
        {
            yield return id;
        }
        foreach (var id in Neow.PositiveRelicIds)
        {
            yield return id;
        }
        yield return Neow.LavaRockId;
        yield return Neow.NeowsTalismanId;
        yield return Neow.NutritiousOysterId;
        yield return Neow.PomanderId;
        yield return Neow.SmallCapsuleId;
        yield return Neow.StoneHumidifierId;
    }

    private static List<string> GetValidRelicIds(RunState rs, PlayerState owner)
    {
        var result = new List<string>();
        foreach (var id in AllPossibleNeowRelicIds())
        {
            if (id == RelicId)
            {
                continue;
            }
            if (IsAllowedAtNeow(rs, owner, id))
            {
                result.Add(id);
            }
        }
        return result;
    }

    private static bool IsAllowedAtNeow(RunState rs, PlayerState owner, string relicId)
    {
        if (RelicRegistry.Models.Contains(relicId))
        {
            return RelicRegistry.Create(relicId).IsAllowedAtNeow(rs, owner);
        }
        rs.Fidelity.Record(FidelityFlag.ApproximatedRule, UnportedAllowedAtNeowSite + " (" + relicId + ")");
        switch (relicId)
        {
            case "MASSIVE_SCROLL":
                return rs.Players.Count > 1;
            case "WINGED_BOOTS":
            case "SILVER_CRUCIBLE":
                return rs.Players.Count == 1;
            case "KALEIDOSCOPE":
                return rs.Unlocks.CharacterCardPools.Count == CardPools.AllCharacterIds.Count;
            default:
                return true;
        }
    }

    public override void AfterObtained(RunState rs)
    {
        if (!SmallCapsule.HasRunLayer(rs, this))
        {
            return;
        }
        var val4 = GetValidRelicIds(rs, Owner);
        Owner.PlayerRng!.Rewards.Shuffle(val4);
        var rewards = new List<Reward>();
        foreach (var id in val4.Take(DynamicVars[RelicCountKey].IntValue))
        {
            if (!RelicRegistry.Models.Contains(id))
            {
                rs.Fidelity.Record(FidelityFlag.ApproximatedRule, UnportedNeowRelicSite + " (" + id + ")");
                continue;
            }
            rewards.Add(new RelicReward(RelicRegistry.Create(id), Owner));
        }
        new RewardsSet(Owner).WithCustomRewards(rewards).WithSkippingDisallowed().Offer(rs);
        var availableCurses = CardPools.Curse.GetUnlockedCards(rs.Unlocks, CardPools.MultiplayerConstraintFor(rs))
            .Where(c => c.CanBeGeneratedByModifiers)
            .OrderBy(c => c.Id, StringComparer.Ordinal)
            .ToList();
        for (int i = 0; i < DynamicVars[CursesCountKey].IntValue; i++)
        {
            var cardModel = rs.Rng.Niche.NextItem(availableCurses);
            if (cardModel == null)
            {
                break;
            }
            availableCurses.Remove(cardModel);
            var card = CardFactory.CreateRunCard(rs, cardModel, Owner);
            CardPileCmd.Add(rs, card, PileType.Deck);
        }
    }
}
