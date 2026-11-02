namespace Sts2Sim.Core.Run;

public abstract partial class Reward : ICanonical
{
    protected Reward(PlayerState player)
    {
        Player = player;
    }

    public PlayerState Player { get; protected set; }

    public abstract RewardType RewardType { get; }

    public abstract int RewardsSetIndex { get; }

    public abstract bool IsPopulated { get; }

    public bool SuccessfullySelected { get; protected set; }

    protected Rng? RngOverride { get; private set; }

    public Reward SetRng(Rng rng)
    {
        RngOverride = rng;
        return this;
    }

    public abstract void Populate(RunState rs);

    protected abstract bool OnSelect(RunState rs);

    public virtual bool OnSelectWrapper(RunState rs)
    {
        ArgumentNullException.ThrowIfNull(rs);
        bool success = OnSelect(rs);
        return CompleteSelection(rs, success);
    }

    protected bool CompleteSelection(RunState rs, bool success)
    {
        if (success)
        {
            Hook.AfterRewardTaken(rs, Player, this);
            SuccessfullySelected = true;
        }
        return success;
    }

    public virtual void OnSkipped(RunState rs)
    {
    }

    public Reward CloneFor(PlayerState player)
    {
        ArgumentNullException.ThrowIfNull(player);
        var clone = CloneCore(player);
        clone.SuccessfullySelected = SuccessfullySelected;
        clone.RngOverride = CardCreationOptions.RemapRng(RngOverride, Player, player);
        return clone;
    }

    protected abstract Reward CloneCore(PlayerState player);

    /// <summary>
    /// Clone fidelity for objects a claimed reward shares with its player's state (CONTRACTS.md Part A §6: one object in the
    /// source → one object in the clone). RunState.DeepClone clones the players before the rooms and rewards, so a potion
    /// that is still in <paramref name="source"/>'s belt resolves to <paramref name="player"/>'s potion in the same slot;
    /// anything else is copied.
    /// </summary>
    protected static PotionModel CloneBeltPotion(PotionModel potion, PlayerState source, PlayerState player)
    {
        int slot = source.GetPotionSlotIndex(potion);
        var slots = player.PotionSlots;
        if ((uint)slot < (uint)slots.Length && slots[slot] is { } counterpart && string.Equals(counterpart.Id, potion.Id, StringComparison.Ordinal))
        {
            return counterpart;
        }
        return potion.CloneFor(player);
    }

    /// <summary>
    /// As <see cref="CloneBeltPotion"/> for a card the reward put into the deck: a card still in <paramref name="source"/>'s
    /// deck resolves to <paramref name="player"/>'s card at the same deck position; a card no longer in the deck is copied.
    /// </summary>
    protected static CardInstance CloneDeckCard(CardInstance card, PlayerState source, PlayerState player)
    {
        var deck = source.Deck;
        for (int i = 0; i < deck.Count; i++)
        {
            if (ReferenceEquals(deck[i], card))
            {
                var clones = player.Deck;
                if (i < clones.Count && clones[i].InstanceId == card.InstanceId && string.Equals(clones[i].Id, card.Id, StringComparison.Ordinal))
                {
                    return clones[i];
                }
                break;
            }
        }
        return card.CloneFor(player);
    }

    public void WriteCanonical(CanonicalWriter w)
    {
        w.WriteTag(RewardsCanonicalTags.Reward);
        w.WriteByte((byte)RewardType);
        w.WriteBool(SuccessfullySelected);
        w.WriteBool(RngOverride != null);
        RngOverride?.WriteCanonical(w);
        WriteFields(w);
    }

    protected abstract void WriteFields(CanonicalWriter w);
}
