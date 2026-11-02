namespace Sts2Sim.Core.Run;

public sealed class AbyssalBaths : EventModel
{
    public const string EventId = "ABYSSAL_BATHS";

    public const int BaseDamage = 3;

    public const int DamageScaling = 1;

    public const decimal MaxHpGain = 2m;

    public const decimal HealAmount = 10m;

    public const int MaxLingerCount = 9;

    private int _lingerCount;

    private decimal _damage = BaseDamage;

    public override string Id => EventId;

    public int LingerCount => _lingerCount;

    public decimal Damage => _damage;

    protected override IReadOnlyList<EventOption> GenerateInitialOptions(RunState rs)
    {
        return new[]
        {
            new EventOption("ABYSSAL_BATHS.pages.INITIAL.options.IMMERSE", Immerse),
            new EventOption("ABYSSAL_BATHS.pages.INITIAL.options.ABSTAIN", Abstain)
        };
    }

    private void Immerse(RunState rs)
    {
        OnImmerse(rs);
        SetEventState("ABYSSAL_BATHS.pages.IMMERSE.description", LingerOptions());
    }

    private void Abstain(RunState rs)
    {
        Events121Support.Heal(rs, Owner!, (int)HealAmount);
        SetEventFinished("ABYSSAL_BATHS.pages.ABSTAIN.description");
    }

    private void Linger(RunState rs)
    {
        _lingerCount++;
        if (_lingerCount > MaxLingerCount)
        {
            _lingerCount = MaxLingerCount;
        }
        OnImmerse(rs);
        decimal damage = (int)_damage - MaxHpGain;
        if (WillKillPlayer(damage))
        {
            SetEventState("ABYSSAL_BATHS.pages.DEATH_WARNING.description", LingerOptions());
        }
        else
        {
            SetEventState("ABYSSAL_BATHS.pages.LINGER" + _lingerCount + ".description", LingerOptions());
        }
    }

    private EventOption[] LingerOptions() => new[]
    {
        new EventOption("ABYSSAL_BATHS.pages.ALL.options.LINGER", Linger),
        new EventOption("ABYSSAL_BATHS.pages.ALL.options.EXIT_BATHS", ExitBaths)
    };

    private bool WillKillPlayer(decimal damage) => Owner!.CurrentHp <= damage;

    private void ExitBaths(RunState rs)
    {
        SetEventFinished("ABYSSAL_BATHS.pages.EXIT_BATHS.description");
    }

    private void OnImmerse(RunState rs)
    {
        var player = Owner!;
        Events121Support.GainMaxHp(rs, player, MaxHpGain);
        Events121Support.Damage(rs, player, _damage, Events121Support.HpLossProps);
        _damage += DamageScaling;
    }

    protected override void WriteEventFields(CanonicalWriter w)
    {
        w.WriteInt32(_lingerCount);
        w.WriteDecimal(_damage);
    }
}
