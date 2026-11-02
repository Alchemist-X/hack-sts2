namespace Sts2Sim.Core.Relics;

public sealed class DivineDestiny : RelicModel
{
    public const string RelicId = "DIVINE_DESTINY";

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Starter;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new StarsVar(7)
    };

    public override void AfterSideTurnStart(CombatState cs, CombatSide side, IReadOnlyList<CreatureState> participants)
    {
        if (participants.Any(p => ReferenceEquals(p, Owner)) && Owner.TurnNumber <= 1)
        {
            PlayerCmd.GainStars(cs, DynamicVars.Stars.BaseValue, Owner);
        }
    }
}
