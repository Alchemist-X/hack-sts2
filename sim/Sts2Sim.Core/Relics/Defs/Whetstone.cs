namespace Sts2Sim.Core.Relics;

public sealed class Whetstone : RelicModel
{
    public const string RelicId = "WHETSTONE";

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Common;

    public override bool HasUponPickupEffect => true;

    protected override IEnumerable<DynamicVar> CanonicalVars => new[]
    {
        new DynamicVar(DynamicVarSet.CardsName, 2m)
    };

    public override void AfterObtained(RunState rs)
    {
        var candidates = new List<CardInstance>();
        var deck = Owner.Deck;
        for (int i = 0; i < deck.Count; i++)
        {
            var c = deck[i];
            if (c != null && c.Type == CardType.Attack && c.IsUpgradable)
            {
                candidates.Add(c);
            }
        }
        var chosen = candidates.StableShuffle(rs.Rng.Niche, CardPileCmd.CompareForShuffle).Take(DynamicVars.Cards.IntValue);
        foreach (var current in chosen)
        {
            PendingKernelCalls.CardCmdUpgrade(rs.Combat, current);
        }
    }
}
