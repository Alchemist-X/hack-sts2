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
        if (cardPlay.Target != null && ReferenceEquals(CombatStatus.CardOwnerCreature(cs, cardPlay.Card), Owner))
        {
            UpdateDirection(cardPlay.Target);
        }
    }

    public override void BeforePotionUsed(RunState rs, PotionModel potion, CreatureState? target)
    {
        var cs = rs.Combat;
        if (cs != null && CombatStatus.IsInProgress(cs) && target != null && potion.HasOwner && ReferenceEquals(potion.Owner, Owner))
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
        var cs = rs.Combat!;
        var hittable = new List<MonsterState>();
        cs.GetHittableEnemies(hittable);
        if (hittable.Count == 0)
        {
            return;
        }
        if (hittable.TrueForAll(static e => e.HasPower<BackAttackLeftPower>()) || hittable.TrueForAll(static e => e.HasPower<BackAttackRightPower>()))
        {
            UpdateDirection(hittable[0]);
        }
    }

    private void UpdateDirection(CreatureState target)
    {
        if (Facing != Direction.Right)
        {
            if (Facing == Direction.Left && target.HasPower<BackAttackRightPower>())
            {
                Facing = Direction.Right;
            }
        }
        else if (target.HasPower<BackAttackLeftPower>())
        {
            Facing = Direction.Left;
        }
    }

    protected override void WriteExtraCanonical(CanonicalWriter w)
    {
        w.WriteInt32((int)Facing);
    }
}
