namespace Sts2Sim.Core.Relics;

public sealed class GremlinHorn : RelicModel
{
    public const string RelicId = "GREMLIN_HORN";

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Uncommon;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new EnergyVar(1),
        new CardsVar(1)
    };

    public override void AfterDeath(RunState rs, CreatureState creature, bool wasRemovalPrevented)
    {
        if (creature.Side == Owner.Side)
        {
            return;
        }
        var cs = rs.Combat!;
        PlayerCmd.GainEnergy(cs, DynamicVars.Energy.BaseValue, Owner);
        CardPileCmd.Draw(cs, DynamicVars.Cards.BaseValue, Owner);
    }
}
