namespace Sts2Sim.Core.Relics;

public sealed class WhisperingEarring : RelicModel
{
    public const string RelicId = "WHISPERING_EARRING";

    public const int maxCardsToPlay = 13;

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Ancient;

    protected override IEnumerable<DynamicVar> CanonicalVars => new[]
    {
        new EnergyVar(1)
    };

    public override decimal ModifyMaxEnergy(CombatState cs, PlayerState player, decimal amount)
    {
        if (!ReferenceEquals(player, Owner))
        {
            return amount;
        }
        return amount + DynamicVars.Energy.BaseValue;
    }

    public override void AfterAutoPrePlayPhaseEnteredLate(CombatState cs, PlayerState player)
    {
        if (!ReferenceEquals(player, Owner))
        {
            return;
        }
        if (Owner.TurnNumber > 1)
        {
            return;
        }
        using var selector = cs.Choice.PushSelectorScope(new VakuuCardSelector());
        int cardsPlayed = 0;
        int startTurn = Owner.TurnNumber;
        while (cardsPlayed < maxCardsToPlay && !CombatStatus.IsOverOrEnding(cs) && Owner.TurnNumber == startTurn)
        {
            var card = Owner.Hand.Cards.FirstOrDefault(c => c.CanPlay(cs));
            if (card == null)
            {
                break;
            }
            var target = GetTarget(cs, card);
            ActionExecutor.SpendResources(cs, card);
            CardCmd.AutoPlay(cs, card, target, AutoPlayType.Default, skipXCapture: true);
            cardsPlayed++;
        }
    }

    private CreatureState? GetTarget(CombatState cs, CardInstance card)
    {
        var combatTargets = cs.Rng.CombatTargets;
        switch (card.TargetType)
        {
            case TargetType.AnyEnemy:
            {
                var hittable = new List<MonsterState>(cs.Monsters.Count);
                cs.GetHittableEnemies(hittable);
                return hittable.FirstOrDefault();
            }
            case TargetType.AnyAlly:
                return combatTargets.NextItem(cs.Allies.Where(c => c != null && c.IsAlive && c.IsPlayer && !ReferenceEquals(c, Owner)));
            case TargetType.AnyPlayer:
                return Owner;
            default:
                return null;
        }
    }
}
