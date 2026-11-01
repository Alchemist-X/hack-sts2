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

    public IReadOnlyList<CreatureState> CreaturesOnCurrentSide => GetCreaturesOnSide(CurrentSide);

    public CreatureState? GetCreatureById(int combatId)
    {
        var allies = _allies;
        for (int i = 0; i < allies.Count; i++)
        {
            if (allies[i].CombatId == combatId)
            {
                return allies[i];
            }
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
        ArgumentNullException.ThrowIfNull(buffer);
        buffer.Clear();
        var allies = _allies;
        for (int i = 0; i < allies.Count; i++)
        {
            buffer.Add(allies[i]);
        }
        var monsters = Monsters;
        for (int i = 0; i < monsters.Count; i++)
        {
            buffer.Add(monsters[i]);
        }
    }

    public IReadOnlyList<CreatureState> GetCreaturesOnSide(CombatSide side)
    {
        if (side != CombatSide.Enemy)
        {
            return _allies;
        }
        return Monsters;
    }

    public void GetCreaturesOnSide(CombatSide side, List<CreatureState> buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        buffer.Clear();
        var creatures = GetCreaturesOnSide(side);
        for (int i = 0; i < creatures.Count; i++)
        {
            buffer.Add(creatures[i]);
        }
    }

    public IReadOnlyList<CreatureState> GetOpponentsOf(CreatureState creature)
    {
        ArgumentNullException.ThrowIfNull(creature);
        return GetCreaturesOnSide(CombatStatus.OppositeSide(creature.Side));
    }

    public IReadOnlyList<CreatureState> GetTeammatesOf(CreatureState creature)
    {
        ArgumentNullException.ThrowIfNull(creature);
        return GetCreaturesOnSide(creature.Side);
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

    public bool ContainsCreature(CreatureState creature)
    {
        ArgumentNullException.ThrowIfNull(creature);
        return IndexOfAlly(creature) >= 0 || IndexOfMonster(creature) >= 0;
    }

    public void AddCreature(CreatureState creature)
    {
        ArgumentNullException.ThrowIfNull(creature);
        if (ContainsCreature(creature))
        {
            throw new InvalidOperationException("Creature is already in this combat, but AddCreature was called on it again.");
        }
        if (creature.Side == CombatSide.Player)
        {
            _allies.Add(creature);
            return;
        }
        if (creature is not MonsterState monster)
        {
            throw new ArgumentException($"Only a monster can join the enemy side; creature {creature.CombatId} is a player.", nameof(creature));
        }
        Monsters.Add(monster);
    }

    public void RemoveCreature(CreatureState creature)
    {
        ArgumentNullException.ThrowIfNull(creature);
        int enemyIndex = IndexOfMonster(creature);
        if (enemyIndex >= 0)
        {
            Monsters.RemoveAt(enemyIndex);
            return;
        }
        int allyIndex = IndexOfAlly(creature);
        if (allyIndex < 0)
        {
            throw new InvalidOperationException($"Removed creature '{Describe(creature)}' (combat id {creature.CombatId}) was not found.");
        }
        if (ReferenceEquals(creature, Player))
        {
            throw new InvalidOperationException($"The combat's player (combat id {creature.CombatId}) stays in Allies[0]; the sim never removes it.");
        }
        _allies.RemoveAt(allyIndex);
    }

    public void AddMonster(MonsterState monster)
    {
        ArgumentNullException.ThrowIfNull(monster);
        AddCreature(monster);
    }

    public void RemoveMonster(MonsterState monster)
    {
        ArgumentNullException.ThrowIfNull(monster);
        RemoveCreature(monster);
    }

    public void AddAlly(MonsterState pet)
    {
        ArgumentNullException.ThrowIfNull(pet);
        if (pet.Side != CombatSide.Player)
        {
            throw new ArgumentException($"AddAlly needs a player-side creature; {pet.MonsterId} (combat id {pet.CombatId}) is on side {pet.Side}.", nameof(pet));
        }
        AddCreature(pet);
    }

    public void RemoveAlly(MonsterState pet)
    {
        ArgumentNullException.ThrowIfNull(pet);
        RemoveCreature(pet);
    }

    public void SetEnemyIndex(MonsterState creature, int index)
    {
        ArgumentNullException.ThrowIfNull(creature);
        if (!EncounterRegistry.TryGet(EncounterId, out var encounter))
        {
            throw new InvalidOperationException($"Encounter {EncounterId} is not registered, so its slots are unknown.");
        }
        if (encounter.Slots.Count > 0)
        {
            throw new InvalidOperationException("Cannot modify turn order of a combat with pre-set slots");
        }
        int at = IndexOfMonster(creature);
        if (at < 0)
        {
            throw new ArgumentException("Creature must be a valid enemy to change its turn order.", nameof(creature));
        }
        Monsters.RemoveAt(at);
        Monsters.Insert(Math.Min(index, Monsters.Count - 1), creature);
    }

    public void InvokeDiedEvent(CreatureState creature)
    {
        ArgumentNullException.ThrowIfNull(creature);
        if (creature is not MonsterState pet || pet.PetOwnerId == PowerModel.NoCreature)
        {
            return;
        }
        if (GetCreatureById(pet.PetOwnerId) is PlayerState owner && owner.PetIds.Contains(pet.CombatId))
        {
            owner.OnPetDied(this, pet);
        }
    }

    public CardInstance CreateCard(CardDef def, PlayerState owner)
    {
        ArgumentNullException.ThrowIfNull(def);
        ArgumentNullException.ThrowIfNull(owner);
        var card = new CardInstance(def, AllocateCardInstanceId());
        card.AttachTo(owner);
        def.AfterCreated(card);
        return card;
    }

    public CardInstance CreateCard(string cardId, PlayerState owner)
    {
        ArgumentNullException.ThrowIfNull(cardId);
        return CreateCard(CardRegistry.Get(cardId), owner);
    }

    public CardInstance CloneCard(CardInstance mutableCard)
    {
        ArgumentNullException.ThrowIfNull(mutableCard);
        var clone = mutableCard.CloneForCombat(AllocateCardInstanceId());
        clone.DeckVersionId = CardInstance.NoDeckVersion;
        return clone;
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

    private int IndexOfMonster(CreatureState creature)
    {
        var monsters = Monsters;
        for (int i = 0; i < monsters.Count; i++)
        {
            if (ReferenceEquals(monsters[i], creature))
            {
                return i;
            }
        }
        return -1;
    }

    private int IndexOfAlly(CreatureState creature)
    {
        var allies = _allies;
        for (int i = 0; i < allies.Count; i++)
        {
            if (ReferenceEquals(allies[i], creature))
            {
                return i;
            }
        }
        return -1;
    }

    private static string Describe(CreatureState creature) => creature is MonsterState monster ? monster.MonsterId : "player";
}
