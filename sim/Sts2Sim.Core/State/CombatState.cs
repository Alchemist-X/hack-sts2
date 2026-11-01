namespace Sts2Sim.Core.State;

public sealed class CombatState : ICanonical
{
    private const int DefaultMonsterCapacity = 6;

    private const int DefaultAllyCapacity = 2;

    private readonly List<CreatureState> _allies;

    public CombatState(PlayerState player, string encounterId, int ascension, RunRngSet rng)
        : this(RunState.CreateStandalone(player, rng, ascension), player, encounterId)
    {
    }

    public CombatState(RunState run, PlayerState player, string encounterId)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(player);
        Run = run;
        Player = player;
        EncounterId = encounterId;
        Monsters = new List<MonsterState>(DefaultMonsterCapacity);
        _allies = new List<CreatureState>(DefaultAllyCapacity) { player };
        History = new CombatHistory();
        PlayersTakingExtraTurn = new List<int>();
        RoundNumber = 1;
        CurrentSide = CombatSide.Player;
        NextCreatureId = player.CombatId + 1;
        run.Combat ??= this;
    }

    private CombatState(CombatState source, RunState run, PlayerState player, List<MonsterState> monsters, List<CreatureState> allies)
    {
        Run = run;
        Player = player;
        EncounterId = source.EncounterId;
        Monsters = monsters;
        _allies = allies;
        History = source.History.DeepClone();
        PlayersTakingExtraTurn = new List<int>(source.PlayersTakingExtraTurn);
        CardOrPotionEffectDepth = source.CardOrPotionEffectDepth;
        RoundNumber = source.RoundNumber;
        CurrentSide = source.CurrentSide;
        Outcome = source.Outcome;
        IsInProgress = source.IsInProgress;
        IsStarting = source.IsStarting;
        NextCreatureId = source.NextCreatureId;
        NextCardPlayId = source.NextCardPlayId;
    }

    public RunState Run { get; }

    public PlayerState Player { get; }

    public List<MonsterState> Monsters { get; }

    public IReadOnlyList<CreatureState> Allies => _allies;

    public string EncounterId { get; }

    public int Ascension => Run.Ascension;

    public RunRngSet Rng => Run.Rng;

    public ChoiceRuntime Choice => Run.Choice;

    public CombatHistory History { get; }

    public int CardOrPotionEffectDepth { get; set; }

    public List<int> PlayersTakingExtraTurn { get; }

    public int RoundNumber { get; set; }

    public CombatSide CurrentSide { get; set; }

    public CombatOutcome Outcome { get; set; }

    public bool IsInProgress { get; set; }

    public bool IsStarting { get; set; }

    public bool IsOver => Outcome != CombatOutcome.InProgress;

    public int NextCreatureId { get; set; }

    public int NextCardInstanceId
    {
        get => Run.NextCardInstanceId;
        set => Run.NextCardInstanceId = value;
    }

    public int NextCardPlayId { get; set; }

    public int AllocateCreatureId() => NextCreatureId++;

    public int AllocateCardInstanceId() => Run.AllocateCardInstanceId();

    public int AllocateCardPlayId() => NextCardPlayId++;

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

    public void AddAlly(MonsterState pet)
        => throw new NotImplementedException("CombatState.AddAlly: state agent (Combat/CombatState.cs:993 AddCreature on _allies, CONTRACTS Part B §B2)");

    public void RemoveAlly(MonsterState pet)
        => throw new NotImplementedException("CombatState.RemoveAlly: state agent (Combat/CombatState.cs:739 RemoveCreature on _allies, CONTRACTS Part B §B2)");

    public CardInstance CreateCard(CardDef def, PlayerState owner)
    {
        ArgumentNullException.ThrowIfNull(def);
        ArgumentNullException.ThrowIfNull(owner);
        var card = new CardInstance(def, AllocateCardInstanceId());
        card.AttachTo(owner);
        def.AfterCreated(card);
        return card;
    }

    public CombatState DeepClone()
    {
        var run = Run.CloneWithoutCombat();
        var clone = CloneInto(run, Run);
        if (ReferenceEquals(Run.Combat, this))
        {
            run.Combat = clone;
        }
        return clone;
    }

    internal CombatState CloneInto(RunState run, RunState sourceRun)
    {
        int playerIndex = sourceRun.Players.IndexOf(Player);
        var player = playerIndex >= 0 ? run.Players[playerIndex] : Player.DeepClone();
        var source = Monsters;
        var monsters = new List<MonsterState>(Math.Max(source.Count, DefaultMonsterCapacity));
        for (int i = 0; i < source.Count; i++)
        {
            monsters.Add(source[i].DeepClone());
        }
        var sourceAllies = _allies;
        var allies = new List<CreatureState>(Math.Max(sourceAllies.Count, DefaultAllyCapacity));
        for (int i = 0; i < sourceAllies.Count; i++)
        {
            var ally = sourceAllies[i];
            allies.Add(ReferenceEquals(ally, Player) ? player : ally.DeepCloneCreature());
        }
        return new CombatState(this, run, player, monsters, allies);
    }

    public void WriteCanonical(CanonicalWriter w)
    {
        Run.WriteRunFields(w);
        WriteCombatFields(w);
    }

    internal void WriteCombatFields(CanonicalWriter w)
    {
        w.WriteTag(StateCanonicalTags.CombatState);
        w.WriteString(EncounterId);
        w.WriteInt32(RoundNumber);
        w.WriteInt32((int)CurrentSide);
        w.WriteByte((byte)Outcome);
        w.WriteBool(IsInProgress);
        w.WriteBool(IsStarting);
        w.WriteInt32(NextCreatureId);
        int playerIndex = Run.Players.IndexOf(Player);
        w.WriteInt32(playerIndex);
        if (playerIndex < 0)
        {
            Player.WriteCanonical(w);
        }
        var monsters = Monsters;
        w.WriteTag(StateCanonicalTags.MonsterList);
        w.WriteCount(monsters.Count);
        for (int i = 0; i < monsters.Count; i++)
        {
            monsters[i].WriteCanonical(w);
        }
        var allies = _allies;
        w.WriteTag(StateCanonicalTags.AllyList);
        w.WriteCount(allies.Count);
        for (int i = 0; i < allies.Count; i++)
        {
            var ally = allies[i];
            bool isPlayer = ReferenceEquals(ally, Player);
            w.WriteBool(isPlayer);
            if (!isPlayer)
            {
                ally.WriteCanonical(w);
            }
        }
        w.WriteTag(StateCanonicalTags.CombatKernel);
        w.WriteInt32(CardOrPotionEffectDepth);
        w.WriteInt32(NextCardPlayId);
        var extraTurns = PlayersTakingExtraTurn;
        w.WriteCount(extraTurns.Count);
        for (int i = 0; i < extraTurns.Count; i++)
        {
            w.WriteInt32(extraTurns[i]);
        }
        History.WriteCanonical(w);
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
