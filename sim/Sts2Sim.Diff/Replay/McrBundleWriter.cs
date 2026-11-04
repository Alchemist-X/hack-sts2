using Sts2Sim.Core.Run;

namespace Sts2Sim.Diff.Replay;

public sealed record McrConversion(JsonObject Bundle, List<string> IneligibleReasons, string? GateReason);

public static partial class McrBundleConverter
{
    private const string FinishedPlayCard = "finished action execution PlayCardAction";
    private const string TurnPhaseOneEnd = "After player turn phase one end";

    private static readonly Dictionary<string, string> RunStreamNames =
        Enum.GetValues<McrRunRngType>().ToDictionary(t => Sts2Sim.Core.Random.DeterministicHash.SnakeCase(t.ToString()), t => t.ToString(), StringComparer.Ordinal);

    public static string Seeding(McrReplay replay) => replay.Table.Legacy ? RngSetup.SeedingLegacyV18 : RngSetup.SeedingXxh64;

    private static string CardKey(SavedCard c) => c.Id + (c.CurrentUpgradeLevel > 0 ? "+" : string.Empty);

    private static bool SameInstanceShape(SavedCard a, SavedCard b)
        => a.Id == b.Id && a.CurrentUpgradeLevel == b.CurrentUpgradeLevel && a.Enchantment?.Id == b.Enchantment?.Id
           && (a.Props == null || a.Props.IsEmpty) && (b.Props == null || b.Props.IsEmpty);

    public static int? ResolveHandIndex(McrPlayerState pre, McrPlayerState? post, string cardId, out string? reason)
    {
        reason = null;
        var hand = pre.Pile(McrPileType.Hand)?.Cards ?? new List<McrCardState>();
        var candidates = Enumerable.Range(0, hand.Count).Where(i => hand[i].Card.Id == cardId).ToList();
        if (candidates.Count == 0)
        {
            reason = $"card {cardId} not in pre-state hand";
            return null;
        }
        if (candidates.All(i => SameInstanceShape(hand[i].Card, hand[candidates[0]].Card)))
        {
            return candidates[0];
        }
        var after = post?.Pile(McrPileType.Hand)?.Cards;
        if (after != null)
        {
            for (int i = 0; i < hand.Count; i++)
            {
                if (i >= after.Count || !SameInstanceShape(hand[i].Card, after[i].Card))
                {
                    if (hand[i].Card.Id == cardId)
                    {
                        return i;
                    }
                    break;
                }
            }
        }
        reason = $"card {cardId}: {candidates.Count} non-identical copies in hand and the post-action hand does not single one out";
        return null;
    }

    public static JsonObject ChecksumProjection(McrChecksum c)
    {
        var p = c.State.Players.FirstOrDefault();
        var player = c.State.Creatures.FirstOrDefault(x => x.PlayerId != null);
        JsonArray Keys(McrPileType t) => new((p?.Pile(t)?.Cards ?? new()).Select(x => (JsonNode?)JsonValue.Create(CardKey(x.Card))).ToArray());
        JsonArray Powers(McrCreature cr) => new(cr.Powers.Select(pw => (JsonNode?)new JsonObject { ["id"] = pw.Id, ["amount"] = pw.Amount }).ToArray());
        return new JsonObject
        {
            ["checksum_id"] = c.Id,
            ["context"] = c.Context,
            ["turn"] = p?.TurnNumber,
            ["phase"] = p?.Phase.ToString(),
            ["energy"] = p?.Energy,
            ["hp"] = player?.CurrentHp,
            ["max_hp"] = player?.MaxHp,
            ["block"] = player?.Block,
            ["player_powers"] = player != null ? Powers(player) : null,
            ["hand"] = Keys(McrPileType.Hand),
            ["draw"] = Keys(McrPileType.Draw),
            ["discard"] = Keys(McrPileType.Discard),
            ["exhaust"] = Keys(McrPileType.Exhaust),
            ["enemies"] = new JsonArray(c.State.Creatures.Where(x => x.MonsterId != null).Select(m => (JsonNode?)new JsonObject
            {
                ["monster_id"] = m.MonsterId,
                ["hp"] = m.CurrentHp,
                ["max_hp"] = m.MaxHp,
                ["block"] = m.Block,
                ["powers"] = Powers(m)
            }).ToArray())
        };
    }

    public static McrConversion Convert(McrReplay replay, string? session, string? part)
    {
        var reasons = new List<string>();
        var run = replay.Run;
        var p = replay.Player;
        if (!replay.HashVerified)
        {
            reasons.Add($"model_table_unverified:{replay.Version}");
        }
        if (run.Players.Count != 1)
        {
            reasons.Add("multiplayer");
        }
        if (run.GameMode != "standard" || run.DailyTime != null || run.Modifiers.Count > 0)
        {
            reasons.Add("non_standard_run");
        }
        var enc = ResolveEncounter(replay);
        if (enc.EncounterId == null)
        {
            reasons.Add("encounter_unresolved:" + enc.Reason);
        }
        if (p != null && p.Deck.Any(c => c.Enchantment != null))
        {
            reasons.Add("enchanted_card_in_deck");
        }
        if (replay.Checksums.Count == 0)
        {
            reasons.Add("no_checksum_states");
        }

        var decisions = new JsonArray();
        int cp = 0;
        var checks = replay.Checksums;
        McrChecksum? lastPre = checks.Count > 0 ? checks[0] : null;
        foreach (var e in replay.Events)
        {
            if (e.Type == McrEventType.PlayerChoice)
            {
                reasons.Add("unsupported_action:player_choice");
                continue;
            }
            if (e.Type != McrEventType.GameAction)
            {
                continue;
            }
            switch (e.Action)
            {
                case McrPlayCard play:
                {
                    int j = -1;
                    for (int k = cp; k < checks.Count; k++)
                    {
                        if (checks[k].Context.StartsWith(FinishedPlayCard, StringComparison.Ordinal)
                            && checks[k].Context.Contains($" index: {play.CombatCardIndex} ", StringComparison.Ordinal))
                        {
                            j = k;
                            break;
                        }
                    }
                    McrChecksum? pre = j > 0 ? checks[j - 1] : (cp > 0 && cp - 1 < checks.Count ? checks[cp - 1] : lastPre);
                    McrChecksum? post = j >= 0 ? checks[j] : null;
                    if (j >= 0)
                    {
                        cp = j + 1;
                    }
                    int? handIndex = null;
                    string? why = "no checksum state before this play";
                    var prePlayer = pre?.State.Players.FirstOrDefault();
                    if (prePlayer != null && play.CardId != null)
                    {
                        handIndex = ResolveHandIndex(prePlayer, post?.State.Players.FirstOrDefault(), play.CardId, out why);
                    }
                    if (handIndex == null)
                    {
                        reasons.Add("hand_index_unresolved");
                    }
                    var hand = prePlayer?.Pile(McrPileType.Hand)?.Cards;
                    bool? upgraded = handIndex is int hi && hand != null ? hand[hi].Card.CurrentUpgradeLevel > 0 : null;
                    var action = new JsonObject
                    {
                        ["type"] = BundleAction.PlayCardType,
                        ["hand_index"] = handIndex ?? 0,
                        ["card_id"] = play.CardId
                    };
                    if (upgraded is bool u)
                    {
                        action["upgraded"] = u;
                    }
                    action["target_combat_id"] = play.TargetId is uint t ? JsonValue.Create((int)t) : null;
                    var oracle = new JsonObject
                    {
                        ["source"] = "mcr",
                        ["combat_card_index"] = play.CombatCardIndex,
                        ["unobserved_reason"] = "mcr checksum states lack intents / can_play / cost; pre_state left unobserved",
                        ["checksum_state"] = pre != null ? ChecksumProjection(pre) : null
                    };
                    if (why != null && handIndex == null)
                    {
                        oracle["hand_index_reason"] = why;
                    }
                    decisions.Add(new JsonObject { ["index"] = decisions.Count, ["pre_state"] = null, ["oracle"] = oracle, ["action"] = action });
                    if (pre != null)
                    {
                        lastPre = post ?? pre;
                    }
                    break;
                }
                case McrEndPlayerTurn:
                {
                    McrChecksum? pre = cp > 0 && cp - 1 < checks.Count ? checks[cp - 1] : lastPre;
                    for (int k = cp; k < checks.Count; k++)
                    {
                        if (checks[k].Context == TurnPhaseOneEnd)
                        {
                            pre = k > 0 ? checks[k - 1] : null;
                            cp = k + 1;
                            break;
                        }
                    }
                    var oracle = new JsonObject
                    {
                        ["source"] = "mcr",
                        ["unobserved_reason"] = "mcr checksum states lack intents / can_play / cost; pre_state left unobserved",
                        ["checksum_state"] = pre != null ? ChecksumProjection(pre) : null
                    };
                    decisions.Add(new JsonObject { ["index"] = decisions.Count, ["pre_state"] = null, ["oracle"] = oracle, ["action"] = new JsonObject { ["type"] = BundleAction.EndTurnType } });
                    break;
                }
                case McrReadyToBeginEnemyTurn:
                    break;
                case McrUsePotion:
                    reasons.Add("unsupported_action:use_potion");
                    break;
                case McrDiscardPotion:
                    reasons.Add("unsupported_action:discard_potion");
                    break;
                case McrUndoEndPlayerTurn:
                    reasons.Add("unsupported_action:undo_end_turn");
                    break;
                default:
                    reasons.Add("unsupported_action:" + e.Action!.Name);
                    break;
            }
        }

        string outcome = DiffBundle.OutcomeAbandoned;
        int? finalHp = null;
        if (checks.Count > 0)
        {
            var last = checks[^1].State;
            var player = last.Creatures.FirstOrDefault(x => x.PlayerId != null);
            finalHp = player?.CurrentHp ?? 0;
            if (player == null || player.CurrentHp <= 0)
            {
                outcome = DiffBundle.OutcomeLoss;
            }
            else if (!last.Creatures.Any(x => x.MonsterId != null && x.CurrentHp > 0))
            {
                outcome = DiffBundle.OutcomeWin;
            }
        }
        if (outcome == DiffBundle.OutcomeAbandoned)
        {
            reasons.Add("outcome_not_terminal");
        }

        var offsets = new JsonObject();
        foreach (var (name, snap) in run.Rng.Rngs.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            if (snap.Counter != 0 && RunStreamNames.TryGetValue(name, out var pascal))
            {
                offsets[pascal] = snap.Counter;
            }
        }
        var deck = new JsonArray((p?.Deck ?? new()).Select(c => (JsonNode?)new JsonObject { ["id"] = c.Id, ["upgraded"] = c.CurrentUpgradeLevel > 0 }).ToArray());
        var relics = new JsonArray((p?.Relics ?? new()).Select(r => (JsonNode?)JsonValue.Create(r.Id)).ToArray());
        // The relics' run-time state (SavedRelic.Props from the initial SerializableRun), aligned with `relics`. Card-valued
        // properties (a relic holding a card) are not carried: that relic starts fresh and the bundle says so.
        var relicProps = new List<SavedProperties?>();
        foreach (var r in p?.Relics ?? new())
        {
            bool cardValued = r.Props is { } rp && (rp.Cards.Count > 0 || rp.CardArrays.Count > 0);
            relicProps.Add(cardValued ? null : r.Props);
            if (cardValued)
            {
                reasons.Add("relic_props_card_valued:" + r.Id);
            }
        }
        var distinct = reasons.Distinct(StringComparer.Ordinal).ToList();
        var bundle = new JsonObject
        {
            ["bundle_version"] = BundleJson.SupportedVersion,
            ["source"] = new JsonObject
            {
                ["session"] = session,
                ["part"] = part,
                ["attempt"] = 1,
                ["act"] = run.CurrentActIndex + 1,
                ["floor"] = replay.TotalFloor + 1,
                ["origin"] = "native/replay.mcr",
                ["mcr_hash_verified"] = replay.HashVerified,
                ["encounter_resolution"] = enc.Reason
            },
            ["game_version"] = replay.Version,
            ["seeding"] = Seeding(replay),
            ["seed"] = run.Rng.Seed,
            ["character"] = p?.CharacterId ?? McrModelNetIds.NoneEntry,
            ["ascension"] = run.Ascension,
            ["encounter_id"] = enc.EncounterId ?? McrModelNetIds.NoneEntry,
            ["player"] = new JsonObject { ["hp"] = p?.CurrentHp ?? 0, ["max_hp"] = p?.MaxHp ?? 0 },
            ["deck"] = deck,
            ["relics"] = relics,
            ["rng_offsets"] = offsets,
            ["decisions"] = decisions,
            ["outcome"] = outcome,
            ["final_hp"] = finalHp
        };
        if (relicProps.Any(x => x is { IsEmpty: false }))
        {
            bundle[BundleJson.RelicPropsKey] = SavedPropsJson.WriteList(relicProps);
        }
        if (distinct.Count > 0)
        {
            bundle[BundleJson.IneligibleReasonsKey] = new JsonArray(distinct.Select(r => (JsonNode?)JsonValue.Create(r)).ToArray());
        }
        string? gate = null;
        try
        {
            using var doc = JsonDocument.Parse(bundle.ToJsonString());
            gate = BundleRunner.IneligibleReason(BundleJson.Read(doc.RootElement), includeExporterIneligible: true);
        }
        catch (BundleFormatException ex)
        {
            gate = "bundle format: " + ex.Message;
        }
        return new McrConversion(bundle, distinct, gate);
    }

    private static (string? Session, string? Part) SessionOf(string file)
    {
        var native = Path.GetDirectoryName(file);
        var partDir = native != null ? Path.GetDirectoryName(native) : null;
        string? part = partDir != null ? Path.GetFileName(partDir) : null;
        string? session = part;
        int idx = part?.LastIndexOf("-part", StringComparison.Ordinal) ?? -1;
        if (part != null && idx > 0)
        {
            session = part[..idx];
        }
        return (session, part);
    }

    public static int ConvertMain(string[] args)
    {
        if (args.Length != 2)
        {
            Console.Error.WriteLine("usage: Sts2Sim.Diff replay-convert <file.mcr|dir> <out-dir>");
            return 2;
        }
        var files = ReplayCli.DiscoverReplays(args[0]);
        string outDir = Path.GetFullPath(args[1]);
        Directory.CreateDirectory(Path.Combine(outDir, "eligible"));
        Directory.CreateDirectory(Path.Combine(outDir, "ineligible"));
        int decoded = 0;
        int failed = 0;
        int eligible = 0;
        int contentEligible = 0;
        var reasonCounts = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var gateCounts = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var index = new JsonArray();
        var opts = new JsonSerializerOptions { WriteIndented = true };
        foreach (string file in files)
        {
            McrReplay replay;
            try
            {
                replay = McrReader.ReadFile(file);
            }
            catch (Exception ex) when (ex is McrFormatException or IOException or UnauthorizedAccessException)
            {
                failed++;
                index.Add(new JsonObject { ["file"] = file, ["error"] = ex.Message });
                continue;
            }
            decoded++;
            var (session, part) = SessionOf(file);
            var conv = Convert(replay, session, part);
            if (conv.GateReason == null)
            {
                contentEligible++;
            }
            bool ok = conv.GateReason == null && conv.IneligibleReasons.Count == 0;
            if (ok)
            {
                eligible++;
            }
            foreach (var r in conv.IneligibleReasons)
            {
                string key = r.Split(':')[0] + (r.StartsWith("unsupported_action", StringComparison.Ordinal) ? ":" + r.Split(':')[1] : string.Empty);
                reasonCounts[key] = reasonCounts.GetValueOrDefault(key) + 1;
            }
            if (conv.GateReason != null)
            {
                string g = conv.GateReason.Split(' ')[0] + " " + (conv.GateReason.Contains("not ported") ? "not ported" : conv.GateReason.Contains("whitelist") ? "not whitelisted" : conv.GateReason.Contains("character") ? "not supported" : "other");
                gateCounts[g] = gateCounts.GetValueOrDefault(g) + 1;
            }
            string name = (part ?? Path.GetFileNameWithoutExtension(file)) + ".json";
            string target = Path.Combine(outDir, ok ? "eligible" : "ineligible", name);
            File.WriteAllText(target, conv.Bundle.ToJsonString(opts));
            index.Add(new JsonObject
            {
                ["file"] = file,
                ["bundle"] = target,
                ["version"] = replay.Version,
                ["eligible"] = ok,
                ["gate"] = conv.GateReason,
                ["reasons"] = new JsonArray(conv.IneligibleReasons.Select(r => (JsonNode?)JsonValue.Create(r)).ToArray())
            });
        }
        File.WriteAllText(Path.Combine(outDir, "index.json"), index.ToJsonString(opts));
        Console.WriteLine($"replays: {files.Count} decoded: {decoded} failed: {failed} sim-gate-eligible (ported content): {contentEligible} fully eligible: {eligible}");
        Console.WriteLine("exporter reasons: " + string.Join(", ", reasonCounts.Select(kv => kv.Key + "=" + kv.Value)));
        Console.WriteLine("sim gate: " + string.Join(", ", gateCounts.Select(kv => kv.Key + "=" + kv.Value)));
        Console.WriteLine($"index: {Path.Combine(outDir, "index.json")}");
        return failed > 0 ? 1 : 0;
    }
}
