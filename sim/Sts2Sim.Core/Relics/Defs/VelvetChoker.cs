namespace Sts2Sim.Core.Relics;

public sealed class VelvetChoker : RelicModel
{
    public const string RelicId = "VELVET_CHOKER";

    private int _cardsPlayedThisTurn;

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Ancient;

    public override int DisplayAmount => _cardsPlayedThisTurn;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new CardsVar(6),
        new EnergyVar(1)
    };

    public int CardsPlayedThisTurn => _cardsPlayedThisTurn;

    private bool ShouldPreventCardPlay => _cardsPlayedThisTurn >= DynamicVars.Cards.IntValue;

    public override decimal ModifyMaxEnergy(CombatState cs, PlayerState player, decimal amount)
    {
        if (!ReferenceEquals(player, Owner))
        {
            return amount;
        }
        return amount + DynamicVars.Energy.BaseValue;
    }

    public override bool ShouldPlay(CombatState cs, CardInstance card, AutoPlayType autoPlayType)
    {
        if (!ReferenceEquals(CombatStatus.CardOwner(cs, card), Owner))
        {
            return true;
        }
        return !ShouldPreventCardPlay;
    }

    public override void AfterCardPlayed(CombatState cs, CardPlay cardPlay)
    {
        if (ReferenceEquals(CombatStatus.CardOwner(cs, cardPlay.Card), Owner))
        {
            _cardsPlayedThisTurn++;
        }
    }

    public override void AfterRoomEntered(RunState rs, AbstractRoom room)
    {
        if (room is not CombatRoom)
        {
            return;
        }
        _cardsPlayedThisTurn = 0;
    }

    public override void AfterCombatEnd(RunState rs, CombatRoom? room)
    {
        _cardsPlayedThisTurn = 0;
    }

    public override void BeforeSideTurnStart(CombatState cs, CombatSide side, IReadOnlyList<CreatureState> participants)
    {
        if (!CombatStatus.ContainsById(participants, Owner))
        {
            return;
        }
        _cardsPlayedThisTurn = 0;
    }

    protected override void WriteExtraCanonical(CanonicalWriter w)
    {
        w.WriteInt32(_cardsPlayedThisTurn);
    }
}
