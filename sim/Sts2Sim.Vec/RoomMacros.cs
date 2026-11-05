using Sts2Sim.Core;
using Sts2Sim.Core.Choice;
using Sts2Sim.Core.Run;

namespace Sts2Sim.Vec;

/// <summary>Which room macros an env lists (<c>config_json</c> key <c>macros</c>; default none).</summary>
[Flags]
public enum RoomMacroSet
{
    None = 0,

    /// <summary><c>rest</c>: SMITH at a rest site is one decision per upgradable deck card.</summary>
    Rest = 1,

    /// <summary><c>shop</c>: the shop's card removal is one decision per removable deck card.</summary>
    Shop = 2,

    /// <summary>
    /// <c>prompt</c> (W4 2026-11-04, <see cref="PromptMacros"/>): every pending choice prompt lists no PromptCancel row, a
    /// pick that leaves Confirm as the only way on confirms in the same step, and the rest / shop macro rows fall back to
    /// the plain open row when they would push the list past <see cref="PromptMacros.MaxRows"/>.
    /// </summary>
    Prompt = 4,

    /// <summary><c>all</c>: rest, shop and prompt.</summary>
    All = Rest | Shop | Prompt,
}

/// <summary>
/// Rest-site and shop macro candidates (Vec layer only, opt-in with <c>config_json</c> <c>"macros": ["rest", "shop"]</c>;
/// W3 2026-11-04). Off (the default) the candidate list is exactly what it was. On, the room screens the policy used to
/// loop on (docs/diag-2026-11-04/q4-loopbreaker.md: SMITH → pick → cancel back to the identical rest site, and the same
/// with the shop's removal) take the whole choice in one decision:
/// <list type="bullet">
/// <item><b>rest</b> — at a rest site (loop phase RestSite, no pending prompt) the <c>ChooseRestOption(A)</c> row of a
/// <see cref="SmithRestSiteOption"/> with <c>SmithCount == 1</c> is replaced by one row <c>ChooseRestOption(A, B = d)</c>
/// per upgradable master-deck card d (<c>deck</c> row d; <c>CardInstance.IsUpgradable</c>, the filter of
/// <see cref="RoomPrompts.FromDeckForUpgrade"/>). Executing one runs the game's own clicks: <c>ChooseRestOption(A)</c>
/// (opens the DeckUpgrade prompt), <c>PromptSelect(j)</c> of the option holding that card, <c>PromptConfirm</c>.</item>
/// <item><b>shop</b> — in a shop (loop phase Shop, no pending prompt) the legal <c>BuyEntry(A)</c> row of the
/// <see cref="MerchantCardRemovalEntry"/> is replaced by one row <c>BuyEntry(A, B = d)</c> per removable deck card d
/// (<c>IsRemovable</c>, the filter of <see cref="RoomPrompts.FromDeckForRemoval"/>), expanding to <c>BuyEntry(A)</c>,
/// <c>PromptSelect(j)</c>, <c>PromptConfirm</c>; the price is the entry's (charged by the game as before) and the rows
/// are listed only when the plain purchase is (stocked, enough gold).</item>
/// </list>
/// The macro rows keep the published kinds (<c>ChooseRestOption</c> / <c>BuyEntry</c>): a game action always has
/// <c>B = -1</c> for these kinds, a macro row has <c>B &gt;= 0</c>, so the schema, the RunActionKind enum and the
/// encoder_version are the same with macros on or off, and a checkpoint's spec is compatible either way. The rows are
/// appended after the screen's other candidates (deck order), so the room's own actions (HEAL, purchases, leave) keep
/// their low indices under the trainer's 64-row candidate cut. Other options (HEAL, DIG, LIFT, ...), a SMITH with
/// <c>SmithCount != 1</c>, other prompts (events, cards, relics) and every other screen are unchanged. A macro never
/// improvises: when the game does not open the expected prompt or the card is not among its options the step throws,
/// which the env reports as SIM_ERROR.
/// </summary>
public static class RoomMacros
{
    public static readonly IReadOnlyList<(string Name, RoomMacroSet Flag)> Names = new[] { ("rest", RoomMacroSet.Rest), ("shop", RoomMacroSet.Shop), ("prompt", RoomMacroSet.Prompt) };

    /// <summary>Parses <c>rest</c> / <c>shop</c> / <c>prompt</c> names and the alias <c>all</c> (case-insensitive; empty = none).</summary>
    public static RoomMacroSet Parse(IEnumerable<string> names)
    {
        ArgumentNullException.ThrowIfNull(names);
        var set = RoomMacroSet.None;
        foreach (var raw in names)
        {
            var name = (raw ?? "").Trim().ToLowerInvariant();
            if (name.Length == 0)
            {
                continue;
            }
            if (name == "all")
            {
                set |= RoomMacroSet.All;
                continue;
            }
            var hit = Names.FirstOrDefault(n => n.Name == name);
            if (hit.Name == null)
            {
                throw new ArgumentException($"unknown macro \"{raw}\" (known: {string.Join(", ", Names.Select(n => n.Name))}, all)");
            }
            set |= hit.Flag;
        }
        return set;
    }

    /// <summary>The names of <paramref name="set"/> (Names order).</summary>
    public static IReadOnlyList<string> NamesOf(RoomMacroSet set) => Names.Where(n => (set & n.Flag) != 0).Select(n => n.Name).ToArray();

    /// <summary>A candidate that is a room macro row (a ChooseRestOption / BuyEntry row with a deck card in B).</summary>
    public static bool IsMacro(RunAction a) => a.B >= 0 && a.Kind is RunActionKind.ChooseRestOption or RunActionKind.BuyEntry;

    /// <summary>
    /// Applies the room substitutions of <paramref name="set"/> to the Vec candidate list <paramref name="cands"/> in place
    /// (after <see cref="RewardMacros.Fill"/>, which leaves rest-site and shop screens unchanged). No-op for
    /// <see cref="RoomMacroSet.None"/> and on every other screen.
    /// </summary>
    public static void Apply(RunSim sim, List<RunAction> cands, RoomMacroSet set) => Apply(sim, cands, set, int.MaxValue, out _);

    /// <summary>
    /// <see cref="Apply(RunSim, List{RunAction}, RoomMacroSet)"/> with a row cap: when the substitution would leave more
    /// than <paramref name="maxRows"/> candidates the plain open row stays (the game's sequential flow) and
    /// <paramref name="fellBack"/> is true. <c>int.MaxValue</c> = no cap (the rest / shop behaviour of W3).
    /// </summary>
    public static void Apply(RunSim sim, List<RunAction> cands, RoomMacroSet set, int maxRows, out bool fellBack)
    {
        fellBack = false;
        ArgumentNullException.ThrowIfNull(sim);
        ArgumentNullException.ThrowIfNull(cands);
        if (set == RoomMacroSet.None || sim.HasPendingChoice || sim.IsTerminal)
        {
            return;
        }
        var rs = sim.State;
        if (rs.Players.Count == 0)
        {
            return;
        }
        var player = rs.Players[0];
        var deck = player.Deck;
        if ((set & RoomMacroSet.Rest) != 0 && sim.Loop.Phase == RunPhase.RestSite && RoomStack.CurrentRoom(rs) is RestSiteRoom room)
        {
            int smith = -1, smithRow = -1;
            for (int j = 0; j < cands.Count; j++)
            {
                var a = cands[j];
                if (a.Kind == RunActionKind.ChooseRestOption && a.B < 0 && (uint)a.A < (uint)room.Options.Count
                    && room.Options[a.A] is SmithRestSiteOption { SmithCount: 1 })
                {
                    smith = a.A;
                    smithRow = j;
                    cands.RemoveAt(j);
                    break;
                }
            }
            if (smith >= 0)
            {
                int add = deck.Count(c => c.IsUpgradable);
                if (cands.Count + add > maxRows)
                {
                    cands.Insert(smithRow, RunAction.Of(RunActionKind.ChooseRestOption, smith));
                    fellBack = true;
                    return;
                }
                for (int d = 0; d < deck.Count; d++)
                {
                    if (deck[d].IsUpgradable)
                    {
                        cands.Add(RunAction.Of(RunActionKind.ChooseRestOption, smith, d));
                    }
                }
            }
        }
        else if ((set & RoomMacroSet.Shop) != 0 && sim.Loop.Phase == RunPhase.Shop && !player.IsDead
                 && RoomStack.CurrentRoom(rs) is MerchantRoom { Inventory: { } inventory })
        {
            var entries = inventory.AllEntries;
            int removal = -1, removalRow = -1;
            for (int j = 0; j < cands.Count; j++)
            {
                var a = cands[j];
                if (a.Kind == RunActionKind.BuyEntry && a.B < 0 && (uint)a.A < (uint)entries.Count && entries[a.A] is MerchantCardRemovalEntry)
                {
                    removal = a.A;
                    removalRow = j;
                    cands.RemoveAt(j);
                    break;
                }
            }
            if (removal >= 0)
            {
                int add = deck.Count(c => c.IsRemovable);
                if (cands.Count + add > maxRows)
                {
                    cands.Insert(removalRow, RunAction.Of(RunActionKind.BuyEntry, removal));
                    fellBack = true;
                    return;
                }
                for (int d = 0; d < deck.Count; d++)
                {
                    if (deck[d].IsRemovable)
                    {
                        cands.Add(RunAction.Of(RunActionKind.BuyEntry, removal, d));
                    }
                }
            }
        }
    }

    /// <summary>The Vec candidate list of <paramref name="sim"/> with the room macros <paramref name="set"/> (a new list; tests and tools).</summary>
    public static List<RunAction> Candidates(RunSim sim, RoomMacroSet set)
    {
        var cands = RewardMacros.Candidates(sim);
        Apply(sim, cands, set);
        return cands;
    }

    /// <summary>The prompt kind and site the macro's first click opens.</summary>
    private static (ChoicePromptKind Kind, string Site) PromptOf(RunAction macro) => macro.Kind == RunActionKind.ChooseRestOption
        ? (ChoicePromptKind.DeckUpgrade, RoomPrompts.SmithSite)
        : (ChoicePromptKind.DeckRemove, RoomPrompts.MerchantRemovalSite);

    /// <summary>
    /// Runs room macro <paramref name="macro"/> on <paramref name="sim"/> through <paramref name="apply"/> (the env's
    /// one-action step): the open click <c>Kind(A)</c>, then <c>PromptSelect</c> of the option holding deck card B,
    /// then <c>PromptConfirm</c> while that prompt is still pending. Throws <see cref="InvalidOperationException"/> when
    /// the game diverges from that (fail closed; the env reports SIM_ERROR). A prompt the game opens afterwards (a hook)
    /// is left for the policy.
    /// </summary>
    public static void Execute(RunSim sim, RunAction macro, Action<RunAction> apply)
    {
        ArgumentNullException.ThrowIfNull(sim);
        ArgumentNullException.ThrowIfNull(apply);
        if (!IsMacro(macro))
        {
            throw new ArgumentException($"{macro} is not a room macro", nameof(macro));
        }
        var deck = sim.State.Players[0].Deck;
        if ((uint)macro.B >= (uint)deck.Count)
        {
            throw new InvalidOperationException($"room macro {macro}: deck card {macro.B} outside 0..{deck.Count - 1}");
        }
        int instance = deck[macro.B].InstanceId;
        string cardId = deck[macro.B].Id;
        var (kind, site) = PromptOf(macro);
        apply(RunAction.Of(macro.Kind, macro.A));
        var prompt = sim.PendingChoice;
        if (prompt == null || prompt.Kind != kind || prompt.SiteId != site)
        {
            throw new InvalidOperationException($"room macro {macro} diverged after {macro.Kind}({macro.A}): expected a {kind} prompt at {site}, got "
                + (prompt == null ? "no prompt" : $"{prompt.Kind} at {prompt.SiteId}"));
        }
        int option = -1;
        for (int j = 0; j < prompt.Options.Count; j++)
        {
            if (prompt.Options[j].InstanceId == instance)
            {
                option = j;
                break;
            }
        }
        if (option < 0)
        {
            throw new InvalidOperationException($"room macro {macro}: deck card {macro.B} ({cardId}#{instance}) is not an option of the {kind} prompt");
        }
        apply(RunAction.Of(RunActionKind.PromptSelect, option));
        if (ReferenceEquals(sim.PendingChoice, prompt))
        {
            apply(RunAction.Of(RunActionKind.PromptConfirm));
        }
        if (ReferenceEquals(sim.PendingChoice, prompt))
        {
            throw new InvalidOperationException($"room macro {macro}: the {kind} prompt is still pending after PromptConfirm");
        }
    }
}
