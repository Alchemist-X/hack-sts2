namespace Sts2Sim.Core.Run;

internal static class Events121Support
{
    public const ValueProp HpLossProps = ValueProp.Unblockable | ValueProp.Unpowered;

    public const string UnportedRelicSite = "Event: relic def not ported; obtain skipped";

    public const string UnportedCardSite = "Event: card def not ported; deck add skipped";

    public static void Heal(RunState rs, CreatureState creature, decimal amount)
    {
        CreatureInternals.HealInternal(creature, amount);
        if (amount > 0m)
        {
            Hook.AfterCurrentHpChanged(rs, null, creature, amount);
        }
    }

    public static void GainMaxHp(RunState rs, CreatureState creature, decimal amount)
    {
        if (amount < 0m)
        {
            throw new ArgumentException("amount must be non-negative. Use LoseMaxHp for max HP loss.");
        }
        int oldMaxHp = creature.MaxHp;
        CreatureInternals.SetMaxHpInternal(creature, Math.Max(0m, creature.MaxHp + amount));
        decimal gained = creature.MaxHp - oldMaxHp;
        if (creature.MaxHp <= 0)
        {
            Kill(rs, creature);
        }
        Heal(rs, creature, gained);
    }

    public static void LoseMaxHp(RunState rs, CreatureState creature, decimal amount, bool isFromCard)
    {
        if (amount < 0m)
        {
            throw new ArgumentException("amount must be non-negative. Use GainMaxHp for max HP gain.");
        }
        decimal newMaxHp = creature.MaxHp - amount;
        if (newMaxHp < creature.CurrentHp)
        {
            var props = isFromCard ? (HpLossProps | ValueProp.Move) : HpLossProps;
            Damage(rs, creature, creature.CurrentHp - newMaxHp, props);
        }
        CreatureInternals.SetMaxHpInternal(creature, Math.Max(0m, Math.Max(1.0m, newMaxHp)));
        if (creature.MaxHp <= 0)
        {
            Kill(rs, creature);
        }
    }

    public static DamageResult Damage(RunState rs, CreatureState target, decimal amount, ValueProp props)
    {
        if (target.IsDead)
        {
            return new DamageResult(target, props);
        }
        List<AbstractModel>? modifiers = null;
        decimal modifiedAmount = Hook.ModifyDamage(rs, null, target, null, amount, props, null, null, ModifyDamageHookType.All, ref modifiers);
        Hook.AfterModifyingDamageAmount(rs, null, null, modifiers);
        Hook.BeforeDamageReceived(rs, null, target, modifiedAmount, props, null, null);
        decimal blockedDamage = CreatureInternals.DamageBlockInternal(target, modifiedAmount, props);
        modifiers = null;
        decimal unblockedDamage = Hook.ModifyHpLost(rs, null, target, Math.Max(modifiedAmount - blockedDamage, 0m), props, null, null, HpLossHookPhase.BeforeOsty, ref modifiers);
        Hook.AfterModifyingHpLostBeforeOsty(rs, null, modifiers);
        modifiers = null;
        unblockedDamage = Hook.ModifyHpLost(rs, null, target, unblockedDamage, props, null, null, HpLossHookPhase.AfterOsty, ref modifiers);
        Hook.AfterModifyingHpLostAfterOsty(rs, null, modifiers);
        var result = CreatureInternals.LoseHpInternal(target, unblockedDamage, props);
        result.BlockedDamage = (int)blockedDamage;
        if (result.UnblockedDamage > 0)
        {
            Hook.AfterCurrentHpChanged(rs, null, target, -result.UnblockedDamage);
        }
        if (!result.WasTargetKilled || !target.IsDead)
        {
            Hook.AfterDamageReceived(rs, null, target, result, props, null, null);
        }
        else
        {
            Kill(rs, target);
        }
        return result;
    }

    public static void Kill(RunState rs, CreatureState creature) => Kill(rs, creature, 0);

    private static void Kill(RunState rs, CreatureState creature, int recursion)
    {
        if (!creature.IsPlayer)
        {
            return;
        }
        int currentHp = creature.CurrentHp;
        if (currentHp > 0)
        {
            CreatureInternals.LoseHpInternal(creature, currentHp, HpLossProps);
            Hook.AfterCurrentHpChanged(rs, null, creature, -currentHp);
        }
        Hook.BeforeDeath(rs, null, creature);
        AbstractModel? preventer = null;
        if (creature.MaxHp <= 0 || Hook.ShouldDie(rs, null, creature, out preventer))
        {
            Hook.AfterDeath(rs, null, creature, false);
            if (creature is PlayerState player)
            {
                player.OrbQueue.Clear();
                player.DeactivateHooks();
            }
            return;
        }
        if (recursion >= 10)
        {
            throw new InvalidOperationException("Something is continually preventing the player from being killed!");
        }
        Hook.AfterDeath(rs, null, creature, true);
        Hook.AfterPreventingDeath(rs, null, preventer!, creature);
        if (creature.IsDead)
        {
            Kill(rs, creature, recursion + 1);
        }
    }

    public static RelicModel? ObtainRelic(RunState rs, string relicId, PlayerState player)
    {
        if (!RelicRegistry.Models.Contains(relicId))
        {
            rs.Fidelity.Record(FidelityFlag.ApproximatedRule, UnportedRelicSite + " (" + relicId + ")");
            return null;
        }
        return RelicCmd.Obtain(rs, RelicRegistry.Create(relicId), player);
    }

    public static CardInstance? AddCardToDeck(RunState rs, string cardId, PlayerState player)
    {
        if (!CardRegistry.TryGet(cardId, out var def))
        {
            rs.Fidelity.Record(FidelityFlag.ApproximatedRule, UnportedCardSite + " (" + cardId + ")");
            return null;
        }
        var card = CardFactory.CreateRunCard(rs, def, player);
        CardPileCmd.Add(rs, card, PileType.Deck);
        return card;
    }

    public static CardInstance? AddCurseToDeck(RunState rs, string curseId, PlayerState player)
    {
        if (!CardRegistry.TryGet(curseId, out var def))
        {
            rs.Fidelity.Record(FidelityFlag.ApproximatedRule, UnportedCardSite + " (" + curseId + ")");
            return null;
        }
        return CardPileCmd.AddCurseToDeck(rs, def, player);
    }

    public static CardInstance CloneCard(RunState rs, CardInstance mutableCard)
    {
        var clone = mutableCard.CloneForCombat(rs.AllocateCardInstanceId());
        clone.DeckVersionId = CardInstance.NoDeckVersion;
        return clone;
    }

    public static RelicModel? FindRelic(PlayerState player, string relicId)
    {
        var relics = player.Relics;
        for (int i = 0; i < relics.Count; i++)
        {
            if (relics[i].Id == relicId)
            {
                return relics[i];
            }
        }
        return null;
    }
}
