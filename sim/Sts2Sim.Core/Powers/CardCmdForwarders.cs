namespace Sts2Sim.Core.Powers;

public static class CardCmdForwarders
{
    public static void AutoPlay(CombatState cs, CardInstance card, CreatureState? target, AutoPlayType type = AutoPlayType.Default)
        => CardCmd.AutoPlay(cs, card, target, type);

    public static void Discard(CombatState cs, IReadOnlyList<CardInstance> cards)
        => CardCmd.Discard(cs, cards);

    public static void TransformToRandom(CombatState cs, CardInstance card, Rng rng)
        => CardCmd.TransformToRandom(cs, card, rng);
}
