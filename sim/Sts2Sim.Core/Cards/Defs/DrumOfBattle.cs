namespace Sts2Sim.Core.Cards;

public sealed class DrumOfBattle : CardDef
{
    public const string CardId = "DRUM_OF_BATTLE";
    public const int Cost = 1;
    public const int BaseCards = 2;
    public const int BaseEnergy = 2;
    public const decimal EnergyUpgrade = 1m;

    public DrumOfBattle()
        : base(CardId, Cost, CardType.Skill, CardRarity.Uncommon, TargetType.Self)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new CardsVar(BaseCards),
        new EnergyVar(BaseEnergy)
    };

    public override AbstractModel? CreateListener(CardInstance card) => new DrumOfBattleListener(card);

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var drumOfBattle = play.Card;
        CardPileCmd.Draw(cs, drumOfBattle.Vars.Cards.BaseValue, drumOfBattle.Owner);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Energy.UpgradeValueBy(EnergyUpgrade);
    }

    private sealed class DrumOfBattleListener : CardListener
    {
        public DrumOfBattleListener(CardInstance card)
            : base(card)
        {
        }

        public override AbstractModel CloneFor(CardInstance newOwner) => new DrumOfBattleListener(newOwner);

        public override void AfterCardExhausted(CombatState cs, CardInstance card, bool causedByEthereal)
        {
            var drumOfBattle = Card;
            if (!ReferenceEquals(card, drumOfBattle))
            {
                return;
            }
            int playCount = CardCmd.GeneratePlayCount(cs, drumOfBattle, null);
            for (int i = 0; i < playCount; i++)
            {
                PlayerCmd.GainEnergy(cs, drumOfBattle.Vars.Energy.BaseValue, drumOfBattle.Owner);
            }
        }
    }
}
