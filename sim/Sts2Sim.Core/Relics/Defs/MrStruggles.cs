namespace Sts2Sim.Core.Relics;

public sealed class MrStruggles : RelicModel
{
    public const string RelicId = "MR_STRUGGLES";

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Event;

    public override void AfterPlayerTurnStart(CombatState cs, PlayerState player)
    {
        if (!ReferenceEquals(player, Owner))
        {
            return;
        }
        var hittable = new List<MonsterState>(cs.Monsters.Count);
        cs.GetHittableEnemies(hittable);
        CreatureCmd.Damage(cs, hittable, Owner.TurnNumber, ValueProp.Unpowered, Owner);
    }
}
