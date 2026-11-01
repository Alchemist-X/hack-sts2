namespace Sts2Sim.Core.Potions;

public sealed class FoulPotion : PotionModel
{
    public const string PotionId = "FOUL_POTION";

    public const string FakeMerchantEventId = "FAKE_MERCHANT";

    public override string Id => PotionId;

    public override PotionRarity Rarity => PotionRarity.Event;

    public override PotionUsage Usage => PotionUsage.AnyTime;

    public override TargetType TargetType => TargetType.AllEnemies;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DamageVar(12m, ValueProp.Unpowered),
        new GoldVar(100)
    };

    protected override void OnUse(RunState rs, CreatureState? target)
    {
        var cs = rs.Combat;
        if (cs != null && cs.IsInProgress)
        {
            var creatures = new List<CreatureState>();
            cs.GetCreatures(creatures);
            var targets = creatures.Where(c => !CombatStatus.IsPet(cs, c)).ToList();
            var damage = DynamicVars.Damage;
            CreatureCmd.Damage(cs, targets, damage.BaseValue, damage.Props, Owner, null, null);
            return;
        }
        var currentRoom = RoomStack.CurrentRoom(rs);
        if (currentRoom is MerchantRoom)
        {
            PlayerCmd.GainGold(rs, DynamicVars.Gold.BaseValue, Owner);
        }
        if (currentRoom is EventRoom eventRoom && eventRoom.CanonicalEvent.Id == FakeMerchantEventId)
        {
            throw new NotImplementedException("W3 content: FoulPotion thrown in the FAKE_MERCHANT event calls FakeMerchant.FoulPotionThrown (Models.Potions/FoulPotion.cs:67-77); the event is not ported");
        }
    }
}
