namespace Sts2Sim.Core.Relics;

public sealed class GhostSeed : RelicModel
{
    public const string RelicId = "GHOST_SEED";

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Shop;

    public override void AfterCardEnteredCombat(CombatState cs, CardInstance card)
    {
        if (!CanAffect(cs, card))
        {
            return;
        }
        if (!card.HasOwner || !ReferenceEquals(card.Owner, Owner))
        {
            return;
        }
        CardCmd.ApplyKeyword(card, CardKeyword.Ethereal);
    }

    public override void AfterRoomEntered(RunState rs, AbstractRoom room)
    {
        if (room is not CombatRoom)
        {
            return;
        }
        var cs = rs.Combat;
        foreach (var card in Owner.AllPiles.SelectMany(p => p.Cards).ToList())
        {
            if (CanAffect(cs, card))
            {
                CardCmd.ApplyKeyword(card, CardKeyword.Ethereal);
            }
        }
    }

    private static bool CanAffect(CombatState? cs, CardInstance card)
    {
        if (card.Rarity == CardRarity.Basic && (card.HasTag(CardTag.Strike) || card.HasTag(CardTag.Defend)))
        {
            return !card.GetKeywordsWithSources(cs, KeywordSources.Local).Contains(CardKeyword.Ethereal);
        }
        return false;
    }
}
