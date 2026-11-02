namespace Sts2Sim.Core.Cards;

public sealed class MultiCast : CardDef
{
    public const string CardId = "MULTI_CAST";
    public const int Cost = 0;

    public MultiCast()
        : base(CardId, Cost, CardType.Skill, CardRarity.Rare, TargetType.Self, costsX: true)
    {
    }

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var multiCast = play.Card;
        var owner = multiCast.Owner;
        int evokeCount = multiCast.ResolveEnergyXValue(cs);
        if (multiCast.IsUpgraded)
        {
            evokeCount++;
        }
        for (int i = 0; i < evokeCount; i++)
        {
            OrbCmd.EvokeNext(cs, owner, i == evokeCount - 1);
        }
    }
}
