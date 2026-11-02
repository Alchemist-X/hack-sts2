namespace Sts2Sim.Core.Relics;

public sealed class DistinguishedCape : RelicModel
{
    public const string RelicId = "DISTINGUISHED_CAPE";

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Ancient;

    public override bool HasUponPickupEffect => true;

    public const string CursesKey = "Curses";

    public const string ApparitionCardId = "APPARITION";

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DynamicVar(CursesKey, 2m),
        new CardsVar(3)
    };

    public override void AfterObtained(RunState rs)
    {
        var availableCurses = CardPools.Curse.GetUnlockedCards(rs.Unlocks, CardPools.MultiplayerConstraintFor(rs))
            .Where(c => c.CanBeGeneratedByModifiers)
            .OrderBy(c => c.Id, StringComparer.Ordinal)
            .ToList();
        for (int i = 0; i < DynamicVars[CursesKey].IntValue; i++)
        {
            var curse = rs.Rng.Niche.NextItem((IReadOnlyList<CardDef>)availableCurses);
            availableCurses.Remove(curse!);
            CardPileCmd.Add(rs, CardFactory.CreateRunCard(rs, curse, Owner), PileType.Deck);
        }
        for (int i = 0; i < DynamicVars.Cards.IntValue; i++)
        {
            CardPileCmd.Add(rs, CardFactory.CreateRunCard(rs, CardRegistry.Get(ApparitionCardId), Owner), PileType.Deck);
        }
    }
}
