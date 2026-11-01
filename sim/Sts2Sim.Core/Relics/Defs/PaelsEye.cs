namespace Sts2Sim.Core.Relics;

public sealed class PaelsEye : RelicModel
{
    public const string RelicId = "PAELS_EYE";

    public const string WhisperingEarringId = "WHISPERING_EARRING";

    private bool _usedThisCombat;
    private bool _wasOwnerPartOfLastPlayerTurn = true;

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Ancient;

    private bool UsedThisCombat
    {
        get => _usedThisCombat;
        set => _usedThisCombat = value;
    }

    private bool WasOwnerPartOfLastPlayerTurn
    {
        get => _wasOwnerPartOfLastPlayerTurn;
        set => _wasOwnerPartOfLastPlayerTurn = value;
    }

    public override void AfterObtained(RunState rs)
    {
        WasOwnerPartOfLastPlayerTurn = IsPartOfPlayerTurn(rs.Combat, Owner);
    }

    public override void BeforeCardPlayed(CombatState cs, CardPlay cardPlay)
    {
        if (!cs.IsInProgress)
        {
            return;
        }
        if (UsedThisCombat)
        {
            return;
        }
        if (cardPlay.IsAutoPlay)
        {
            return;
        }
        if (!cardPlay.Card.HasOwner || !ReferenceEquals(cardPlay.Card.Owner, Owner))
        {
            return;
        }
        Status = RelicStatus.Normal;
    }

    public override void AfterSideTurnStart(CombatState cs, CombatSide side, IReadOnlyList<CreatureState> participants)
    {
        if (side != Owner.Side)
        {
            return;
        }
        if (UsedThisCombat)
        {
            return;
        }
        if (participants.Any(p => ReferenceEquals(p, Owner)))
        {
            Status = RelicStatus.Active;
            WasOwnerPartOfLastPlayerTurn = true;
        }
        else
        {
            Status = RelicStatus.Normal;
            WasOwnerPartOfLastPlayerTurn = false;
        }
    }

    public override bool ShouldTakeExtraTurn(CombatState cs, PlayerState player)
    {
        if (!UsedThisCombat && !AnyCardsPlayedThisTurn(cs) && WasOwnerPartOfLastPlayerTurn)
        {
            return ReferenceEquals(player, Owner);
        }
        return false;
    }

    public override void BeforeSideTurnEndEarly(CombatState cs, CombatSide side, IReadOnlyList<CreatureState> participants)
    {
        if (!participants.Any(p => ReferenceEquals(p, Owner)) || UsedThisCombat || AnyCardsPlayedThisTurn(cs) || !WasOwnerPartOfLastPlayerTurn)
        {
            return;
        }
        foreach (var card in Owner.Hand.Cards.ToList())
        {
            CardCmd.Exhaust(cs, card);
        }
    }

    public override void AfterTakingExtraTurn(CombatState cs, PlayerState player)
    {
        if (!ReferenceEquals(player, Owner))
        {
            return;
        }
        Status = RelicStatus.Normal;
        UsedThisCombat = true;
    }

    public override void AfterCombatEnd(RunState rs, CombatRoom? room)
    {
        Status = RelicStatus.Normal;
        UsedThisCombat = false;
    }

    private bool AnyCardsPlayedThisTurn(CombatState cs)
    {
        if (Owner.TurnNumber == 1 && Owner.Relics.Any(r => r.Id == WhisperingEarringId))
        {
            return true;
        }
        return cs.History.CardPlaysFinished.Any(e => e.Stamp.ActorId == Owner.CombatId && e.HappenedThisTurn(cs) && !e.CardPlay.IsAutoPlay);
    }

    private static bool IsPartOfPlayerTurn(CombatState? cs, PlayerState player)
    {
        if (cs == null || cs.CurrentSide != CombatSide.Player)
        {
            return false;
        }
        if (cs.PlayersTakingExtraTurn.Count == 0)
        {
            return true;
        }
        return cs.PlayersTakingExtraTurn.Contains(player.CombatId);
    }

    protected override void WriteExtraCanonical(CanonicalWriter w)
    {
        w.WriteBool(_usedThisCombat);
        w.WriteBool(_wasOwnerPartOfLastPlayerTurn);
    }
}
