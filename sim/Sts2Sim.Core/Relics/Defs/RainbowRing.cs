namespace Sts2Sim.Core.Relics;

public sealed class RainbowRing : RelicModel
{
    public const string RelicId = "RAINBOW_RING";

    public const int MaxActivationsPerTurn = 1;

    private int _attacksPlayedThisTurn;
    private int _skillsPlayedThisTurn;
    private int _powersPlayedThisTurn;
    private int _activationCountThisTurn;

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Rare;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new PowerVar<StrengthPower>(1m),
        new PowerVar<DexterityPower>(1m)
    };

    public int ActivationCountThisTurn
    {
        get => _activationCountThisTurn;
        private set
        {
            _activationCountThisTurn = value;
            Status = _activationCountThisTurn > 0 ? RelicStatus.Active : RelicStatus.Normal;
        }
    }

    public override void BeforeSideTurnStart(CombatState cs, CombatSide side, IReadOnlyList<CreatureState> participants)
    {
        if (!CombatStatus.ContainsById(participants, Owner))
        {
            return;
        }
        ResetCounters();
    }

    public override void AfterCardPlayed(CombatState cs, CardPlay cardPlay)
    {
        var card = cardPlay.Card;
        if (!ReferenceEquals(CombatStatus.CardOwner(cs, card), Owner) || !cs.IsInProgress || ActivationCountThisTurn >= MaxActivationsPerTurn)
        {
            return;
        }
        _attacksPlayedThisTurn += card.Type == CardType.Attack ? 1 : 0;
        _skillsPlayedThisTurn += card.Type == CardType.Skill ? 1 : 0;
        _powersPlayedThisTurn += card.Type == CardType.Power ? 1 : 0;
        if (_attacksPlayedThisTurn > 0 && _skillsPlayedThisTurn > 0 && _powersPlayedThisTurn > 0)
        {
            PowerCmd.Apply<StrengthPower>(cs, Owner, DynamicVars.Strength.BaseValue, Owner, null);
            PowerCmd.Apply<DexterityPower>(cs, Owner, DynamicVars.Dexterity.BaseValue, Owner, null);
            ActivationCountThisTurn++;
        }
    }

    public override void AfterCombatEnd(RunState rs, CombatRoom? room)
    {
        ResetCounters();
    }

    private void ResetCounters()
    {
        _attacksPlayedThisTurn = 0;
        _skillsPlayedThisTurn = 0;
        _powersPlayedThisTurn = 0;
        ActivationCountThisTurn = 0;
    }

    protected override void WriteExtraCanonical(CanonicalWriter w)
    {
        w.WriteInt32(_attacksPlayedThisTurn);
        w.WriteInt32(_skillsPlayedThisTurn);
        w.WriteInt32(_powersPlayedThisTurn);
        w.WriteInt32(_activationCountThisTurn);
    }
}
