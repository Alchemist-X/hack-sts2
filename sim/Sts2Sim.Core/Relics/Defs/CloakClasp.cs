namespace Sts2Sim.Core.Relics;

public sealed class CloakClasp : RelicModel
{
    public const string RelicId = "CLOAK_CLASP";

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Rare;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new BlockVar(1m, ValueProp.Unpowered)
    };

    public override void BeforeSideTurnEnd(CombatState cs, CombatSide side, IReadOnlyList<CreatureState> participants)
    {
        if (!CombatStatus.ContainsById(participants, Owner))
        {
            return;
        }
        var cards = Owner.Hand.Cards;
        if (cards.Count == 0)
        {
            return;
        }
        int num = (int)(cards.Count * DynamicVars.Block.BaseValue);
        CreatureCmd.GainBlock(cs, Owner, num, ValueProp.Unpowered, null);
    }
}
