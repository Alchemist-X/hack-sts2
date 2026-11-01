namespace Sts2Sim.Core.Powers;

public interface IVulnerableMultiplierModifier
{
    decimal ModifyVulnerableMultiplier(CombatState cs, CreatureState target, decimal amount, ValueProp props, CreatureState? dealer, CardInstance? cardSource);
}

public interface IWeakMultiplierModifier
{
    decimal ModifyWeakMultiplier(CombatState cs, CreatureState target, decimal amount, ValueProp props, CreatureState? dealer, CardInstance? cardSource);
}

public static class MultiplierModifierIds
{
    public const string CrueltyPower = "CRUELTY_POWER";

    public const string DebilitatePower = "DEBILITATE_POWER";

    public const string PaperPhrogRelic = "PAPER_PHROG";

    public const string PaperKraneRelic = "PAPER_KRANE";
}

public static class MultiplierModifiers
{
    public static IVulnerableMultiplierModifier? FirstVulnerableRelic(PlayerState? player)
    {
        if (player == null)
        {
            return null;
        }
        var relics = player.Relics;
        for (int i = 0; i < relics.Count; i++)
        {
            if (relics[i] is IVulnerableMultiplierModifier modifier)
            {
                return modifier;
            }
        }
        return null;
    }

    public static IWeakMultiplierModifier? FirstWeakRelic(PlayerState? player)
    {
        if (player == null)
        {
            return null;
        }
        var relics = player.Relics;
        for (int i = 0; i < relics.Count; i++)
        {
            if (relics[i] is IWeakMultiplierModifier modifier)
            {
                return modifier;
            }
        }
        return null;
    }
}
