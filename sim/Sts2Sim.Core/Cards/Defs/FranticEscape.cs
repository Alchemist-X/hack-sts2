namespace Sts2Sim.Core.Cards;

public sealed class FranticEscape : CardDef
{
    public const string CardId = "FRANTIC_ESCAPE";
    public const int Cost = 1;
    public const string SandpitPowerId = "SANDPIT_POWER";
    public const decimal SandpitIncrease = 1m;
    public const int CostIncreasePerPlay = 1;

    public FranticEscape()
        : base(CardId, Cost, CardType.Status, CardRarity.Status, TargetType.Self)
    {
    }

    public override int MaxUpgradeLevel => 0;

    public override bool CanBeGeneratedInCombat => false;

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var card = play.Card;
        var owner = card.Owner;
        var sandpitEnemy = GetSandpitEnemy(cs);
        PowerModel? sandpitPower = null;
        if (sandpitEnemy != null)
        {
            var powers = sandpitEnemy.Powers;
            for (int i = 0; i < powers.Count; i++)
            {
                if (powers[i].Id == SandpitPowerId && powers[i].TargetId == owner.CombatId)
                {
                    sandpitPower = powers[i];
                    break;
                }
            }
        }
        if (sandpitPower != null)
        {
            PowerCmd.ModifyAmount(cs, sandpitPower, SandpitIncrease, sandpitEnemy, card);
        }
        card.EnergyCost.AddThisCombat(CostIncreasePerPlay);
    }

    private static CreatureState? GetSandpitEnemy(CombatState cs)
    {
        var enemies = cs.GetCreaturesOnSide(CombatSide.Enemy);
        for (int i = 0; i < enemies.Count; i++)
        {
            if (enemies[i].HasPower(SandpitPowerId))
            {
                return enemies[i];
            }
        }
        return null;
    }
}
