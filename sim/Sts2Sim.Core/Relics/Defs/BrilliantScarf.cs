namespace Sts2Sim.Core.Relics;

public sealed class BrilliantScarf : RelicModel
{
    public const string RelicId = "BRILLIANT_SCARF";

    private int _cardsPlayedThisTurn;

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Ancient;

    public override int DisplayAmount => CardsPlayedThisTurn;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new CardsVar(5)
    };

    private int CardsPlayedThisTurn
    {
        get => _cardsPlayedThisTurn;
        set
        {
            _cardsPlayedThisTurn = value;
            UpdateDisplay();
        }
    }

    private void UpdateDisplay()
    {
        int intValue = DynamicVars.Cards.IntValue;
        Status = CardsPlayedThisTurn == intValue - 1 ? RelicStatus.Active : RelicStatus.Normal;
    }

    public override bool TryModifyEnergyCostInCombatLate(CombatState cs, CardInstance card, decimal originalCost, out decimal modifiedCost)
    {
        modifiedCost = originalCost;
        if (!ShouldModifyCost(cs, card))
        {
            return false;
        }
        modifiedCost = default;
        return true;
    }

    public override bool TryModifyStarCost(CombatState cs, CardInstance card, decimal originalCost, out decimal modifiedCost)
    {
        modifiedCost = originalCost;
        if (!ShouldModifyCost(cs, card))
        {
            return false;
        }
        modifiedCost = default;
        return true;
    }

    public override void BeforeSideTurnStart(CombatState cs, CombatSide side, IReadOnlyList<CreatureState> participants)
    {
        if (!CombatStatus.ContainsById(participants, Owner))
        {
            return;
        }
        CardsPlayedThisTurn = 0;
    }

    public override void AfterCardPlayed(CombatState cs, CardPlay cardPlay)
    {
        if (!cs.IsInProgress)
        {
            return;
        }
        if (cardPlay.IsAutoPlay)
        {
            return;
        }
        var card = cardPlay.Card;
        if (!card.HasOwner || !ReferenceEquals(card.Owner, Owner))
        {
            return;
        }
        CardsPlayedThisTurn++;
    }

    public override void AfterCombatEnd(RunState rs, CombatRoom? room)
    {
        CardsPlayedThisTurn = 0;
    }

    private bool ShouldModifyCost(CombatState cs, CardInstance card)
    {
        if (!cs.IsInProgress)
        {
            return false;
        }
        if (!card.HasOwner || !ReferenceEquals(card.Owner, Owner))
        {
            return false;
        }
        if (CardsPlayedThisTurn != DynamicVars.Cards.BaseValue - 1m)
        {
            return false;
        }
        var pileType = card.GetPileType(cs);
        if (pileType != PileType.Hand && pileType != PileType.Play)
        {
            return false;
        }
        return true;
    }

    protected override void WriteExtraCanonical(CanonicalWriter w)
    {
        w.WriteInt32(_cardsPlayedThisTurn);
    }
}
