namespace Sts2Sim.Core.Relics;

public sealed class StoneCracker : RelicModel
{
    public const string RelicId = "STONE_CRACKER";

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Uncommon;

    protected override IEnumerable<DynamicVar> CanonicalVars => new[]
    {
        new CardsVar(2)
    };

    public override void AfterRoomEntered(RunState rs, AbstractRoom room)
    {
        if (room is not CombatRoom)
        {
            return;
        }
        var cs = rs.Combat ?? throw new InvalidOperationException(RelicId + ".AfterRoomEntered: combat room without an attached combat");
        var cards = Owner.DrawPile.Cards
            .Where(c => c.IsUpgradable)
            .ToList()
            .StableShuffle(cs.Rng.CombatCardSelection, CardPileCmd.CompareForShuffle)
            .Take(DynamicVars.Cards.IntValue)
            .ToList();
        CardCmd.Upgrade(cs, cards);
    }
}
