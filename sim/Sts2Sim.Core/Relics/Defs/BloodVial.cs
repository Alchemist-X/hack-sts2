namespace Sts2Sim.Core.Relics;

public sealed class BloodVial : RelicModel
{
    public const string RelicId = "BLOOD_VIAL";

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Common;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new HealVar(2m)
    };

    public override void AfterPlayerTurnStartLate(CombatState cs, PlayerState player)
    {
        if (ReferenceEquals(player, Owner) && Owner.TurnNumber <= 1)
        {
            CreatureCmd.Heal(cs, Owner, DynamicVars.Heal.IntValue);
        }
    }
}
