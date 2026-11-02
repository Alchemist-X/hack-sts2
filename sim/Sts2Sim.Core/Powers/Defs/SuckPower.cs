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
        var hits = command.HitResults;
        int num = 0;
        for (int i = 0; i < hits.Count; i++)
        {
            var current = hits[i];
            var petHits = new List<DamageResult>();
            for (int j = 0; j < current.Count; j++)
            {
                if (CombatStatus.IsPet(cs, current[j].Receiver))
                {
                    petHits.Add(current[j]);
                }
            }
            for (int j = 0; j < petHits.Count; j++)
            {
                var petOwner = CombatStatus.PetOwnerOf(cs, petHits[j].Receiver);
                current.RemoveAll(r => ReferenceEquals(r.Receiver, petOwner));
            }
            for (int j = 0; j < current.Count; j++)
            {
                if (current[j].UnblockedDamage > 0)
                {
                    num++;
                    break;
                }
            }
        }
        if (num > 0)
        {
            PowerCmd.Apply<StrengthPower>(cs, Owner, Amount * num, Owner, null);
        }
    }
}
