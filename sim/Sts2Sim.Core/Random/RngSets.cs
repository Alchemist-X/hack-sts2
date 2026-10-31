namespace Sts2Sim.Core.Random;

public sealed class RunRngSet : ICanonical
{
    private static readonly string[] StreamNames = CreateStreamNames();

    private static readonly ulong[] StreamNameHashes = CreateStreamNameHashes(StreamNames);

    private readonly Rng[] _rngs;

    public RunRngSet(ulong seed)
        : this(string.Empty, seed)
    {
    }

    public RunRngSet(string seed)
        : this(seed, RngSeeds.RunSeed(seed))
    {
    }

    public RunRngSet(Rng shared)
    {
        StringSeed = string.Empty;
        Seed = 0uL;
        _rngs = new Rng[StreamNames.Length];
        Array.Fill(_rngs, shared);
    }

    private RunRngSet(string stringSeed, ulong seed)
    {
        StringSeed = stringSeed;
        Seed = seed;
        _rngs = new Rng[StreamNames.Length];
        for (int i = 0; i < _rngs.Length; i++)
        {
            _rngs[i] = new Rng(unchecked(seed + StreamNameHashes[i]));
        }
    }

    private RunRngSet(string stringSeed, ulong seed, Rng[] rngs)
    {
        StringSeed = stringSeed;
        Seed = seed;
        _rngs = rngs;
    }

    public static RunRngSet CreateMockInstance() => new(string.Empty);

    public static string StreamName(RunRngType type) => StreamNames[(int)type];

    public static int StreamCount => StreamNames.Length;

    public string StringSeed { get; }

    public ulong Seed { get; }

    public Rng Get(RunRngType type) => _rngs[(int)type];

    public Rng GetRng(RunRngType rngType) => _rngs[(int)rngType];

    public Rng UpFront => Get(RunRngType.UpFront);

    public Rng Shuffle => Get(RunRngType.Shuffle);

    public Rng UnknownMapPoint => Get(RunRngType.UnknownMapPoint);

    public Rng CombatCardGeneration => Get(RunRngType.CombatCardGeneration);

    public Rng CombatPotionGeneration => Get(RunRngType.CombatPotionGeneration);

    public Rng CombatCardSelection => Get(RunRngType.CombatCardSelection);

    public Rng CombatEnergyCosts => Get(RunRngType.CombatEnergyCosts);

    public Rng CombatTargets => Get(RunRngType.CombatTargets);

    public Rng MonsterAi => Get(RunRngType.MonsterAi);

    public Rng Niche => Get(RunRngType.Niche);

    public Rng CombatOrbGeneration => Get(RunRngType.CombatOrbs);

    public Rng TreasureRoomRelics => Get(RunRngType.TreasureRoomRelics);

    public void MockRng(RunRngType rngType, ulong seed)
    {
        _rngs[(int)rngType] = new Rng(seed);
    }

    public RunRngSetSnapshot ToSnapshot()
    {
        RunRngSetSnapshot snapshot = new RunRngSetSnapshot
        {
            Seed = StringSeed,
            HashedSeed = Seed
        };
        for (int i = 0; i < _rngs.Length; i++)
        {
            snapshot.Rngs[(RunRngType)i] = _rngs[i].ToSnapshot();
        }
        return snapshot;
    }

    public static RunRngSet FromSnapshot(RunRngSetSnapshot save)
    {
        RunRngSet runRngSet = save.HashedSeed is ulong hashedSeed
            ? new RunRngSet(save.Seed ?? string.Empty, hashedSeed)
            : new RunRngSet(save.Seed!);
        foreach (KeyValuePair<RunRngType, RngSnapshot> entry in save.Rngs)
        {
            runRngSet._rngs[(int)entry.Key] = new Rng(entry.Value);
        }
        return runRngSet;
    }

    public void LoadFromSnapshot(RunRngSetSnapshot save)
    {
        if (StringSeed != save.Seed || (save.HashedSeed is ulong hashedSeed && hashedSeed != Seed))
        {
            throw new NotImplementedException("RngSet seed should not change during the run!");
        }
        foreach (KeyValuePair<RunRngType, RngSnapshot> entry in save.Rngs)
        {
            _rngs[(int)entry.Key].LoadFromSnapshot(entry.Value);
        }
    }

    public int AliasOf(RunRngType type)
    {
        int index = (int)type;
        Rng rng = _rngs[index];
        for (int j = 0; j < index; j++)
        {
            if (ReferenceEquals(_rngs[j], rng))
            {
                return j;
            }
        }
        return index;
    }

    public RunRngSet DeepClone()
    {
        Rng[] clones = new Rng[_rngs.Length];
        for (int i = 0; i < _rngs.Length; i++)
        {
            Rng source = _rngs[i];
            Rng? clone = null;
            for (int j = 0; j < i; j++)
            {
                if (ReferenceEquals(_rngs[j], source))
                {
                    clone = clones[j];
                    break;
                }
            }
            clones[i] = clone ?? source.DeepClone();
        }
        return new RunRngSet(StringSeed, Seed, clones);
    }

    public void WriteCanonical(CanonicalWriter w)
    {
        w.WriteTag(RngCanonicalTags.RunRngSet);
        w.WriteString(StringSeed);
        w.WriteUInt64(Seed);
        w.WriteCount(_rngs.Length);
        for (int i = 0; i < _rngs.Length; i++)
        {
            int alias = AliasOf((RunRngType)i);
            w.WriteInt32(alias);
            if (alias == i)
            {
                _rngs[i].WriteCanonical(w);
            }
        }
    }

    private static string[] CreateStreamNames()
    {
        RunRngType[] values = Enum.GetValues<RunRngType>();
        string[] names = new string[values.Length];
        foreach (RunRngType value in values)
        {
            names[(int)value] = DeterministicHash.SnakeCase(value.ToString());
        }
        return names;
    }

    private static ulong[] CreateStreamNameHashes(string[] names)
    {
        ulong[] hashes = new ulong[names.Length];
        for (int i = 0; i < names.Length; i++)
        {
            hashes[i] = DeterministicHash.GetDeterministicHashCode(names[i]);
        }
        return hashes;
    }
}

public sealed class PlayerRngSet : ICanonical
{
    private static readonly string[] StreamNames = CreateStreamNames();

    private static readonly ulong[] StreamNameHashes = CreateStreamNameHashes(StreamNames);

    private readonly Rng[] _rngs;

    public PlayerRngSet(ulong seed)
    {
        Seed = seed;
        _rngs = new Rng[StreamNames.Length];
        for (int i = 0; i < _rngs.Length; i++)
        {
            _rngs[i] = new Rng(unchecked(seed + StreamNameHashes[i]));
        }
    }

    private PlayerRngSet(ulong seed, Rng[] rngs)
    {
        Seed = seed;
        _rngs = rngs;
    }

    public static PlayerRngSet ForPlayer(string runStringSeed, int playerSlotIndex)
    {
        return new PlayerRngSet(RngSeeds.PlayerSet(runStringSeed, playerSlotIndex));
    }

    public static string StreamName(PlayerRngType type) => StreamNames[(int)type];

    public static int StreamCount => StreamNames.Length;

    public ulong Seed { get; }

    public Rng Get(PlayerRngType type) => _rngs[(int)type];

    public Rng GetRng(PlayerRngType rngType) => _rngs[(int)rngType];

    public Rng Rewards => Get(PlayerRngType.Rewards);

    public Rng Shops => Get(PlayerRngType.Shops);

    public Rng Transformations => Get(PlayerRngType.Transformations);

    public PlayerRngSetSnapshot ToSnapshot()
    {
        PlayerRngSetSnapshot snapshot = new PlayerRngSetSnapshot
        {
            Seed = Seed
        };
        for (int i = 0; i < _rngs.Length; i++)
        {
            snapshot.Rngs[(PlayerRngType)i] = _rngs[i].ToSnapshot();
        }
        return snapshot;
    }

    public static PlayerRngSet FromSnapshot(PlayerRngSetSnapshot save)
    {
        PlayerRngSet playerRngSet = new PlayerRngSet(save.Seed);
        foreach (KeyValuePair<PlayerRngType, RngSnapshot> entry in save.Rngs)
        {
            playerRngSet._rngs[(int)entry.Key] = new Rng(entry.Value);
        }
        return playerRngSet;
    }

    public void LoadFromSnapshot(PlayerRngSetSnapshot save)
    {
        if (Seed != save.Seed)
        {
            throw new NotImplementedException("RngSet seed should not change during the run!");
        }
        foreach (KeyValuePair<PlayerRngType, RngSnapshot> entry in save.Rngs)
        {
            _rngs[(int)entry.Key].LoadFromSnapshot(entry.Value);
        }
    }

    public PlayerRngSet DeepClone()
    {
        Rng[] clones = new Rng[_rngs.Length];
        for (int i = 0; i < _rngs.Length; i++)
        {
            clones[i] = _rngs[i].DeepClone();
        }
        return new PlayerRngSet(Seed, clones);
    }

    public void WriteCanonical(CanonicalWriter w)
    {
        w.WriteTag(RngCanonicalTags.PlayerRngSet);
        w.WriteUInt64(Seed);
        w.WriteCount(_rngs.Length);
        for (int i = 0; i < _rngs.Length; i++)
        {
            _rngs[i].WriteCanonical(w);
        }
    }

    private static string[] CreateStreamNames()
    {
        PlayerRngType[] values = Enum.GetValues<PlayerRngType>();
        string[] names = new string[values.Length];
        foreach (PlayerRngType value in values)
        {
            names[(int)value] = DeterministicHash.SnakeCase(value.ToString());
        }
        return names;
    }

    private static ulong[] CreateStreamNameHashes(string[] names)
    {
        ulong[] hashes = new ulong[names.Length];
        for (int i = 0; i < names.Length; i++)
        {
            hashes[i] = DeterministicHash.GetDeterministicHashCode(names[i]);
        }
        return hashes;
    }
}
