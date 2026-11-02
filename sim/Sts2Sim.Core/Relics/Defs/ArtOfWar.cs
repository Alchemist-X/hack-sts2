namespace Sts2Sim.Core.Relics;

public sealed class ArtOfWar : RelicModel
{
    public const string RelicId = "ART_OF_WAR";

    private bool _anyAttacksPlayedLastTurn;
    private bool _anyAttacksPlayedThisTurn;

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Rare;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new EnergyVar(1)
    };

    private bool AnyAttacksPlayedLastTurn
    {
        get => _anyAttacksPlayedLastTurn;
        set => _anyAttacksPlayedLastTurn = value;
    }

    private bool AnyAttacksPlayedThisTurn
    {
        get => _anyAttacksPlayedThisTurn;
        set => _anyAttacksPlayedThisTurn = value;
    }

    public override void AfterCardPlayed(CombatState cs, CardPlay cardPlay)
    {
        var card = cardPlay.Card;
        if (!card.HasOwner || !ReferenceEquals(Owner, card.Owner))
        {
            return;
        }
        if (!cs.IsInProgress)
        {
            return;
        }
        if (card.Type != CardType.Attack)
        {
            return;
        }
        if (AnyAttacksPlayedLastTurn)
        {
            return;
        }
        Status = RelicStatus.Normal;
        AnyAttacksPlayedThisTurn = true;
    }

    public override void AfterSideTurnEnd(CombatState cs, CombatSide side, IReadOnlyList<CreatureState> participants)
    {
        if (!participants.Any(p => ReferenceEquals(p, Owner)))
        {
            return;
        }
        AnyAttacksPlayedLastTurn = AnyAttacksPlayedThisTurn;
        AnyAttacksPlayedThisTurn = false;
    }

    public override void AfterEnergyReset(CombatState cs, PlayerState player)
    {
        if (!ReferenceEquals(player, Owner))
        {
            return;
        }
        Status = RelicStatus.Active;
        if (Owner.TurnNumber <= 1)
        {
            return;
        }
        if (!AnyAttacksPlayedLastTurn)
        {
            PlayerCmd.GainEnergy(cs, DynamicVars.Energy.BaseValue, Owner);
        }
        AnyAttacksPlayedLastTurn = false;
        AnyAttacksPlayedThisTurn = false;
    }

    public override void AfterCombatEnd(RunState rs, CombatRoom? room)
    {
        Status = RelicStatus.Normal;
        AnyAttacksPlayedLastTurn = false;
        AnyAttacksPlayedThisTurn = false;
    }

    protected override void WriteExtraCanonical(CanonicalWriter w)
    {
        w.WriteBool(_anyAttacksPlayedLastTurn);
        w.WriteBool(_anyAttacksPlayedThisTurn);
    }
}
