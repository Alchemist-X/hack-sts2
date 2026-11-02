namespace Sts2Sim.Core.Cards;

public sealed class MadScience : CardDef
{
    public const string CardId = "MAD_SCIENCE";
    public const int Cost = 1;
    public const int AttackDamage = 12;
    public const int SkillBlock = 8;
    public const string SappingWeakKey = "SappingWeak";
    public const int SappingWeakValue = 2;
    public const string SappingVulnerableKey = "SappingVulnerable";
    public const int SappingVulnerableValue = 2;
    public const string ViolenceHitsKey = "ViolenceHits";
    public const int ViolenceHitsValue = 3;
    public const string ChokingDamageKey = "ChokingDamage";
    public const int ChokingDamageValue = 6;
    public const string EnergizedEnergyKey = "EnergizedEnergy";
    public const int EnergizedEnergyValue = 2;
    public const string WisdomCardsKey = "WisdomCards";
    public const int WisdomCardsValue = 3;
    public const string ExpertiseStrengthKey = "ExpertiseStrength";
    public const int ExpertiseStrengthValue = 2;
    public const string ExpertiseDexterityKey = "ExpertiseDexterity";
    public const int ExpertiseDexterityValue = 2;
    public const string CuriousReductionKey = "CuriousReduction";
    public const int CuriousReductionValue = 1;
    public const string CuriousPowerId = "CURIOUS_POWER";
    public const string ImprovementPowerId = "IMPROVEMENT_POWER";
    public const decimal ImprovementAmount = 1m;
    public const int ChaosCardCount = 1;
    public const ushort StateTag = 0xCB65;

    public enum RiderEffect
    {
        None,
        Sapping,
        Violence,
        Choking,
        Energized,
        Wisdom,
        Chaos,
        Expertise,
        Curious,
        Improvement
    }

    public MadScience()
        : base(CardId, Cost, CardType.None, CardRarity.Event, TargetType.AnyEnemy)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DamageVar(AttackDamage, ValueProp.Move),
        new BlockVar(SkillBlock, ValueProp.Move),
        new PowerVar<WeakPower>(SappingWeakKey, SappingWeakValue),
        new PowerVar<VulnerablePower>(SappingVulnerableKey, SappingVulnerableValue),
        new DynamicVar(ViolenceHitsKey, ViolenceHitsValue),
        new PowerVar<StranglePower>(ChokingDamageKey, ChokingDamageValue),
        new EnergyVar(EnergizedEnergyKey, EnergizedEnergyValue),
        new CardsVar(WisdomCardsKey, WisdomCardsValue),
        new PowerVar<StrengthPower>(ExpertiseStrengthKey, ExpertiseStrengthValue),
        new PowerVar<DexterityPower>(ExpertiseDexterityKey, ExpertiseDexterityValue),
        new DynamicVar(CuriousReductionKey, CuriousReductionValue)
    };

    public override CardExtraState? CreateExtraState() => new MadScienceState();

    public static CardType GetTinkerTimeType(CardInstance card) => (CardType)((MadScienceState)card.Extra!).TinkerTimeType;

    public static RiderEffect GetTinkerTimeRider(CardInstance card) => (RiderEffect)((MadScienceState)card.Extra!).TinkerTimeRider;

    public static void SetTinkerTimeType(CardInstance card, CardType type) => ((MadScienceState)card.Extra!).TinkerTimeType = (int)type;

    public static void SetTinkerTimeRider(CardInstance card, RiderEffect rider) => ((MadScienceState)card.Extra!).TinkerTimeRider = (int)rider;

    public override CardType GetCardType(CardInstance card) => GetTinkerTimeType(card);

    public override TargetType GetTargetType(CardInstance card)
    {
        if (GetTinkerTimeType(card) != CardType.Attack)
        {
            return TargetType.Self;
        }
        return TargetType.AnyEnemy;
    }

    public override bool GetGainsBlock(CardInstance card) => GetTinkerTimeType(card) == CardType.Skill;

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var madScience = play.Card;
        if (madScience.TargetType == TargetType.AnyEnemy && play.Target == null)
        {
            ArgumentNullException.ThrowIfNull(play.Target);
        }
        switch (GetTinkerTimeType(madScience))
        {
            case CardType.Attack:
                ExecuteAttack(cs, play.Target!, play);
                break;
            case CardType.Skill:
                ExecuteSkill(cs, play);
                break;
            case CardType.Power:
                ExecutePower(cs, play);
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }
        var tinkerTimeRider = GetTinkerTimeRider(madScience);
        if (tinkerTimeRider == RiderEffect.Sapping || (uint)(tinkerTimeRider - 3) <= 3u)
        {
            ExecuteRider(cs, play, tinkerTimeRider, play.Target);
        }
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.AddKeyword(CardKeyword.Innate);
    }

    private static void ExecuteAttack(CombatState cs, CreatureState target, CardPlay play)
    {
        var madScience = play.Card;
        int hitCount = GetTinkerTimeRider(madScience) != RiderEffect.Violence ? 1 : madScience.Vars[ViolenceHitsKey].IntValue;
        DamageCmd.Attack(cs, madScience.Vars.Damage.BaseValue).WithHitCount(hitCount).FromCard(madScience, play).Targeting(target).Execute();
    }

    private static void ExecuteSkill(CombatState cs, CardPlay play)
    {
        var madScience = play.Card;
        CreatureCmd.GainBlock(cs, madScience.Owner, madScience.Vars.Block, play);
    }

    private static void ExecutePower(CombatState cs, CardPlay play)
    {
        var madScience = play.Card;
        var owner = madScience.Owner;
        switch (GetTinkerTimeRider(madScience))
        {
            case RiderEffect.Expertise:
                PowerCmd.Apply<StrengthPower>(cs, owner, madScience.Vars[ExpertiseStrengthKey].BaseValue, owner, madScience);
                PowerCmd.Apply<DexterityPower>(cs, owner, madScience.Vars[ExpertiseDexterityKey].BaseValue, owner, madScience);
                break;
            case RiderEffect.Curious:
                PowerCmd.Apply(cs, PowerRegistry.Create(CuriousPowerId), owner, madScience.Vars[CuriousReductionKey].BaseValue, owner, madScience);
                break;
            case RiderEffect.Improvement:
                PowerCmd.Apply(cs, PowerRegistry.Create(ImprovementPowerId), owner, ImprovementAmount, owner, madScience);
                break;
        }
    }

    private static void ExecuteRider(CombatState cs, CardPlay play, RiderEffect rider, CreatureState? target)
    {
        var madScience = play.Card;
        var owner = madScience.Owner;
        switch (rider)
        {
            case RiderEffect.Sapping:
                PowerCmd.Apply<WeakPower>(cs, target!, madScience.Vars[SappingWeakKey].BaseValue, owner, madScience);
                PowerCmd.Apply<VulnerablePower>(cs, target!, madScience.Vars[SappingVulnerableKey].BaseValue, owner, madScience);
                break;
            case RiderEffect.Choking:
                PowerCmd.Apply<StranglePower>(cs, target!, madScience.Vars[ChokingDamageKey].BaseValue, owner, madScience);
                break;
            case RiderEffect.Energized:
                PlayerCmd.GainEnergy(cs, madScience.Vars[EnergizedEnergyKey].IntValue, owner);
                break;
            case RiderEffect.Wisdom:
                CardPileCmd.Draw(cs, madScience.Vars[WisdomCardsKey].IntValue, owner);
                break;
            case RiderEffect.Chaos:
            {
                var rs = cs.Run;
                var pool = CardPools.ForCharacter(owner.CharacterId);
                var options = pool.GetUnlockedCards(rs.Unlocks, CardPools.MultiplayerConstraintFor(rs));
                var cardModel = CardFactory.GetDistinctForCombat(cs, owner, options, ChaosCardCount, rs.Rng.CombatCardGeneration).First();
                cardModel.SetToFreeThisTurn();
                CardPileCmd.AddGeneratedCardToCombat(cs, cardModel, PileType.Hand, owner);
                break;
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(rider), rider, null);
        }
    }

    private sealed class MadScienceState : CardExtraState
    {
        public int TinkerTimeType { get; set; }

        public int TinkerTimeRider { get; set; }

        public override CardExtraState DeepClone() => (MadScienceState)MemberwiseClone();

        public override void WriteCanonical(CanonicalWriter w)
        {
            w.WriteTag(StateTag);
            w.WriteInt32(TinkerTimeType);
            w.WriteInt32(TinkerTimeRider);
        }
    }
}
