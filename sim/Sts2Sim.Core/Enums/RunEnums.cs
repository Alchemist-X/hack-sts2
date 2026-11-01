namespace Sts2Sim.Core;

public enum RoomType
{
    Unassigned,
    Monster,
    Elite,
    Boss,
    Treasure,
    Shop,
    Event,
    RestSite,
    Map
}

public enum MapPointType
{
    Unassigned,
    Unknown,
    Shop,
    Treasure,
    RestSite,
    Monster,
    Elite,
    Boss,
    Ancient
}

public enum RewardType
{
    None,
    Card,
    Gold,
    Potion,
    Relic,
    RemoveCard,
    SpecialCard
}

public enum GoldLossType
{
    None,
    Spent,
    Lost,
    Stolen
}

public enum CardRarityOddsType
{
    None,
    RegularEncounter,
    EliteEncounter,
    BossEncounter,
    Shop,
    Uniform
}
