namespace Sts2Sim.Core.Run;

public sealed class TabletOfTruth : EventModel
{
    public const string EventId = "TABLET_OF_TRUTH";

    public const decimal SmashHpGain = 20m;

    public const decimal InitialDecipherMaxHpLoss = 3m;

    public const int FinalDecipherCount = 5;

    private int _decipherCount;

    private decimal _decipherMaxHpLoss = InitialDecipherMaxHpLoss;

    public override string Id => EventId;

    public int DecipherCount => _decipherCount;

    public decimal DecipherMaxHpLoss => _decipherMaxHpLoss;

    protected override IReadOnlyList<EventOption> GenerateInitialOptions(RunState rs)
    {
        return new[]
        {
            new EventOption("TABLET_OF_TRUTH.pages.INITIAL.options.DECIPHER_1", Decipher),
            new EventOption("TABLET_OF_TRUTH.pages.INITIAL.options.SMASH", Smash)
        };
    }

    private void Decipher(RunState rs)
    {
        LoseMaxHpAndUpgrade(rs, _decipherMaxHpLoss);
        _decipherCount++;
        if (_decipherCount == FinalDecipherCount)
        {
            SetEventFinished("TABLET_OF_TRUTH.pages.DECIPHER_" + _decipherCount + ".description");
            return;
        }
        _decipherMaxHpLoss = GetDecipherCost();
        SetEventState("TABLET_OF_TRUTH.pages.DECIPHER_" + _decipherCount + ".description", new[]
        {
            new EventOption("TABLET_OF_TRUTH.pages.DECIPHER_" + _decipherCount + ".options.DECIPHER", Decipher),
            new EventOption("TABLET_OF_TRUTH.pages.DECIPHER.options.GIVE_UP", GiveUp)
        });
    }

    private void Smash(RunState rs)
    {
        Events121Support.Heal(rs, Owner!, SmashHpGain);
        SetEventFinished("TABLET_OF_TRUTH.pages.SMASH.description");
    }

    public int GetDecipherCost()
    {
        var owner = Owner!;
        switch (_decipherCount)
        {
            case 1:
                return 6;
            case 2:
                return 12;
            case 3:
                return 24;
            case 4:
                return owner.MaxHp - 1;
            default:
                return 999999999;
        }
    }

    private void GiveUp(RunState rs)
    {
        SetEventFinished("TABLET_OF_TRUTH.pages.GIVE_UP.description");
    }

    private void LoseMaxHpAndUpgrade(RunState rs, decimal hp)
    {
        var owner = Owner!;
        if (hp < owner.MaxHp)
        {
            Events121Support.LoseMaxHp(rs, owner, hp, isFromCard: false);
            var upgradable = owner.Deck.Where(c => c.IsUpgradable).ToList();
            if (_decipherCount == 4)
            {
                for (int i = 0; i < upgradable.Count; i++)
                {
                    CardCmd.Upgrade(rs.Combat, upgradable[i]);
                }
            }
            else if (upgradable.Count != 0)
            {
                var card = Rng!.NextItem((IEnumerable<CardInstance>)upgradable)!;
                CardCmd.Upgrade(rs.Combat, card);
            }
            return;
        }
        Events121Support.LoseMaxHp(rs, owner, owner.MaxHp - 1, isFromCard: false);
        Events121Support.Kill(rs, owner);
    }

    protected override void WriteEventFields(CanonicalWriter w)
    {
        w.WriteInt32(_decipherCount);
        w.WriteDecimal(_decipherMaxHpLoss);
    }
}
