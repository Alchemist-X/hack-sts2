namespace Sts2Sim.Core.Relics;

public sealed class FishingRod : RelicModel
{
    public const string RelicId = "FISHING_ROD";

    public const string CombatsKey = "Combats";

    private int _combatsSeen;

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Ancient;

    public override int DisplayAmount => CombatsSeen % DynamicVars[CombatsKey].IntValue;

    public override bool ShowCounter => true;

    public int CombatsSeen
    {
        get => _combatsSeen;
        set => _combatsSeen = value;
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DynamicVar(CombatsKey, 3m)
    };

    public override void AfterCombatEnd(RunState rs, CombatRoom? room)
    {
        if (room == null || room.Encounter.RoomType != RoomType.Monster)
        {
            return;
        }
        CombatsSeen++;
        if (CombatsSeen % DynamicVars[CombatsKey].IntValue == 0)
        {
            var items = Owner.Deck.Where(c => c.IsUpgradable);
            var cardModel = rs.Rng.Niche.NextItem(items);
            if (cardModel != null)
            {
                CardCmd.Upgrade(rs.Combat, cardModel);
            }
        }
    }

    protected override void WriteExtraCanonical(CanonicalWriter w)
    {
        w.WriteInt32(_combatsSeen);
    }
}
