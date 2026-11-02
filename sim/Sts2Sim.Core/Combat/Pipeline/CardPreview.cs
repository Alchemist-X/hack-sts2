namespace Sts2Sim.Core.Combat.Pipeline;

// Read-only port of the game's card preview numbers: DynamicVar.UpdateCardPreview(card, previewMode, target,
// runGlobalHooks: true) for the damage and block vars (Localization.DynamicVars/DamageVar.cs, CalculatedDamageVar.cs,
// OstyDamageVar.cs, BlockVar.cs, CalculatedBlockVar.cs). That is the number the card shows against a target: the
// same Hook.ModifyDamage / Hook.ModifyBlock query the play path runs (enchantment, then the additive and
// multiplicative listeners: Strength, Vigor, Weak, Vulnerable, Frail, Dexterity, ...), with cardPlay null as in the
// game's preview. Pure: no AfterModifying* hooks (where consumable modifiers are spent), no RNG, no state writes.
// Not part of the simulation; observation helpers only.
public static class CardPreview
{
    // The previewed per-hit damage of the card's damage var against target (null target: untargeted preview), or
    // null when the card has no damage var (or its Osty damage has no live Osty to deal it).
    public static decimal? Damage(CombatState cs, CardInstance card, CreatureState? target)
    {
        ArgumentNullException.ThrowIfNull(cs);
        ArgumentNullException.ThrowIfNull(card);
        var vars = card.Vars;
        var owner = CombatStatus.CardOwnerCreature(cs, card);
        if (vars.TryGet(CalculatedDamageVar.DefaultName) is CalculatedDamageVar calculated && calculated.HasMultiplier)
        {
            CreatureState? dealer = owner;
            if (calculated.IsFromOsty)
            {
                dealer = owner is PlayerState p ? CombatStatus.GetOsty(cs, p) : null;
                if (dealer == null)
                {
                    return null;
                }
            }
            decimal num = calculated.Calculate(cs, card, target);
            return Math.Max(0m, Hook.ModifyDamage(cs.Run, cs, target, dealer, num, calculated.Props, card, null, ModifyDamageHookType.All));
        }
        if (vars.TryGet(DamageVar.DefaultName) is DamageVar damage)
        {
            return Hook.ModifyDamage(cs.Run, cs, target, owner, damage.BaseValue, damage.Props, card, null, ModifyDamageHookType.All);
        }
        if (vars.TryGet(OstyDamageVar.DefaultName) is OstyDamageVar ostyDamage)
        {
            var osty = owner is PlayerState p ? CombatStatus.GetOsty(cs, p) : null;
            if (osty == null)
            {
                return null;
            }
            return Hook.ModifyDamage(cs.Run, cs, target, osty, ostyDamage.BaseValue, ostyDamage.Props, card, null, ModifyDamageHookType.All);
        }
        return null;
    }

    // The previewed block of the card's block var (target: the card's target, used only by calculated block), or
    // null when the card has no block var.
    public static decimal? Block(CombatState cs, CardInstance card, CreatureState? target)
    {
        ArgumentNullException.ThrowIfNull(cs);
        ArgumentNullException.ThrowIfNull(card);
        var vars = card.Vars;
        var owner = CombatStatus.CardOwnerCreature(cs, card);
        if (vars.TryGet(CalculatedBlockVar.DefaultName) is CalculatedBlockVar calculated && calculated.HasMultiplier)
        {
            return Hook.ModifyBlock(cs, owner, calculated.Calculate(cs, card, target), calculated.Props, card, null);
        }
        if (vars.TryGet(BlockVar.DefaultName) is BlockVar block)
        {
            return Hook.ModifyBlock(cs, owner, block.BaseValue, block.Props, card, null);
        }
        return null;
    }
}
