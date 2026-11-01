namespace Sts2Sim.Core.Powers;

public sealed class TrashToTreasurePower : PowerModel
{
    public const string PowerId = "TRASH_TO_TREASURE_POWER";

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override void AfterCardGeneratedForCombat(CombatState cs, CardInstance card, PlayerState? creator)
    {
        if (card.Type != CardType.Status || creator == null || !ReferenceEquals(creator, Owner))
        {
            return;
        }
        using var throwing = cs.Choice.PushContext(ChoiceContextKind.Throwing);
        for (int i = 0; i < Amount; i++)
        {
            var orb = OrbModel.GetRandomOrb(cs.Rng.CombatOrbGeneration);
            OrbCmd.Channel(cs, orb, creator);
        }
    }
}
