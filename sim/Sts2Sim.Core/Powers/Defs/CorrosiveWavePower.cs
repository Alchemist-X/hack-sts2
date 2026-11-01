namespace Sts2Sim.Core.Powers;

public sealed class CorrosiveWavePower : PowerModel
{
    public const string PowerId = "CORROSIVE_WAVE_POWER";

    public const string PoisonPowerId = "POISON_POWER";

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override void AfterCardDrawn(CombatState cs, CardInstance card, bool fromHandDraw)
    {
        if (!ReferenceEquals(CombatStatus.CardOwnerCreature(cs, card), Owner))
        {
            return;
        }
        var targets = CountdownPower.HittableEnemies(cs);
        for (int i = 0; i < targets.Count; i++)
        {
            PowerCmd.Apply(cs, PowerRegistry.Create(PoisonPowerId), targets[i], Amount, Owner, null);
        }
    }

    public override void AfterSideTurnEnd(CombatState cs, CombatSide side, IReadOnlyList<CreatureState> participants)
    {
        if (CombatStatus.ContainsById(participants, Owner))
        {
            PowerCmd.Remove(cs, this);
        }
    }
}
