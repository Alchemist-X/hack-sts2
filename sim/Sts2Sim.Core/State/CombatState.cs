namespace Sts2Sim.Core.State;

public sealed class CombatState : ICanonical
{
    private const int DefaultMonsterCapacity = 6;

    public CombatState(PlayerState player, string encounterId, int ascension, RunRngSet rng)
    {
        ArgumentNullException.ThrowIfNull(player);
        Player = player;
        EncounterId = encounterId;
        Ascension = ascension;
        Rng = rng;
        Monsters = new List<MonsterState>(DefaultMonsterCapacity);
        RoundNumber = 1;
        CurrentSide = CombatSide.Player;
        NextCreatureId = player.CombatId + 1;
    }

    private CombatState(CombatState source, PlayerState player, RunRngSet rng, List<MonsterState> monsters)
    {
        Player = player;
        EncounterId = source.EncounterId;
        Ascension = source.Ascension;
        Rng = rng;
        Monsters = monsters;
        RoundNumber = source.RoundNumber;
        CurrentSide = source.CurrentSide;
        Outcome = source.Outcome;
        IsInProgress = source.IsInProgress;
        IsStarting = source.IsStarting;
        NextCreatureId = source.NextCreatureId;
        NextCardInstanceId = source.NextCardInstanceId;
    }

    public PlayerState Player { get; }

    public List<MonsterState> Monsters { get; }

    public string EncounterId { get; }

    public int Ascension { get; }

    public RunRngSet Rng { get; }

    public int RoundNumber { get; set; }

    public CombatSide CurrentSide { get; set; }

    public CombatOutcome Outcome { get; set; }

    public bool IsInProgress { get; set; }

    public bool IsStarting { get; set; }

    public bool IsOver => Outcome != CombatOutcome.InProgress;

    public int NextCreatureId { get; set; }

    public int NextCardInstanceId { get; set; }

    public int AllocateCreatureId() => NextCreatureId++;

    public int AllocateCardInstanceId() => NextCardInstanceId++;

    public bool HasAscension(AscensionLevel level) => Ascension >= (int)level;

    public int GetValueIfAscension(AscensionLevel level, int ascensionValue, int fallbackValue)
        => HasAscension(level) ? ascensionValue : fallbackValue;

    public decimal GetValueIfAscension(AscensionLevel level, decimal ascensionValue, decimal fallbackValue)
        => HasAscension(level) ? ascensionValue : fallbackValue;

    public float GetValueIfAscension(AscensionLevel level, float ascensionValue, float fallbackValue)
        => HasAscension(level) ? ascensionValue : fallbackValue;

    public CreatureState? GetCreatureById(int combatId)
    {
        if (Player.CombatId == combatId)
        {
            return Player;
        }
        var monsters = Monsters;
        for (int i = 0; i < monsters.Count; i++)
        {
            if (monsters[i].CombatId == combatId)
            {
                return monsters[i];
            }
        }
        return null;
    }

    public void GetCreatures(List<CreatureState> buffer)
    {
        buffer.Clear();
        buffer.Add(Player);
        var monsters = Monsters;
        for (int i = 0; i < monsters.Count; i++)
        {
            buffer.Add(monsters[i]);
        }
    }

    public void GetHittableEnemies(List<MonsterState> buffer)
    {
        buffer.Clear();
        var monsters = Monsters;
        for (int i = 0; i < monsters.Count; i++)
        {
            var monster = monsters[i];
            if (!monster.IsDead && Hook.ShouldAllowHitting(this, monster))
            {
                buffer.Add(monster);
            }
        }
    }

    public void AddMonster(MonsterState monster)
    {
        ArgumentNullException.ThrowIfNull(monster);
        if (IndexOfMonster(monster) >= 0)
        {
            throw new InvalidOperationException("Creature is already in this combat, but AddCreature was called on it again.");
        }
        Monsters.Add(monster);
    }

    public void RemoveMonster(MonsterState monster)
    {
        ArgumentNullException.ThrowIfNull(monster);
        int index = IndexOfMonster(monster);
        if (index < 0)
        {
            throw new InvalidOperationException($"Removed creature '{monster.MonsterId}' (combat id {monster.CombatId}) was not found.");
        }
        Monsters.RemoveAt(index);
    }

    public CombatState DeepClone()
    {
        var player = Player.DeepClone();
        var rng = Rng is null ? null! : Rng.DeepClone();
        var source = Monsters;
        var monsters = new List<MonsterState>(Math.Max(source.Count, DefaultMonsterCapacity));
        for (int i = 0; i < source.Count; i++)
        {
            monsters.Add(source[i].DeepClone());
        }
        return new CombatState(this, player, rng, monsters);
    }

    public void WriteCanonical(CanonicalWriter w)
    {
        w.WriteTag(StateCanonicalTags.CombatState);
        w.WriteString(EncounterId);
        w.WriteInt32(Ascension);
        w.WriteInt32(RoundNumber);
        w.WriteInt32((int)CurrentSide);
        w.WriteByte((byte)Outcome);
        w.WriteBool(IsInProgress);
        w.WriteBool(IsStarting);
        w.WriteInt32(NextCreatureId);
        w.WriteInt32(NextCardInstanceId);
        Player.WriteCanonical(w);
        var monsters = Monsters;
        w.WriteTag(StateCanonicalTags.MonsterList);
        w.WriteCount(monsters.Count);
        for (int i = 0; i < monsters.Count; i++)
        {
            monsters[i].WriteCanonical(w);
        }
        var rng = Rng;
        w.WriteBool(rng is not null);
        rng?.WriteCanonical(w);
    }

    private int IndexOfMonster(MonsterState monster)
    {
        var monsters = Monsters;
        for (int i = 0; i < monsters.Count; i++)
        {
            if (ReferenceEquals(monsters[i], monster))
            {
                return i;
            }
        }
        return -1;
    }
}
