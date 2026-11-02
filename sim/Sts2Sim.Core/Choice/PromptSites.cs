namespace Sts2Sim.Core.Choice;

/// <summary>Lookups over the generated prompt-site table (<c>PromptSites.g.cs</c>).</summary>
public static partial class PromptSites
{
    /// <summary>A card that can reach a prompt while it merely exists (a hook outside OnPlay, or a hazardous enchantment/affliction on it).</summary>
    public static bool IsPresenceCard(CardInstance card)
    {
        if (CardsPresence.Contains(card.Id))
        {
            return true;
        }
        if (card.Enchantment is { } enchantment && Enchantments.Contains(enchantment.Id))
        {
            return true;
        }
        return card.Affliction is { } affliction && Afflictions.Contains(affliction.Id);
    }

    /// <summary>A card that can reach a prompt when it is played (by the player or auto-played).</summary>
    public static bool IsPlayCard(CardInstance card) => CardsOnPlay.Contains(card.Id) || IsPresenceCard(card);

    /// <summary>A potion that can reach a prompt when used.</summary>
    public static bool IsUsePotion(PotionModel potion) => PotionsOnUse.Contains(potion.Id) || PotionsPresence.Contains(potion.Id);

    /// <summary>True when <paramref name="sourceId"/> (a <see cref="ChoicePrompt.SourceId"/>) is a known prompt source.</summary>
    public static bool IsKnownSource(string sourceId) => PromptSourceIds.Contains(sourceId);
}
