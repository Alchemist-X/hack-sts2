using System.Collections.ObjectModel;

namespace Sts2Sim.Core.Cards;

public sealed class Offering : CardDef
{
    public const string CardId = "OFFERING";
    public const int Cost = 0;
    public const decimal BaseHpLoss = 6m;
    public const int BaseEnergy = 2;
    public const int BaseCards = 3;
    public const decimal CardsUpgrade = 2m;
    public const ValueProp SelfDamageProps = ValueProp.Unblockable | ValueProp.Unpowered | ValueProp.Move;

    private static readonly ReadOnlyCollection<CardKeyword> Keywords = Array.AsReadOnly(new[] { CardKeyword.Exhaust });

    public Offering()
        : base(CardId, Cost, CardType.Skill, CardRarity.Rare, TargetType.Self)
    {
    }

    public override IReadOnlyCollection<CardKeyword> CanonicalKeywords => Keywords;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new HpLossVar(BaseHpLoss),
        new EnergyVar(BaseEnergy),
        new CardsVar(BaseCards)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var card = play.Card;
        var owner = card.Owner;
        CreatureCmd.Damage(cs, owner, card.Vars.HpLoss.BaseValue, SelfDamageProps, card, play);
        PlayerCmd.GainEnergy(cs, card.Vars.Energy.IntValue, owner);
        CardPileCmd.Draw(cs, card.Vars.Cards.BaseValue, owner);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Cards.UpgradeValueBy(CardsUpgrade);
    }
}
