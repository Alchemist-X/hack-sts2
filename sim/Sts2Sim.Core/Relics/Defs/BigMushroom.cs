namespace Sts2Sim.Core.Relics;

public sealed class BigMushroom : RelicModel
{
    public const string RelicId = "BIG_MUSHROOM";

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Event;

    public override bool HasUponPickupEffect => true;

    public override decimal ModifyHandDraw(CombatState cs, PlayerState player, decimal count)
    {
        if (!ReferenceEquals(player, Owner))
        {
            return count;
        }
        if (Owner.TurnNumber != 1)
        {
            return count;
        }
        return count - DynamicVars.Cards.IntValue;
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new MaxHpVar(20m),
        new CardsVar(2)
    };

    public override void AfterObtained(RunState rs)
    {
        CreatureCmd.GainMaxHp(rs, Owner, DynamicVars.MaxHp.BaseValue);
    }
}
