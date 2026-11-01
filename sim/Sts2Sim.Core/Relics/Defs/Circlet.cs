namespace Sts2Sim.Core.Relics;

public sealed class Circlet : RelicModel
{
    public const string RelicId = "CIRCLET";

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.None;

    public override bool IsStackable => true;
}
