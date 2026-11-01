namespace Sts2Sim.Core.Cards;

public sealed class Darkness : CardDef
{
    public const string CardId = "DARKNESS";
    public const int Cost = 1;
    public const int TriggerCount = 1;
    public const int UpgradedTriggerCount = 2;

    public Darkness()
        : base(CardId, Cost, CardType.Skill, CardRarity.Uncommon, TargetType.Self)
    {
    }

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var darkness = play.Card;
        var owner = darkness.Owner;
        OrbCmd.Channel<DarkOrb>(cs, owner);
        var enumerable = owner.OrbQueue.Orbs.Where(orb => orb is DarkOrb);
        int triggerCount = !darkness.IsUpgraded ? TriggerCount : UpgradedTriggerCount;
        foreach (var darknessOrb in enumerable)
        {
            for (int i = 0; i < triggerCount; i++)
            {
                OrbCmd.Passive(cs, darknessOrb, null);
            }
        }
    }
}
