using System.Collections.ObjectModel;

namespace Sts2Sim.Core.Cards;

public sealed class Voltaic : CardDef
{
    public const string CardId = "VOLTAIC";
    public const int Cost = 3;
    public const string CalculatedChannelsKey = "CalculatedChannels";
    public const decimal CalculationBase = 0m;
    public const decimal CalculationExtra = 1m;

    private static readonly ReadOnlyCollection<CardKeyword> Keywords = Array.AsReadOnly(new[] { CardKeyword.Exhaust });

    public Voltaic()
        : base(CardId, Cost, CardType.Skill, CardRarity.Rare, TargetType.Self)
    {
    }

    public override IReadOnlyCollection<CardKeyword> CanonicalKeywords => Keywords;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new CalculationBaseVar(CalculationBase),
        new CalculationExtraVar(CalculationExtra),
        new CalculatedVar(CalculatedChannelsKey).WithMultiplier(static (cs, card, _) => CountLightningChanneled(cs, card))
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var voltaic = play.Card;
        int lightningChanneledCount = (int)((CalculatedVar)voltaic.Vars[CalculatedChannelsKey]).Calculate(cs, voltaic, play.Target);
        for (int i = 0; i < lightningChanneledCount; i++)
        {
            OrbCmd.Channel<LightningOrb>(cs, voltaic.Owner);
        }
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.RemoveKeyword(CardKeyword.Exhaust);
    }

    private static decimal CountLightningChanneled(CombatState cs, CardInstance card)
    {
        int ownerId = card.Owner.CombatId;
        int count = 0;
        var entries = cs.History.OrbChanneledEntries;
        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            if (e.Stamp.ActorId == ownerId && e.OrbId == LightningOrb.OrbIdEntry)
            {
                count++;
            }
        }
        return count;
    }
}
