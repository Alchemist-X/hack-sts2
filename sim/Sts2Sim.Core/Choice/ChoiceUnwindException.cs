namespace Sts2Sim.Core.Choice;

public sealed class ChoiceUnwindException : Exception
{
    public ChoiceUnwindException(string siteId)
        : base($"The combat ended during the first-pause steps of the turn-setup context paused by {siteId}; the rest of the action is unwound.")
    {
        SiteId = siteId;
    }

    public string SiteId { get; }
}
