using System.Collections.Frozen;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Sts2Sim.Tests")]

namespace Sts2Sim.Core.Combat.Pipeline;

public enum HookId : byte
{
    AfterActEntered,
    AfterAddToDeckPrevented,
    BeforeAttack,
    AfterAttack,
    AfterAutoPostPlayPhaseEntered,
    AfterAutoPrePlayPhaseEnteredEarly,
    AfterAutoPrePlayPhaseEntered,
    AfterAutoPrePlayPhaseEnteredLate,
    AfterBlockCleared,
    BeforeBlockGained,
    AfterBlockGained,
    AfterBlockBroken,
    AfterCardChangedPiles,
    AfterCardChangedPilesLate,
    AfterCardDiscarded,
    AfterCardDrawnEarly,
    AfterCardDrawn,
    AfterCardEnteredCombat,
    AfterCardGeneratedForCombat,
    AfterCardExhausted,
    BeforeCardAutoPlayed,
    BeforeCardPlayed,
    AfterCardPlayed,
    AfterCardPlayedLate,
    BeforeCombatStart,
    BeforeCombatStartLate,
    AfterCombatEnd,
    BeforeCombatRewardOffered,
    AfterCombatVictoryEarly,
    AfterCombatVictory,
    AfterCreatureAddedToCombat,
    AfterCurrentHpChanged,
    AfterDamageGiven,
    BeforeDamageReceived,
    AfterDamageReceived,
    AfterDamageReceivedLate,
    BeforeDeath,
    AfterDeath,
    AfterDiedToDoom,
    AfterEnergyReset,
    AfterEnergyResetLate,
    AfterEnergySpent,
    BeforeCardRemoved,
    BeforeFlush,
    BeforeFlushLate,
    AfterFlush,
    AfterGoldGained,
    BeforeHandDraw,
    BeforeHandDrawLate,
    AfterHandEmptied,
    AfterItemPurchased,
    AfterMapGenerated,
    AfterModifyingBlockAmount,
    AfterModifyingCardPlayCount,
    AfterModifyingCardPlayResultLocation,
    AfterModifyingOrbPassiveTriggerCount,
    AfterModifyingCardRewardOptions,
    AfterModifyingDamageAmount,
    AfterModifyingEnergyGain,
    AfterModifyingGoldGained,
    AfterModifyingHandDraw,
    AfterPreventingDraw,
    AfterModifyingHpLostBeforeOsty,
    AfterModifyingHpLostAfterOsty,
    AfterModifyingPowerAmountReceived,
    AfterModifyingPowerAmountGiven,
    AfterModifyingRewards,
    AfterOrbChanneled,
    AfterOrbEvoked,
    AfterOstyRevived,
    BeforePotionUsed,
    AfterPotionUsed,
    AfterPotionDiscarded,
    AfterPotionProcured,
    BeforePowerAmountChanged,
    AfterPowerAmountChanged,
    AfterPreventingBlockClear,
    AfterPreventingDeath,
    AfterRestSiteHeal,
    AfterRestSiteSmith,
    AfterRewardTaken,
    BeforeRoomEntered,
    AfterRoomEntered,
    AfterShuffle,
    AfterStarsSpent,
    AfterStarsGained,
    AfterForge,
    AfterSummon,
    AfterTakingExtraTurn,
    AfterTargetingBlockedVfx,
    BeforeSideTurnStart,
    AfterSideTurnStart,
    AfterSideTurnStartLate,
    AfterPlayerTurnStartEarly,
    AfterPlayerTurnStart,
    AfterPlayerTurnStartLate,
    BeforeSideTurnEndVeryEarly,
    BeforeSideTurnEndEarly,
    BeforeSideTurnEnd,
    AfterSideTurnEnd,
    AfterSideTurnEndLate,
    ModifyAttackHitCount,
    ModifyBlockAdditive,
    ModifyBlockMultiplicative,
    ModifyCardPlayCount,
    ModifyCardPlayResultLocation,
    ModifyOrbPassiveTriggerCounts,
    ModifyCardRewardCreationOptions,
    ModifyCardRewardCreationOptionsLate,
    ModifyCardRewardUpgradeOdds,
    ModifyDamageAdditive,
    ModifyDamageCap,
    ModifyDamageMultiplicative,
    ModifyEnergyGain,
    ModifyGoldGained,
    ModifyGeneratedMap,
    ModifyGeneratedMapLate,
    ModifyHandDraw,
    ModifyHandDrawLate,
    ModifyHpLostBeforeOsty,
    ModifyHpLostBeforeOstyLate,
    ModifyHpLostAfterOsty,
    ModifyHpLostAfterOstyLate,
    ModifyMaxEnergy,
    ModifyMerchantCardPool,
    ModifyMerchantCardRarity,
    ModifyMerchantCardCreationResults,
    ModifyMerchantPrice,
    ModifyOrbValue,
    ModifyPowerAmountGivenAdditive,
    ModifyPowerAmountGivenMultiplicative,
    ModifyRestSiteHealAmount,
    ModifyShuffleOrder,
    ModifySummonAmount,
    ModifyUnblockedDamageTarget,
    ModifyNextEvent,
    ModifyUnknownMapPointRoomTypes,
    ModifyOddsIncreaseForUnrolledRoomType,
    ModifyXValue,
    TryModifyCardBeingAddedToDeck,
    TryModifyCardBeingAddedToDeckLate,
    TryModifyCardRewardAlternatives,
    TryModifyCardRewardOptions,
    TryModifyCardRewardOptionsLate,
    TryModifyEnergyCostInCombat,
    TryModifyEnergyCostInCombatLate,
    TryModifyKeywordsInCombat,
    TryModifyStarCost,
    TryModifyPowerAmountReceived,
    TryModifyRestSiteOptions,
    TryModifyRestSiteHealRewards,
    TryModifyRewards,
    TryModifyRewardsLate,
    ModifyExtraRestSiteHealText,
    ShouldAddToDeck,
    ShouldAfflict,
    ShouldAllowAncient,
    ShouldAllowHitting,
    ShouldAllowTargeting,
    ShouldAllowSelectingMoreCardRewards,
    ShouldClearBlock,
    ShouldDie,
    ShouldDieLate,
    ShouldDisableRemainingRestSiteOptions,
    ShouldDraw,
    ShouldEtherealTrigger,
    ShouldFlush,
    ShouldGainStars,
    ShouldGenerateTreasure,
    ShouldPayExcessEnergyCostWithStars,
    ShouldPlay,
    ShouldPlayerResetEnergy,
    ShouldProceedToNextMapPoint,
    ShouldProcurePotion,
    ShouldPowerBeRemovedOnDeath,
    ShouldRefillMerchantEntry,
    ShouldAllowMerchantCardRemoval,
    ShouldCreatureBeRemovedFromCombatAfterDeath,
    ShouldStopCombatFromEnding,
    ShouldTakeExtraTurn,
    ShouldForcePotionReward,
    ShouldAllowFreeTravel
}

public sealed class HookMask
{
    public const int HookCount = (int)HookId.ShouldAllowFreeTravel + 1;

    private const BindingFlags InstanceMethods = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private static readonly Lazy<FrozenDictionary<string, HookId>> HooksByName = new(BuildHooksByName);

    private static readonly Lazy<FrozenDictionary<Type, HookMask>> CoreMasks = new(BuildCoreMasks);

    private readonly ulong _bits0;
    private readonly ulong _bits1;
    private readonly ulong _bits2;

    private HookMask(ulong bits0, ulong bits1, ulong bits2)
    {
        _bits0 = bits0;
        _bits1 = bits1;
        _bits2 = bits2;
    }

    public static HookMask None { get; } = new(0UL, 0UL, 0UL);

    public static HookMask All { get; } = Of(Enum.GetValues<HookId>());

    public int Count => BitOperations.PopCount(_bits0) + BitOperations.PopCount(_bits1) + BitOperations.PopCount(_bits2);

    public bool Overrides(HookId hook)
    {
        int bit = (int)hook;
        if (bit < 64)
        {
            return ((_bits0 >> bit) & 1UL) != 0UL;
        }
        if (bit < 128)
        {
            return ((_bits1 >> (bit - 64)) & 1UL) != 0UL;
        }
        return ((_bits2 >> (bit - 128)) & 1UL) != 0UL;
    }

    public static HookMask Of(params HookId[] hooks)
    {
        ArgumentNullException.ThrowIfNull(hooks);
        ulong bits0 = 0UL, bits1 = 0UL, bits2 = 0UL;
        for (int i = 0; i < hooks.Length; i++)
        {
            Set(ref bits0, ref bits1, ref bits2, hooks[i]);
        }
        return new HookMask(bits0, bits1, bits2);
    }

    public static HookMask For(Type modelType)
    {
        ArgumentNullException.ThrowIfNull(modelType);
        return CoreMasks.Value.TryGetValue(modelType, out var mask) ? mask : Compute(modelType);
    }

    public static HookMask Compute(Type modelType)
    {
        ArgumentNullException.ThrowIfNull(modelType);
        if (!typeof(AbstractModel).IsAssignableFrom(modelType))
        {
            throw new ArgumentException($"{modelType.FullName} is not an AbstractModel.", nameof(modelType));
        }
        var byName = HooksByName.Value;
        ulong bits0 = 0UL, bits1 = 0UL, bits2 = 0UL;
        foreach (var method in modelType.GetMethods(InstanceMethods))
        {
            if (!method.IsVirtual || method.DeclaringType == typeof(AbstractModel))
            {
                continue;
            }
            var baseDefinition = method.GetBaseDefinition();
            if (baseDefinition.DeclaringType != typeof(AbstractModel))
            {
                continue;
            }
            if (byName.TryGetValue(baseDefinition.Name, out var hook))
            {
                Set(ref bits0, ref bits1, ref bits2, hook);
            }
        }
        return new HookMask(bits0, bits1, bits2);
    }

    private static void Set(ref ulong bits0, ref ulong bits1, ref ulong bits2, HookId hook)
    {
        int bit = (int)hook;
        if ((uint)bit >= HookCount || bit >= 192)
        {
            throw new ArgumentOutOfRangeException(nameof(hook), hook, null);
        }
        if (bit < 64)
        {
            bits0 |= 1UL << bit;
        }
        else if (bit < 128)
        {
            bits1 |= 1UL << (bit - 64);
        }
        else
        {
            bits2 |= 1UL << (bit - 128);
        }
    }

    private static FrozenDictionary<string, HookId> BuildHooksByName()
    {
        var map = new Dictionary<string, HookId>(HookCount, StringComparer.Ordinal);
        foreach (var method in typeof(AbstractModel).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly))
        {
            if (!method.IsVirtual)
            {
                continue;
            }
            if (!Enum.TryParse<HookId>(method.Name, ignoreCase: false, out var hook) || !Enum.IsDefined(hook))
            {
                throw new InvalidOperationException($"AbstractModel.{method.Name} has no HookId.");
            }
            if (!map.TryAdd(method.Name, hook))
            {
                throw new InvalidOperationException($"AbstractModel declares {method.Name} more than once.");
            }
        }
        int hookIds = Enum.GetValues<HookId>().Length;
        if (map.Count != HookCount || hookIds != HookCount)
        {
            throw new InvalidOperationException($"AbstractModel declares {map.Count} hook virtuals; HookId has {hookIds} values; HookMask.HookCount is {HookCount}.");
        }
        return map.ToFrozenDictionary(StringComparer.Ordinal);
    }

    private static FrozenDictionary<Type, HookMask> BuildCoreMasks()
    {
        var map = new Dictionary<Type, HookMask>();
        foreach (var type in typeof(AbstractModel).Assembly.GetTypes())
        {
            if (type.IsClass && !type.IsAbstract && !type.ContainsGenericParameters && type.IsSubclassOf(typeof(AbstractModel)))
            {
                map.Add(type, Compute(type));
            }
        }
        return map.ToFrozenDictionary();
    }
}
