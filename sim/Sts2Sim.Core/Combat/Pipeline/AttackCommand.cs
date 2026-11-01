namespace Sts2Sim.Core.Combat.Pipeline;

public static class DamageCmd
{
    public static AttackCommand Attack(CombatState cs, decimal damagePerHit) => new(cs, damagePerHit);

    public static AttackCommand Attack(CombatState cs, Func<CombatState, CreatureState?, decimal> calculatedDamage) => new(cs, calculatedDamage);
}

public sealed class AttackCommand
{
    public const decimal CalculatedDamagePerHit = -1m;

    private readonly List<List<DamageResult>> _hitResults = new(1);
    private readonly Func<CombatState, CreatureState?, decimal>? _calculatedDamage;
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

    public AttackCommand(CombatState cs, Func<CombatState, CreatureState?, decimal> calculatedDamage)
        : this(cs, CalculatedDamagePerHit)
    {
        ArgumentNullException.ThrowIfNull(calculatedDamage);
        _calculatedDamage = calculatedDamage;
    }

    public CombatState Combat { get; }

    public decimal DamagePerHit { get; }

    public bool IsCalculated => _calculatedDamage != null;

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

    public bool IsFromMonster => _sourceType == AttackSourceType.Monster;

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

    public AttackCommand FromOsty(CreatureState osty, CardInstance card, CardPlay? cardPlay)
    {
        if (osty is not MonsterState monster || !string.Equals(monster.MonsterId, CombatStatus.OstyMonsterId, StringComparison.Ordinal))
        {
            throw new ArgumentException("Creature is not Osty");
        }
        Attacker = osty;
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
                CombatStatus.GetPlayerCreatures(Combat, buffer);
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

    private Rng RandomTargetRng(CombatState cs)
    {
        var attacker = Attacker!;
        if (attacker is PlayerState || CombatStatus.PetOwnerOf(cs, attacker) != null)
        {
            return cs.Rng.CombatTargets;
        }
        throw new InvalidOperationException("Random targeting reads (Attacker.Player ?? Attacker.PetOwner).RunState.Rng.CombatTargets, which has no player for a monster attacker.");
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
        decimal attackCount = Hook.ModifyAttackHitCount(cs, this, HitCount);
        if (_sourceType == AttackSourceType.Monster)
        {
            using var blocking = cs.Choice.PushContext(ChoiceContextKind.Inline);
            ExecuteHits(cs, attackCount);
        }
        else
        {
            ExecuteHits(cs, attackCount);
        }
        return this;
    }

    private void ExecuteHits(CombatState cs, decimal attackCount)
    {
        var attacker = Attacker!;
        var validTargets = new List<CreatureState>(4);
        for (int i = 0; i < attackCount; i++)
        {
            if (attacker.IsDead)
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
                singleTarget = RandomTargetRng(cs).NextItem((IReadOnlyList<CreatureState>)validTargets);
            }
            _beforeDamage?.Invoke(cs, this);
            decimal amount = _calculatedDamage == null ? DamagePerHit : _calculatedDamage(cs, singleTarget);
            IReadOnlyList<DamageResult> results = singleTarget == null
                ? CreatureCmd.Damage(cs, validTargets, amount, DamageProps, attacker, ModelSource, CardPlay)
                : CreatureCmd.Damage(cs, singleTarget, amount, DamageProps, attacker, ModelSource, CardPlay);
            AddResultsInternal(results);
            HitsCompleted++;
        }
        cs.History.AppendCreatureAttacked(cs, attacker, Results);
        Hook.AfterAttack(cs, this);
    }
}
