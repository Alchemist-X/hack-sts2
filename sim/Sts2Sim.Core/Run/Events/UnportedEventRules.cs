namespace Sts2Sim.Core.Run;

/// <summary>
/// <c>EventModel.IsAllowed</c> of the shared-pool events whose model is not ported (not in <see cref="EventRegistry"/>).
/// <see cref="RoomSet.EnsureNextEventIsValid"/> walks the whole pool, so an unported event that the game would skip must
/// still be skipped: treating it as allowed pulls it where the game pulled the next one.
/// </summary>
public static class UnportedEventRules
{
    public const string FakeMerchantId = "FAKE_MERCHANT";

    public const string CrystalSphereId = "CRYSTAL_SPHERE";

    public const string FoulPotionId = "FOUL_POTION";

    /// <summary>True when <paramref name="eventId"/> has a ported eligibility rule here (the event itself is unregistered).</summary>
    public static bool Has(string eventId) => eventId is FakeMerchantId or CrystalSphereId;

    public static bool IsAllowed(string eventId, RunState runState)
    {
        ArgumentNullException.ThrowIfNull(runState);
        switch (eventId)
        {
            case FakeMerchantId:
                // Models.Events/FakeMerchant.cs:194-205
                if (runState.CurrentActIndex < 1 || runState.Players.Count > 1)
                {
                    return false;
                }
                return runState.Players.All(p => p.Gold >= 100 || p.Potions.Any(x => string.Equals(x.Id, FoulPotionId, StringComparison.Ordinal)));
            case CrystalSphereId:
                // Models.Events/CrystalSphere.cs:236-243
                return runState.Players.All(p => p.Gold >= 100) && runState.CurrentActIndex > 0;
            default:
                return true;
        }
    }
}
