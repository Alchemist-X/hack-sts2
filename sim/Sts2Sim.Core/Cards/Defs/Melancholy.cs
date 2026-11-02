namespace Sts2Sim.Core.Cards;

public sealed class Melancholy : CardDef
{
    public const string CardId = "MELANCHOLY";
    public const int Cost = 3;
    public const decimal BaseBlock = 13m;
    public const decimal BlockUpgrade = 4m;
    public const int BaseEnergy = 1;

    public Melancholy()
        : base(CardId, Cost, CardType.Skill, CardRarity.Uncommon, TargetType.Self)
    {
    }

    public override bool GainsBlock => true;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new BlockVar(BaseBlock, ValueProp.Move),
        new EnergyVar(BaseEnergy)
    };

    public override AbstractModel? CreateListener(CardInstance card) => new Listener(card);

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var melancholy = play.Card;
        CreatureCmd.GainBlock(cs, melancholy.Owner, melancholy.Vars.Block, play);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Block.UpgradeValueBy(BlockUpgrade);
    }

    private sealed class Listener : CardListener
    {
        public Listener(CardInstance card)
            : base(card)
        {
        }

        public override AbstractModel CloneFor(CardInstance newOwner) => new Listener(newOwner);

        public override void AfterDeath(RunState rs, CreatureState creature, bool wasRemovalPrevented)
        {
            if (wasRemovalPrevented)
            {
                return;
            }
            var melancholy = Card;
            var pile = melancholy.HasOwner ? melancholy.Owner.FindPileContaining(melancholy) : null;
            if (pile == null || !pile.IsCombatPile)
            {
                return;
            }
            melancholy.EnergyCost.AddThisCombat(-melancholy.Vars.Energy.IntValue);
        }
    }
}
