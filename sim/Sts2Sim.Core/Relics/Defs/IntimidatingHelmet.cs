namespace Sts2Sim.Core.Relics;

public sealed class IntimidatingHelmet : RelicModel
{
    public const string RelicId = "INTIMIDATING_HELMET";

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Rare;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new BlockVar(4m, ValueProp.Unpowered),
        new EnergyVar(2)
    };

    public override void BeforeCardPlayed(CombatState cs, CardPlay cardPlay)
    {
        var card = cardPlay.Card;
        if (!card.HasOwner || !ReferenceEquals(card.Owner, Owner) || cardPlay.Resources.EnergyValue < DynamicVars.Energy.IntValue)
        {
            return;
        }
        CreatureCmd.GainBlock(cs, Owner, DynamicVars.Block, null);
    }
}
