namespace Sts2Sim.Core.Run;

public sealed class RanwidTheElder : EventModel
{
    public const string EventId = "RANWID_THE_ELDER";

    public const string PotionChoiceKey = "RANWID_THE_ELDER.pages.INITIAL.options.POTION";

    public const string RelicChoiceKey = "RANWID_THE_ELDER.pages.INITIAL.options.RELIC";

    public const int Gold = 100;

    private int _potionSlot = -1;

    private int _relicIndex = -1;

    public override string Id => EventId;

    protected override void BeforeEventStarted(RunState rs, bool isPreFinished)
    {
        Owner!.CanUseOrRemovePotions = false;
    }

    protected override void OnEventFinished()
    {
        Owner!.CanUseOrRemovePotions = true;
    }

    public override bool IsAllowed(RunState runState)
    {
        if (runState.CurrentActIndex == 0)
        {
            return false;
        }
        if (runState.Players.Any(p => !p.Relics.Any(r => r.IsTradable)))
        {
            return false;
        }
        if (runState.Players.Any(p => p.Gold < Gold))
        {
            return false;
        }
        if (runState.Players.Any(p => !p.Potions.Any()))
        {
            return false;
        }
        return true;
    }

    protected override IReadOnlyList<EventOption> GenerateInitialOptions(RunState rs)
    {
        var owner = Owner!;
        var rng = Rng!;
        var options = new List<EventOption>(3);
        var potionSlots = new List<int>(owner.PotionSlots.Length);
        for (int i = 0; i < owner.PotionSlots.Length; i++)
        {
            if (owner.PotionSlots[i] != null)
            {
                potionSlots.Add(i);
            }
        }
        _potionSlot = potionSlots.Count == 0 ? -1 : rng.NextItem((IEnumerable<int>)potionSlots);
        if (_potionSlot >= 0)
        {
            options.Add(new EventOption(PotionChoiceKey, GivePotion));
        }
        else
        {
            options.Add(new EventOption("RANWID_THE_ELDER.pages.INITIAL.options.POTION_LOCKED", null));
        }
        options.Add(new EventOption("RANWID_THE_ELDER.pages.INITIAL.options.GOLD", GiveGold));
        var relicIndices = new List<int>(owner.Relics.Count);
        for (int i = 0; i < owner.Relics.Count; i++)
        {
            if (owner.Relics[i].IsTradable)
            {
                relicIndices.Add(i);
            }
        }
        _relicIndex = relicIndices.Count == 0 ? -1 : rng.NextItem((IEnumerable<int>)relicIndices);
        if (_relicIndex >= 0)
        {
            options.Add(new EventOption(RelicChoiceKey, GiveRelic));
        }
        else
        {
            options.Add(new EventOption("RANWID_THE_ELDER.pages.INITIAL.options.RELIC_LOCKED", null));
        }
        return options;
    }

    private void GivePotion(RunState rs)
    {
        var owner = Owner!;
        var potion = owner.PotionSlots[_potionSlot] ?? throw new InvalidOperationException("RanwidTheElder: offered potion slot is empty.");
        PotionCmd.Discard(rs, potion);
        RelicCmd.Obtain(rs, RelicFactory.PullNextRelicFromFront(rs, owner), owner);
        SetEventFinished("RANWID_THE_ELDER.pages.POTION.description");
    }

    private void GiveGold(RunState rs)
    {
        var owner = Owner!;
        PlayerCmd.LoseGold(rs, Gold, owner, GoldLossType.Spent);
        RelicCmd.Obtain(rs, RelicFactory.PullNextRelicFromFront(rs, owner), owner);
        SetEventFinished("RANWID_THE_ELDER.pages.GOLD.description");
    }

    private void GiveRelic(RunState rs)
    {
        var owner = Owner!;
        RelicCmd.Remove(rs, owner.Relics[_relicIndex]);
        for (int i = 0; i < 2; i++)
        {
            RelicCmd.Obtain(rs, RelicFactory.PullNextRelicFromFront(rs, owner), owner);
        }
        SetEventFinished("RANWID_THE_ELDER.pages.RELIC.description");
    }

    protected override void WriteEventFields(CanonicalWriter w)
    {
        w.WriteInt32(_potionSlot);
        w.WriteInt32(_relicIndex);
    }
}
