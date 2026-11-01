namespace Sts2Sim.Core.Powers;

public sealed class PainfulStabsPower : PowerModel
{
    public const string PowerId = "PAINFUL_STABS_POWER";

    public const string WoundCardId = "WOUND";

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override bool ShouldPowerBeRemovedAfterOwnerDeath() => false;

    public override bool ShouldCreatureBeRemovedFromCombatAfterDeath(CombatState cs, CreatureState creature)
        => !ReferenceEquals(creature, Owner);

    public override void AfterAttack(CombatState cs, AttackCommand command)
    {
        if (!ReferenceEquals(command.Attacker, Owner) || command.TargetSide == Owner.Side || !command.DamageProps.IsPoweredAttack())
        {
            return;
        }
        var results = new List<DamageResult>();
        var hits = command.HitResults;
        for (int h = 0; h < hits.Count; h++)
        {
            results.AddRange(hits[h]);
        }
        bool anyUnblocked = false;
        for (int i = 0; i < results.Count; i++)
        {
            if (results[i].UnblockedDamage > 0)
            {
                anyUnblocked = true;
                break;
            }
        }
        if (!anyUnblocked)
        {
            return;
        }
        var receivers = new List<CreatureState>();
        var counts = new List<int>();
        for (int i = 0; i < results.Count; i++)
        {
            var result = results[i];
            if (!result.Receiver.IsPlayer)
            {
                continue;
            }
            int index = receivers.IndexOf(result.Receiver);
            if (index < 0)
            {
                receivers.Add(result.Receiver);
                counts.Add(0);
                index = receivers.Count - 1;
            }
            if (result.UnblockedDamage > 0)
            {
                counts[index]++;
            }
        }
        for (int i = 0; i < receivers.Count; i++)
        {
            CardPileCmd.AddToCombatAndPreview(cs, receivers[i], WoundCardId, PileType.Discard, Amount * counts[i], null);
        }
    }
}
