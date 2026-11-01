namespace Sts2Sim.Core.Run;

public sealed class JungleMazeAdventure : EventModel
{
    public const string EventId = "JUNGLE_MAZE_ADVENTURE";

    public const decimal BaseSoloGold = 150m;

    public const decimal SoloHp = 18m;

    public const decimal BaseJoinForcesGold = 50m;

    public const int FxCount = 3;

    private decimal _soloGold = BaseSoloGold;

    private decimal _joinForcesGold = BaseJoinForcesGold;

    public override string Id => EventId;

    public override bool IsShared => true;

    public decimal SoloGold => _soloGold;

    public decimal JoinForcesGold => _joinForcesGold;

    public override bool IsAllowed(RunState runState)
    {
        var players = runState.Players;
        if (players.Count == 1)
        {
            return true;
        }
        for (int i = 0; i < players.Count; i++)
        {
            if (players[i].CurrentHp <= SoloHp)
            {
                return false;
            }
        }
        return true;
    }

    public override void CalculateVars(RunState rs)
    {
        _soloGold += (decimal)Rng!.NextFloat(-15f, 15f);
        _joinForcesGold += (decimal)Rng!.NextFloat(-15f, 15f);
    }

    protected override IReadOnlyList<EventOption> GenerateInitialOptions(RunState rs)
    {
        return new[]
        {
            new EventOption("JUNGLE_MAZE_ADVENTURE.pages.INITIAL.options.SOLO_QUEST", DontNeedHelp),
            new EventOption("JUNGLE_MAZE_ADVENTURE.pages.INITIAL.options.JOIN_FORCES", SafetyInNumbers)
        };
    }

    private void DontNeedHelp(RunState rs)
    {
        var fx = new List<int>(FxCount);
        for (int i = 0; i < FxCount; i++)
        {
            fx.Add(i);
        }
        fx.StableShuffle(Rng!);
        Events121Support.Damage(rs, Owner!, SoloHp, Events121Support.HpLossProps);
        PlayerCmd.GainGold(rs, _soloGold, Owner!);
        SetEventFinished("JUNGLE_MAZE_ADVENTURE.pages.SOLO_QUEST.description");
    }

    private void SafetyInNumbers(RunState rs)
    {
        PlayerCmd.GainGold(rs, _joinForcesGold, Owner!);
        SetEventFinished("JUNGLE_MAZE_ADVENTURE.pages.JOIN_FORCES.description");
    }

    protected override void WriteEventFields(CanonicalWriter w)
    {
        w.WriteDecimal(_soloGold);
        w.WriteDecimal(_joinForcesGold);
    }
}
