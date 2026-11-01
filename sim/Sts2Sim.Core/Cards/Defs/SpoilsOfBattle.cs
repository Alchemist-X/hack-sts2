namespace Sts2Sim.Core.Cards;

public sealed class SpoilsOfBattle : CardDef
{
    public const string CardId = "SPOILS_OF_BATTLE";
    public const int Cost = 1;
    public const int BaseForge = 6;
    public const int BaseCards = 2;
    public const decimal ForgeUpgrade = 3m;

    public SpoilsOfBattle()
        : base(CardId, Cost, CardType.Skill, CardRarity.Common, TargetType.Self)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new ForgeVar(BaseForge),
        new CardsVar(BaseCards)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var spoilsOfBattle = play.Card;
        var owner = spoilsOfBattle.Owner;
        ForgeCmd.Forge(cs, spoilsOfBattle.Vars.Forge.IntValue, owner, null);
        CardPileCmd.Draw(cs, spoilsOfBattle.Vars.Cards.BaseValue, owner);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Forge.UpgradeValueBy(ForgeUpgrade);
    }
}
