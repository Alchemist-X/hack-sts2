namespace Sts2Sim.Core.Cards;

public sealed class Scourge : CardDef
{
    public const string CardId = "SCOURGE";
    public const int Cost = 1;
    public const decimal BaseDoom = 13m;
    public const int BaseCards = 1;
    public const decimal DoomUpgrade = 3m;
    public const decimal CardsUpgrade = 1m;
    public const string DoomPowerId = "DOOM_POWER";

    public Scourge()
        : base(CardId, Cost, CardType.Skill, CardRarity.Common, TargetType.AnyEnemy)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DynamicVar(DynamicVarSet.DoomName, BaseDoom),
        new CardsVar(BaseCards)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var card = play.Card;
        var target = play.Target;
        ArgumentNullException.ThrowIfNull(target);
        var owner = card.Owner;
        PowerCmd.Apply(cs, DoomPowerId, target, card.Vars.Doom.BaseValue, owner, card);
        CardPileCmd.Draw(cs, card.Vars.Cards.BaseValue, owner);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Doom.UpgradeValueBy(DoomUpgrade);
        card.Vars.Cards.UpgradeValueBy(CardsUpgrade);
    }
}
