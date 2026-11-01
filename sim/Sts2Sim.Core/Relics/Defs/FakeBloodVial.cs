namespace Sts2Sim.Core.Relics;

public sealed class FakeBloodVial : RelicModel
{
    public const string RelicId = "FAKE_BLOOD_VIAL";

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Event;

    public override int MerchantCost => 50;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new HealVar(1m)
    };

    public override void AfterPlayerTurnStartLate(CombatState cs, PlayerState player)
    {
        if (ReferenceEquals(player, Owner) && Owner.TurnNumber <= 1)
        {
            CreatureCmd.Heal(cs, Owner, DynamicVars.Heal.IntValue);
        }
    }
}
