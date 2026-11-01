using System.Collections.ObjectModel;

namespace Sts2Sim.Core.Cards;

public sealed class Chill : CardDef
{
    public const string CardId = "CHILL";
    public const int Cost = 0;

    private static readonly ReadOnlyCollection<CardKeyword> Keywords = Array.AsReadOnly(new[] { CardKeyword.Exhaust });

    public Chill()
        : base(CardId, Cost, CardType.Skill, CardRarity.Uncommon, TargetType.Self)
    {
    }

    public override IReadOnlyCollection<CardKeyword> CanonicalKeywords => Keywords;

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var chill = play.Card;
        var hittableEnemies = new List<MonsterState>();
        cs.GetHittableEnemies(hittableEnemies);
        for (int i = 0; i < hittableEnemies.Count; i++)
        {
            OrbCmd.Channel<FrostOrb>(cs, chill.Owner);
        }
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.RemoveKeyword(CardKeyword.Exhaust);
    }
}
