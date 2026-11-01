namespace Sts2Sim.Core.Relics;

public sealed class RoyalPoison : RelicModel
{
    public const string RelicId = "ROYAL_POISON";

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Event;

    protected override IEnumerable<DynamicVar> CanonicalVars => new[]
    {
        new DamageVar(4m, ValueProp.Unblockable | ValueProp.Unpowered)
    };

    public override void AfterPlayerTurnStart(CombatState cs, PlayerState player)
    {
        if (!ReferenceEquals(player, Owner) || Owner.TurnNumber > 1)
        {
            return;
        }
        CreatureCmd.Damage(cs, Owner, DynamicVars.Damage, (CreatureState?)null, null, null);
    }
}
