namespace Sts2Sim.Core.Combat.Pipeline;

// A player-view rule the projection has no port for yet (CONTRACTS.md D9). Thrown by the strict PlayerView helpers;
// Sts2Sim.Diff's ProjectionGapException derives from it.
public class PlayerViewGapException : Exception
{
    public PlayerViewGapException(string message)
        : base(message)
    {
    }
}

// One intent as the game's UI shows it (MegaCrit.Sts2.Core.MonsterMoves.Intents): the type, the per-hit damage
// after modifiers and the hit count for attack intents, the card count for status-card intents. Never the move id.
public readonly record struct IntentView(IntentType Type, int? Damage, int? Times, int? CardCount);

// The card's energy cost as shown: X, or the amount to spend now.
public readonly record struct CostView(bool IsX, int Value);

// Player-view helpers shared by the diff projection (Sts2Sim.Diff/SimProjector.cs), the play server
// (Sts2Sim.Server/Describe.cs) and the vector env encoder (Sts2Sim.Vec). Moved from SimProjector (D9) with the same
// semantics: the strict IsVisible / DisplayAmount throw PlayerViewGapException where no rule is ported. The *ToPlayer
// variants never throw for a gap: they apply the game's rule where Core carries the state for it (decompile citations
// inline) and otherwise fall back to the raw amount. Pure: no state writes, no RNG.
public static class PlayerView
{
    public static readonly IReadOnlySet<string> PowersWithDisplayAmountOverride = new HashSet<string>(StringComparer.Ordinal)
    {
        "AUTOMATION_POWER", "CACOPHONY_POWER", "FERAL_POWER", "MONOLOGUE_POWER", "ORBIT_POWER", "PALE_BLUE_DOT_POWER",
        "PANACHE_POWER", "SLOTH_POWER", "TAG_TEAM_POWER", "WITHERING_PRESENCE_POWER"
    };

    public static readonly IReadOnlySet<string> PowersWithVisibilityOverride = new HashSet<string>(StringComparer.Ordinal)
    {
        "AMBERGRIS_POWER"
    };

    public static bool IsVisible(CombatState cs, PowerModel power)
    {
        if (PowersWithVisibilityOverride.Contains(power.Id))
        {
            throw new PlayerViewGapException($"{power.Id} overrides PowerModel.IsVisibleInternal in the game; the diff projection has no rule for it yet");
        }
        return IsVisibleByTarget(cs, power);
    }

    // IsVisible without the gap (Models/PowerModel.cs:161): the model's IsVisibleInternal (AMBERGRIS is hidden,
    // Models.Powers/AmbergrisPower.cs:87 `IsVisibleInternal => false`), then the target rule.
    public static bool IsVisibleToPlayer(CombatState cs, PowerModel power)
    {
        if (!power.IsVisible)
        {
            return false;
        }
        return IsVisibleByTarget(cs, power);
    }

    private static bool IsVisibleByTarget(CombatState cs, PowerModel power)
    {
        int targetId = power.TargetId;
        if (targetId == PowerModel.NoCreature || targetId == cs.Player.CombatId)
        {
            return true;
        }
        var target = cs.GetCreatureById(targetId);
        return target == null || target.IsEnemy;
    }

    public static int DisplayAmount(PowerModel power)
    {
        if (TryMappedDisplayAmount(power, out int amount))
        {
            return amount;
        }
        if (PowersWithDisplayAmountOverride.Contains(power.Id))
        {
            throw new PlayerViewGapException($"{power.Id} overrides PowerModel.DisplayAmount in the game; the diff projection has no rule for it yet");
        }
        return power.Amount;
    }

    // DisplayAmount without the gap: the remaining overrides read the Core port's state (CACOPHONY and TAG_TEAM are
    // not ported and fall back to Amount).
    public static int DisplayAmountToPlayer(CombatState cs, PowerModel power)
    {
        if (TryMappedDisplayAmount(power, out int amount))
        {
            return amount;
        }
        switch (power)
        {
            // Models.Powers/AutomationPower.cs:111 `GetInternalData<Data>().cardsLeft`
            case AutomationPower automation:
                return automation.DisplayAmount;
            // Models.Powers/FeralPower.cs:26 `Max(0, Amount - zeroCostAttacksPlayed)`
            case FeralPower feral:
                return feral.DisplayAmount;
            // Models.Powers/MonologuePower.cs:218 `DynamicVars["StrengthApplied"].IntValue`
            case MonologuePower monologue:
                return (int)monologue.StrengthApplied;
            // Models.Powers/OrbitPower.cs:118 `4 - energySpent % 4`
            case OrbitPower orbit:
                return 4 - orbit.EnergySpent % 4;
            // Models.Powers/PanachePower.cs:154 `DynamicVars["CardsLeft"].IntValue`
            case PanachePower panache:
                return panache.CardsLeft;
            // Models.Powers/SlothPower.cs:20 `_cardsPlayedThisTurn`
            case SlothPower sloth:
                return sloth.CardsPlayedThisTurn;
            // Models.Powers/PaleBlueDotPower.cs:139 `Max(0, 5 - AttacksPlayedThisTurn)` (:147: card plays finished
            // this turn by the owner, as the Core port counts them in AfterCardPlayed)
            case PaleBlueDotPower:
            {
                var owner = power.Owner;
                int played = cs.History.CountOf<CardPlayFinishedEntry>(c => c.Stamp.HappenedThisTurn(cs) && owner is PlayerState p && c.CardPlay.PlayerId == p.CombatId);
                return Math.Max(0, PaleBlueDotPower.CardPlayThresholdValue - played);
            }
            // Models.Powers/WitheringPresencePower.cs:133 `DynamicVars["CardsLeft"].IntValue`
            case WitheringPresencePower withering:
                return (int)withering.CardsLeft;
        }
        return power.Amount;
    }

    private static bool TryMappedDisplayAmount(PowerModel power, out int amount)
    {
        switch (power)
        {
            // Models.Powers/HardenedShellPower.cs:25 `Max(0, Amount - damageReceivedThisTurn)`
            case HardenedShellPower shell:
                amount = (int)Math.Max(0m, shell.Amount - shell.DamageReceivedThisTurn);
                return true;
            // Models.Powers/SlowPower.cs:24 `DynamicVars["SlowAmount"].IntValue * 10`
            case SlowPower slow:
                amount = slow.SlowAmount * 10;
                return true;
            // Models.Powers/TenderPower.cs:229 `CardsPlayedThisTurn`
            case TenderPower tender:
                amount = tender.CardsPlayedThisTurn;
                return true;
        }
        amount = 0;
        return false;
    }

    public static IntentView Intent(CombatState cs, MonsterState owner, AbstractIntent intent, IReadOnlyList<CreatureState> targets)
    {
        var type = intent.IntentType;
        switch (intent)
        {
            case AttackIntent attack:
                return AttackIntentView(cs, owner, attack, targets);
            case StatusIntent status:
                return StatusIntentView(status);
            default:
                return new IntentView(type, null, null, null);
        }
    }

    public static IntentView AttackIntentView(CombatState cs, MonsterState owner, AttackIntent attack, IReadOnlyList<CreatureState> targets)
        => new(attack.IntentType, attack.GetSingleDamage(cs, targets, owner), attack.GetRepeats(cs, owner), null);

    public static IntentView StatusIntentView(StatusIntent status)
        => new(status.IntentType, null, null, status.CardCount);

    public static CostView CostOf(CombatState cs, CardInstance card)
        => card.EnergyCost.CostsX ? new CostView(true, 0) : new CostView(false, card.EnergyCost.GetAmountToSpend(cs, card));

    public static bool CanPlay(CombatState cs, CardInstance card)
        => card.CanPlay(cs, out var reason) && reason == UnplayableReason.None;

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Type, int> HitCountConstants = new();

    private const int NoHitCountConstant = int.MinValue;

    // Hits per play as the play server reports them: the card's Repeat var, else its def's public const int HitCount,
    // else 1 (X cost: null). Cards that compute the hit count from other state at play time report 1.
    public static int? HitCount(CardInstance card)
    {
        if (card.Vars.TryGet(DynamicVarSet.RepeatName) is { } repeat)
        {
            return repeat.IntValue;
        }
        var type = card.Def.GetType();
        if (!HitCountConstants.TryGetValue(type, out int n))
        {
            n = NoHitCountConstant;
            var field = type.GetField("HitCount", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            if (field != null && field.IsLiteral && field.GetRawConstantValue() is int value)
            {
                n = value;
            }
            HitCountConstants.TryAdd(type, n);
        }
        if (n != NoHitCountConstant)
        {
            return n;
        }
        return card.EnergyCost.CostsX ? null : 1;
    }
}
