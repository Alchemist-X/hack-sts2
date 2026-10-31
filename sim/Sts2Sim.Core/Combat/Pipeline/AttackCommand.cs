namespace Sts2Sim.Core.Combat.Pipeline;

public static class DamageCmd
{
    public static AttackCommand Attack(CombatState cs, decimal damagePerHit) => new(cs, damagePerHit);
}

public sealed class AttackCommand
{
    private readonly List<List<DamageResult>> _hitResults = new(1);
    private AttackSourceType _sourceType;
    private bool _targetsOpponents;
    private Action<CombatState, AttackCommand>? _beforeDamage;

    private enum AttackSourceType : byte
    {
        None,
        Card,
        Monster
    }

    public AttackCommand(CombatState cs, decimal damagePerHit)
    {
        Combat = cs;
        DamagePerHit = damagePerHit;
        HitCount = 1;
        DamageProps = ValueProp.Move;
        AllowDuplicateRandomTargets = true;
    }

    public CombatState Combat { get; }

    public decimal DamagePerHit { get; }

    public int HitCount { get; private set; }

    public ValueProp DamageProps { get; private set; }

    public CreatureState? Attacker { get; private set; }

    public CardInstance? ModelSource { get; private set; }

    public CardPlay? CardPlay { get; private set; }

    public CreatureState? SingleTarget { get; private set; }

    public CombatSide TargetSide { get; private set; }

    public bool TargetsAllOpponents => _targetsOpponents && !TargetsRandomOpponents;

    public bool TargetsRandomOpponents { get; private set; }

    public bool AllowDuplicateRandomTargets { get; private set; }

    public bool IsSingleTargeted => SingleTarget != null;

    public bool IsMultiTargeted => _targetsOpponents;

    public int HitsCompleted { get; private set; }

    public List<DamageResult> Results { get; } = new(2);

    public IReadOnlyList<List<DamageResult>> HitResults => _hitResults;

    public AttackCommand FromCard(CardInstance card, CardPlay? cardPlay)
    {
        if (Attacker != null)
        {
            throw new InvalidOperationException("Attacker has already been set.");
        }
        if (ModelSource != null)
        {
            throw new InvalidOperationException("ModelSource has already been set.");
        }
        if (cardPlay != null && CardPlay != null)
        {
            throw new InvalidOperationException("CardPlay has already been set.");
        }
        Attacker = CombatStatus.CardOwnerCreature(Combat, card);
        ModelSource = card;
        CardPlay = cardPlay;
        _sourceType = AttackSourceType.Card;
        return this;
    }

    public AttackCommand FromMonster(MonsterState monster)
    {
        if (Attacker != null)
        {
            throw new InvalidOperationException("Attacker has already been set.");
        }
        Attacker = monster;
        _sourceType = AttackSourceType.Monster;
        return TargetingAllOpponents();
    }

    public AttackCommand Targeting(CreatureState target)
    {
        if (SingleTarget != null)
        {
            throw new InvalidOperationException("Targets already set.");
        }
        if (_targetsOpponents)
        {
            throw new InvalidOperationException("Already set to target opponents of attacker");
        }
        SingleTarget = target;
        TargetSide = target.Side;
        return this;
    }

    public AttackCommand TargetingAllOpponents()
    {
        if (SingleTarget != null)
        {
            throw new InvalidOperationException("Targets already set.");
        }
        if (_targetsOpponents)
        {
            throw new InvalidOperationException("Already set to target opponents of attacker");
        }
        if (Attacker == null)
        {
            throw new InvalidOperationException("We require an attacker to be able to grab its opponents");
        }
        _targetsOpponents = true;
        TargetSide = Attacker.Side == CombatSide.Enemy ? CombatSide.Player : CombatSide.Enemy;
        return this;
    }

    public AttackCommand TargetingRandomOpponents(bool allowDuplicates = true)
    {
        if (SingleTarget != null)
        {
            throw new InvalidOperationException("Targets already set.");
        }
        if (_targetsOpponents)
        {
            throw new InvalidOperationException("Already set to target opponents of attacker");
        }
        if (Attacker == null)
        {
            throw new InvalidOperationException("We require an attacker to be able to grab its opponents");
        }
        _targetsOpponents = true;
        TargetsRandomOpponents = true;
        AllowDuplicateRandomTargets = allowDuplicates;
        return this;
    }

    public AttackCommand Unpowered()
    {
        DamageProps |= ValueProp.Unpowered;
        return this;
    }

    public AttackCommand WithHitCount(int hitCount)
    {
        HitCount = hitCount;
        return this;
    }

    public AttackCommand BeforeDamage(Action<CombatState, AttackCommand> beforeDamage)
    {
        _beforeDamage = beforeDamage;
        return this;
    }

    public void IncrementHitsInternal()
    {
        HitCount++;
    }

    public void AddResultsInternal(IReadOnlyList<DamageResult> results)
    {
        var hit = new List<DamageResult>(results.Count);
        for (int i = 0; i < results.Count; i++)
        {
            hit.Add(results[i]);
        }
        _hitResults.Add(hit);
        Results.AddRange(hit);
    }

    private void GetPossibleTargets(List<CreatureState> buffer)
    {
        buffer.Clear();
        if (SingleTarget != null)
        {
            buffer.Add(SingleTarget);
            return;
        }
        if (_targetsOpponents)
        {
            if (_sourceType == AttackSourceType.Monster)
            {
                buffer.Add(Combat.Player);
                return;
            }
            if (Attacker == null)
            {
                throw new InvalidOperationException("We require an attacker to be able to grab its opponents");
            }
            CombatStatus.GetOpponentsOf(Combat, Attacker, buffer);
            return;
        }
        throw new InvalidOperationException("No targets set, a Targeting method must be called before Execute");
    }

    private bool WasHitAlready(CreatureState creature)
    {
        for (int i = 0; i < _hitResults.Count; i++)
        {
            var hit = _hitResults[i];
            for (int j = 0; j < hit.Count; j++)
            {
                if (ReferenceEquals(hit[j].Receiver, creature))
                {
                    return true;
                }
            }
        }
        return false;
    }

    public AttackCommand Execute()
    {
        var cs = Combat;
        if (Attacker == null)
        {
            throw new InvalidOperationException("No attacker set.");
        }
        if (CombatStatus.IsOverOrEnding(cs))
        {
            return this;
        }
        if (!CombatStatus.IsAttached(cs, Attacker))
        {
            throw new InvalidOperationException("No combat state even though combat is not over.");
        }
        if (Attacker.IsDead)
        {
            return this;
        }
        if (!IsSingleTargeted && !IsMultiTargeted)
        {
            throw new InvalidOperationException("No targets set.");
        }
        Hook.BeforeAttack(cs, this);
        int attackCount = Hook.ModifyAttackHitCount(cs, this, HitCount);
        var validTargets = new List<CreatureState>(4);
        for (int i = 0; i < attackCount; i++)
        {
            if (Attacker.IsDead)
            {
                break;
            }
            GetPossibleTargets(validTargets);
            validTargets.RemoveAll(static c => !c.IsAlive);
            if (validTargets.Count == 0)
            {
                break;
            }
            CreatureState? singleTarget;
            if (!TargetsRandomOpponents)
            {
                singleTarget = validTargets.Count != 1 ? null : validTargets[0];
            }
            else
            {
                if (!AllowDuplicateRandomTargets)
                {
                    validTargets.RemoveAll(WasHitAlready);
                    if (validTargets.Count == 0)
                    {
                        throw new InvalidOperationException("No valid targets for attack with duplicates disallowed.");
                    }
                }
                if (Attacker is not PlayerState)
                {
                    throw new InvalidOperationException("Random targeting reads (Attacker.Player ?? Attacker.PetOwner).RunState.Rng.CombatTargets, which has no player for a monster attacker.");
                }
                singleTarget = cs.Rng.CombatTargets.NextItem((IReadOnlyList<CreatureState>)validTargets);
            }
            _beforeDamage?.Invoke(cs, this);
            decimal amount = DamagePerHit;
            IReadOnlyList<DamageResult> results = singleTarget == null
                ? CreatureCmd.Damage(cs, validTargets, amount, DamageProps, Attacker, ModelSource, CardPlay)
                : CreatureCmd.Damage(cs, singleTarget, amount, DamageProps, Attacker, ModelSource, CardPlay);
            AddResultsInternal(results);
            HitsCompleted++;
        }
        Hook.AfterAttack(cs, this);
        return this;
    }
}
