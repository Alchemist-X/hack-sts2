using System.Collections.ObjectModel;

namespace Sts2Sim.Core.Cards;

public sealed class Relax : CardDef
{
    public const string CardId = "RELAX";
    public const int Cost = 3;
    public const decimal BaseBlock = 16m;
    public const int BaseCards = 2;
    public const int BaseEnergy = 2;
    public const decimal BlockUpgrade = 2m;
    public const decimal CardsUpgrade = 1m;
    public const decimal EnergyUpgrade = 1m;
    public const string DrawCardsNextTurnPowerId = "DRAW_CARDS_NEXT_TURN_POWER";
    public const string EnergyNextTurnPowerId = "ENERGY_NEXT_TURN_POWER";

    private static readonly ReadOnlyCollection<CardKeyword> Keywords = Array.AsReadOnly(new[] { CardKeyword.Exhaust });

    public Relax()
        : base(CardId, Cost, CardType.Skill, CardRarity.Ancient, TargetType.Self)
    {
    }

    public override bool GainsBlock => true;

    public override IReadOnlyCollection<CardKeyword> CanonicalKeywords => Keywords;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new BlockVar(BaseBlock, ValueProp.Move),
        new CardsVar(BaseCards),
        new EnergyVar(BaseEnergy)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var card = play.Card;
        var owner = card.Owner;
        CreatureCmd.GainBlock(cs, owner, card.Vars.Block, play);
        PowerCmd.Apply(cs, DrawCardsNextTurnPowerId, owner, card.Vars.Cards.BaseValue, owner, card);
        PowerCmd.Apply(cs, EnergyNextTurnPowerId, owner, card.Vars.Energy.BaseValue, owner, card);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Block.UpgradeValueBy(BlockUpgrade);
        card.Vars.Cards.UpgradeValueBy(CardsUpgrade);
        card.Vars.Energy.UpgradeValueBy(EnergyUpgrade);
    }
}
