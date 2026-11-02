namespace Sts2Sim.Core.Combat.Pipeline;

public interface ISovereignBladeDef
{
    void AddDamage(CardInstance card, decimal amount);
}

public static class ForgeCmd
{
    public const string SovereignBladeId = "SOVEREIGN_BLADE";
    public const string AddDamageFallbackSite = SovereignBladeId + ":AddDamage";

    public static IReadOnlyList<CardInstance> Forge(CombatState cs, decimal amount, PlayerState player, AbstractModel? source)
    {
        ArgumentNullException.ThrowIfNull(cs);
        ArgumentNullException.ThrowIfNull(player);
        if (CombatStatus.IsOverOrEnding(cs))
        {
            return Array.Empty<CardInstance>();
        }
        var blades = GetSovereignBlades(player, includeExhausted: false);
        if (blades.Count == 0)
        {
            var sovereignBlade = cs.CreateCard(CardRegistry.Get(SovereignBladeId), player);
            CardPileCmd.AddGeneratedCardToCombat(cs, sovereignBlade, PileType.Hand, player);
            blades.Add(sovereignBlade);
        }
        IncreaseSovereignBladeDamage(cs, amount, player);
        Hook.AfterForge(cs, amount, player, source);
        return blades;
    }

    public static List<CardInstance> GetSovereignBlades(PlayerState player, bool includeExhausted)
    {
        ArgumentNullException.ThrowIfNull(player);
        var blades = new List<CardInstance>();
        var piles = player.AllPiles;
        for (int p = 0; p < piles.Count; p++)
        {
            var pile = piles[p];
            if (!includeExhausted && pile.Type == PileType.Exhaust)
            {
                continue;
            }
            var cards = pile.Cards;
            for (int i = 0; i < cards.Count; i++)
            {
                var card = cards[i];
                if (!card.IsDupe && card.Id == SovereignBladeId)
                {
                    blades.Add(card);
                }
            }
        }
        return blades;
    }

    private static void IncreaseSovereignBladeDamage(CombatState cs, decimal amount, PlayerState player)
    {
        var blades = GetSovereignBlades(player, includeExhausted: true);
        for (int i = 0; i < blades.Count; i++)
        {
            AddDamage(cs, blades[i], amount);
        }
    }

    private static void AddDamage(CombatState cs, CardInstance blade, decimal amount)
    {
        if (blade.Def is ISovereignBladeDef def)
        {
            def.AddDamage(blade, amount);
            return;
        }
        cs.Run.Fidelity.Record(FidelityFlag.ApproximatedRule, AddDamageFallbackSite);
        blade.Vars.Damage.BaseValue += amount;
    }
}
