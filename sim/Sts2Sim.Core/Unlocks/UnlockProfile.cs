using System.Collections.ObjectModel;
using CardPoolTable = Sts2Sim.Core.Cards.CardPools;

namespace Sts2Sim.Core.Unlocks;

public sealed class UnlockProfile
{
    public const int AllNumberOfRuns = 999999999;

    public const string Silent1EpochId = "SILENT1_EPOCH";

    public const string Regent1EpochId = "REGENT1_EPOCH";

    public const string Necrobinder1EpochId = "NECROBINDER1_EPOCH";

    public const string Defect1EpochId = "DEFECT1_EPOCH";

    private static readonly ReadOnlyCollection<string> EpochIds = Array.AsReadOnly(new[]
    {
        "ACT2_B_EPOCH", "ACT3_B_EPOCH", "COLORLESS1_EPOCH", "COLORLESS2_EPOCH", "COLORLESS3_EPOCH", "COLORLESS4_EPOCH",
        "COLORLESS5_EPOCH", "CUSTOM_AND_SEEDS_EPOCH", "DAILY_RUN_EPOCH", "DARV_EPOCH", "DEFECT1_EPOCH", "DEFECT2_EPOCH",
        "DEFECT3_EPOCH", "DEFECT4_EPOCH", "DEFECT5_EPOCH", "DEFECT6_EPOCH", "DEFECT7_EPOCH", "EVENT1_EPOCH",
        "EVENT2_EPOCH", "EVENT3_EPOCH", "IRONCLAD2_EPOCH", "IRONCLAD3_EPOCH", "IRONCLAD4_EPOCH", "IRONCLAD5_EPOCH",
        "IRONCLAD6_EPOCH", "IRONCLAD7_EPOCH", "NECROBINDER1_EPOCH", "NECROBINDER2_EPOCH", "NECROBINDER3_EPOCH",
        "NECROBINDER4_EPOCH", "NECROBINDER5_EPOCH", "NECROBINDER6_EPOCH", "NECROBINDER7_EPOCH", "NEOW_EPOCH",
        "OROBAS_EPOCH", "POTION1_EPOCH", "POTION2_EPOCH", "REGENT1_EPOCH", "REGENT2_EPOCH", "REGENT3_EPOCH",
        "REGENT4_EPOCH", "REGENT5_EPOCH", "REGENT6_EPOCH", "REGENT7_EPOCH", "RELIC1_EPOCH", "RELIC2_EPOCH",
        "RELIC3_EPOCH", "RELIC4_EPOCH", "RELIC5_EPOCH", "SILENT1_EPOCH", "SILENT2_EPOCH", "SILENT3_EPOCH",
        "SILENT4_EPOCH", "SILENT5_EPOCH", "SILENT6_EPOCH", "SILENT7_EPOCH", "UNDERDOCKS_EPOCH",
    });

    private static readonly ReadOnlyCollection<string> EncounterIds = Array.AsReadOnly(new[]
    {
        "BYGONE_EFFIGY_ELITE", "BYRDONIS_ELITE", "CEREMONIAL_BEAST_BOSS", "CUBEX_CONSTRUCT_NORMAL", "FLYCONID_NORMAL",
        "FOGMOG_NORMAL", "FUZZY_WURM_CRAWLER_WEAK", "INKLETS_NORMAL", "MAWLER_NORMAL", "NIBBITS_NORMAL", "NIBBITS_WEAK",
        "OVERGROWTH_CRAWLERS", "PHROG_PARASITE_ELITE", "RUBY_RAIDERS_NORMAL", "SHRINKER_BEETLE_WEAK", "SLIMES_NORMAL",
        "SLIMES_WEAK", "SLITHERING_STRANGLER_NORMAL", "SNAPPING_JAXFRUIT_NORMAL", "THE_KIN_BOSS", "VANTOM_BOSS",
        "VINE_SHAMBLER_NORMAL",
        "CORPSE_SLUGS_NORMAL", "CORPSE_SLUGS_WEAK", "CULTISTS_NORMAL", "FOSSIL_STALKER_NORMAL", "GREMLIN_MERC_NORMAL",
        "HAUNTED_SHIP_NORMAL", "LAGAVULIN_MATRIARCH_BOSS", "LIVING_FOG_NORMAL", "PHANTASMAL_GARDENERS_ELITE",
        "PUNCH_CONSTRUCT_NORMAL", "SEAPUNK_NORMAL", "SEAPUNK_WEAK", "SEWER_CLAM_NORMAL", "SKULKING_COLONY_ELITE",
        "SLUDGE_SPINNER_WEAK", "SOUL_FYSH_BOSS", "TERROR_EEL_ELITE", "TOADPOLES_WEAK", "TWO_TAILED_RATS_NORMAL",
        "WATERFALL_GIANT_BOSS",
        "BOWLBUGS_NORMAL", "BOWLBUGS_WEAK", "CHOMPERS_NORMAL", "DECIMILLIPEDE_ELITE", "ENTOMANCER_ELITE",
        "EXOSKELETONS_NORMAL", "EXOSKELETONS_WEAK", "HUNTER_KILLER_NORMAL", "KAISER_CRAB_BOSS", "INFESTED_PRISMS_ELITE",
        "KNOWLEDGE_DEMON_BOSS", "LOUSE_PROGENITOR_NORMAL", "MYTES_NORMAL", "OVICOPTER_NORMAL", "SLUMBERING_BEETLE_NORMAL",
        "SPINY_TOAD_NORMAL", "THE_INSATIABLE_BOSS", "THE_OBSCURA_NORMAL", "THIEVING_HOPPER_WEAK", "TUNNELER_WEAK",
        "AXEBOTS_NORMAL", "CONSTRUCT_MENAGERIE_NORMAL", "DEVOTED_SCULPTOR_WEAK", "AEONGLASS_BOSS", "FABRICATOR_NORMAL",
        "FROG_KNIGHT_NORMAL", "GLOBE_HEAD_NORMAL", "KNIGHTS_ELITE", "MECHA_KNIGHT_ELITE", "OWL_MAGISTRATE_NORMAL",
        "QUEEN_BOSS", "SCROLLS_OF_BITING_NORMAL", "SCROLLS_OF_BITING_WEAK", "SLIMED_BERSERKER_NORMAL", "SOUL_NEXUS_ELITE",
        "TEST_SUBJECT_BOSS", "THE_LOST_AND_FORGOTTEN_NORMAL", "TURRET_OPERATOR_WEAK",
        "FAKE_MERCHANT_EVENT_ENCOUNTER", "MYSTERIOUS_KNIGHT_EVENT_ENCOUNTER", "BATTLEWORN_DUMMY_EVENT_V1_ENCOUNTER",
        "BATTLEWORN_DUMMY_EVENT_V2_ENCOUNTER", "BATTLEWORN_DUMMY_EVENT_V3_ENCOUNTER",
    });

    private static readonly IdNode? ValidEpochs = IdNode.Build(EpochIds, out _);

    public static readonly UnlockProfile None = new(Array.Empty<string>(), Array.Empty<string>(), 0);

    public static readonly UnlockProfile All = new(EpochIds, EncounterIds, AllNumberOfRuns);

    private readonly IdNode? _unlockedEpochIds;
    private readonly int _unlockedEpochCount;
    private readonly IdNode? _encountersSeen;
    private readonly int _encountersSeenCount;

    public UnlockProfile(IEnumerable<string> unlockedEpochIds, IEnumerable<string> encountersSeen, int numberOfRuns)
    {
        ArgumentNullException.ThrowIfNull(unlockedEpochIds);
        ArgumentNullException.ThrowIfNull(encountersSeen);
        _unlockedEpochIds = IdNode.Build(unlockedEpochIds.Where(IsValidEpoch), out _unlockedEpochCount);
        _encountersSeen = IdNode.Build(encountersSeen, out _encountersSeenCount);
        NumberOfRuns = numberOfRuns;
        Digest = ComputeDigest();
    }

    public UnlockProfile(IEnumerable<UnlockProfile> profiles)
    {
        ArgumentNullException.ThrowIfNull(profiles);
        var array = profiles.ToArray();
        var epochs = new List<string>();
        var encounters = new List<string>();
        int runs = int.MinValue;
        for (int i = 0; i < array.Length; i++)
        {
            array[i]._unlockedEpochIds?.AppendInOrder(epochs);
            array[i]._encountersSeen?.AppendInOrder(encounters);
            runs = Math.Max(runs, array[i].NumberOfRuns);
        }
        if (array.Length == 0)
        {
            throw new InvalidOperationException("Sequence contains no elements");
        }
        _unlockedEpochIds = IdNode.Build(epochs.Where(IsValidEpoch), out _unlockedEpochCount);
        _encountersSeen = IdNode.Build(encounters, out _encountersSeenCount);
        NumberOfRuns = runs;
        Digest = ComputeDigest();
    }

    public static IReadOnlyList<string> AllEpochIds => EpochIds;

    public static IReadOnlyList<string> AllEncounterIds => EncounterIds;

    public int NumberOfRuns { get; }

    public ulong Digest { get; }

    public static bool IsValidEpoch(string epochId) => IdNode.Contains(ValidEpochs, epochId);

    public bool IsEpochRevealed(string epochId) => IdNode.Contains(_unlockedEpochIds, epochId);

    public int EpochUnlockCount() => _unlockedEpochCount;

    public bool HasSeenEncounter(string encounterId) => IdNode.Contains(_encountersSeen, encounterId);

    public int EncountersSeenCount => _encountersSeenCount;

    public IReadOnlyList<string> Characters
    {
        get
        {
            var characters = new List<string>(CardPoolTable.AllCharacterIds);
            if (!IsEpochRevealed(Silent1EpochId))
            {
                characters.Remove(CardPoolTable.SilentCharacterId);
            }
            if (!IsEpochRevealed(Regent1EpochId))
            {
                characters.Remove(CardPoolTable.RegentCharacterId);
            }
            if (!IsEpochRevealed(Necrobinder1EpochId))
            {
                characters.Remove(CardPoolTable.NecrobinderCharacterId);
            }
            if (!IsEpochRevealed(Defect1EpochId))
            {
                characters.Remove(CardPoolTable.DefectCharacterId);
            }
            return characters;
        }
    }

    public IReadOnlyList<CardPool> CharacterCardPools
    {
        get
        {
            var characters = Characters;
            var pools = new List<CardPool>(characters.Count);
            for (int i = 0; i < characters.Count; i++)
            {
                pools.Add(CardPoolTable.ForCharacter(characters[i]));
            }
            return pools;
        }
    }

    public IReadOnlyList<CardPool> CardPools
    {
        get
        {
            var pools = new List<CardPool>(CharacterCardPools);
            var shared = CardPoolTable.AllSharedCardPools;
            for (int i = 0; i < shared.Count; i++)
            {
                if (!pools.Contains(shared[i]))
                {
                    pools.Add(shared[i]);
                }
            }
            return pools;
        }
    }

    public IReadOnlyList<string> UnlockedEpochIds()
    {
        var list = new List<string>(_unlockedEpochCount);
        _unlockedEpochIds?.AppendInOrder(list);
        return list;
    }

    public IReadOnlyList<string> EncountersSeen()
    {
        var list = new List<string>(_encountersSeenCount);
        _encountersSeen?.AppendInOrder(list);
        return list;
    }

    public void WriteCanonical(CanonicalWriter w)
    {
        w.WriteTag(UnlockCanonicalTags.Profile);
        w.WriteUInt64(Digest);
    }

    private ulong ComputeDigest()
    {
        var w = new CanonicalWriter(1024);
        w.WriteTag(UnlockCanonicalTags.DigestEpochs);
        w.WriteCount(_unlockedEpochCount);
        WriteIds(w, _unlockedEpochIds);
        w.WriteTag(UnlockCanonicalTags.DigestEncounters);
        w.WriteCount(_encountersSeenCount);
        WriteIds(w, _encountersSeen);
        w.WriteInt32(NumberOfRuns);
        return StateHasher.Hash(w.WrittenSpan);
    }

    private static void WriteIds(CanonicalWriter w, IdNode? root)
    {
        var ids = new List<string>();
        root?.AppendInOrder(ids);
        for (int i = 0; i < ids.Count; i++)
        {
            w.WriteString(ids[i]);
        }
    }

    private sealed class IdNode
    {
        private readonly string _id;
        private readonly IdNode? _left;
        private readonly IdNode? _right;

        private IdNode(string id, IdNode? left, IdNode? right)
        {
            _id = id;
            _left = left;
            _right = right;
        }

        public static IdNode? Build(IEnumerable<string> ids, out int count)
        {
            var sorted = new SortedSet<string>(ids, StringComparer.Ordinal);
            var array = new string[sorted.Count];
            sorted.CopyTo(array);
            count = array.Length;
            return Build(array, 0, array.Length);
        }

        private static IdNode? Build(string[] sorted, int start, int end)
        {
            if (start >= end)
            {
                return null;
            }
            int mid = start + ((end - start) >> 1);
            return new IdNode(sorted[mid], Build(sorted, start, mid), Build(sorted, mid + 1, end));
        }

        public static bool Contains(IdNode? node, string id)
        {
            if (id is null)
            {
                return false;
            }
            while (node != null)
            {
                int c = string.CompareOrdinal(id, node._id);
                if (c == 0)
                {
                    return true;
                }
                node = c < 0 ? node._left : node._right;
            }
            return false;
        }

        public void AppendInOrder(List<string> buffer)
        {
            _left?.AppendInOrder(buffer);
            buffer.Add(_id);
            _right?.AppendInOrder(buffer);
        }
    }
}
