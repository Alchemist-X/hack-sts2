namespace Sts2Sim.Core.Relics;

public sealed class SilkenTress : RelicModel
{
    public const string RelicId = "SILKEN_TRESS";

    private bool _isUsed;

    public override string Id => RelicId;

    public override bool IsUsedUp => IsUsed;

    public override RelicRarity Rarity => RelicRarity.Ancient;

    private bool IsUsed
    {
        get => _isUsed;
        set
        {
            _isUsed = value;
            if (IsUsedUp)
            {
                Status = RelicStatus.Disabled;
            }
        }
    }

    public override void AfterObtained(RunState rs)
    {
        PlayerCmd.LoseGold(rs, Owner.Gold, Owner);
    }

    public override bool TryModifyCardRewardOptionsLate(RunState rs, PlayerState player, List<CardCreationResult> cardRewardOptions, CardCreationOptions creationOptions)
    {
        if (!ReferenceEquals(player, Owner))
        {
            return false;
        }
        if (!creationOptions.Flags.HasFlag(CardCreationFlags.IsCardReward))
        {
            return false;
        }
        if (IsUsed)
        {
            return false;
        }
        var glam = new Glam();
        foreach (var current in cardRewardOptions)
        {
            var card = current.Card;
            if (glam.CanEnchant(card))
            {
                var card2 = MoltenEgg.CloneRunCard(rs, card);
                CardCmd.Enchant<Glam>(card2, 1m);
                current.ModifyCard(card2, this);
            }
        }
        return true;
    }

    public override void AfterModifyingCardRewardOptions(RunState rs)
    {
        if (IsUsed)
        {
            return;
        }
        IsUsed = true;
    }

    protected override void WriteExtraCanonical(CanonicalWriter w)
    {
        w.WriteBool(_isUsed);
    }
}
