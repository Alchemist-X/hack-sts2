namespace Sts2Sim.Core.Run;

public abstract partial class RestSiteOption
{
    protected RestSiteOption(PlayerState owner)
    {
        Owner = owner;
    }

    public PlayerState Owner { get; protected set; }

    public abstract string OptionId { get; }

    public virtual bool IsEnabled => true;

    public static List<RestSiteOption> Generate(RunState rs, PlayerState player)
    {
        ArgumentNullException.ThrowIfNull(rs);
        ArgumentNullException.ThrowIfNull(player);
        var options = new List<RestSiteOption>(2)
        {
            new HealRestSiteOption(player),
            new SmithRestSiteOption(player),
        };
        if (rs.Players.Count > 1)
        {
            options.Add(new MendRestSiteOption(player));
        }
        Hook.ModifyRestSiteOptions(rs, player, options);
        return options;
    }

    public virtual bool OnSelect(RunState rs) => throw new NotImplementedException($"W2 R4: {GetType().Name}.OnSelect");

    public virtual RestSiteOption CloneFor(PlayerState owner)
    {
        var clone = (RestSiteOption)MemberwiseClone();
        clone.Owner = owner;
        return clone;
    }
}

public sealed class HealRestSiteOption : RestSiteOption
{
    public HealRestSiteOption(PlayerState owner) : base(owner)
    {
    }

    public override string OptionId => "HEAL";

    public static decimal GetBaseHealAmount(CreatureState creature) => creature.MaxHp * 0.3m;

    public static decimal GetHealAmount(RunState rs, PlayerState player) => Hook.ModifyRestSiteHealAmount(rs, player, GetBaseHealAmount(player));

    public override bool OnSelect(RunState rs)
    {
        ExecuteRestSiteHeal(rs, Owner, false);
        return true;
    }

    public static void ExecuteRestSiteHeal(RunState rs, PlayerState player, bool isMimicked)
    {
        ArgumentNullException.ThrowIfNull(rs);
        ArgumentNullException.ThrowIfNull(player);
        CreatureInternals.HealInternal(player, GetHealAmount(rs, player));
        Hook.AfterRestSiteHeal(rs, player, isMimicked);
        var rewards = new List<Reward>();
        Hook.ModifyRestSiteHealRewards(rs, player, rewards, isMimicked);
        RewardsCmd.OfferCustom(rs, player, rewards);
    }
}

public sealed class SmithRestSiteOption : RestSiteOption
{
    public SmithRestSiteOption(PlayerState owner) : base(owner)
    {
    }

    public override string OptionId => "SMITH";

    public int SmithCount { get; set; } = 1;

    public override bool IsEnabled => Owner.Deck.Count(c => c.IsUpgradable) != 0;

    public override bool OnSelect(RunState rs)
    {
        var prefs = new CardSelectorPrefs(CardSelectorPrefs.UpgradeSelectionPrompt, SmithCount)
        {
            Cancelable = true,
            RequireManualConfirmation = true,
        };
        var selection = RoomPrompts.FromDeckForUpgrade(rs, Owner, prefs, RoomPrompts.SmithSite, OptionId);
        if (selection.Count == 0)
        {
            return false;
        }
        for (int i = 0; i < selection.Count; i++)
        {
            CardCmd.Upgrade(null, selection[i]);
        }
        Hook.AfterRestSiteSmith(rs, Owner);
        return true;
    }
}

public sealed class DigRestSiteOption : RestSiteOption
{
    public DigRestSiteOption(PlayerState owner) : base(owner)
    {
    }

    public override string OptionId => "DIG";

    public override bool OnSelect(RunState rs)
    {
        RelicCmd.Obtain(rs, RelicFactory.PullNextRelicFromFront(rs, Owner), Owner);
        return true;
    }
}

public sealed class CookRestSiteOption : RestSiteOption
{
    public const int CardsToRemove = 2;

    public const int MaxHpGain = 5;

    public CookRestSiteOption(PlayerState owner) : base(owner)
    {
    }

    public override string OptionId => "COOK";

    public override bool IsEnabled => Owner.Deck.Count(c => c.IsRemovable) >= CardsToRemove;

    public override bool OnSelect(RunState rs)
    {
        var prefs = new CardSelectorPrefs(CardSelectorPrefs.RemoveSelectionPrompt, CardsToRemove)
        {
            Cancelable = true,
            RequireManualConfirmation = true,
        };
        var selection = RoomPrompts.FromDeckForRemoval(rs, Owner, prefs, RoomPrompts.CookSite, OptionId);
        if (selection.Count == 0)
        {
            return false;
        }
        for (int i = 0; i < selection.Count; i++)
        {
            CardPileCmd.RemoveFromDeck(rs, selection[i]);
        }
        int oldMaxHp = Owner.MaxHp;
        CreatureInternals.SetMaxHpInternal(Owner, Owner.MaxHp + (decimal)MaxHpGain);
        CreatureInternals.HealInternal(Owner, Owner.MaxHp - oldMaxHp);
        return true;
    }
}

public sealed class HatchRestSiteOption : RestSiteOption
{
    public const string ByrdpipId = "BYRDPIP";

    public HatchRestSiteOption(PlayerState owner) : base(owner)
    {
    }

    public override string OptionId => "HATCH";

    public override bool OnSelect(RunState rs)
    {
        RelicCmd.Obtain(rs, ByrdpipId, Owner);
        return true;
    }
}

public sealed class LiftRestSiteOption : RestSiteOption
{
    public LiftRestSiteOption(PlayerState owner) : base(owner)
    {
    }

    public override string OptionId => "LIFT";

    public override bool OnSelect(RunState rs)
    {
        var girya = Owner.GetRelic<Girya>() ?? throw new InvalidOperationException("LiftRestSiteOption.OnSelect: owner has no Girya.");
        girya.TimesLifted++;
        return true;
    }
}

public sealed class KindleRestSiteOption : RestSiteOption
{
    public KindleRestSiteOption(PlayerState owner) : base(owner)
    {
    }

    public override string OptionId => "KINDLE";

    public override bool OnSelect(RunState rs) => throw new NotImplementedException("W3 content: KindleRestSiteOption.OnSelect needs PumpkinCandle.Rekindle");
}

public sealed class CloneRestSiteOption : RestSiteOption
{
    public CloneRestSiteOption(PlayerState owner) : base(owner)
    {
    }

    public override string OptionId => "CLONE";

    public override bool OnSelect(RunState rs) => throw new NotImplementedException("W3 content: CloneRestSiteOption.OnSelect needs RunState.CloneCard for deck cards with the Clone enchantment");
}

public sealed class MendRestSiteOption : RestSiteOption
{
    public MendRestSiteOption(PlayerState owner) : base(owner)
    {
    }

    public override string OptionId => "MEND";

    public override bool OnSelect(RunState rs) => throw new NotSupportedException("MendRestSiteOption is multiplayer-only.");
}
