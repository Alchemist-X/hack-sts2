using Sts2Sim.Core;
using Sts2Sim.Core.Registry;

namespace Sts2Sim.Tests;

public static class SmokeTests
{
    public static void Test_Smoke_GameActionValueSemantics()
    {
        var a = GameAction.Play(2, 0);
        var b = new GameAction(ActionKind.PlayCard, 2, 0);
        Assert.Equal(a, b);
        Assert.NotEqual(a, GameAction.EndTurn);
        Assert.Equal(GameAction.NoIndex, GameAction.EndTurn.CardIndex);
        Assert.Equal("Play(2->0)", a.ToString());
    }

    public static void Test_Smoke_EnumsMirrorDecompileOrdinals()
    {
        Assert.Equal(1, (int)CardType.Attack);
        Assert.Equal(2, (int)TargetType.AnyEnemy);
        Assert.Equal(8, (int)ValueProp.Move);
        Assert.Equal(12, Enum.GetValues<RunRngType>().Length);
        Assert.Equal(8, (int)RunRngType.MonsterAi);
    }

    public static void Test_Smoke_RegistriesDiscoverWithoutThrowing()
    {
        Assert.True(CardRegistry.Defs.Count >= 0);
        Assert.True(MonsterRegistry.Defs.Count >= 0);
        Assert.True(EncounterRegistry.Defs.Count >= 0);
        Assert.True(PowerRegistry.Count >= 0);
        Assert.Throws<KeyNotFoundException>(() => CardRegistry.Get("NO_SUCH_CARD"));
    }
}
