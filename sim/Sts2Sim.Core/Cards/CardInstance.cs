namespace Sts2Sim.Core.Cards;

public sealed class CardInstance : ICanonical
{
    public const int NoTarget = -1;

    private int _upgradeLevel;
    private int _baseStarCost;
    private List<TemporaryCardCost>? _temporaryStarCosts;

    public CardInstance(CardDef def, int instanceId)
    {
        Def = def;
        InstanceId = instanceId;
        Vars = def.CreateVars();
        EnergyCost = new CardEnergyCost(def.CanonicalCost, def.CostsX);
        _baseStarCost = def.CanonicalStarCost;
        foreach (var keyword in def.CanonicalKeywords)
        {
            KeywordMask |= 1 << (int)keyword;
        }
    }

    public static CardInstance Create(CombatState cs, string cardId) => Create(cs, CardRegistry.Get(cardId));

    public static CardInstance Create(CombatState cs, CardDef def) => new(def, cs.AllocateCardInstanceId());

    public CardDef Def { get; }

    public string Id => Def.Id;

    public int InstanceId { get; }

    public CardType Type => Def.Type;

    public CardRarity Rarity => Def.Rarity;

    public TargetType TargetType => Def.TargetType;

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

    public DynamicVarSet Vars { get; set; }

    public CardEnergyCost EnergyCost { get; set; }

    public int KeywordMask { get; set; }

    public bool ExhaustOnNextPlay { get; set; }

    public bool HasSingleTurnRetain { get; set; }

    public bool HasSingleTurnSly { get; set; }

    public int BaseReplayCount { get; set; }

    public bool IsDupe { get; set; }

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

    public bool GainsBlock => Def.GainsBlock;

    public bool IsBasicStrikeOrDefend => Def.IsBasicStrikeOrDefend;

    public bool HasTurnEndInHandEffect => Def.HasTurnEndInHandEffect;

    public bool HasKeyword(CardKeyword keyword) => (KeywordMask & (1 << (int)keyword)) != 0;

    public void AddKeyword(CardKeyword keyword) => KeywordMask |= 1 << (int)keyword;

    public void RemoveKeyword(CardKeyword keyword) => KeywordMask &= ~(1 << (int)keyword);

    public bool HasTag(CardTag tag) => Def.CanonicalTags.Contains(tag);

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
            return cs.Player.Stars;
        }
        int cost = CurrentStarCost;
        if (cost >= 0 && cs.Player.FindPileContaining(this) != null)
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

    public int GetEnchantedReplayCount() => BaseReplayCount;

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

    public bool HasEnoughResources(CombatState cs, out UnplayableReason reason)
    {
        var player = cs.Player;
        int energyCost = Math.Max(0, EnergyCost.GetWithModifiers(cs, this, CostModifiers.All));
        int starCost = Math.Max(0, GetStarCostWithModifiers(cs));
        if (energyCost > player.Energy && Hook.ShouldPayExcessEnergyCostWithStars(cs, player))
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

    public bool CanPlay(CombatState cs) => CanPlay(cs, out _);

    public bool CanPlay(CombatState cs, out UnplayableReason reason)
    {
        reason = UnplayableReason.None;
        if (HasKeyword(CardKeyword.Unplayable))
        {
            reason |= UnplayableReason.HasUnplayableKeyword;
        }
        if (!HasEnoughResources(cs, out var resourceReason))
        {
            reason |= resourceReason;
        }
        if (TargetType == TargetType.AnyAlly && CountLivingPlayerCreatures(cs) <= 1)
        {
            reason |= UnplayableReason.NoLivingAllies;
        }
        if (!Hook.ShouldPlay(cs, this))
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
        if (target == null)
        {
            return TargetType != TargetType.AnyEnemy && TargetType != TargetType.AnyAlly;
        }
        if (!target.IsAlive)
        {
            return false;
        }
        if (TargetType == TargetType.AnyEnemy)
        {
            return target.Side != cs.Player.Side;
        }
        if (TargetType == TargetType.AnyAlly)
        {
            return target.Side == cs.Player.Side;
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

    public CardInstance DeepClone()
    {
        var clone = (CardInstance)MemberwiseClone();
        clone.Vars = Vars.DeepClone();
        clone.EnergyCost = EnergyCost.DeepClone();
        clone._temporaryStarCosts = _temporaryStarCosts is { Count: > 0 } costs ? new List<TemporaryCardCost>(costs) : null;
        return clone;
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
