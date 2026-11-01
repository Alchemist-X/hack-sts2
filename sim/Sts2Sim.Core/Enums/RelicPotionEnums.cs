namespace Sts2Sim.Core;

public enum PotionUsage
{
    None,
    CombatOnly,
    AnyTime,
    Automatic
}

public enum PotionRarity
{
    None,
    Common,
    Uncommon,
    Rare,
    Event,
    Token
}

public enum RelicRarity
{
    None,
    Starter,
    Common,
    Uncommon,
    Rare,
    Shop,
    Event,
    Ancient
}

public enum RelicStatus
{
    Normal,
    Active,
    Disabled
}

public enum EnchantmentStatus
{
    Normal,
    Disabled
}

public enum PotionProcureFailureReason
{
    None,
    TooFull,
    NotAllowed
}

public readonly record struct PotionProcureResult(bool Success, PotionModel Potion, PotionProcureFailureReason FailureReason);
