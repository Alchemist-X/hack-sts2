namespace Sts2Sim.Core.Relics;

public sealed class ChosenCheese : RelicModel
{
    public const string RelicId = "CHOSEN_CHEESE";

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Event;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new MaxHpVar(1m)
    };

    public override void AfterCombatEnd(RunState rs, CombatRoom? room)
    {
        var cs = rs.Combat ?? throw new InvalidOperationException("ChosenCheese.AfterCombatEnd: no combat on the run");
        CreatureCmd.GainMaxHp(cs, Owner, DynamicVars.MaxHp.BaseValue);
    }
}
