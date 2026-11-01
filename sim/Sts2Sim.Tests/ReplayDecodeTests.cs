using System.Runtime.CompilerServices;
using System.Text;
using Sts2Sim.Core.Run;
using Sts2Sim.Diff.Replay;

namespace Sts2Sim.Tests;

public static class ReplayDecodeTests
{
    private static string Fixture(string name, [CallerFilePath] string source = "")
        => Path.Combine(Path.GetDirectoryName(source)!, "Fixtures", name);

    private static McrReplay V0111() => McrReader.ReadFile(Fixture("mcr_v0111_silent.mcr"));

    private static McrReplay V0107() => McrReader.ReadFile(Fixture("mcr_v0107.mcr"));

    public static void Test_XxHash32_KnownVectors()
    {
        var empty = new XxHash32();
        Assert.Equal(0x02CC5D05u, empty.GetCurrentHash());
        var abc = new XxHash32();
        abc.Append(Encoding.ASCII.GetBytes("a"));
        abc.Append(Encoding.ASCII.GetBytes("bc"));
        Assert.Equal(0x32D153FFu, abc.GetCurrentHash());
        var longer = new XxHash32();
        longer.Append(Encoding.ASCII.GetBytes("Nobody inspects the spammish repetition"));
        Assert.Equal(0xE2293B2Fu, longer.GetCurrentHash());
    }

    public static void Test_BitReader_IsLsbFirstAndUnaligned()
    {
        var r = new McrBitReader(new byte[] { 0b1011_0100, 0b0000_0001 });
        Assert.Equal(4, r.ReadInt(3));
        Assert.False(r.ReadBool());
        Assert.Equal(11, r.ReadInt(4));
        Assert.Equal(1, r.ReadInt(2));
        Assert.Equal(6, r.RemainingBits);
        Assert.Equal(3, McrBitReader.EnumBits(4));
        Assert.Equal(3, McrBitReader.EnumBits(3));
        Assert.Equal(4, McrBitReader.EnumBits(8));
    }

    public static void Test_ModelNetIdTable_V0111_MatchesReplayHash()
    {
        Assert.Equal(1568834832u, McrModelNetIds.V0111.Hash);
        Assert.Equal(11, McrModelNetIds.V0111.EntryIdBitSize);
        Assert.Equal("NONE", McrModelNetIds.V0111.Entry(0));
        Assert.True(McrModelNetIds.V0107.Legacy);
        Assert.False(McrModelNetIds.V0111.Legacy);
    }

    public static void Test_McrV0111_DecodesRunStartEventsAndChecksums()
    {
        var replay = V0111();
        Assert.Equal("v0.111.0", replay.Version);
        Assert.True(replay.HashVerified);
        Assert.True(replay.TrailingBits < 8);
        var run = replay.Run;
        Assert.Equal(SavedRun.SupportedSchemaVersion, run.SchemaVersion);
        Assert.Equal("GM28D28DX794", run.Rng.Seed);
        Assert.Equal(6, run.Ascension);
        Assert.Equal(1, run.CurrentActIndex);
        Assert.Equal(30, replay.TotalFloor + 1);
        var p = replay.Player!;
        Assert.Equal("SILENT", p.CharacterId);
        Assert.Equal(39, p.CurrentHp);
        Assert.Equal(70, p.MaxHp);
        Assert.Equal(25, p.Deck.Count);
        Assert.Equal(14, p.Relics.Count);
        Assert.Equal(2, p.Potions.Count);
        Assert.True(run.Rng.Rngs.ContainsKey("niche"), "run rng streams keyed by snake_case name");
        Assert.Equal(1, replay.Events.Count);
        var play = (McrPlayCard)replay.Events[0].Action!;
        Assert.Equal("SKEWER", play.CardId);
        Assert.Equal(8u, play.CombatCardIndex);
        Assert.Equal(1u, play.TargetId ?? 0u);
        Assert.Equal(2, replay.Checksums.Count);
        var c0 = replay.Checksums[0];
        Assert.Equal("After player turn start", c0.Context);
        var hand = c0.State.Players[0].Pile(McrPileType.Hand)!.Cards;
        Assert.Equal(9, hand.Count);
        Assert.Equal("SKEWER", hand[8].Card.Id);
        Assert.Equal("MYSTERIOUS_KNIGHT", c0.State.Creatures.First(c => c.MonsterId != null).MonsterId);
    }

    public static void Test_McrV0107_DecodesLegacyCounterRngAndUnverifiedTable()
    {
        var replay = V0107();
        Assert.Equal("v0.107.1", replay.Version);
        Assert.False(replay.HashVerified);
        Assert.Equal("v0.107.1", replay.Table.GameVersion);
        Assert.Equal("REGENT", replay.Player!.CharacterId);
        Assert.Equal(11, replay.Player.Deck.Count);
        Assert.Equal(11, replay.Events.Count);
        Assert.Equal(0, replay.Checksums.Count);
        Assert.True(replay.Run.Rng.Rngs.Values.All(s => s.S0 == 0 && s.S1 == 0), "v0.107.1 streams carry counters only");
        var enc = McrBundleConverter.ResolveEncounter(replay);
        Assert.Equal("FUZZY_WURM_CRAWLER_WEAK", enc.EncounterId);
        Assert.Equal(Sts2Sim.Core.MapPointType.Monster, enc.PointType);
    }

    public static void Test_McrConvert_WritesV1BundleWithResolvedHandIndex()
    {
        var conv = McrBundleConverter.Convert(V0111(), "S", "S-part8");
        var b = conv.Bundle;
        Assert.Equal(1, (int)b["bundle_version"]!);
        Assert.Equal("xxh64", (string)b["seeding"]!);
        Assert.Equal(30, (int)b["source"]!["floor"]!);
        var d0 = b["decisions"]!.AsArray()[0]!;
        Assert.Null(d0["pre_state"]);
        Assert.Equal("play_card", (string)d0["action"]!["type"]!);
        Assert.Equal(8, (int)d0["action"]!["hand_index"]!);
        Assert.Equal("SKEWER", (string)d0["action"]!["card_id"]!);
        Assert.True((bool)d0["action"]!["upgraded"]!);
        Assert.Equal(1, (int)d0["action"]!["target_combat_id"]!);
        Assert.Equal("win", (string)b["outcome"]!);
        Assert.Equal(39, (int)b["final_hp"]!);
        Assert.True(conv.IneligibleReasons.Any(r => r.StartsWith("encounter_unresolved", StringComparison.Ordinal)));
        Assert.True(conv.GateReason != null && conv.GateReason.Contains("encounter NONE"), conv.GateReason);
    }

    public static void Test_McrConvert_LegacyUsesLegacySeedingAndCounters()
    {
        var conv = McrBundleConverter.Convert(V0107(), null, null);
        Assert.Equal("legacy_v18", (string)conv.Bundle["seeding"]!);
        Assert.True(conv.IneligibleReasons.Contains("no_checksum_states"));
        Assert.True(conv.IneligibleReasons.Contains("model_table_unverified:v0.107.1"));
    }

    private static McrCardState Card(string id, int level = 0) => new() { Card = new SavedCard { Id = id, CurrentUpgradeLevel = level } };

    private static McrPlayerState Hand(params McrCardState[] cards)
        => new() { Piles = new List<McrPile> { new(McrPileType.Hand, cards.ToList()) } };

    public static void Test_ResolveHandIndex_IdenticalCopiesTakeFirstElsePostDiff()
    {
        var pre = Hand(Card("STRIKE"), Card("BASH"), Card("STRIKE"));
        Assert.Equal(0, McrBundleConverter.ResolveHandIndex(pre, null, "STRIKE", out _) ?? -1);
        var mixed = Hand(Card("STRIKE", 1), Card("BASH"), Card("STRIKE"));
        var post = Hand(Card("STRIKE", 1), Card("BASH"));
        Assert.Equal(2, McrBundleConverter.ResolveHandIndex(mixed, post, "STRIKE", out _) ?? -1);
        Assert.Null(McrBundleConverter.ResolveHandIndex(mixed, null, "STRIKE", out var why));
        Assert.True(why != null && why.Contains("non-identical"), why);
        Assert.Null(McrBundleConverter.ResolveHandIndex(pre, null, "DEFEND", out _));
    }
}
