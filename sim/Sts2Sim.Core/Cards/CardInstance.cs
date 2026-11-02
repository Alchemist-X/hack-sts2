namespace Sts2Sim.Core.Cards;

public sealed class CardInstance : ICanonical, IComparable<CardInstance>
{
    public const int NoTarget = -1;

    public const int NoDeckVersion = -1;

    public const int NoOwner = -1;

    private PlayerState? _owner;
    private int _upgradeLevel;
    private int _baseStarCost;
    private List<TemporaryCardCost>? _temporaryStarCosts;
    private EnchantmentModel? _enchantment;
    private AfflictionModel? _affliction;

    public CardInstance(CardDef def, int instanceId)
    {
        ArgumentNullException.ThrowIfNull(def);
        if (def is UnportedCardDef)
        {
            throw new NotImplementedException($"Card {def.Id} is not ported: only its pool metadata is known (cards-kernel catalog, sim/specs/cards-kernel.md section 6)");
        }
        Def = def;
        InstanceId = instanceId;
        Vars = def.CreateVars();
        EnergyCost = new CardEnergyCost(def.CanonicalCost, def.CostsX);
        _baseStarCost = def.CanonicalStarCost;
        foreach (var keyword in def.CanonicalKeywords)
        {
            KeywordMask |= CardKeywordSet.BitOf(keyword);
        }
        Extra = def.CreateExtraState();
        Listener = def.CreateListener(this);
    }

    public static CardInstance Create(CombatState cs, string cardId) => Create(cs, CardRegistry.Get(cardId));

    public static CardInstance Create(CombatState cs, CardDef def) => new(def, cs.AllocateCardInstanceId());

    public static CardInstance Create(CombatState cs, CardSpec spec)
    {
        var def = ResolveDef(spec);
        var card = new CardInstance(def, cs.AllocateCardInstanceId());
        if (spec.Upgraded)
        {
            card.Upgrade();
        }
        return card;
    }

    public static CardDef ResolveDef(CardSpec spec)
    {
        if (spec.Id is null)
        {
            throw new ArgumentException("A card spec needs a card id.", nameof(spec));
        }
        var def = CardRegistry.Get(spec.Id);
        if (spec.Upgraded && def.MaxUpgradeLevel < 1)
        {
            throw new ArgumentException($"{spec.Id} cannot be upgraded (MaxUpgradeLevel {def.MaxUpgradeLevel}), so '{spec}' is not a card the game can hold.", nameof(spec));
        }
        return def;
    }

    public CardDef Def { get; }

    public string Id => Def.Id;

    public int InstanceId { get; private set; }

    public CardType Type => Def.GetCardType(this);

    public CardRarity Rarity => Def.Rarity;

    public TargetType TargetType => Def.GetTargetType(this);

    public int UpgradeLevel
    {
        get => _upgradeLevel;
        set
        {
            if (value > Def.MaxUpgradeLevel)
            {
                throw new InvalidOperationException($"{Def.Id} cannot be upgraded past its MaxUpgradeLevel.");
            }
            _upgradeLevel = value;
        }
    }

    public int MaxUpgradeLevel => Def.MaxUpgradeLevel;

    public bool IsUpgraded => UpgradeLevel > 0;

    public bool IsUpgradable => UpgradeLevel < Def.MaxUpgradeLevel;

    public CardSpec Spec => new(Def.Id, IsUpgraded);

    public DynamicVarSet Vars { get; set; }

    public CardEnergyCost EnergyCost { get; set; }

    public int KeywordMask { get; set; }

    public bool ExhaustOnNextPlay { get; set; }

    public bool HasSingleTurnRetain { get; set; }

    public bool HasSingleTurnSly { get; set; }

    public int BaseReplayCount { get; set; }

    public bool IsDupe { get; set; }

    public bool IsClone { get; set; }

    public int? FloorAddedToDeck { get; set; }

    public int CurrentTargetId { get; set; } = NoTarget;

    public int CurrentPlayIndex { get; set; }

    public int BaseStarCost
    {
        get => _baseStarCost;
        private set
        {
            if (!Def.HasStarCostX)
            {
                _baseStarCost = value;
            }
        }
    }

    public bool WasStarCostJustUpgraded { get; private set; }

    public int LastStarsSpent { get; set; }

    public PlayerState Owner => _owner ?? throw new InvalidOperationException($"Card {Id}#{InstanceId} has no owner");

    public bool HasOwner => _owner != null;

    public EnchantmentModel? Enchantment
    {
        get => _enchantment;
        set
        {
            value?.BindCard(this);
            _enchantment = value;
        }
    }

    public AfflictionModel? Affliction
    {
        get => _affliction;
        set
        {
            value?.BindCard(this);
            _affliction = value;
        }
    }

    public AbstractModel? Listener { get; set; }

    public CardExtraState? Extra { get; private set; }

    public bool HasBeenRemovedFromState { get; set; }

    public int DeckVersionId { get; set; } = NoDeckVersion;

    public IReadOnlyList<TemporaryCardCost> TemporaryStarCosts => _temporaryStarCosts is null ? Array.Empty<TemporaryCardCost>() : _temporaryStarCosts;

    public TemporaryCardCost? TemporaryStarCost => _temporaryStarCosts is { Count: > 0 } costs ? costs[costs.Count - 1] : null;

    public int CurrentStarCost
    {
        get
        {
            var temporary = TemporaryStarCost;
            if (temporary != null)
            {
                if (temporary.Cost == 0 && BaseStarCost < 0)
                {
                    return BaseStarCost;
                }
                return temporary.Cost;
            }
            return BaseStarCost;
        }
    }

    public bool ShouldRetainThisTurn => HasKeyword(CardKeyword.Retain) || HasSingleTurnRetain;

    public bool IsSlyThisTurn => HasKeyword(CardKeyword.Sly) || HasSingleTurnSly;

    public bool IsRemovable => !HasKeyword(CardKeyword.Eternal);

    public bool GainsBlock => Def.GetGainsBlock(this);

    public bool IsBasicStrikeOrDefend => Def.IsBasicStrikeOrDefend;

    public bool HasTurnEndInHandEffect => Def.HasTurnEndInHandEffect;

    public bool IsInDeck => _owner != null && _owner.Deck.Contains(this);

    public bool IsLiveHookListener => !HasBeenRemovedFromState && _owner != null && _owner.IsActiveForHooks;

    public bool HasKeyword(CardKeyword keyword) => (KeywordMask & CardKeywordSet.BitOf(keyword)) != 0;

    public void AddKeyword(CardKeyword keyword) => KeywordMask |= CardKeywordSet.BitOf(keyword);

    public void RemoveKeyword(CardKeyword keyword) => KeywordMask &= ~CardKeywordSet.BitOf(keyword);

    public bool HasTag(CardTag tag) => Def.CanonicalTags.Contains(tag);

    public CardPile? GetCombatPile(CombatState? cs) => (_owner ?? cs?.Player)?.FindPileContaining(this);

    public bool IsInCombat(CombatState? cs) => GetCombatPile(cs) != null;

    public PileType GetPileType(CombatState? cs)
    {
        var pile = GetCombatPile(cs);
        if (pile != null)
        {
            return pile.Type;
        }
        return IsInDeck ? PileType.Deck : PileType.None;
    }

    public int GetKeywordMaskWithSources(CombatState? cs, KeywordSources sources)
    {
        bool local = (sources & KeywordSources.Local) != 0;
        if ((sources & KeywordSources.Global) == 0 || cs is null || !IsInCombat(cs))
        {
            return local ? KeywordMask : 0;
        }
        var keywords = new CardKeywordSet(local ? KeywordMask : 0);
        Hook.ModifyKeywordsInCombat(cs, this, keywords);
        return keywords.Mask;
    }

    public IReadOnlySet<CardKeyword> GetKeywordsWithSources(CombatState? cs, KeywordSources sources) => new CardKeywordSet(GetKeywordMaskWithSources(cs, sources));

    public IReadOnlySet<CardKeyword> Keywords(CombatState? cs) => GetKeywordsWithSources(cs, KeywordSources.All);

    public bool HasKeywordInCombat(CombatState? cs, CardKeyword keyword) => (GetKeywordMaskWithSources(cs, KeywordSources.All) & CardKeywordSet.BitOf(keyword)) != 0;

    public bool ShouldRetainThisTurnIn(CombatState? cs) => HasKeywordInCombat(cs, CardKeyword.Retain) || HasSingleTurnRetain;

    public bool IsSlyThisTurnIn(CombatState? cs) => HasKeywordInCombat(cs, CardKeyword.Sly) || HasSingleTurnSly;

    public bool IsRemovableIn(CombatState? cs) => !HasKeywordInCombat(cs, CardKeyword.Eternal);

    public bool IsTransformable(CombatState? cs)
    {
        if (!IsRemovableIn(cs))
        {
            return GetPileType(cs) != PileType.Deck;
        }
        return true;
    }

    public void GiveSingleTurnRetain() => HasSingleTurnRetain = true;

    public void GiveSingleTurnSly() => HasSingleTurnSly = true;

    public void SetToFreeThisTurn()
    {
        EnergyCost.SetThisTurnOrUntilPlayed(0);
        SetStarCostThisTurn(0);
    }

    public void SetToFreeThisCombat()
    {
        EnergyCost.SetThisCombat(0);
        SetStarCostThisCombat(0);
    }

    public void SetStarCostUntilPlayed(int cost) => AddTemporaryStarCost(TemporaryCardCost.UntilPlayed(cost));

    public void SetStarCostThisTurn(int cost) => AddTemporaryStarCost(TemporaryCardCost.ThisTurn(cost));

    public void SetStarCostThisCombat(int cost) => AddTemporaryStarCost(TemporaryCardCost.ThisCombat(cost));

    public int GetStarCostThisCombat()
    {
        var costs = _temporaryStarCosts;
        if (costs != null)
        {
            for (int i = 0; i < costs.Count; i++)
            {
                var cost = costs[i];
                if (!cost.ClearsWhenTurnEnds && !cost.ClearsWhenCardIsPlayed)
                {
                    return cost.Cost;
                }
            }
        }
        return BaseStarCost;
    }

    public void UpgradeStarCostBy(int addend)
    {
        if (Def.HasStarCostX)
        {
            throw new InvalidOperationException("UpgradeStarCostBy called on " + Def.Id + " which has star cost X.");
        }
        if (addend == 0)
        {
            return;
        }
        int oldBaseStarCost = BaseStarCost;
        BaseStarCost += addend;
        WasStarCostJustUpgraded = true;
        var costs = _temporaryStarCosts;
        if (BaseStarCost < oldBaseStarCost && costs != null)
        {
            int newBaseStarCost = BaseStarCost;
            for (int i = costs.Count - 1; i >= 0; i--)
            {
                if (costs[i].Cost > newBaseStarCost)
                {
                    costs.RemoveAt(i);
                }
            }
        }
    }

    public int GetStarCostWithModifiers(CombatState cs)
    {
        if (Def.HasStarCostX)
        {
            return (_owner ?? cs.Player).Stars;
        }
        int cost = CurrentStarCost;
        if (cost >= 0 && IsInCombat(cs))
        {
            return (int)Hook.ModifyStarCost(cs, this, cost);
        }
        return cost;
    }

    public bool CostsEnergyOrStars(CombatState cs, bool includeGlobalModifiers)
    {
        if (includeGlobalModifiers)
        {
            if (!EnergyCost.CostsX && EnergyCost.GetWithModifiers(cs, this, CostModifiers.All) > 0)
            {
                return true;
            }
            if (!Def.HasStarCostX && GetStarCostWithModifiers(cs) > 0)
            {
                return true;
            }
        }
        else if (EnergyCost.GetWithModifiers(CostModifiers.Local) > 0 || CurrentStarCost > 0)
        {
            return true;
        }
        return false;
    }

    public int GetEnchantedReplayCount() => _enchantment?.EnchantPlayCount(BaseReplayCount) ?? BaseReplayCount;

    public void CaptureXValuesForAutoPlay(CombatState cs, bool skipXCapture)
    {
        var player = _owner ?? cs.Player;
        if (EnergyCost.CostsX && !skipXCapture)
        {
            EnergyCost.CapturedXValue = player.Energy;
        }
        if (!skipXCapture)
        {
            LastStarsSpent = Def.HasStarCostX ? player.Stars : Math.Max(0, GetStarCostWithModifiers(cs));
        }
    }

    public CardInstance? FindDeckVersion()
    {
        if (DeckVersionId == NoDeckVersion || _owner == null)
        {
            return null;
        }
        var deck = _owner.Deck;
        for (int i = 0; i < deck.Count; i++)
        {
            if (deck[i].InstanceId == DeckVersionId)
            {
                return deck[i];
            }
        }
        return null;
    }

    public int ResolveEnergyXValue(CombatState cs)
    {
        if (!EnergyCost.CostsX)
        {
            throw new InvalidOperationException("This card does not have an X-cost.");
        }
        return Hook.ModifyXValue(cs, this, EnergyCost.CapturedXValue);
    }

    public int ResolveStarXValue(CombatState cs)
    {
        if (!Def.HasStarCostX)
        {
            throw new InvalidOperationException("This card does not have an X-cost.");
        }
        return Hook.ModifyXValue(cs, this, LastStarsSpent);
    }

    public bool HasEnoughResourcesFor(CombatState cs, out UnplayableReason reason)
    {
        var player = _owner ?? cs.Player;
        int energyCost = Math.Max(0, EnergyCost.GetWithModifiers(cs, this, CostModifiers.All));
        int starCost = Math.Max(0, GetStarCostWithModifiers(cs));
        if (energyCost > player.Energy && IsInCombat(cs) && Hook.ShouldPayExcessEnergyCostWithStars(cs, player))
        {
            starCost += (energyCost - player.Energy) * 2;
            energyCost = player.Energy;
        }
        reason = UnplayableReason.None;
        if (energyCost > player.Energy)
        {
            reason |= UnplayableReason.EnergyCostTooHigh;
        }
        if (starCost > player.Stars)
        {
            reason |= UnplayableReason.StarCostTooHigh;
        }
        return reason == UnplayableReason.None;
    }

    public bool HasEnoughResources(CombatState cs, out UnplayableReason reason) => HasEnoughResourcesFor(cs, out reason);

    public bool CanPlay(CombatState cs) => CanPlay(cs, out _);

    public bool CanPlay(CombatState cs, out UnplayableReason reason) => CanPlay(cs, out reason, out _);

    public bool CanPlay(CombatState cs, out UnplayableReason reason, out AbstractModel? preventer)
    {
        reason = UnplayableReason.None;
        if (!IsAlly(cs, _owner ?? cs.Player))
        {
            preventer = null;
            return false;
        }
        if (HasKeywordInCombat(cs, CardKeyword.Unplayable))
        {
            reason |= UnplayableReason.HasUnplayableKeyword;
        }
        if (!HasEnoughResourcesFor(cs, out var resourceReason))
        {
            reason |= resourceReason;
        }
        if (TargetType == TargetType.AnyAlly && CountLivingPlayerCreatures(cs) <= 1)
        {
            reason |= UnplayableReason.NoLivingAllies;
        }
        if (!Hook.ShouldPlay(cs, this, out preventer, AutoPlayType.None))
        {
            reason |= UnplayableReason.BlockedByHook;
        }
        if (!Def.IsPlayable(cs, this))
        {
            reason |= UnplayableReason.BlockedByCardLogic;
        }
        return reason == UnplayableReason.None;
    }

    public bool CanPlayTargeting(CombatState cs, CreatureState? target)
    {
        if (!IsValidTarget(cs, target))
        {
            return false;
        }
        return CanPlay(cs);
    }

    public bool IsValidTarget(CombatState cs, CreatureState? target)
    {
        var targetType = TargetType;
        if (target == null)
        {
            return targetType != TargetType.AnyEnemy && targetType != TargetType.AnyAlly;
        }
        if (!target.IsAlive)
        {
            return false;
        }
        var side = (_owner ?? cs.Player).Side;
        if (targetType == TargetType.AnyEnemy)
        {
            return target.Side != side;
        }
        if (targetType == TargetType.AnyAlly)
        {
            return target.Side == side;
        }
        return false;
    }

    public void SetCurrentTarget(CreatureState? target) => CurrentTargetId = target?.CombatId ?? NoTarget;

    public CreatureState? GetCurrentTarget(CombatState cs) => CurrentTargetId == NoTarget ? null : cs.GetCreatureById(CurrentTargetId);

    public void OnPlay(CombatState cs, CardPlay play)
    {
        if (!ReferenceEquals(play.Card, this))
        {
            throw new ArgumentException("The card play belongs to a different card instance.", nameof(play));
        }
        Def.OnPlay(cs, play);
    }

    public void OnTurnEndInHand(CombatState cs) => Def.OnTurnEndInHand(cs, this);

    public CardLocation GetResultLocationForCardPlay(CombatState cs) => Def.GetResultLocationForCardPlay(cs, this);

    public void EndOfTurnCleanup()
    {
        ExhaustOnNextPlay = false;
        HasSingleTurnRetain = false;
        HasSingleTurnSly = false;
        EnergyCost.EndOfTurnCleanup();
        RemoveTemporaryStarCosts(true);
    }

    public void AfterCardPlayedCleanup()
    {
        EnergyCost.AfterCardPlayedCleanup();
        RemoveTemporaryStarCosts(false);
        CurrentTargetId = NoTarget;
        CurrentPlayIndex = 0;
    }

    public void UpgradeInternal()
    {
        UpgradeLevel = _upgradeLevel + 1;
        Def.OnUpgrade(this);
        Vars.RecalculateForUpgradeOrEnchant();
    }

    public void FinalizeUpgradeInternal()
    {
        Vars.FinalizeUpgrade();
        EnergyCost.FinalizeUpgrade();
        WasStarCostJustUpgraded = false;
    }

    public void Upgrade()
    {
        if (!IsUpgradable)
        {
            return;
        }
        UpgradeInternal();
        FinalizeUpgradeInternal();
    }

    public void DowngradeInternal()
    {
        UpgradeLevel = 0;
        Vars = Def.CreateVars();
        EnergyCost.ResetForDowngrade();
        _baseStarCost = Def.CanonicalStarCost;
        int keywords = 0;
        foreach (var keyword in Def.CanonicalKeywords)
        {
            keywords |= CardKeywordSet.BitOf(keyword);
        }
        KeywordMask = keywords;
        Def.AfterDowngraded(this);
        _enchantment?.ModifyCard();
        _affliction?.AfterApplied();
    }

    public void EnchantInternal(EnchantmentModel enchantment, decimal amount)
    {
        ArgumentNullException.ThrowIfNull(enchantment);
        _enchantment = enchantment;
        enchantment.ApplyInternal(this, amount);
    }

    public void AfflictInternal(AfflictionModel affliction, decimal amount)
    {
        ArgumentNullException.ThrowIfNull(affliction);
        if (_affliction != null)
        {
            throw new InvalidOperationException($"Attempted to afflict card {Id}#{InstanceId} that was already afflicted! This is not allowed");
        }
        _affliction = affliction;
        affliction.BindCard(this);
        affliction.Amount = (int)amount;
    }

    public void ClearEnchantmentInternal()
    {
        var enchantment = _enchantment;
        if (enchantment != null)
        {
            enchantment.ClearInternal();
            _enchantment = null;
        }
    }

    public void ClearAfflictionInternal()
    {
        var affliction = _affliction;
        if (affliction != null)
        {
            affliction.ClearInternal();
            _affliction = null;
            RecalculateCardValues(Owner);
        }
    }

    public static void RecalculateCardValues(PlayerState owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var piles = owner.AllPiles;
        for (int p = 0; p < piles.Count; p++)
        {
            var cards = piles[p].Cards;
            for (int i = 0; i < cards.Count; i++)
            {
                cards[i]._enchantment?.RecalculateValues();
            }
        }
    }

    public void RemoveFromCurrentPile(CombatState? cs)
    {
        var pile = GetCombatPile(cs);
        if (pile != null)
        {
            pile.RemoveInternal(this);
            return;
        }
        if (IsInDeck)
        {
            _owner!.Deck.Remove(this);
        }
    }

    public void RemoveFromState(CombatState? cs)
    {
        RemoveFromCurrentPile(cs);
        HasBeenRemovedFromState = true;
    }

    public void AttachTo(PlayerState owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        if (_owner != null)
        {
            throw new InvalidOperationException("Card " + Id + " already has an owner.");
        }
        _owner = owner;
    }

    public void AppendHookListeners(List<AbstractModel> buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        if (Listener != null)
        {
            buffer.Add(Listener);
        }
        if (_affliction != null)
        {
            buffer.Add(_affliction);
        }
        if (_enchantment != null)
        {
            buffer.Add(_enchantment);
        }
    }

    public CardInstance CloneForCombat(int newInstanceId)
    {
        var clone = (CardInstance)MemberwiseClone();
        clone.InstanceId = newInstanceId;
        clone.Vars = Vars.CloneForOwner();
        clone.EnergyCost = EnergyCost.DeepClone();
        clone._temporaryStarCosts = _temporaryStarCosts is { Count: > 0 } costs ? new List<TemporaryCardCost>(costs) : null;
        clone.Extra = Extra?.DeepClone();
        clone.Listener = Listener is IOwnedListener<CardInstance> owned ? owned.CloneFor(clone) : Listener;
        clone._enchantment = null;
        clone._affliction = null;
        if (_enchantment != null)
        {
            clone.EnchantInternal(_enchantment.MutableClone(), _enchantment.Amount);
        }
        if (_affliction != null)
        {
            var affliction = _affliction.MutableClone();
            clone.AfflictInternal(affliction, affliction.Amount);
        }
        clone.CurrentTargetId = NoTarget;
        clone.CurrentPlayIndex = 0;
        clone.DeckVersionId = NoDeckVersion;
        clone.HasBeenRemovedFromState = false;
        clone.Def.AfterCloned(clone);
        clone.DeckVersionId = InstanceId;
        return clone;
    }

    public CardInstance CreateClone(CombatState cs)
    {
        ArgumentNullException.ThrowIfNull(cs);
        if (GetCombatPile(cs) == null && IsInDeck)
        {
            throw new InvalidOperationException("Cannot create a clone of a card that is not in a combat pile.");
        }
        var clone = cs.CloneCard(this);
        clone.IsClone = true;
        clone.ExhaustOnNextPlay = false;
        return clone;
    }

    public CardInstance CreateCloneForPlayer(CombatState cs, PlayerState player)
    {
        ArgumentNullException.ThrowIfNull(player);
        var clone = CreateClone(cs);
        clone._owner = player;
        return clone;
    }

    public CardInstance DeepClone() => CloneFor(_owner);

    public CardInstance CloneFor(PlayerState? newOwner)
    {
        var clone = (CardInstance)MemberwiseClone();
        clone.Vars = Vars.DeepClone();
        clone.EnergyCost = EnergyCost.DeepClone();
        clone._temporaryStarCosts = _temporaryStarCosts is { Count: > 0 } costs ? new List<TemporaryCardCost>(costs) : null;
        clone._owner = _owner is null ? null : newOwner;
        clone.Extra = Extra?.DeepClone();
        clone._enchantment = _enchantment?.CloneFor(clone);
        clone._affliction = _affliction?.CloneFor(clone);
        clone.Listener = Listener is IOwnedListener<CardInstance> owned ? owned.CloneFor(clone) : Listener;
        return clone;
    }

    public int CompareTo(CardInstance? other)
    {
        if (ReferenceEquals(this, other))
        {
            return 0;
        }
        if (other is null)
        {
            return 1;
        }
        int byId = string.CompareOrdinal(Id, other.Id);
        if (byId != 0)
        {
            return byId;
        }
        return _upgradeLevel.CompareTo(other._upgradeLevel);
    }

    public void WriteCanonical(CanonicalWriter w)
    {
        w.WriteTag(CardCanonicalTags.CardInstance);
        w.WriteString(Def.Id);
        w.WriteInt32(InstanceId);
        w.WriteInt32(_upgradeLevel);
        w.WriteInt32(KeywordMask);
        w.WriteBool(ExhaustOnNextPlay);
        w.WriteBool(HasSingleTurnRetain);
        w.WriteBool(HasSingleTurnSly);
        w.WriteInt32(BaseReplayCount);
        w.WriteBool(IsDupe);
        w.WriteBool(IsClone);
        w.WriteInt32(FloorAddedToDeck ?? -1);
        w.WriteInt32(CurrentTargetId);
        w.WriteInt32(CurrentPlayIndex);
        w.WriteInt32(_baseStarCost);
        w.WriteBool(WasStarCostJustUpgraded);
        w.WriteInt32(LastStarsSpent);
        var costs = _temporaryStarCosts;
        int count = costs?.Count ?? 0;
        w.WriteCount(count);
        for (int i = 0; i < count; i++)
        {
            costs![i].WriteCanonical(w);
        }
        EnergyCost.WriteCanonical(w);
        Vars.WriteCanonical(w);
        w.WriteTag(CardCanonicalTags.CardKernel);
        w.WriteInt32(_owner?.CombatId ?? NoOwner);
        w.WriteInt32(DeckVersionId);
        w.WriteBool(HasBeenRemovedFromState);
        var enchantment = _enchantment;
        w.WriteBool(enchantment != null);
        enchantment?.WriteCanonical(w);
        var affliction = _affliction;
        w.WriteBool(affliction != null);
        affliction?.WriteCanonical(w);
        var listener = Listener;
        w.WriteBool(listener != null);
        if (listener != null)
        {
            w.WriteTag(CardCanonicalTags.CardListener);
            w.WriteString(listener.GetType().Name);
            if (listener is ICanonical canonical)
            {
                canonical.WriteCanonical(w);
            }
        }
        Extra?.WriteCanonical(w);
    }

    public override string ToString() => $"{Id}#{InstanceId}";

    private static bool IsAlly(CombatState cs, PlayerState owner)
    {
        var allies = cs.Allies;
        for (int i = 0; i < allies.Count; i++)
        {
            if (ReferenceEquals(allies[i], owner))
            {
                return true;
            }
        }
        return false;
    }

    private static int CountLivingPlayerCreatures(CombatState cs) => cs.Player.IsAlive ? 1 : 0;

    private void AddTemporaryStarCost(TemporaryCardCost cost) => (_temporaryStarCosts ??= new List<TemporaryCardCost>(2)).Add(cost);

    private void RemoveTemporaryStarCosts(bool turnEnded)
    {
        var costs = _temporaryStarCosts;
        if (costs is not { Count: > 0 })
        {
            return;
        }
        for (int i = costs.Count - 1; i >= 0; i--)
        {
            var cost = costs[i];
            if (turnEnded ? cost.ClearsWhenTurnEnds : cost.ClearsWhenCardIsPlayed)
            {
                costs.RemoveAt(i);
            }
        }
    }
}
