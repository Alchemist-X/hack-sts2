namespace Sts2Sim.Core.Relics;

public sealed class PaelsLegion : RelicModel
{
    public const string RelicId = "PAELS_LEGION";

    public const string PetMonsterId = "PAELS_LEGION";

    private const string TurnsKey = "Turns";

    private int _cooldown;
    private bool _triggeredBlockLastTurn;
    public const int NoCardPlay = -1;

    private int _affectedCardPlayId = NoCardPlay;

    public override string Id => RelicId;

    public override bool AddsPet => true;

    public override RelicRarity Rarity => RelicRarity.Ancient;

    public override bool ShowCounter => DisplayAmount > 0;

    public override int DisplayAmount
    {
        get
        {
            if (_cooldown <= 0)
            {
                return -1;
            }
            return _cooldown;
        }
    }

    public int Cooldown
    {
        get => _cooldown;
        private set => _cooldown = value;
    }

    private bool TriggeredBlockLastTurn
    {
        get => _triggeredBlockLastTurn;
        set => _triggeredBlockLastTurn = value;
    }

    private int AffectedCardPlayId
    {
        get => _affectedCardPlayId;
        set => _affectedCardPlayId = value;
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new[]
    {
        new DynamicVar(TurnsKey, 2m)
    };

    public override void AfterObtained(RunState rs)
    {
        var cs = rs.Combat;
        if (cs != null && cs.IsInProgress)
        {
            SummonPet(cs);
        }
    }

    public override void BeforeCombatStart(RunState rs)
    {
        SummonPet(rs.Combat ?? throw new InvalidOperationException(RelicId + ".BeforeCombatStart without combat"));
    }

    public override decimal ModifyBlockMultiplicative(CombatState cs, CreatureState target, decimal block, ValueProp props, CardInstance? cardSource, CardPlay? cardPlay)
    {
        if (!props.IsCardOrMonsterMove())
        {
            return 1m;
        }
        if (cardSource == null)
        {
            return 1m;
        }
        if (!ReferenceEquals(cardSource.Owner, Owner))
        {
            return 1m;
        }
        if (Cooldown > 0)
        {
            return 1m;
        }
        return 2m;
    }

    public override void AfterModifyingBlockAmount(CombatState cs, decimal modifiedAmount, CardInstance? cardSource, CardPlay? cardPlay)
    {
        if (modifiedAmount <= 0m)
        {
            return;
        }
        if (cardPlay == null)
        {
            return;
        }
        if (AffectedCardPlayId != NoCardPlay && AffectedCardPlayId != cardPlay.PlayId)
        {
            return;
        }
        AffectedCardPlayId = cardPlay.PlayId;
    }

    public override void AfterCardPlayed(CombatState cs, CardPlay cardPlay)
    {
        if (AffectedCardPlayId == NoCardPlay || AffectedCardPlayId != cardPlay.PlayId)
        {
            return;
        }
        AffectedCardPlayId = NoCardPlay;
        Cooldown = DynamicVars[TurnsKey].IntValue;
        Status = RelicStatus.Normal;
        TriggeredBlockLastTurn = true;
    }

    public override void AfterSideTurnStart(CombatState cs, CombatSide side, IReadOnlyList<CreatureState> participants)
    {
        if (!participants.Any(p => ReferenceEquals(p, Owner)))
        {
            return;
        }
        Cooldown--;
        if (Cooldown <= 0)
        {
            Status = RelicStatus.Active;
        }
        TriggeredBlockLastTurn = false;
    }

    public override void AfterCombatEnd(RunState rs, CombatRoom? room)
    {
        Status = RelicStatus.Normal;
        Cooldown = 0;
        TriggeredBlockLastTurn = false;
        AffectedCardPlayId = NoCardPlay;
    }

    private void SummonPet(CombatState cs)
    {
        if (!MonsterRegistry.TryGet(PetMonsterId, out var def))
        {
            return;
        }
        PlayerCmd.AddPet(cs, Owner, def);
    }

    protected override void WriteExtraCanonical(CanonicalWriter w)
    {
        w.WriteInt32(_cooldown);
        w.WriteBool(_triggeredBlockLastTurn);
        w.WriteInt32(_affectedCardPlayId);
    }
}
