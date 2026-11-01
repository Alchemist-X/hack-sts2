namespace Sts2Sim.Core.Powers;

public sealed class SurroundedPower : PowerModel
{
    public enum Direction
    {
        Right,
        Left
    }

    public const string PowerId = "SURROUNDED_POWER";

    public const decimal BackAttackMultiplier = 1.5m;

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Debuff;

    public override PowerStackType StackType => PowerStackType.Single;

    public Direction Facing { get; private set; }

    public override decimal ModifyDamageMultiplicative(RunState rs, CreatureState? target, decimal amount, ValueProp props, CreatureState? dealer, CardInstance? cardSource, CardPlay? cardPlay)
    {
        if (dealer == null)
        {
            return 1m;
        }
        if (!ReferenceEquals(target, Owner))
        {
            return 1m;
        }
        switch (Facing)
        {
            case Direction.Right:
                if (!dealer.HasPower<BackAttackLeftPower>())
                {
                    return 1m;
                }
                break;
            case Direction.Left:
                if (!dealer.HasPower<BackAttackRightPower>())
                {
                    return 1m;
                }
                break;
        }
        return BackAttackMultiplier;
    }

    public override void BeforeCardPlayed(CombatState cs, CardPlay cardPlay)
    {
        if (cardPlay.Target != null && ReferenceEquals(CombatStatus.CardOwner(cs, cardPlay.Card), Owner as PlayerState))
        {
            UpdateDirection(cardPlay.Target);
        }
    }

    public override void BeforePotionUsed(RunState rs, PotionModel potion, CreatureState? target)
    {
        if (rs.Combat is { } cs && CombatStatus.IsInProgress(cs) && target != null && potion.HasOwner && ReferenceEquals(potion.Owner, Owner as PlayerState))
        {
            UpdateDirection(target);
        }
    }

    public override void AfterDeath(RunState rs, CreatureState creature, bool wasRemovalPrevented)
    {
        if (wasRemovalPrevented || creature.Side == Owner.Side)
        {
            return;
        }
        var hittable = new List<MonsterState>();
        rs.Combat!.GetHittableEnemies(hittable);
        if (hittable.Count != 0 && (hittable.All(e => e.HasPower<BackAttackLeftPower>()) || hittable.All(e => e.HasPower<BackAttackRightPower>())))
        {
            UpdateDirection(hittable[0]);
        }
    }

    private void UpdateDirection(CreatureState target)
    {
        var facing = Facing;
        if (facing != Direction.Right)
        {
            if (facing == Direction.Left && target.HasPower<BackAttackRightPower>())
            {
                FaceDirection(Direction.Right);
            }
        }
        else if (target.HasPower<BackAttackLeftPower>())
        {
            FaceDirection(Direction.Left);
        }
    }

    private void FaceDirection(Direction direction)
    {
        Facing = direction;
    }

    protected override void WriteExtraCanonical(CanonicalWriter w)
    {
        w.WriteInt32((int)Facing);
    }
}
