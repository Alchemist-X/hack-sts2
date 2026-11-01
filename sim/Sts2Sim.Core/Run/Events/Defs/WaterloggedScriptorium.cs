namespace Sts2Sim.Core.Run;

public sealed class WaterloggedScriptorium : EventModel
{
    public const string EventId = "WATERLOGGED_SCRIPTORIUM";

    public const decimal MaxHp = 6m;

    public const int Gold = 55;

    public const int PricklySpongeGold = 99;

    public const int Cards = 2;

    public const int SpawnGoldRequirement = 55;

    public const string TentacleQuillSite = "WATERLOGGED_SCRIPTORIUM:TENTACLE_QUILL";

    public const string PricklySpongeSite = "WATERLOGGED_SCRIPTORIUM:PRICKLY_SPONGE";

    public override string Id => EventId;

    public override bool IsAllowed(RunState rs) => rs.Players.All(p => p.Gold >= SpawnGoldRequirement);

    protected override IReadOnlyList<EventOption> GenerateInitialOptions(RunState rs)
    {
        var owner = Owner!;
        var options = new List<EventOption>
        {
            new EventOption(InitialOptionKey("BLOODY_INK"), BloodyInk)
        };
        options.Add(owner.Gold >= Gold
            ? new EventOption(InitialOptionKey("TENTACLE_QUILL"), TentacleQuill)
            : new EventOption(InitialOptionKey("TENTACLE_QUILL_LOCKED"), null));
        options.Add(owner.Gold >= PricklySpongeGold
            ? new EventOption(InitialOptionKey("PRICKLY_SPONGE"), PricklySponge)
            : new EventOption(InitialOptionKey("PRICKLY_SPONGE_LOCKED"), null));
        return options;
    }

    private void PricklySponge(RunState rs)
    {
        var owner = Owner!;
        PlayerCmd.LoseGold(rs, PricklySpongeGold, owner, GoldLossType.Spent);
        var picked = CardSelectCmd.FromDeckForEnchantment(rs, owner, new Steady(), 1, new CardSelectorPrefs(CardSelectorPrefs.EnchantSelectionPrompt, Cards), new ChoiceSource(PricklySpongeSite, Id, ChoiceSource.NoInstance)).ToList();
        foreach (var card in picked)
        {
            CardCmd.Enchant<Steady>(card, 1m);
        }
        SetEventFinished(Id + ".pages.PRICKLY_SPONGE.description");
    }

    private void TentacleQuill(RunState rs)
    {
        var owner = Owner!;
        PlayerCmd.LoseGold(rs, Gold, owner, GoldLossType.Spent);
        var picked = CardSelectCmd.FromDeckForEnchantment(rs, owner, new Steady(), 1, new CardSelectorPrefs(CardSelectorPrefs.EnchantSelectionPrompt, 1), new ChoiceSource(TentacleQuillSite, Id, ChoiceSource.NoInstance));
        var card = picked.FirstOrDefault();
        if (card != null)
        {
            CardCmd.Enchant<Steady>(card, 1m);
        }
        SetEventFinished(Id + ".pages.TENTACLE_QUILL.description");
    }

    private void BloodyInk(RunState rs)
    {
        CreatureCmd.GainMaxHp(rs, Owner!, MaxHp);
        SetEventFinished(Id + ".pages.BLOODY_INK.description");
    }
}
