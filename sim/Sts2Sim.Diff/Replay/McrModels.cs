using Sts2Sim.Core.Run;

namespace Sts2Sim.Diff.Replay;

public enum McrEventType
{
    None,
    GameAction,
    HookAction,
    ResumeAction,
    PlayerChoice
}

public enum McrGameActionType
{
    None,
    Combat,
    CombatPlayPhaseOnly,
    NonCombat,
    Any
}

public enum McrPlayerTurnPhase
{
    None,
    Start,
    AutoPrePlay,
    Play,
    AutoPostPlay,
    End
}

public enum McrPileType
{
    None,
    Draw,
    Hand,
    Discard,
    Exhaust,
    Play,
    Deck
}

public enum McrCardKeyword
{
    None,
    Exhaust,
    Ethereal,
    Innate,
    Unplayable,
    Retain,
    Sly,
    Eternal
}

public enum McrPlayerChoiceType
{
    None,
    CanonicalCard,
    CombatCard,
    DeckCard,
    MutableCard,
    Player,
    Index
}

public enum McrGameMode
{
    None,
    Standard,
    Daily,
    Custom
}

public enum McrRunRngType
{
    UpFront,
    Shuffle,
    UnknownMapPoint,
    CombatCardGeneration,
    CombatPotionGeneration,
    CombatCardSelection,
    CombatEnergyCosts,
    CombatTargets,
    MonsterAi,
    Niche,
    CombatOrbs,
    TreasureRoomRelics
}

public enum McrPlayerRngType
{
    Rewards,
    Shops,
    Transformations
}

public enum McrCardCreationSource
{
    None,
    Encounter,
    Shop,
    Other
}

public enum McrCardRarityOddsType
{
    None,
    RegularEncounter,
    EliteEncounter,
    BossEncounter,
    Shop,
    Uniform
}

public enum McrDynamicVarType
{
    None,
    BaseDynamic,
    DynamicString,
    Decimal,
    String,
    Bool
}

public enum McrRewardType
{
    None,
    Card,
    Gold,
    Potion,
    Relic,
    RemoveCard,
    SpecialCard
}

public abstract record McrNetAction
{
    public abstract string Name { get; }
}

public sealed record McrPlayCard(uint CombatCardIndex, string? CardId, uint? TargetId) : McrNetAction
{
    public override string Name => "NetPlayCardAction";
}

public sealed record McrEndPlayerTurn(int TurnNumber) : McrNetAction
{
    public override string Name => "NetEndPlayerTurnAction";
}

public sealed record McrUndoEndPlayerTurn(int TurnNumber) : McrNetAction
{
    public override string Name => "NetUndoEndPlayerTurnAction";
}

public sealed record McrReadyToBeginEnemyTurn : McrNetAction
{
    public override string Name => "NetReadyToBeginEnemyTurnAction";
}

public sealed record McrUsePotion(uint PotionIndex, bool EnqueuedInCombat, uint? TargetId, ulong? TargetPlayerId) : McrNetAction
{
    public override string Name => "NetUsePotionAction";
}

public sealed record McrDiscardPotion(uint PotionSlotIndex, bool WasEnqueuedInCombat) : McrNetAction
{
    public override string Name => "NetDiscardPotionGameAction";
}

public sealed record McrConsoleCmd(string Cmd, bool InCombat) : McrNetAction
{
    public override string Name => "NetConsoleCmdGameAction";
}

public sealed record McrPickRelic(int? RelicIndex) : McrNetAction
{
    public override string Name => "NetPickRelicAction";
}

public sealed record McrMoveToMapCoord(MapCoord Destination) : McrNetAction
{
    public override string Name => "NetMoveToMapCoordAction";
}

public sealed record McrVoteForMapCoord(int SourceActIndex, MapCoord? SourceCoord, int? MapGenerationCount, MapCoord? Destination) : McrNetAction
{
    public override string Name => "NetVoteForMapCoordAction";
}

public sealed record McrVoteToMoveToNextAct(int CurrentActIndex) : McrNetAction
{
    public override string Name => "NetVoteToMoveToNextActAction";
}

public sealed class McrChoiceResult
{
    public McrPlayerChoiceType Type { get; init; }

    public List<string?>? CanonicalCards { get; init; }

    public List<uint>? CombatCards { get; init; }

    public List<uint>? DeckCards { get; init; }

    public List<SavedCard>? MutableCards { get; init; }

    public ulong? MutableCardOwner { get; init; }

    public ulong? PlayerId { get; init; }

    public List<int>? Indexes { get; init; }
}

public sealed class McrEvent
{
    public McrEventType Type { get; init; }

    public ulong? PlayerId { get; init; }

    public McrNetAction? Action { get; init; }

    public uint? HookId { get; init; }

    public McrGameActionType? GameActionType { get; init; }

    public uint? ActionId { get; init; }

    public uint? ChoiceId { get; init; }

    public McrChoiceResult? Choice { get; init; }
}

public sealed record McrPower(string? Id, int Amount);

public sealed class McrCreature
{
    public string? MonsterId { get; init; }

    public ulong? PlayerId { get; init; }

    public int CurrentHp { get; init; }

    public int MaxHp { get; init; }

    public int Block { get; init; }

    public List<McrPower> Powers { get; init; } = new();
}

public sealed class McrCardState
{
    public required SavedCard Card { get; init; }

    public string? Affliction { get; init; }

    public int AfflictionCount { get; init; }

    public int? EnergyCost { get; init; }

    public List<McrCardKeyword>? Keywords { get; init; }
}

public sealed record McrPile(McrPileType Type, List<McrCardState> Cards);

public sealed record McrOrb(string? Id, int Passive, int Evoke);

public sealed class McrPlayerState
{
    public ulong PlayerId { get; init; }

    public string? CharacterId { get; init; }

    public int TurnNumber { get; init; }

    public McrPlayerTurnPhase Phase { get; init; }

    public int Energy { get; init; }

    public int Stars { get; init; }

    public int MaxPotionCount { get; init; }

    public int Gold { get; init; }

    public List<McrPile> Piles { get; init; } = new();

    public List<string?> Potions { get; init; } = new();

    public List<SavedRelic> Relics { get; init; } = new();

    public List<McrOrb> Orbs { get; init; } = new();

    public SavedPlayerRng Rng { get; init; } = new();

    public SavedRelicGrabBag RelicGrabBag { get; init; } = new();

    public McrPile? Pile(McrPileType type) => Piles.FirstOrDefault(p => p.Type == type);
}

public sealed class McrCombatState
{
    public List<McrCreature> Creatures { get; init; } = new();

    public List<McrPlayerState> Players { get; init; } = new();

    public SavedRunRng Rng { get; init; } = new();

    public List<uint> NextChoiceIds { get; init; } = new();

    public List<int> NextRewardIds { get; init; } = new();

    public uint? LastExecutedActionId { get; init; }

    public uint? LastExecutedHookId { get; init; }
}

public sealed class McrChecksum
{
    public uint Id { get; init; }

    public uint Checksum { get; init; }

    public string Context { get; init; } = string.Empty;

    public required McrCombatState State { get; init; }
}

public sealed class McrReplay
{
    public string Version { get; init; } = string.Empty;

    public string GitCommit { get; init; } = string.Empty;

    public uint ModelIdHash { get; init; }

    public required McrModelNetIds Table { get; init; }

    public bool HashVerified { get; init; }

    public List<uint> ChoiceIds { get; init; } = new();

    public List<int> RewardIds { get; init; } = new();

    public uint NextActionId { get; init; }

    public uint NextChecksumId { get; init; }

    public uint NextHookId { get; init; }

    public required SavedRun Run { get; init; }

    public McrGameMode GameMode { get; init; }

    public List<McrEvent> Events { get; init; } = new();

    public List<McrChecksum> Checksums { get; init; } = new();

    public int TrailingBits { get; init; }

    public int TotalFloor => Run.MapPointHistory.Sum(a => a.Count);

    public SavedPlayer? Player => Run.Players.Count > 0 ? Run.Players[0] : null;
}
