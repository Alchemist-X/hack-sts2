namespace Sts2Sim.Core.Cards;

public sealed class EndOfDays : CardDef
{
    public const string CardId = "END_OF_DAYS";
    public const int Cost = 3;
    public const int DoomAmount = 29;
    public const decimal DoomUpgrade = 8m;
    public const string DoomPowerId = "DOOM_POWER";

    public EndOfDays()
        : base(CardId, Cost, CardType.Skill, CardRarity.Rare, TargetType.AllEnemies)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DynamicVar(DynamicVarSet.DoomName, DoomAmount)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var endOfDays = play.Card;
        var owner = endOfDays.Owner;
        var hittable = new List<MonsterState>(cs.Monsters.Count);
        cs.GetHittableEnemies(hittable);
        for (int i = 0; i < hittable.Count; i++)
        {
            PowerCmd.Apply(cs, PowerRegistry.Create(DoomPowerId), hittable[i], endOfDays.Vars.Doom.BaseValue, owner, endOfDays);
        }
        cs.GetHittableEnemies(hittable);
        DoomKill(cs, GetDoomedCreatures(hittable));
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Doom.UpgradeValueBy(DoomUpgrade);
    }

    private static List<CreatureState> GetDoomedCreatures(IReadOnlyList<CreatureState> creatures)
    {
        var doomed = new List<CreatureState>(creatures.Count);
        for (int i = 0; i < creatures.Count; i++)
        {
            var c = creatures[i];
            var doom = c.GetPower(DoomPowerId);
            if (doom != null && c.CurrentHp <= doom.Amount)
            {
                doomed.Add(c);
            }
        }
        return doomed;
    }

    private static void DoomKill(CombatState cs, IReadOnlyList<CreatureState> creatures)
    {
        if (creatures.Count == 0)
        {
            return;
        }
        for (int i = 0; i < creatures.Count; i++)
        {
            CreatureCmd.Kill(cs, creatures[i]);
        }
        Hook.AfterDiedToDoom(cs, creatures);
    }
}
