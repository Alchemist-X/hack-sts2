namespace Sts2Sim.Core.Relics;

public sealed class BookRepairKnife : RelicModel
{
    public const string RelicId = "BOOK_REPAIR_KNIFE";

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Uncommon;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new HealVar(3m)
    };

    public override void AfterDiedToDoom(CombatState cs, IReadOnlyList<CreatureState> creatures)
    {
        int num = creatures.Count(c => !ReferenceEquals(c, Owner) && c.Powers.All(p => p.ShouldOwnerDeathTriggerFatal()));
        if (num == 0)
        {
            return;
        }
        CreatureCmd.Heal(cs, Owner, DynamicVars.Heal.BaseValue * num);
    }
}
