namespace Sts2Sim.Core.Relics;

public sealed class IvoryTile : RelicModel
{
    public const string RelicId = "IVORY_TILE";

    public const string EnergyThresholdKey = "EnergyThreshold";

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Rare;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new EnergyVar(1),
        new EnergyVar(EnergyThresholdKey, 3)
    };

    public override void AfterCardPlayed(CombatState cs, CardPlay cardPlay)
    {
        var card = cardPlay.Card;
        if (!card.HasOwner || !ReferenceEquals(card.Owner, Owner) || cardPlay.Resources.EnergyValue < DynamicVars[EnergyThresholdKey].IntValue)
        {
            return;
        }
        PlayerCmd.GainEnergy(cs, DynamicVars.Energy.BaseValue, Owner);
    }
}
