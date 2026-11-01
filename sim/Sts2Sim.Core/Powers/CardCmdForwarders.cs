namespace Sts2Sim.Core.Powers;

public static class CardCmdForwarders
{
    public static void AutoPlay(CombatState cs, CardInstance card, CreatureState? target, AutoPlayType type = AutoPlayType.Default)
        => throw new NotImplementedException("CardCmd.AutoPlay(cs, card, target, type): loop, Combat/Loop/CardCmd.Play.cs (Commands/CardCmd.cs:1779, state machine :284-560); no CardCmd stub exists at 263810d, integrate forwards this body to it");

    public static void Discard(CombatState cs, IReadOnlyList<CardInstance> cards)
        => throw new NotImplementedException("CardCmd.Discard(cs, cards): loop, Combat/Loop/CardCmd.Play.cs (Commands/CardCmd.cs:1825 -> DiscardAndDraw(cards, 0), :721-960); no CardCmd stub exists at 263810d, integrate forwards this body to it");

    public static void TransformToRandom(CombatState cs, CardInstance card, Rng rng)
        => throw new NotImplementedException("CardCmd.TransformToRandom(cs, card, rng): cards-kernel, Combat/Pipeline/CardCmd.cs (Commands/CardCmd.cs:1949); no CardCmd stub exists at 263810d, integrate forwards this body to it");
}
