namespace Sts2Sim.Core.Relics;

public sealed class BurningSticks : RelicModel
{
    public const string RelicId = "BURNING_STICKS";

    private bool _wasUsedThisCombat;

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Shop;

    private bool WasUsedThisCombat
    {
        get => _wasUsedThisCombat;
        set => _wasUsedThisCombat = value;
    }

    public override void AfterRoomEntered(RunState rs, AbstractRoom room)
    {
        if (room is not CombatRoom)
        {
            return;
        }
        WasUsedThisCombat = false;
        Status = RelicStatus.Active;
    }

    public override void AfterCardExhausted(CombatState cs, CardInstance card, bool causedByEthereal)
    {
        if (!card.HasOwner || !ReferenceEquals(card.Owner, Owner) || WasUsedThisCombat || card.Type != CardType.Skill)
        {
            return;
        }
        var clone = cs.CloneCard(card);
        clone.ExhaustOnNextPlay = false;
        CardPileCmd.AddGeneratedCardToCombat(cs, clone, PileType.Hand, Owner);
        Status = RelicStatus.Normal;
        WasUsedThisCombat = true;
    }

    public override void AfterCombatEnd(RunState rs, CombatRoom? room)
    {
        WasUsedThisCombat = false;
        Status = RelicStatus.Normal;
    }

    protected override void WriteExtraCanonical(CanonicalWriter w)
    {
        w.WriteBool(_wasUsedThisCombat);
    }
}
