namespace Sts2Sim.Core.Powers;

public sealed class PersonalHivePower : PowerModel
{
    public const string PowerId = "PERSONAL_HIVE_POWER";

    public const string StatusCardId = "DAZED";

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
        if (dealer is MonsterState monster && monster.Def is Osty)
        {
            dealer = CombatStatus.PetOwnerOf(cs, dealer);
        }
        if (dealer is not PlayerState player)
        {
            return;
        }
        int count = Amount;
        for (int i = 0; i < count; i++)
        {
            var card = cs.CreateCard(StatusCardId, player);
            CardPileCmd.AddGeneratedCardToCombat(cs, card, PileType.Draw, null, CardPilePosition.Random);
        }
    }
}
