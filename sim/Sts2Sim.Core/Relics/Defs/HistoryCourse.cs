namespace Sts2Sim.Core.Relics;

public sealed class HistoryCourse : RelicModel
{
    public const string RelicId = "HISTORY_COURSE";

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Event;

    public override void AfterAutoPrePlayPhaseEntered(CombatState cs, PlayerState player)
    {
        if (!ReferenceEquals(player, Owner) || Owner.TurnNumber == 1)
        {
            return;
        }
        HistoryCardPlay? last = null;
        foreach (var e in cs.History.CardPlaysFinished)
        {
            if (e.CardPlay.PlayerId == Owner.CombatId && e.HappenedLastPlayerTurn(Owner) && e.CardPlay.CardType == CardType.Attack && !e.CardPlay.IsDupe)
            {
                last = e.CardPlay;
            }
        }
        if (last == null)
        {
            return;
        }
        var card = FindCard(Owner, last.Value.CardInstanceId)
            ?? throw new NotImplementedException("W3 gap: HISTORY_COURSE needs the CardInstance of a played card that has left every combat pile (the game keeps the CardModel reference in CardPlayFinishedEntry)");
        CardCmd.AutoPlay(cs, CreateDupe(cs, card), null);
    }

    private static CardInstance CreateDupe(CombatState cs, CardInstance card)
    {
        var dupe = cs.CloneCard(card);
        dupe.ExhaustOnNextPlay = false;
        dupe.IsDupe = true;
        dupe.RemoveKeyword(CardKeyword.Exhaust);
        return dupe;
    }

    private static CardInstance? FindCard(PlayerState player, int instanceId)
    {
        foreach (var pile in player.AllPiles)
        {
            foreach (var c in pile.Cards)
            {
                if (c.InstanceId == instanceId)
                {
                    return c;
                }
            }
        }
        return null;
    }
}
