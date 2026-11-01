namespace Sts2Sim.Core.Run;

public sealed class BattlewornDummy : EventModel
{
    public const string EventId = "BATTLEWORN_DUMMY";

    public const string V1EncounterId = "BATTLEWORN_DUMMY_EVENT_V1_ENCOUNTER";

    public const string V2EncounterId = "BATTLEWORN_DUMMY_EVENT_V2_ENCOUNTER";

    public const string V3EncounterId = "BATTLEWORN_DUMMY_EVENT_V3_ENCOUNTER";

    public const int UpgradeCount = 2;

    public override string Id => EventId;

    public override bool IsShared => true;

    protected override IReadOnlyList<EventOption> GenerateInitialOptions(RunState rs) => new[]
    {
        new EventOption(InitialOptionKey("SETTING_1"), Setting1),
        new EventOption(InitialOptionKey("SETTING_2"), Setting2),
        new EventOption(InitialOptionKey("SETTING_3"), Setting3)
    };

    private void Setting1(RunState rs)
    {
        EnterCombatWithoutExitingEvent(rs, EncounterRegistry.Get(V1EncounterId), Array.Empty<Reward>(), shouldResumeAfterCombat: true);
    }

    private void Setting2(RunState rs)
    {
        EnterCombatWithoutExitingEvent(rs, EncounterRegistry.Get(V2EncounterId), Array.Empty<Reward>(), shouldResumeAfterCombat: true);
    }

    private void Setting3(RunState rs)
    {
        EnterCombatWithoutExitingEvent(rs, EncounterRegistry.Get(V3EncounterId), Array.Empty<Reward>(), shouldResumeAfterCombat: true);
    }

    public override void Resume(RunState rs, AbstractRoom exitedRoom)
    {
        var combatRoom = (CombatRoom)exitedRoom;
        var encounterId = combatRoom.Encounter.Id;
        if (RanOutOfTime(combatRoom))
        {
            SetEventFinished(Id + ".pages.DEFEAT.description");
            return;
        }
        SetEventFinished(Id + ".pages.VICTORY.description");
        var owner = Owner!;
        var val2 = new List<Reward>();
        if (encounterId == V1EncounterId)
        {
            var items = PotionFactory.GetPotionOptions(rs, owner);
            var potionModel = owner.PlayerRng!.Rewards.NextItem(items);
            if (potionModel != null)
            {
                val2.Add(new PotionReward(PotionRegistry.Create(potionModel.Id), owner));
            }
        }
        else if (encounterId == V2EncounterId)
        {
            var enumerable = owner.Deck.Where(c => c?.IsUpgradable ?? false).ToList().StableShuffle(Rng!).Take(UpgradeCount);
            foreach (var current in enumerable)
            {
                CardCmd.Upgrade(null, current);
            }
        }
        else if (encounterId == V3EncounterId)
        {
            var relic = RelicFactory.PullNextRelicFromFront(rs, owner);
            val2.Add(new RelicReward(relic, owner));
        }
        if (combatRoom.ExtraRewards.Count > 0)
        {
            val2 = val2.Concat(combatRoom.ExtraRewards).ToList();
        }
        if (val2.Count > 0)
        {
            RewardsCmd.OfferCustom(rs, owner, val2);
        }
    }

    private static bool RanOutOfTime(CombatRoom combatRoom)
        => throw new NotImplementedException("W3.5 kernel-gaps: CombatState.EncounterExtra (BattlewornDummyEventEncounter.RanOutOfTime read by BattlewornDummy.Resume; Models.Events/BattlewornDummy.cs:56-58) for " + combatRoom.Encounter.Id);
}
