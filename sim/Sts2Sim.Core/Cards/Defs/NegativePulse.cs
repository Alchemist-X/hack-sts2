namespace Sts2Sim.Core.Cards;

public sealed class NegativePulse : CardDef
{
    public const string CardId = "NEGATIVE_PULSE";
    public const int Cost = 1;
    public const string DoomPowerId = "DOOM_POWER";
    public const decimal BaseBlock = 5m;
    public const decimal BaseDoom = 7m;
    public const decimal BlockUpgrade = 1m;
    public const decimal DoomUpgrade = 4m;

    public NegativePulse()
        : base(CardId, Cost, CardType.Skill, CardRarity.Common, TargetType.AllEnemies)
    {
    }

    public override bool GainsBlock => true;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new BlockVar(BaseBlock, ValueProp.Move),
        new DynamicVar(DynamicVarSet.DoomName, BaseDoom)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var card = play.Card;
        var owner = card.Owner;
        CreatureCmd.GainBlock(cs, owner, card.Vars.Block, play);
        var hittable = HittableEnemies(cs);
        for (int i = 0; i < hittable.Count; i++)
        {
            PowerCmd.Apply(cs, PowerRegistry.Create(DoomPowerId), hittable[i], card.Vars.Doom.BaseValue, owner, card);
        }
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Block.UpgradeValueBy(BlockUpgrade);
        card.Vars.Doom.UpgradeValueBy(DoomUpgrade);
    }

    private static List<CreatureState> HittableEnemies(CombatState cs)
    {
        var monsters = cs.Monsters;
        var hittable = new List<CreatureState>(monsters.Count);
        for (int i = 0; i < monsters.Count; i++)
        {
            if (CombatStatus.IsHittable(cs, monsters[i]))
            {
                hittable.Add(monsters[i]);
            }
        }
        return hittable;
    }
}
