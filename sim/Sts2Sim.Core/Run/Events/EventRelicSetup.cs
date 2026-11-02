namespace Sts2Sim.Core.Run;

public interface IEventRelicSetup
{
    bool SetupForPlayer(RunState rs, PlayerState player);
}

public interface ICharacterBoundRelic
{
    string? CharacterId { get; set; }
}
