namespace Sts2Sim.Core;

public enum CardType
{
    None,
    Attack,
    Skill,
    Power,
    Status,
    Curse,
    Quest
}

public enum TargetType
{
    None,
    Self,
    AnyEnemy,
    AllEnemies,
    RandomEnemy,
    AnyPlayer,
    AnyAlly,
    AllAllies,
    TargetedNoCreature,
    Osty
}

public enum PileType
{
    None,
    Draw,
    Hand,
    Discard,
    Exhaust,
    Play,
    Deck
}

public enum CardKeyword
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

public enum CardRarity
{
    None,
    Basic,
    Common,
    Uncommon,
    Rare,
    Ancient,
    Event,
    Token,
    Status,
    Curse,
    Quest
}

public enum CardTag
{
    None,
    Strike,
    Defend,
    Minion,
    OstyAttack,
    Shiv
}

[Flags]
public enum UnplayableReason
{
    None = 0,
    HasUnplayableKeyword = 2,
    BlockedByHook = 4,
    BlockedByCardLogic = 8,
    EnergyCostTooHigh = 0x10,
    StarCostTooHigh = 0x20,
    NoLivingAllies = 0x40
}

public enum CardPilePosition
{
    None,
    Bottom,
    Top,
    Random
}
