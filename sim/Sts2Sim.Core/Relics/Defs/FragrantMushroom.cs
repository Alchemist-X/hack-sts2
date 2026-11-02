namespace Sts2Sim.Core.Relics;

public sealed class FragrantMushroom : RelicModel
{
    public const string RelicId = "FRAGRANT_MUSHROOM";

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Event;

    public override bool HasUponPickupEffect => true;

    public const int HpLoss = 15;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new HpLossVar(15m),
        new CardsVar(2)
    };

    public override void AfterObtained(RunState rs)
    {
        CreatureCmd.Damage(rs, Owner, DynamicVars.HpLoss.BaseValue, ValueProp.Unblockable | ValueProp.Unpowered);
        var chosen = Owner.Deck.Where(c => c?.IsUpgradable ?? false).ToList().StableShuffle(rs.Rng.Niche, CardPileCmd.CompareForShuffle).Take(DynamicVars.Cards.IntValue).ToList();
        foreach (var current in chosen)
        {
            CardCmd.Upgrade(rs.Combat, current);
        }
    }
}
