namespace Sts2Sim.Core.Powers;

public sealed class PersonalHivePower : PowerModel
{
    public const string PowerId = "PERSONAL_HIVE_POWER";

    public const string DazedCardId = "DAZED";

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override void AfterDamageReceived(RunState rs, CreatureState target, DamageResult result, ValueProp props, CreatureState? dealer, CardInstance? cardSource)
    {
        if (!ReferenceEquals(target, Owner) || dealer == null || !props.IsPoweredAttack())
        {
            return;
        }
        var cs = rs.Combat!;
        if (dealer is MonsterState { MonsterId: CombatStatus.OstyMonsterId } osty)
        {
            dealer = CombatStatus.PetOwnerOf(cs, osty) ?? throw new InvalidOperationException($"{PowerId}: Osty has no pet owner");
        }
        var player = dealer as PlayerState ?? throw new NullReferenceException($"{PowerId}: dealer.Player is null (Models.Powers/PersonalHivePower.cs:108)");
        for (int i = 0; i < Amount; i++)
        {
            var card = cs.CreateCard(DazedCardId, player);
            CardPileCmd.AddGeneratedCardToCombat(cs, card, PileType.Draw, null, CardPilePosition.Random);
        }
    }
}
