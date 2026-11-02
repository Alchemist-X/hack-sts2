using System.Collections.ObjectModel;

namespace Sts2Sim.Core.Cards;

public sealed class Shame : CardDef
{
    public const string CardId = "SHAME";
    public const int Cost = -1;
    public const string FrailKey = "Frail";
    public const decimal BaseFrail = 1m;

    private static readonly ReadOnlyCollection<CardKeyword> Keywords = Array.AsReadOnly(new[] { CardKeyword.Unplayable });

    public Shame()
        : base(CardId, Cost, CardType.Curse, CardRarity.Curse, TargetType.None)
    {
    }

    public override int MaxUpgradeLevel => 0;

    public override IReadOnlyCollection<CardKeyword> CanonicalKeywords => Keywords;

    public override bool HasTurnEndInHandEffect => true;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DynamicVar(FrailKey, BaseFrail)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
    }

    public override void OnTurnEndInHand(CombatState cs, CardInstance card)
    {
        var owner = card.Owner;
        bool alreadyHasFrail = owner.HasPower<FrailPower>();
        var powerModel = PowerCmd.Apply<FrailPower>(cs, owner, card.Vars[FrailKey].BaseValue, null, card);
        if (powerModel != null && !alreadyHasFrail)
        {
            powerModel.SkipNextDurationTick = true;
        }
    }
}
