using System.Collections.ObjectModel;

namespace Sts2Sim.Core.Cards;

public sealed class Doubt : CardDef
{
    public const string CardId = "DOUBT";
    public const int Cost = -1;
    public const decimal BaseWeak = 1m;

    private static readonly ReadOnlyCollection<CardKeyword> Keywords = Array.AsReadOnly(new[] { CardKeyword.Unplayable });

    public Doubt()
        : base(CardId, Cost, CardType.Curse, CardRarity.Curse, TargetType.None)
    {
    }

    public override int MaxUpgradeLevel => 0;

    public override IReadOnlyCollection<CardKeyword> CanonicalKeywords => Keywords;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new PowerVar<WeakPower>(BaseWeak)
    };

    public override bool HasTurnEndInHandEffect => true;

    public override void OnPlay(CombatState cs, CardPlay play)
    {
    }

    public override void OnTurnEndInHand(CombatState cs, CardInstance card)
    {
        var owner = card.Owner;
        bool alreadyHasWeak = owner.HasPower<WeakPower>();
        PowerModel? powerModel = PowerCmd.Apply<WeakPower>(cs, owner, card.Vars.Weak.BaseValue, null, card);
        if (powerModel != null && !alreadyHasWeak)
        {
            powerModel.SkipNextDurationTick = true;
        }
    }
}
