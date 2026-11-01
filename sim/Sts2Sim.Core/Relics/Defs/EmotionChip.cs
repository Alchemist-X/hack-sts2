namespace Sts2Sim.Core.Relics;

public sealed class EmotionChip : RelicModel
{
    public const string RelicId = "EMOTION_CHIP";

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Rare;

    private bool LostHpInPreviousTurn(CombatState cs)
    {
        int ownerId = Owner.CombatId;
        return cs.History.AnyLastPlayerTurn<DamageReceivedEntry>(Owner, e => e.Result.ReceiverId == ownerId && !e.Result.WasFullyBlocked);
    }

    public override void AfterDamageReceived(RunState rs, CreatureState target, DamageResult result, ValueProp props, CreatureState? dealer, CardInstance? cardSource)
    {
        var cs = rs.Combat;
        if (cs == null || !cs.IsInProgress)
        {
            return;
        }
        if (!ReferenceEquals(target, Owner))
        {
            return;
        }
        if (result.UnblockedDamage <= 0)
        {
            return;
        }
        Status = RelicStatus.Active;
    }

    public override void AfterPlayerTurnStart(CombatState cs, PlayerState player)
    {
        if (!ReferenceEquals(player, Owner))
        {
            return;
        }
        Status = RelicStatus.Normal;
        if (!LostHpInPreviousTurn(cs))
        {
            return;
        }
        foreach (var orb in Owner.OrbQueue.Orbs)
        {
            OrbCmd.Passive(cs, orb, null, countAffectedByHooks: true);
        }
    }

    public override void AfterCombatEnd(RunState rs, CombatRoom? room)
    {
        Status = RelicStatus.Normal;
    }
}
