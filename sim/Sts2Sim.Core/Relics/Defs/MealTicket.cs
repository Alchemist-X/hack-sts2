namespace Sts2Sim.Core.Relics;

public sealed class MealTicket : RelicModel
{
    public const string RelicId = "MEAL_TICKET";

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Common;

    protected override IEnumerable<DynamicVar> CanonicalVars => new[]
    {
        new HealVar(15m)
    };

    public override bool IsAllowed(RunState rs) => IsBeforeAct3TreasureChest(rs);

    public override void AfterRoomEntered(RunState rs, AbstractRoom room)
    {
        if (!Owner.IsDead && room is MerchantRoom)
        {
            CreatureCmd.Heal(rs, Owner, DynamicVars.Heal.BaseValue);
        }
    }
}
