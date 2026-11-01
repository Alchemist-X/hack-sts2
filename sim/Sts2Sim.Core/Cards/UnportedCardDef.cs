namespace Sts2Sim.Core.Cards;

[SimExclude]
public sealed class UnportedCardDef : CardDef
{
    public UnportedCardDef(CardCatalogEntry entry)
        : base(entry.Id, entry.CanonicalCost, entry.Type, entry.Rarity, entry.TargetType, entry.CostsX)
    {
        Entry = entry;
    }

    public CardCatalogEntry Entry { get; }

    public override CardMultiplayerConstraint MultiplayerConstraint => Entry.MultiplayerConstraint;

    public override bool CanBeGeneratedInCombat => Entry.CanBeGeneratedInCombat;

    public override bool CanBeGeneratedByModifiers => Entry.CanBeGeneratedByModifiers;

    public override int CanonicalStarCost => throw NotPorted(nameof(CanonicalStarCost));

    public override bool HasStarCostX => throw NotPorted(nameof(HasStarCostX));

    public override int MaxUpgradeLevel => throw NotPorted(nameof(MaxUpgradeLevel));

    public override IReadOnlyCollection<CardKeyword> CanonicalKeywords => throw NotPorted(nameof(CanonicalKeywords));

    public override IReadOnlyCollection<CardTag> CanonicalTags => throw NotPorted(nameof(CanonicalTags));

    public override bool GainsBlock => throw NotPorted(nameof(GainsBlock));

    public override bool HasTurnEndInHandEffect => throw NotPorted(nameof(HasTurnEndInHandEffect));

    protected override IEnumerable<DynamicVar> CanonicalVars => throw NotPorted(nameof(CanonicalVars));

    public override void OnPlay(CombatState cs, CardPlay play) => throw NotPorted(nameof(OnPlay));

    private NotImplementedException NotPorted(string member)
        => new($"Card {Id} is not ported: only its pool metadata is known ({member}; cards-kernel catalog, sim/specs/cards-kernel.md section 6)");
}
