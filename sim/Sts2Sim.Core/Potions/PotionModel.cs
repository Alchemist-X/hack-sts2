using System.Diagnostics.CodeAnalysis;
using System.Runtime.ExceptionServices;

namespace Sts2Sim.Core.Potions;

public abstract class PotionModel : AbstractModel, ICanonical
{
    public const int NoOwner = -1;

    private PlayerState? _owner;
    private DynamicVarSet _dynamicVars;

    protected PotionModel()
    {
        _dynamicVars = new DynamicVarSet(CanonicalVars);
    }

    public abstract string Id { get; }

    public abstract PotionRarity Rarity { get; }

    public abstract PotionUsage Usage { get; }

    public abstract TargetType TargetType { get; }

    public string Pool => PotionPools.PoolOf(Id);

    public CanonicalPotion CanonicalInstance => PotionPools.Canonical(Id);

    public PlayerState Owner => _owner ?? throw new InvalidOperationException($"Potion {Id} has no owner");

    public bool HasOwner => _owner != null;

    public DynamicVarSet DynamicVars => _dynamicVars;

    protected virtual IEnumerable<DynamicVar> CanonicalVars => Array.Empty<DynamicVar>();

    public virtual bool CanBeGeneratedInCombat => true;

    public virtual bool PassesCustomUsabilityCheck => true;

    public bool HasBeenRemovedFromState { get; private set; }

    public static bool IsSingleTarget(TargetType targetType)
    {
        switch (targetType)
        {
            case TargetType.Self:
            case TargetType.AnyEnemy:
            case TargetType.AnyPlayer:
            case TargetType.AnyAlly:
            case TargetType.TargetedNoCreature:
                return true;
            default:
                return false;
        }
    }

    public void AttachTo(PlayerState owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        if (_owner != null && !ReferenceEquals(_owner, owner))
        {
            throw new InvalidOperationException("Cannot move potion " + Id + " from one owner to another");
        }
        _owner = owner;
    }

    public void Discard()
    {
        Owner.DiscardPotionInternal(this);
        HasBeenRemovedFromState = true;
    }

    public void RemoveBeforeUse()
    {
        Owner.RemoveUsedPotionInternal(this);
        HasBeenRemovedFromState = true;
    }

    public CreatureState? ManualUseTarget(CreatureState? target)
    {
        if (target == null && IsValidTarget(Owner))
        {
            target = Owner;
        }
        return target;
    }

    public bool IsValidTarget(CreatureState? target)
    {
        if (target == null)
        {
            if (TargetType == TargetType.TargetedNoCreature)
            {
                return true;
            }
            return !IsSingleTarget(TargetType);
        }
        if (!target.IsAlive)
        {
            return false;
        }
        if (TargetType == TargetType.AnyEnemy)
        {
            return target.Side != Owner.Side;
        }
        if (TargetType == TargetType.AnyAlly)
        {
            if (target.Side == Owner.Side)
            {
                return !ReferenceEquals(target, Owner);
            }
            return false;
        }
        if (TargetType == TargetType.AnyPlayer)
        {
            return target.IsPlayer;
        }
        if (TargetType == TargetType.Self)
        {
            return ReferenceEquals(target, Owner);
        }
        return false;
    }

    public bool CanThrowAtAlly(RunState rs)
    {
        ArgumentNullException.ThrowIfNull(rs);
        if (TargetType == TargetType.AnyPlayer && rs.Players.Count > 1)
        {
            return rs.Combat?.IsInProgress ?? false;
        }
        return false;
    }

    public void OnUseWrapper(RunState rs, CreatureState? target)
    {
        ArgumentNullException.ThrowIfNull(rs);
        RemoveBeforeUse();
        var cs = CombatOf(rs);
        Hook.BeforePotionUsed(rs, cs, this, target);
        var effectCombat = BeginCardOrPotionEffect(cs);
        Exception? captured = null;
        try
        {
            OnUse(rs, target);
        }
        catch (Exception ex)
        {
            captured = ex;
        }
        EndCardOrPotionEffect(effectCombat);
        if (captured != null)
        {
            ExceptionDispatchInfo.Capture(captured).Throw();
        }
        if (Owner.IsDead)
        {
            return;
        }
        if (cs != null && IsCurrentLiveCombat(effectCombat))
        {
            cs.History.AppendPotionUsed(cs, this, target);
        }
        Hook.AfterPotionUsed(rs, cs, this, target);
        CheckForEmptyHand(effectCombat, Owner);
    }

    protected virtual void OnUse(RunState rs, CreatureState? target)
    {
    }

    protected static void AssertValidForTargetedPotion([NotNull] CreatureState? target)
    {
        if (target == null)
        {
            throw new ArgumentNullException(nameof(target), "Target must be present for targeted potions.");
        }
    }

    protected static CombatState RequireCombat(RunState rs)
        => rs.Combat ?? throw new NotImplementedException("W2: a potion effect outside combat needs the run-layer command overloads (Models/PotionModel.cs:148, combatState null)");

    private CombatState? CombatOf(RunState rs)
    {
        var cs = rs.Combat;
        return cs != null && CombatStatus.IsAttached(cs, Owner) ? cs : null;
    }

    private static CombatState? BeginCardOrPotionEffect(CombatState? cs)
    {
        if (cs == null)
        {
            return null;
        }
        cs.CardOrPotionEffectDepth++;
        return cs;
    }

    private static void EndCardOrPotionEffect(CombatState? effectCombat)
    {
        if (effectCombat == null)
        {
            return;
        }
        int num = effectCombat.CardOrPotionEffectDepth - 1;
        effectCombat.CardOrPotionEffectDepth = num <= 0 ? 0 : num;
    }

    private static bool IsCurrentLiveCombat([NotNullWhen(true)] CombatState? effectCombat) => effectCombat != null && effectCombat.IsInProgress;

    private static void CheckForEmptyHand(CombatState? effectCombat, PlayerState player)
    {
        if (!IsCurrentLiveCombat(effectCombat))
        {
            return;
        }
        var cs = effectCombat;
        if (cs.IsInProgress && cs.CardOrPotionEffectDepth <= 0 && player.Hand.Count == 0)
        {
            Hook.AfterHandEmptied(cs, player);
        }
    }

    public PotionModel CloneFor(PlayerState? newOwner)
    {
        var clone = (PotionModel)MemberwiseClone();
        clone._owner = _owner is null ? null : newOwner;
        clone._dynamicVars = _dynamicVars.DeepClone();
        clone.CopyMutableReferenceState(this);
        return clone;
    }

    protected virtual void CopyMutableReferenceState(PotionModel source)
    {
    }

    public void WriteCanonical(CanonicalWriter w)
    {
        w.WriteTag(PotionCanonicalTags.Potion);
        w.WriteString(Id);
        w.WriteInt32(_owner?.CombatId ?? NoOwner);
        w.WriteBool(HasBeenRemovedFromState);
        w.WriteTag(PotionCanonicalTags.DynamicVars);
        _dynamicVars.WriteCanonical(w);
        w.WriteTag(PotionCanonicalTags.Extra);
        WriteExtraCanonical(w);
    }

    protected virtual void WriteExtraCanonical(CanonicalWriter w)
    {
    }

    public override string ToString() => Id;
}
