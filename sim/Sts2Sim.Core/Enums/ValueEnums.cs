namespace Sts2Sim.Core;

[Flags]
public enum ValueProp
{
    None = 0,
    Unblockable = 2,
    Unpowered = 4,
    Move = 8,
    SkipHurtAnim = 0x10
}

[Flags]
public enum ModifyDamageHookType
{
    None = 0,
    Additive = 2,
    Multiplicative = 4,
    Cap = 8,
    All = 0xE
}

[Flags]
public enum HpLossHookPhase
{
    None = 0,
    BeforeOsty = 1,
    AfterOsty = 2,
    All = 3
}
