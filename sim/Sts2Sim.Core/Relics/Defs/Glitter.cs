namespace Sts2Sim.Core.Relics;

public sealed class Glitter : RelicModel
{
    public const string RelicId = "GLITTER";

    private static readonly Glam CanonicalGlam = new();

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Ancient;

    public override bool TryModifyCardRewardOptionsLate(RunState rs, PlayerState player, List<CardCreationResult> cardRewardOptions, CardCreationOptions creationOptions)
    {
        if (!ReferenceEquals(player, Owner))
        {
            return false;
        }
        foreach (var current in cardRewardOptions)
        {
            var card = current.Card;
            if (CanonicalGlam.CanEnchant(card))
            {
                var card2 = card.CloneForCombat(rs.AllocateCardInstanceId());
                card2.DeckVersionId = CardInstance.NoDeckVersion;
                CardCmd.Enchant<Glam>(card2, 1m);
                current.ModifyCard(card2, this);
            }
        }
        return true;
    }
}
