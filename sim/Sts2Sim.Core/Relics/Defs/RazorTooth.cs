namespace Sts2Sim.Core.Relics;

public sealed class RazorTooth : RelicModel
{
    public const string RelicId = "RAZOR_TOOTH";

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Rare;

    public override void AfterCardPlayed(CombatState cs, CardPlay cardPlay)
    {
        if (!ReferenceEquals(cardPlay.Card.Owner, Owner))
        {
            return;
        }
        var type = cardPlay.Card.Type;
        if ((uint)(type - 1) > 1u)
        {
            return;
        }
        if (!cardPlay.Card.IsUpgradable)
        {
            return;
        }
        CardCmd.Upgrade(cs, cardPlay.Card);
    }
}
