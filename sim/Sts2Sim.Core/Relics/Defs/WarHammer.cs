namespace Sts2Sim.Core.Relics;

public sealed class WarHammer : RelicModel
{
    public const string RelicId = "WAR_HAMMER";

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Ancient;

    protected override IEnumerable<DynamicVar> CanonicalVars => new[]
    {
        new CardsVar(4)
    };

    public override void AfterCombatVictory(RunState rs, CombatRoom? room)
    {
        if (room == null || room.RoomType != RoomType.Elite)
        {
            return;
        }
        var candidates = Owner.Deck.Where(c => c.IsUpgradable).ToList();
        var chosen = candidates.StableShuffle(rs.Rng.Niche, CardPileCmd.CompareForShuffle).Take(DynamicVars.Cards.IntValue);
        foreach (var current in chosen)
        {
            CardCmd.Upgrade(rs.Combat, current);
        }
    }
}
