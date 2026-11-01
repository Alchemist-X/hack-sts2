namespace Sts2Sim.Core.Relics;

public sealed class ThrowingAxe : RelicModel
{
    public const string RelicId = "THROWING_AXE";

    private bool _usedThisCombat;

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Ancient;

    private bool UsedThisCombat
    {
        get => _usedThisCombat;
        set => _usedThisCombat = value;
    }

    public override void AfterRoomEntered(RunState rs, AbstractRoom room)
    {
        if (room is not CombatRoom)
        {
            return;
        }
        UsedThisCombat = false;
        Status = RelicStatus.Active;
    }

    public override int ModifyCardPlayCount(CombatState cs, CardInstance card, CreatureState? target, int playCount)
    {
        if (UsedThisCombat)
        {
            return playCount;
        }
        if (!ReferenceEquals(card.Owner, Owner))
        {
            return playCount;
        }
        return playCount + 1;
    }

    public override void AfterModifyingCardPlayCount(CombatState cs, CardInstance card)
    {
        UsedThisCombat = true;
        Status = RelicStatus.Normal;
    }

    public override void AfterCombatEnd(RunState rs, CombatRoom? room)
    {
        UsedThisCombat = false;
        Status = RelicStatus.Normal;
    }

    protected override void WriteExtraCanonical(CanonicalWriter w)
    {
        w.WriteBool(_usedThisCombat);
    }
}
