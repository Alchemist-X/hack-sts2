namespace Sts2Sim.Core.Relics;

public sealed class SwordOfStone : RelicModel
{
    public const string RelicId = "SWORD_OF_STONE";

    private const string ElitesKey = "Elites";

    private int _elitesDefeated;

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Event;

    public override bool ShowCounter => true;

    public override int DisplayAmount => ElitesDefeated;

    protected override IEnumerable<DynamicVar> CanonicalVars => new[]
    {
        new DynamicVar(ElitesKey, 5m)
    };

    public int ElitesDefeated
    {
        get => _elitesDefeated;
        set => _elitesDefeated = value;
    }

    public override void AfterCombatVictory(RunState rs, CombatRoom? room)
    {
        if (room == null || room.RoomType != RoomType.Elite)
        {
            return;
        }
        ElitesDefeated++;
        if (ElitesDefeated >= DynamicVars[ElitesKey].BaseValue)
        {
            RelicCmd.Replace(rs, this, new SwordOfJade());
        }
    }

    protected override void WriteExtraCanonical(CanonicalWriter w)
    {
        w.WriteInt32(_elitesDefeated);
    }
}
