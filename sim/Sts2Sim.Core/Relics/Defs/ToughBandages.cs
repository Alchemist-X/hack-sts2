namespace Sts2Sim.Core.Relics;

public sealed class ToughBandages : RelicModel
{
    public const string RelicId = "TOUGH_BANDAGES";

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Rare;

    protected override IEnumerable<DynamicVar> CanonicalVars => new[]
    {
        new BlockVar(3m, ValueProp.Unpowered)
    };

    public override void AfterCardDiscarded(CombatState cs, CardInstance card)
    {
        if (!ReferenceEquals(CombatStatus.CardOwner(cs, card), Owner) || Owner.Side != cs.CurrentSide)
        {
            return;
        }
        CreatureCmd.GainBlock(cs, Owner, DynamicVars.Block, null);
    }
}
