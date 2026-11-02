namespace Sts2Sim.Core.Relics;

public sealed class LunarPastry : RelicModel
{
    public const string RelicId = "LUNAR_PASTRY";

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Rare;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new StarsVar(1)
    };

    public override void AfterSideTurnEnd(CombatState cs, CombatSide side, IReadOnlyList<CreatureState> participants)
    {
        if (participants.Any(p => ReferenceEquals(p, Owner)))
        {
            PlayerCmd.GainStars(cs, DynamicVars.Stars.BaseValue, Owner);
        }
    }
}
