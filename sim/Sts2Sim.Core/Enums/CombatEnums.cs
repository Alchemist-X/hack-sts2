namespace Sts2Sim.Core;

public enum CombatSide
{
    None,
    Player,
    Enemy
}

public enum PlayerTurnPhase
{
    None,
    Start,
    AutoPrePlay,
    Play,
    AutoPostPlay,
    End
}

public enum CombatOutcome : byte
{
    InProgress,
    Victory,
    Defeat
}

public enum IntentType
{
    Attack,
    Buff,
    Debuff,
    DebuffStrong,
    Defend,
    Escape,
    Heal,
    Hidden,
    Summon,
    Sleep,
    Stun,
    StatusCard,
    CardDebuff,
    DeathBlow,
    Unknown
}

public enum MoveRepeatType
{
    CanRepeatForever,
    CanRepeatXTimes,
    CannotRepeat,
    UseOnlyOnce
}

public enum AscensionLevel
{
    None,
    SwarmingElites,
    WearyTraveler,
    Poverty,
    TightBelt,
    AscendersBane,
    Inflation,
    Scarcity,
    ToughEnemies,
    DeadlyEnemies,
    DoubleBoss
}

public enum RunRngType
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

public enum PlayerRngType
{
    Rewards,
    Shops,
    Transformations
}
