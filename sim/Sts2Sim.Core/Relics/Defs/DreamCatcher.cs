namespace Sts2Sim.Core.Relics;

public sealed class DreamCatcher : RelicModel
{
    public const string RelicId = "DREAM_CATCHER";

    public const int CardRewardOptionCount = 3;

    public const string AdditionalRestSiteHealText = RelicId + ".additionalRestSiteHealText";

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Event;

    public override bool TryModifyRestSiteHealRewards(RunState rs, PlayerState player, List<Reward> rewards, bool isMimicked)
    {
        if (!ReferenceEquals(player, Owner))
        {
            return false;
        }
        rewards.Add(new CardReward(Owner, CardCreationOptions.ForRoom(rs, player, RoomType.Monster), CardRewardOptionCount));
        return true;
    }

    public override IReadOnlyList<string> ModifyExtraRestSiteHealText(RunState rs, PlayerState player, IReadOnlyList<string> currentExtraText)
    {
        var array = new string[1 + currentExtraText.Count];
        int num = 0;
        foreach (var current in currentExtraText)
        {
            array[num] = current;
            num++;
        }
        array[num] = AdditionalRestSiteHealText;
        return array;
    }
}
