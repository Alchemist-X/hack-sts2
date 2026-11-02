namespace Sts2Sim.Core.Combat.Loop;

public static class ActionExecutor
{
    public static void PlayCard(CombatState cs, int handIndex, CreatureState? target)
    {
        var hand = cs.Player.Hand;
        if ((uint)handIndex >= (uint)hand.Count)
        {
            throw new InvalidOperationException($"Card index {handIndex} is outside the hand (size {hand.Count}).");
        }
        var card = hand[handIndex];
        if (!LegalActionGenerator.CanPlay(cs, card, out var reason))
        {
            throw new InvalidOperationException($"{card.Id} (hand index {handIndex}) cannot be played: {reason}.");
        }
        if (!card.IsValidTarget(cs, target))
        {
            throw new InvalidOperationException($"{card.Id} (hand index {handIndex}) cannot target {(target == null ? "nothing" : "creature " + target.CombatId)}.");
        }
        var (energySpent, starsSpent) = SpendResources(cs, card);
        var resources = new ResourceInfo(EnergySpent: energySpent, EnergyValue: energySpent, StarsSpent: starsSpent, StarValue: starsSpent);
        using var scope = cs.Choice.PushContext(ChoiceContextKind.Inline);
        CardCmd.OnPlayWrapper(cs, card, target, isAutoPlay: false, resources);
    }

    public static (int EnergySpent, int StarsSpent) SpendResources(CombatState cs, CardInstance card)
    {
        var player = CardPileCmd.OwnerOf(cs, card);
        int energy = player.Energy;
        int energyToSpend = card.EnergyCost.GetAmountToSpend(cs, card);
        int starsToSpend = Math.Max(0, card.GetStarCostWithModifiers(cs));
        if (energyToSpend > energy && Hook.ShouldPayExcessEnergyCostWithStars(cs, player))
        {
            starsToSpend += (energyToSpend - energy) * 2;
            energyToSpend = energy;
        }
        EnergyCmd.SpendEnergy(cs, card, energyToSpend);
        SpendStars(cs, card, player, starsToSpend);
        return (energyToSpend, starsToSpend);
    }

    public static void OnPlayWrapper(CombatState cs, CardInstance card, CreatureState? target, bool isAutoPlay, ResourceInfo resources)
        => CardCmd.OnPlayWrapper(cs, card, target, isAutoPlay, resources);

    private static void SpendStars(CombatState cs, CardInstance card, PlayerState player, int amount)
    {
        card.LastStarsSpent = amount;
        StarsCmd.SpendStars(cs, player, amount);
    }
}
