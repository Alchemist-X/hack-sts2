namespace Sts2Sim.Core.Powers;

public sealed class SpiritOfAshPower : PowerModel
{
    public const string PowerId = "SPIRIT_OF_ASH_POWER";

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override void BeforeCardPlayed(CombatState cs, CardPlay cardPlay)
    {
        if (!ReferenceEquals(CombatStatus.CardOwner(cs, cardPlay.Card), Owner) || !CardPileCmd.HasKeywordInCombat(cs, cardPlay.Card, CardKeyword.Ethereal))
        {
            return;
        }
        CreatureCmd.GainBlock(cs, Owner, Amount, ValueProp.Unpowered, null);
    }
}
