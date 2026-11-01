namespace Sts2Sim.Core.Powers;

public sealed class PainfulStabsPower : PowerModel
{
    public const string PowerId = "PAINFUL_STABS_POWER";
    public const string WoundCardId = "WOUND";

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override bool ShouldPowerBeRemovedAfterOwnerDeath() => false;

    public override bool ShouldCreatureBeRemovedFromCombatAfterDeath(CombatState cs, CreatureState creature) => !ReferenceEquals(creature, Owner);

    public override void AfterAttack(CombatState cs, AttackCommand command)
    {
        if (!ReferenceEquals(command.Attacker, Owner) || command.TargetSide == Owner.Side || !command.DamageProps.IsPoweredAttack())
        {
            return;
        }
        var results = command.Results;
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
            var r = results[i];
            if (!r.Receiver.IsPlayer)
            {
                continue;
            }
            int idx = receivers.IndexOf(r.Receiver);
            if (idx < 0)
            {
                receivers.Add(r.Receiver);
                counts.Add(0);
                idx = receivers.Count - 1;
            }
            if (r.UnblockedDamage > 0)
            {
                counts[idx]++;
            }
        }
        for (int i = 0; i < receivers.Count; i++)
        {
            CardPileCmd.AddToCombatAndPreview(cs, receivers[i], WoundCardId, PileType.Discard, Amount * counts[i], null);
        }
    }
}
