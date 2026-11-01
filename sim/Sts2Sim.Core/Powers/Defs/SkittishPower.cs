namespace Sts2Sim.Core.Powers;

public sealed class SkittishPower : PowerModel
{
    public const string PowerId = "SKITTISH_POWER";

    public const decimal MultiplayerScalePerExtraPlayer = 0.5m;

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override bool ShouldScaleInMultiplayer => true;

    public bool HasGainedBlockThisTurn { get; private set; }

    public override void AfterAttack(CombatState cs, AttackCommand command)
    {
        if (HasGainedBlockThisTurn || !command.DamageProps.HasFlag(ValueProp.Move) || command.ModelSource == null)
        {
            return;
        }
        DamageResult? damageResult = null;
        var hits = command.HitResults;
        for (int h = 0; h < hits.Count && damageResult == null; h++)
        {
            var hit = hits[h];
            for (int i = 0; i < hit.Count; i++)
            {
                if (ReferenceEquals(hit[i].Receiver, Owner))
                {
                    damageResult = hit[i];
                    break;
                }
            }
        }
        if (damageResult == null || damageResult.UnblockedDamage == 0)
        {
            return;
        }
        HasGainedBlockThisTurn = true;
        CreatureCmd.GainBlock(cs, Owner, Amount, ValueProp.Unpowered, null);
    }

    public override void AfterSideTurnEnd(CombatState cs, CombatSide side, IReadOnlyList<CreatureState> participants)
    {
        if (side != Owner.Side)
        {
            HasGainedBlockThisTurn = false;
        }
    }

    public override decimal GetScaledAmountForMultiplayer(CombatState cs, CreatureState? applier, decimal amount, CreatureState target, CardInstance? cardSource)
        => amount * (1m + (cs.Run.Players.Count - 1) * MultiplayerScalePerExtraPlayer);

    protected override void WriteExtraCanonical(CanonicalWriter w)
    {
        w.WriteBool(HasGainedBlockThisTurn);
    }
}
