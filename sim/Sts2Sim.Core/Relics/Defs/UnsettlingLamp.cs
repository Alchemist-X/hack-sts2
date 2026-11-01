namespace Sts2Sim.Core.Relics;

public sealed class UnsettlingLamp : RelicModel
{
    public const string RelicId = "UNSETTLING_LAMP";

    public const string ArtifactPowerId = "ARTIFACT_POWER";

    public const int NoCard = -1;

    public const decimal DebuffMultiplier = 2m;

    private int _triggeringCardId = NoCard;
    private List<string?> _doubledPowers = new();
    private bool _isFinishedTriggering;

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Rare;

    private int TriggeringCardId
    {
        get => _triggeringCardId;
        set => _triggeringCardId = value;
    }

    private List<string?> DoubledPowers => _doubledPowers;

    private bool IsFinishedTriggering
    {
        get => _isFinishedTriggering;
        set => _isFinishedTriggering = value;
    }

    public override void BeforeCombatStart(RunState rs)
    {
        TriggeringCardId = NoCard;
        DoubledPowers.Clear();
        IsFinishedTriggering = false;
        Status = RelicStatus.Active;
    }

    public override void BeforePowerAmountChanged(CombatState cs, PowerModel power, decimal amount, CreatureState target, CreatureState? applier, CardInstance? cardSource)
    {
        if (TriggeringCardId != NoCard)
        {
            return;
        }
        if (IsFinishedTriggering)
        {
            return;
        }
        if (cardSource == null)
        {
            return;
        }
        if (!ReferenceEquals(applier, Owner))
        {
            return;
        }
        if (target.Side == Owner.Side)
        {
            return;
        }
        if (!power.IsVisible)
        {
            return;
        }
        if (power.GetTypeForAmount(amount) != PowerType.Debuff)
        {
            return;
        }
        if (target.HasPower(ArtifactPowerId))
        {
            return;
        }
        TriggeringCardId = cardSource.InstanceId;
        DoubledPowers.Add((power as ITemporaryPower)?.InternallyAppliedPowerId);
    }

    public override decimal ModifyPowerAmountGivenMultiplicative(CombatState cs, PowerModel power, CreatureState giver, decimal amount, CreatureState? target, CardInstance? cardSource)
    {
        if (TriggeringCardId == NoCard)
        {
            return 1m;
        }
        if (cardSource == null || cardSource.InstanceId != TriggeringCardId)
        {
            return 1m;
        }
        if (IsFinishedTriggering)
        {
            return 1m;
        }
        if (HasDoubledTemporaryPowerSource(power))
        {
            return 1m;
        }
        if (power.GetTypeForAmount(amount) != PowerType.Debuff)
        {
            return 1m;
        }
        return DebuffMultiplier;
    }

    public override void AfterCardPlayed(CombatState cs, CardPlay cardPlay)
    {
        if (TriggeringCardId == NoCard || cardPlay.Card.InstanceId != TriggeringCardId)
        {
            return;
        }
        if (IsFinishedTriggering)
        {
            return;
        }
        IsFinishedTriggering = true;
        Status = RelicStatus.Normal;
    }

    public override void AfterCombatEnd(RunState rs, CombatRoom? room)
    {
        TriggeringCardId = NoCard;
        DoubledPowers.Clear();
        IsFinishedTriggering = false;
        Status = RelicStatus.Normal;
    }

    private bool HasDoubledTemporaryPowerSource(PowerModel power)
    {
        for (int i = 0; i < DoubledPowers.Count; i++)
        {
            var internallyApplied = DoubledPowers[i];
            if (internallyApplied != null && string.Equals(internallyApplied, power.Id, StringComparison.Ordinal))
            {
                return true;
            }
        }
        return false;
    }

    protected override void CopyMutableReferenceState(RelicModel source)
    {
        _doubledPowers = new List<string?>(((UnsettlingLamp)source)._doubledPowers);
    }

    protected override void WriteExtraCanonical(CanonicalWriter w)
    {
        w.WriteInt32(_triggeringCardId);
        w.WriteBool(_isFinishedTriggering);
        w.WriteInt32(_doubledPowers.Count);
        for (int i = 0; i < _doubledPowers.Count; i++)
        {
            var id = _doubledPowers[i];
            w.WriteBool(id != null);
            if (id != null)
            {
                w.WriteString(id);
            }
        }
    }
}
