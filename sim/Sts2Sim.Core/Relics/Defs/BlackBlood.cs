namespace Sts2Sim.Core.Relics;

public sealed class BlackBlood : RelicModel
{
    public const string RelicId = "BLACK_BLOOD";

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Starter;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new HealVar(12m)
    };

    public override void AfterCombatVictory(RunState rs, CombatRoom? room)
    {
        if (Owner.IsDead)
        {
            return;
        }
        var cs = rs.Combat ?? throw new InvalidOperationException("BlackBlood.AfterCombatVictory: no combat on the run");
        CreatureCmd.Heal(cs, Owner, DynamicVars.Heal.BaseValue);
    }
}
