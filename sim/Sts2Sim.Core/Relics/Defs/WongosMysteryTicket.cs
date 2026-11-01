namespace Sts2Sim.Core.Relics;

public sealed class WongosMysteryTicket : RelicModel
{
    public const string RelicId = "WONGOS_MYSTERY_TICKET";

    private const string RemainingCombatsKey = "RemainingCombats";

    public const int CombatsToActivate = 5;

    public const int RelicCount = 3;

    private int _combatsFinished;
    private bool _gaveRelic;

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Event;

    public override bool IsUsedUp => GaveRelic;

    public override bool ShowCounter => DisplayAmount > 0;

    public override int DisplayAmount => CombatsToActivate - CombatsFinished;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new RepeatVar(3),
        new DynamicVar(RemainingCombatsKey, 5m)
    };

    public int CombatsFinished
    {
        get => _combatsFinished;
        set => _combatsFinished = value;
    }

    public bool GaveRelic
    {
        get => _gaveRelic;
        set
        {
            _gaveRelic = value;
            if (_gaveRelic)
            {
                Status = RelicStatus.Disabled;
            }
        }
    }

    public override void AfterCombatEnd(RunState rs, CombatRoom? room)
    {
        CombatsFinished++;
        int num = CombatsToActivate - CombatsFinished;
        DynamicVars[RemainingCombatsKey].BaseValue = decimal.Max(num, 0m);
    }

    public override bool TryModifyRewards(RunState rs, PlayerState player, List<Reward> rewards, AbstractRoom? room)
    {
        if (!ReferenceEquals(player, Owner))
        {
            return false;
        }
        if (room is not CombatRoom)
        {
            return false;
        }
        if (GaveRelic)
        {
            return false;
        }
        int num = CombatsToActivate - CombatsFinished;
        if (num > 0)
        {
            return false;
        }
        for (int i = 0; i < DynamicVars.Repeat.IntValue; i++)
        {
            rewards.Add(new RelicReward(player));
        }
        return true;
    }

    public override void AfterModifyingRewards(RunState rs)
    {
        GaveRelic = true;
    }

    protected override void WriteExtraCanonical(CanonicalWriter w)
    {
        w.WriteInt32(_combatsFinished);
        w.WriteBool(_gaveRelic);
    }
}
