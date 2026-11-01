namespace Sts2Sim.Core.Relics;

public sealed class EternalFeather : RelicModel
{
    public const string RelicId = "ETERNAL_FEATHER";

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Uncommon;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new CardsVar(5),
        new HealVar(3m)
    };

    public override void AfterRoomEntered(RunState rs, AbstractRoom room)
    {
        if (room is RestSiteRoom)
        {
            int num = Owner.Deck.Count / DynamicVars.Cards.IntValue;
            decimal healAmount = DynamicVars.Heal.BaseValue * num;
            CreatureCmd.Heal(rs, Owner, healAmount);
        }
    }
}
