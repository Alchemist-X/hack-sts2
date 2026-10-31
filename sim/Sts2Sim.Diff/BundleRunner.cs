namespace Sts2Sim.Diff;

public enum BundleStatus
{
    Agree,
    HardDivergence,
    Skipped,
    Error,
    SimException
}

public static class DivergenceKind
{
    public const string State = "state";
    public const string Action = "action";
    public const string Terminal = "terminal";
    public const string Final = "final";
}

public sealed class Divergence
{
    public required string Kind { get; init; }

    public int DecisionIndex { get; init; }

    public required FieldDiff First { get; init; }

    public IReadOnlyList<FieldDiff> AllHard { get; init; } = Array.Empty<FieldDiff>();

    public BundleAction? Action { get; init; }

    public JsonNode? OracleProjection { get; init; }

    public JsonNode? SimProjection { get; init; }
}

public readonly record struct SoftDiff(int DecisionIndex, FieldDiff Diff);

public sealed class BundleResult
{
    public string? Path { get; init; }

    public string GameVersion { get; init; } = BundleRunner.UnknownVersion;

    public BundleStatus Status { get; set; }

    public string? Reason { get; set; }

    public string? ExceptionDetail { get; set; }

    public int? ExceptionDecisionIndex { get; set; }

    public int DecisionsTotal { get; set; }

    public int DecisionsCompared { get; set; }

    public int DecisionsAgreed { get; set; }

    public int DecisionsUnobserved { get; set; }

    public Divergence? Divergence { get; set; }

    public List<SoftDiff> SoftDiffs { get; } = new();

    public bool OutcomeChecked { get; set; }

    public bool OutcomeAgreed { get; set; }

    public string? SimOutcome { get; set; }

    public int? SimFinalHp { get; set; }

    public DiffBundle? Bundle { get; init; }

    public JsonNode? FinalSimProjection { get; set; }
}

public static class BundleRunner
{
    public const string UnknownVersion = "unknown";

    public static readonly IReadOnlySet<string> RelicWhitelist = new HashSet<string>(StringComparer.Ordinal)
    {
        "BURNING_BLOOD", "GOLDEN_PEARL", "NUTRITIOUS_OYSTER", "PHIAL_HOLSTER", "WINGED_BOOTS", "STONE_HUMIDIFIER",
        "LAVA_ROCK", "SILVER_CRUCIBLE", "LOST_COFFER", "KALEIDOSCOPE", "NEOWS_TALISMAN", "POMANDER", "NEW_LEAF",
        "FISHING_ROD", "CURSED_PEARL", "PRECISE_SCISSORS", "PRECARIOUS_SHEARS", "DOWSING_ROD", "NEOWS_BONES",
        "NEOWS_SACRIFICE", "HEFTY_TABLET", "ARCANE_SCROLL", "SCROLL_BOXES", "LEAD_PAPERWEIGHT", "LEAFY_POULTICE",
        "NEOWS_TORMENT", "LARGE_CAPSULE"
    };

    public static string OutcomeName(CombatOutcome outcome) => outcome switch
    {
        CombatOutcome.Victory => DiffBundle.OutcomeWin,
        CombatOutcome.Defeat => DiffBundle.OutcomeLoss,
        _ => DiffBundle.OutcomeAbandoned
    };

    public static string? IneligibleReason(DiffBundle b) => IneligibleReason(b, includeExporterIneligible: false);

    // The harness gate (what the sim can build) comes first; then the exporter's own verdict (`ineligible_reasons`:
    // deck_mismatch, potion_used, unsupported_action, pre_state_hand_unsettled, ... that the bundle body cannot show),
    // which `includeExporterIneligible` (CLI --include-ineligible) overrides for diagnostic replays.
    public static string? IneligibleReason(DiffBundle b, bool includeExporterIneligible)
    {
        string? gate = SimGateReason(b);
        if (gate != null)
        {
            return gate;
        }
        if (!includeExporterIneligible && b.IneligibleReasons.Count > 0)
        {
            return $"the exporter marked this attempt ineligible ({string.Join("; ", b.IneligibleReasons)}); --include-ineligible replays it anyway";
        }
        return null;
    }

    private static string? SimGateReason(DiffBundle b)
    {
        if (!string.Equals(b.Character, StartingDecks.IroncladCharacterId, StringComparison.Ordinal))
        {
            return $"character {b.Character} is not supported (the sim builds an Ironclad player)";
        }
        if (RngSetup.HasOldPrefix(b.Seed))
        {
            return $"seed '{b.Seed}' carries the '{RngSeeds.OldSeedPrefix}' prefix (cross-version continuation; streams not derivable)";
        }
        if (!EncounterRegistry.Defs.Contains(b.EncounterId))
        {
            return $"encounter {b.EncounterId} is not ported";
        }
        foreach (var card in b.Deck)
        {
            if (!CardRegistry.Defs.Contains(card.Id))
            {
                return $"card {card.Id} is not ported";
            }
        }
        foreach (var relic in b.Relics)
        {
            if (!RelicWhitelist.Contains(relic))
            {
                return $"relic {relic} is not on the combat-inert whitelist";
            }
        }
        return null;
    }

    public static CardSpec[] DeckSpecs(DiffBundle b)
    {
        var specs = new CardSpec[b.Deck.Count];
        for (int i = 0; i < specs.Length; i++)
        {
            specs[i] = new CardSpec(b.Deck[i].Id, b.Deck[i].Upgraded);
        }
        return specs;
    }

    // Integration: the encounter-private Rng (SLIMES_WEAK composition, CORPSE_SLUGS_WEAK opener) is seeded
    // from the run seed, the combat's TotalFloor and the seeding scheme (sim/specs/rng-encounter-private.md
    // §2-§3, open point §8). Bundles carry 1-based source.act and source.floor; absent values fall back to
    // the first-combat defaults (act 1, floor 2) that the contract's scope implies.
    public static CombatRunContext RunContext(DiffBundle b) => CombatRunContext.ForAct(
        b.Source.Act ?? CombatRunContext.DefaultActIndex + 1,
        b.Source.Floor ?? CombatRunContext.DefaultTotalFloor,
        null,
        null,
        CombatRunContext.ParseSeeding(b.Seeding));

    public static CombatSim CreateSim(DiffBundle b)
    {
        var rng = RngSetup.CreateForBundle(b);
        return CombatSim.Create(rng, DeckSpecs(b), b.EncounterId, b.Ascension, b.PlayerHp, b.PlayerMaxHp, RunContext(b));
    }

    public static string? BundleInconsistency(DiffBundle b)
    {
        foreach (var d in b.Decisions)
        {
            var a = d.Action;
            if (a.Type != BundleActionType.PlayCard || d.PreState == null)
            {
                continue;
            }
            var hand = d.PreState.Player.Hand;
            if (a.HandIndex >= hand.Count)
            {
                return $"decision {d.Index}: hand_index {a.HandIndex} is outside pre_state hand (size {hand.Count})";
            }
            var card = hand[a.HandIndex];
            if (a.CardId != null && !string.Equals(card.Id, a.CardId, StringComparison.Ordinal))
            {
                return $"decision {d.Index}: action card_id {a.CardId} but pre_state hand[{a.HandIndex}] is {card.Id}";
            }
            if (a.Upgraded is bool u && u != card.Upgraded)
            {
                return $"decision {d.Index}: action upgraded={u} but pre_state hand[{a.HandIndex}] upgraded={card.Upgraded}";
            }
        }
        return null;
    }

    public static BundleResult Run(DiffBundle b) => Run(b, includeExporterIneligible: false);

    public static BundleResult Run(DiffBundle b, bool includeExporterIneligible)
    {
        ArgumentNullException.ThrowIfNull(b);
        var result = new BundleResult
        {
            Path = b.FilePath,
            GameVersion = b.GameVersion,
            Bundle = b,
            DecisionsTotal = b.Decisions.Count
        };
        string? skip = IneligibleReason(b, includeExporterIneligible);
        if (skip != null)
        {
            result.Status = BundleStatus.Skipped;
            result.Reason = skip;
            return result;
        }
        string? inconsistency = BundleInconsistency(b);
        if (inconsistency != null)
        {
            result.Status = BundleStatus.Error;
            result.Reason = "bundle inconsistent: " + inconsistency;
            return result;
        }

        CombatSim sim;
        try
        {
            sim = CreateSim(b);
        }
        catch (ArgumentException ex)
        {
            result.Status = BundleStatus.Error;
            result.Reason = "sim setup rejected the bundle inputs: " + ex.Message;
            return result;
        }
        catch (Exception ex)
        {
            return Crash(result, ex, -1, "CombatSim.Create");
        }

        for (int i = 0; i < b.Decisions.Count; i++)
        {
            var d = b.Decisions[i];
            if (sim.IsTerminal)
            {
                var diff = new FieldDiff("sim.outcome", "in_progress", OutcomeName(sim.Outcome), DiffSeverity.Hard,
                    "the oracle still has a decision here but the sim combat is over");
                return Diverged(result, DivergenceKind.Terminal, i, diff, new[] { diff }, d, SafeProjectJson(sim));
            }

            Projection simProjection;
            try
            {
                simProjection = SimProjector.Project(sim.State);
            }
            catch (ProjectionGapException ex)
            {
                result.Status = BundleStatus.Error;
                result.Reason = $"decision {i}: projection gap: {ex.Message}";
                return result;
            }
            catch (Exception ex)
            {
                return Crash(result, ex, i, "SimProjector.Project");
            }

            bool observed = d.PreState != null;
            if (observed)
            {
                var diffs = ProjectionComparer.Compare(d.PreState!, simProjection);
                result.DecisionsCompared++;
                foreach (var diff in diffs)
                {
                    if (diff.Severity == DiffSeverity.Soft)
                    {
                        result.SoftDiffs.Add(new SoftDiff(i, diff));
                    }
                }
                var hard = diffs.Where(x => x.Severity == DiffSeverity.Hard).ToList();
                if (hard.Count > 0)
                {
                    return Diverged(result, DivergenceKind.State, i, hard[0], hard, d, ProjectionJson.Write(simProjection));
                }
            }
            else
            {
                result.DecisionsUnobserved++;
            }

            if (!TryMapAction(sim.State, d.Action, out var action, out var actionDiff))
            {
                return Diverged(result, DivergenceKind.Action, i, actionDiff!, new[] { actionDiff! }, d, ProjectionJson.Write(simProjection));
            }

            try
            {
                sim.Step(action);
            }
            catch (Exception ex)
            {
                return Crash(result, ex, i, $"CombatSim.Step({action})");
            }
            if (observed)
            {
                result.DecisionsAgreed++;
            }
        }

        result.SimOutcome = OutcomeName(sim.Outcome);
        result.SimFinalHp = sim.State.Player.CurrentHp;
        result.FinalSimProjection = SafeProjectJson(sim);
        result.OutcomeChecked = true;
        var finalDiffs = new List<FieldDiff>();
        if (!string.Equals(b.Outcome, result.SimOutcome, StringComparison.Ordinal))
        {
            finalDiffs.Add(new FieldDiff("outcome", b.Outcome, result.SimOutcome, DiffSeverity.Hard));
        }
        if (b.FinalHp is int finalHp && finalHp != result.SimFinalHp)
        {
            finalDiffs.Add(new FieldDiff("final_hp", finalHp, result.SimFinalHp, DiffSeverity.Hard));
        }
        result.OutcomeAgreed = finalDiffs.Count == 0;
        if (finalDiffs.Count > 0)
        {
            result.Status = BundleStatus.HardDivergence;
            result.Divergence = new Divergence
            {
                Kind = DivergenceKind.Final,
                DecisionIndex = b.Decisions.Count,
                First = finalDiffs[0],
                AllHard = finalDiffs,
                SimProjection = result.FinalSimProjection
            };
            return result;
        }
        result.Status = BundleStatus.Agree;
        return result;
    }

    public static bool TryMapAction(CombatState cs, BundleAction a, out GameAction action, out FieldDiff? diff)
    {
        diff = null;
        if (a.Type == BundleActionType.EndTurn)
        {
            action = GameAction.EndTurn;
        }
        else
        {
            action = default;
            var hand = cs.Player.Hand;
            if (a.HandIndex >= hand.Count)
            {
                diff = new FieldDiff("action.hand_index", a.HandIndex, hand.Count, DiffSeverity.Hard, "hand index is outside the sim hand (sim value = hand size)");
                return false;
            }
            var card = hand[a.HandIndex];
            if (a.CardId != null && !string.Equals(card.Id, a.CardId, StringComparison.Ordinal))
            {
                diff = new FieldDiff("action.card_id", a.CardId, card.Id, DiffSeverity.Hard);
                return false;
            }
            if (a.Upgraded is bool upgraded && upgraded != card.IsUpgraded)
            {
                diff = new FieldDiff("action.upgraded", upgraded, card.IsUpgraded, DiffSeverity.Hard);
                return false;
            }
            int targetIndex = GameAction.NoIndex;
            if (card.TargetType == TargetType.AnyEnemy)
            {
                if (a.TargetCombatId is not int targetId)
                {
                    diff = new FieldDiff("action.target_combat_id", null, null, DiffSeverity.Hard, $"{card.Id} needs an enemy target but the oracle action has none");
                    return false;
                }
                targetIndex = IndexOfMonster(cs, targetId);
                if (targetIndex < 0)
                {
                    diff = new FieldDiff("action.target_combat_id", targetId, null, DiffSeverity.Hard, "no sim monster has this combat id");
                    return false;
                }
            }
            action = GameAction.Play(a.HandIndex, targetIndex);
        }
        if (!LegalActionGenerator.TryResolve(cs, action, out _, out var error))
        {
            diff = new FieldDiff("action.legal", true, false, DiffSeverity.Hard, $"sim rejects {action}: {error}");
            return false;
        }
        return true;
    }

    public static int IndexOfMonster(CombatState cs, int combatId)
    {
        var monsters = cs.Monsters;
        for (int i = 0; i < monsters.Count; i++)
        {
            if (monsters[i].CombatId == combatId)
            {
                return i;
            }
        }
        return -1;
    }

    private static BundleResult Diverged(BundleResult result, string kind, int index, FieldDiff first, IReadOnlyList<FieldDiff> all, BundleDecision d, JsonNode? simProjection)
    {
        result.Status = BundleStatus.HardDivergence;
        result.Divergence = new Divergence
        {
            Kind = kind,
            DecisionIndex = index,
            First = first,
            AllHard = all,
            Action = d.Action,
            OracleProjection = d.PreStateJson?.DeepClone() ?? (d.PreState != null ? ProjectionJson.Write(d.PreState) : null),
            SimProjection = simProjection
        };
        return result;
    }

    private static BundleResult Crash(BundleResult result, Exception ex, int decisionIndex, string where)
    {
        result.Status = BundleStatus.SimException;
        result.ExceptionDecisionIndex = decisionIndex;
        result.Reason = $"{where} threw {ex.GetType().Name}: {ex.Message}";
        result.ExceptionDetail = ex.ToString();
        return result;
    }

    private static JsonNode? SafeProjectJson(CombatSim sim)
    {
        try
        {
            return ProjectionJson.Write(SimProjector.Project(sim.State));
        }
        catch (Exception ex)
        {
            return JsonValue.Create($"<projection failed: {ex.GetType().Name}: {ex.Message}>");
        }
    }
}
