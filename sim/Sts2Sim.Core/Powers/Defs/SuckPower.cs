namespace Sts2Sim.Core.Powers;

public sealed class SuckPower : PowerModel
{
    public const string PowerId = "SUCK_POWER";

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override void AfterAttack(CombatState cs, AttackCommand command)
    {
        if (!ReferenceEquals(command.Attacker, Owner) || command.TargetSide == Owner.Side || !command.DamageProps.IsPoweredAttack())
        {
            return;
        }
        var hits = command.HitResults.ToList();
        int hitsWithUnblocked = 0;
        for (int h = 0; h < hits.Count; h++)
        {
            var current = hits[h];
            var petHits = current.Where(r => CombatStatus.IsPet(cs, r.Receiver)).ToList();
            for (int p = 0; p < petHits.Count; p++)
            {
                var petOwner = CombatStatus.PetOwnerOf(cs, petHits[p].Receiver);
                current.RemoveAll(r => ReferenceEquals(r.Receiver, petOwner));
            }
            if (current.Any(r => r.UnblockedDamage > 0))
            {
                hitsWithUnblocked++;
            }
        }
        command.Results.RemoveAll(r => !StillListed(command, r));
        if (hitsWithUnblocked > 0)
        {
            PowerCmd.Apply<StrengthPower>(cs, Owner, Amount * hitsWithUnblocked, Owner, null);
        }
    }

    private static bool StillListed(AttackCommand command, DamageResult result)
    {
        var hits = command.HitResults;
        for (int h = 0; h < hits.Count; h++)
        {
            if (hits[h].Contains(result))
            {
                return true;
            }
        }
        return false;
    }
}
