namespace Sts2Sim.Core.Relics;

public sealed class Bellows : RelicModel
{
    public const string RelicId = "BELLOWS";

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Rare;

    public override void AfterPlayerTurnStart(CombatState cs, PlayerState player)
    {
        if (!ReferenceEquals(player, Owner))
        {
            return;
        }
        if (Owner.TurnNumber > 1)
        {
            return;
        }
        CardCmd.Upgrade(cs, Owner.Hand.Cards.ToList());
    }
}
